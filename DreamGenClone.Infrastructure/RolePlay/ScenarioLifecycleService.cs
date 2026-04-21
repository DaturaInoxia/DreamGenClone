using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ScenarioLifecycleService : IScenarioLifecycleService
{
    private readonly ILogger<ScenarioLifecycleService> _logger;
    private readonly INarrativeGateProfileService? _gateProfileService;

    public ScenarioLifecycleService(ILogger<ScenarioLifecycleService> logger, INarrativeGateProfileService? gateProfileService = null)
    {
        _logger = logger;
        _gateProfileService = gateProfileService;
    }

    public async Task<PhaseTransitionResult> EvaluateTransitionAsync(
        AdaptiveScenarioState state,
        LifecycleInputs inputs,
        CancellationToken cancellationToken = default)
    {
        NarrativeGateProfile? profile = null;
        if (_gateProfileService is not null)
        {
            if (!string.IsNullOrWhiteSpace(inputs.NarrativeGateProfileId))
            {
                profile = await _gateProfileService.GetAsync(inputs.NarrativeGateProfileId, cancellationToken);
            }

            if (!inputs.SkipDefaultNarrativeGateProfileFallback)
            {
                profile ??= await _gateProfileService.GetDefaultAsync(cancellationToken);
            }
        }

        var (transitioned, targetPhase, triggerType, reasonCode) = ResolveTransition(state, inputs, profile);
        if (!transitioned)
        {
            return new PhaseTransitionResult
            {
                Transitioned = false,
                TargetPhase = state.CurrentPhase,
                Reason = "No transition criteria were met."
            };
        }

        var averageDesire = GetAverageStat(state, static snapshot => snapshot.Desire);
        var averageRestraint = GetAverageStat(state, static snapshot => snapshot.Restraint);

        var transitionEvent = new NarrativePhaseTransitionEvent
        {
            TransitionId = Guid.NewGuid().ToString("N"),
            SessionId = state.SessionId,
            FromPhase = state.CurrentPhase,
            ToPhase = targetPhase,
            TriggerType = triggerType,
            ReasonCode = reasonCode,
            EvidencePayload = JsonSerializer.Serialize(new
            {
                inputs.InteractionsSinceCommitment,
                inputs.ActiveScenarioConfidence,
                inputs.ActiveScenarioFitScore,
                averageDesire,
                averageRestraint,
                inputs.EvidenceSummary,
                inputs.NarrativeGateProfileId,
                inputs.ForceReset,
                inputs.ManualOverride,
                ManualAdvanceTargetPhase = inputs.ManualAdvanceTargetPhase?.ToString(),
                inputs.ClimaxCompletionRequested
            }),
            OccurredUtc = DateTime.UtcNow
        };

        _logger.LogInformation(
            RolePlayV2LogEvents.PhaseTransitionApplied,
            transitionEvent.SessionId,
            transitionEvent.FromPhase,
            transitionEvent.ToPhase,
            transitionEvent.TriggerType,
            transitionEvent.ReasonCode);

        return new PhaseTransitionResult
        {
            Transitioned = true,
            TargetPhase = targetPhase,
            TransitionEvent = transitionEvent,
            Reason = reasonCode
        };
    }

    public Task<AdaptiveScenarioState> ExecuteResetAsync(
        AdaptiveScenarioState state,
        ResetReason reason,
        CancellationToken cancellationToken = default)
    {
        var resetState = new AdaptiveScenarioState
        {
            SessionId = state.SessionId,
            ActiveScenarioId = null,
            CurrentPhase = NarrativePhase.Reset,
            InteractionCountInPhase = 0,
            ConsecutiveLeadCount = 0,
            LastEvaluationUtc = DateTime.UtcNow,
            CycleIndex = state.CycleIndex + 1,
            ActiveFormulaVersion = state.ActiveFormulaVersion,
            CharacterSnapshots = state.CharacterSnapshots
                .Select(ApplySemiResetDecay)
                .ToList()
        };

        _logger.LogInformation(
            "RolePlayV2 reset executed: SessionId={SessionId} NewCycleIndex={CycleIndex} Reason={Reason}",
            state.SessionId,
            resetState.CycleIndex,
            reason);

        return Task.FromResult(resetState);
    }

    private static CharacterStatProfileV2 ApplySemiResetDecay(CharacterStatProfileV2 snapshot)
    {
        return new CharacterStatProfileV2
        {
            CharacterId = snapshot.CharacterId,
            Desire = DecayElevatedStat(snapshot.Desire, baseDecay: 10, elevationMultiplier: 0.45, minimum: 50),
            Restraint = MoveTowardNeutral(snapshot.Restraint, step: 10),
            Tension = DecayElevatedStat(snapshot.Tension, baseDecay: 7, elevationMultiplier: 0.30, minimum: 0),
            Connection = Math.Clamp(snapshot.Connection, 0, 100),
            Dominance = DecayElevatedStat(snapshot.Dominance, baseDecay: 5, elevationMultiplier: 0.25, minimum: 0),
            Loyalty = Math.Clamp(snapshot.Loyalty, 0, 100),
            SelfRespect = Math.Clamp(snapshot.SelfRespect, 0, 100),
            SnapshotUtc = DateTime.UtcNow
        };
    }

    private static int DecayElevatedStat(int value, int baseDecay, double elevationMultiplier, int minimum)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped <= 50)
        {
            return clamped;
        }

        var elevation = clamped - 50;
        var variableDecay = (int)Math.Round(elevation * elevationMultiplier, MidpointRounding.AwayFromZero);
        var decayed = clamped - baseDecay - variableDecay;
        return Math.Clamp(decayed, minimum, 100);
    }

    private static int MoveTowardNeutral(int value, int step)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped < 50)
        {
            return Math.Min(50, clamped + step);
        }

        if (clamped > 50)
        {
            return Math.Max(50, clamped - step);
        }

        return clamped;
    }

    private static (bool Transitioned, NarrativePhase TargetPhase, TransitionTriggerType TriggerType, string ReasonCode) ResolveTransition(
        AdaptiveScenarioState state,
        LifecycleInputs inputs,
        NarrativeGateProfile? profile)
    {
        if (inputs.ForceReset)
        {
            return (true, NarrativePhase.Reset, TransitionTriggerType.Reset, "FORCE_RESET");
        }

        if (inputs.ManualAdvanceTargetPhase.HasValue
            && GetPhaseOrder(inputs.ManualAdvanceTargetPhase.Value) > GetPhaseOrder(state.CurrentPhase)
            && state.CurrentPhase != NarrativePhase.Climax)
        {
            return (true, inputs.ManualAdvanceTargetPhase.Value, TransitionTriggerType.Override, "MANUAL_NEXT_PHASE");
        }

        var averageDesire = GetAverageStat(state, static snapshot => snapshot.Desire);
        var averageRestraint = GetAverageStat(state, static snapshot => snapshot.Restraint);
        var metricValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [NarrativeGateMetricKeys.ActiveScenarioScore] = inputs.ActiveScenarioFitScore,
            [NarrativeGateMetricKeys.AverageDesire] = averageDesire,
            [NarrativeGateMetricKeys.AverageRestraint] = averageRestraint,
            [NarrativeGateMetricKeys.InteractionsSinceCommitment] = inputs.InteractionsSinceCommitment
        };

        if (state.CurrentPhase == NarrativePhase.Committed && HasConfiguredGateRules(profile, "Committed", "Approaching"))
        {
            return EvaluateConfiguredGate(profile, "Committed", "Approaching", metricValues)
                ? (true, NarrativePhase.Approaching, TransitionTriggerType.InteractionCountGate, "COMMITTED_TO_APPROACHING")
                : (false, state.CurrentPhase, TransitionTriggerType.Threshold, "NO_TRANSITION");
        }

        if (state.CurrentPhase == NarrativePhase.Approaching && HasConfiguredGateRules(profile, "Approaching", "Climax"))
        {
            return EvaluateConfiguredGate(profile, "Approaching", "Climax", metricValues)
                ? (true, NarrativePhase.Climax, TransitionTriggerType.Threshold, "APPROACHING_TO_CLIMAX")
                : (false, state.CurrentPhase, TransitionTriggerType.Threshold, "NO_TRANSITION");
        }

        if (state.CurrentPhase == NarrativePhase.Climax)
        {
            // Climax phase exits ONLY via explicit completion (/endclimax) or manual override.
            // Configured gate rules for Climax→Reset are intentionally ignored here.
            if (inputs.ManualOverride || inputs.ClimaxCompletionRequested)
            {
                return (true, NarrativePhase.Reset, TransitionTriggerType.Override, "CLIMAX_TO_RESET_EXPLICIT");
            }

            return (false, state.CurrentPhase, TransitionTriggerType.Threshold, "NO_TRANSITION");
        }

        // BuildUp → Committed is handled exclusively by the commit gate in ScenarioSelectionService.
        // All other phase transitions require configured gate profile rules from the database.
        // No hardcoded fallback thresholds — if profile rules are missing, no transition occurs.

        if (state.CurrentPhase == NarrativePhase.Reset)
        {
            if (HasConfiguredGateRules(profile, "Reset", "BuildUp"))
            {
                return EvaluateConfiguredGate(profile, "Reset", "BuildUp", metricValues)
                    ? (true, NarrativePhase.BuildUp, TransitionTriggerType.Reset, "RESET_TO_BUILDUP")
                    : (false, state.CurrentPhase, TransitionTriggerType.Threshold, "NO_TRANSITION");
            }

            // No configured gate: transition immediately (backward-compatible fallback).
            return (true, NarrativePhase.BuildUp, TransitionTriggerType.Reset, "RESET_TO_BUILDUP");
        }

        return (false, state.CurrentPhase, TransitionTriggerType.Threshold, "NO_TRANSITION");
    }

    private static decimal GetAverageStat(AdaptiveScenarioState state, Func<CharacterStatProfileV2, int> selector)
    {
        if (state.CharacterSnapshots.Count == 0)
        {
            return 50m;
        }

        return (decimal)state.CharacterSnapshots.Average(x => selector(x));
    }

    private static int GetPhaseOrder(NarrativePhase phase)
        => phase switch
        {
            NarrativePhase.BuildUp => 0,
            NarrativePhase.Committed => 1,
            NarrativePhase.Approaching => 2,
            NarrativePhase.Climax => 3,
            NarrativePhase.Reset => 4,
            _ => 0
        };

    private static bool EvaluateConfiguredGate(
        NarrativeGateProfile? profile,
        string fromPhase,
        string toPhase,
        IReadOnlyDictionary<string, decimal> metricValues)
    {
        var rules = profile?.Rules
            .Where(rule => string.Equals(rule.FromPhase, fromPhase, StringComparison.OrdinalIgnoreCase)
                && string.Equals(rule.ToPhase, toPhase, StringComparison.OrdinalIgnoreCase))
            .OrderBy(rule => rule.SortOrder)
            .ToList();

        if (rules is null || rules.Count == 0)
        {
            return false;
        }

        foreach (var rule in rules)
        {
            if (!metricValues.TryGetValue(rule.MetricKey, out var actualValue))
            {
                return false;
            }

            if (!Compare(actualValue, rule.Comparator, rule.Threshold))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasConfiguredGateRules(NarrativeGateProfile? profile, string fromPhase, string toPhase)
    {
        if (profile is null || profile.Rules.Count == 0)
        {
            return false;
        }

        return profile.Rules.Any(rule => string.Equals(rule.FromPhase, fromPhase, StringComparison.OrdinalIgnoreCase)
            && string.Equals(rule.ToPhase, toPhase, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Compare(decimal actual, string comparator, decimal threshold)
    {
        return comparator.Trim() switch
        {
            NarrativeGateComparators.GreaterThanOrEqual => actual >= threshold,
            NarrativeGateComparators.GreaterThan => actual > threshold,
            NarrativeGateComparators.LessThanOrEqual => actual <= threshold,
            NarrativeGateComparators.LessThan => actual < threshold,
            NarrativeGateComparators.Equal => actual == threshold,
            _ => actual >= threshold
        };
    }
}
