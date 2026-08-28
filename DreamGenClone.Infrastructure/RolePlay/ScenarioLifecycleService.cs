using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ScenarioLifecycleService : IScenarioLifecycleService
{
    private static readonly decimal[] DefaultResetDesirePullSchedule =
    [
        0.3333m,
        0.2500m,
        0.2000m,
        0.1500m,
        0.1000m
    ];

    private readonly ILogger<ScenarioLifecycleService> _logger;
    private readonly INarrativeGateProfileService? _gateProfileService;
    private readonly IReadOnlyDictionary<string, int> _resetStatBaselines;
    private readonly IReadOnlyList<decimal> _resetStatPullSchedule;
    private readonly decimal _resetDecayReductionPerCycle;
    private readonly decimal _resetDecayReductionCap;

    public ScenarioLifecycleService(
        ILogger<ScenarioLifecycleService> logger,
        INarrativeGateProfileService? gateProfileService = null,
        IOptions<StoryAnalysisOptions>? storyAnalysisOptions = null)
    {
        _logger = logger;
        _gateProfileService = gateProfileService;
        var configuredBaselines = storyAnalysisOptions?.Value.ResetStatBaselines;
        var baselines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Desire"] = 50,
            ["Restraint"] = 50,
            ["Tension"] = 50,
            ["Connection"] = 50,
            ["Dominance"] = 50,
            ["Loyalty"] = 50,
            ["SelfRespect"] = 50
        };

        if (configuredBaselines is { Count: > 0 })
        {
            foreach (var (key, value) in configuredBaselines)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                baselines[key.Trim()] = Math.Clamp(value, 0, 100);
            }
        }
        else
        {
            // Backward compatibility for pre-all-stats configs.
            baselines["Desire"] = Math.Clamp(storyAnalysisOptions?.Value.ResetDesireBaseline ?? 50, 0, 100);
        }

        _resetStatBaselines = baselines;

        var configuredPullSchedule = storyAnalysisOptions?.Value.ResetStatBaselinePullSchedule
            ?.Select(x => (decimal)Math.Clamp(x, 0d, 1d))
            .Where(x => x > 0m)
            .ToArray();

        if (configuredPullSchedule is not { Length: > 0 })
        {
            configuredPullSchedule = storyAnalysisOptions?.Value.ResetDesireBaselinePullSchedule
                ?.Select(x => (decimal)Math.Clamp(x, 0d, 1d))
                .Where(x => x > 0m)
                .ToArray();
        }

        _resetStatPullSchedule = configuredPullSchedule is { Length: > 0 }
            ? configuredPullSchedule
            : DefaultResetDesirePullSchedule;

        _resetDecayReductionPerCycle = (decimal)Math.Clamp(storyAnalysisOptions?.Value.ResetDecayReductionPerCycle ?? 0d, 0d, 1d);
        _resetDecayReductionCap = (decimal)Math.Clamp(storyAnalysisOptions?.Value.ResetDecayReductionCap ?? 0d, 0d, 1d);
    }

    public async Task<PhaseTransitionResult> EvaluateTransitionAsync(
        AdaptiveScenarioState state,
        LifecycleInputs inputs,
        CancellationToken cancellationToken = default)
    {
        NarrativeGateProfile? profile = null;
        if (inputs.NarrativeGateRules is { Count: > 0 })
        {
            profile = new NarrativeGateProfile
            {
                Id = inputs.NarrativeGateProfileId ?? "theme-local",
                Name = "Theme Local Rules",
                Rules = inputs.NarrativeGateRules
                    .Select((rule, index) => new NarrativeGateRule
                    {
                        SortOrder = index + 1,
                        FromPhase = rule.FromPhase,
                        ToPhase = rule.ToPhase,
                        MetricKey = rule.MetricKey,
                        Comparator = rule.Comparator,
                        Threshold = rule.Threshold
                    })
                    .ToList()
            };
        }

        if (profile is null && _gateProfileService is not null)
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
                inputs.TurnsSinceCommitment,
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
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? perCharacterBaselineOverrides = null,
        IReadOnlyDictionary<string, decimal>? statDecayScaleOverrides = null,
        CancellationToken cancellationToken = default)
    {
        var nextCycleIndex = state.CycleIndex + 1;
        var statPull = ResolveResetBaselinePull(nextCycleIndex, _resetStatPullSchedule);

        // Scale the pull down based on how many cycles have already been completed.
        // Each completed cycle reduces the pull by _resetDecayReductionPerCycle (up to _resetDecayReductionCap),
        // so characters retain progressively more of their earned stat gains in later arcs.
        if (_resetDecayReductionPerCycle > 0m && _resetDecayReductionCap > 0m && state.CycleIndex > 0)
        {
            var reductionFraction = Math.Min(_resetDecayReductionCap, state.CycleIndex * _resetDecayReductionPerCycle);
            statPull = statPull * (1m - reductionFraction);
        }

        var resetState = new AdaptiveScenarioState
        {
            SessionId = state.SessionId,
            // Clear ActiveScenarioId on Reset so the next BuildUp enters observer mode and
            // performs a fresh theme/scenario selection (driven by configured completion penalty,
            // cooldown, and successor causality boosts). Preserving the just-completed scenario
            // here defeats observer re-entry and causes the same scenario to be re-used.
            ActiveScenarioId = null,
            CurrentPhase = NarrativePhase.Reset,
            TurnCountInPhase = 0,
            ConsecutiveLeadCount = 0,
            LastEvaluationUtc = DateTime.UtcNow,
            CycleIndex = nextCycleIndex,
            ActiveFormulaVersion = state.ActiveFormulaVersion,
            CharacterSnapshots = state.CharacterSnapshots
                .Select(snapshot =>
                {
                    var characterBaselines = perCharacterBaselineOverrides is not null
                        && !string.IsNullOrWhiteSpace(snapshot.CharacterId)
                        && perCharacterBaselineOverrides.TryGetValue(snapshot.CharacterId, out var overrides)
                        ? overrides
                        : null;
                    return ApplySemiResetDecay(
                        snapshot,
                        _resetStatBaselines,
                        statPull,
                        characterBaselines,
                        statDecayScaleOverrides);
                })
                .ToList()
        };

        // Per-character before/after snapshot for algorithm validation. Pairs each input
        // snapshot with its post-reset counterpart (matched by CharacterId, falling back to
        // ordinal position) and records the per-stat baseline used plus the resulting delta.
        var beforeAfter = state.CharacterSnapshots
            .Select((before, index) =>
            {
                var after = resetState.CharacterSnapshots
                    .FirstOrDefault(x => string.Equals(x.CharacterId, before.CharacterId, StringComparison.OrdinalIgnoreCase))
                    ?? (index < resetState.CharacterSnapshots.Count ? resetState.CharacterSnapshots[index] : null);

                IReadOnlyDictionary<string, int>? characterBaselineOverride = null;
                if (perCharacterBaselineOverrides is not null
                    && !string.IsNullOrWhiteSpace(before.CharacterId)
                    && perCharacterBaselineOverrides.TryGetValue(before.CharacterId, out var ov))
                {
                    characterBaselineOverride = ov;
                }

                int Baseline(string statName)
                {
                    if (characterBaselineOverride is not null
                        && characterBaselineOverride.TryGetValue(statName, out var perCharacter))
                    {
                        return Math.Clamp(perCharacter, 0, 100);
                    }
                    return _resetStatBaselines.TryGetValue(statName, out var configured)
                        ? Math.Clamp(configured, 0, 100)
                        : 50;
                }

                decimal PullFor(string statName)
                {
                    if (statDecayScaleOverrides is not null
                        && statDecayScaleOverrides.TryGetValue(statName, out var scale))
                    {
                        return Math.Clamp(scale, 0m, 1m);
                    }
                    return statPull;
                }

                object StatRow(string name, int beforeVal, int afterVal) => new
                {
                    stat = name,
                    before = beforeVal,
                    baseline = Baseline(name),
                    pull = PullFor(name),
                    after = afterVal,
                    delta = afterVal - beforeVal
                };

                return new
                {
                    characterId = before.CharacterId,
                    stats = after is null
                        ? Array.Empty<object>()
                        : new[]
                        {
                            StatRow("Desire", before.Desire, after.Desire),
                            StatRow("Restraint", before.Restraint, after.Restraint),
                            StatRow("Tension", before.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50, after.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50),
                            StatRow("Connection", before.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50, after.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50),
                            StatRow("Dominance", before.Dominance, after.Dominance),
                            StatRow("Loyalty", before.Loyalty, after.Loyalty),
                            StatRow("SelfRespect", before.SelfRespect, after.SelfRespect)
                        }
                };
            })
            .ToList();

        _logger.LogInformation(
            "RolePlayV2 reset executed: SessionId={SessionId} NewCycleIndex={CycleIndex} Reason={Reason} StatPull={StatPull} Baselines={Baselines} CharacterStats={CharacterStats}",
            state.SessionId,
            resetState.CycleIndex,
            reason,
            statPull,
            JsonSerializer.Serialize(_resetStatBaselines),
            JsonSerializer.Serialize(beforeAfter));

        return Task.FromResult(resetState);
    }

    private static CharacterStatProfileV2 ApplySemiResetDecay(
        CharacterStatProfileV2 snapshot,
        IReadOnlyDictionary<string, int> baselines,
        decimal statPull,
        IReadOnlyDictionary<string, int>? characterBaselineOverride = null,
        IReadOnlyDictionary<string, decimal>? statDecayScaleOverrides = null)
    {
        int ResolveBaseline(string statName)
        {
            // Per-character baseline (from session start or last Apply) takes priority.
            if (characterBaselineOverride is not null
                && characterBaselineOverride.TryGetValue(statName, out var perCharacter))
            {
                return Math.Clamp(perCharacter, 0, 100);
            }

            return baselines.TryGetValue(statName, out var configured)
                ? Math.Clamp(configured, 0, 100)
                : 50;
        }

        decimal EffectivePull(string statName)
        {
            if (statDecayScaleOverrides is not null
                && statDecayScaleOverrides.TryGetValue(statName, out var scale))
            {
                return Math.Clamp(scale, 0m, 1m);
            }
            return statPull;
        }

        return new CharacterStatProfileV2
        {
            CharacterId = snapshot.CharacterId,
            // Preserve the narrative role across the cycle boundary. The gate's average-stat
            // computation only includes characters with a non-empty CharacterRole, so dropping
            // it here would silently zero out gate metrics (e.g. Avg Desire) on the next arc.
            CharacterRole = snapshot.CharacterRole,
            Desire = MoveTowardBaseline(snapshot.Desire, ResolveBaseline("Desire"), EffectivePull("Desire")),
            Restraint = MoveTowardBaseline(snapshot.Restraint, ResolveBaseline("Restraint"), EffectivePull("Restraint")),
            Dominance = MoveTowardBaseline(snapshot.Dominance, ResolveBaseline("Dominance"), EffectivePull("Dominance")),
            Loyalty = MoveTowardBaseline(snapshot.Loyalty, ResolveBaseline("Loyalty"), EffectivePull("Loyalty")),
            SelfRespect = MoveTowardBaseline(snapshot.SelfRespect, ResolveBaseline("SelfRespect"), EffectivePull("SelfRespect")),
            RuntimeEncounterStats = snapshot.RuntimeEncounterStats is null ? null
                : snapshot.RuntimeEncounterStats
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => MoveTowardBaseline(kvp.Value, ResolveBaseline(kvp.Key), EffectivePull(kvp.Key)),
                        StringComparer.OrdinalIgnoreCase),
            SnapshotUtc = DateTime.UtcNow
        };
    }

    private static decimal ResolveResetBaselinePull(int cycleIndex, IReadOnlyList<decimal> pullSchedule)
    {
        if (pullSchedule.Count == 0)
        {
            return 0m;
        }

        var scheduleIndex = Math.Max(0, cycleIndex - 1);
        if (scheduleIndex >= pullSchedule.Count)
        {
            scheduleIndex = pullSchedule.Count - 1;
        }

        return pullSchedule[scheduleIndex];
    }

    private static int MoveTowardBaseline(int value, int baseline, decimal pull)
    {
        var clamped = Math.Clamp(value, 0, 100);
        var target = Math.Clamp(baseline, 0, 100);
        var normalizedPull = Math.Clamp(pull, 0m, 1m);

        if (clamped == target || normalizedPull <= 0m)
        {
            return clamped;
        }

        var delta = target - clamped;
        var adjustment = (int)Math.Round(delta * (double)normalizedPull, MidpointRounding.AwayFromZero);
        if (adjustment == 0)
        {
            adjustment = delta > 0 ? 1 : -1;
        }

        return Math.Clamp(clamped + adjustment, 0, 100);
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
            [NarrativeGateMetricKeys.TurnsSinceCommitment] = inputs.TurnsSinceCommitment
        };

        if (state.CurrentPhase == NarrativePhase.Committed && HasConfiguredGateRules(profile, "Committed", "Approaching"))
        {
            return EvaluateConfiguredGate(profile, "Committed", "Approaching", metricValues)
                ? (true, NarrativePhase.Approaching, TransitionTriggerType.TurnCountGate, "COMMITTED_TO_APPROACHING")
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
            // /endclimax is an unconditional explicit command — it always exits Climax regardless
            // of whether a configured gate exists or whether its threshold has been reached.
            if (inputs.ClimaxCompletionRequested)
            {
                return (true, NarrativePhase.Reset, TransitionTriggerType.Override, "CLIMAX_TO_RESET");
            }

            // Automatic exit — requires configured gate rules. No hidden fallback threshold.
            if (HasConfiguredGateRules(profile, "Climax", "Reset"))
            {
                return EvaluateConfiguredGate(profile, "Climax", "Reset", metricValues)
                    ? (true, NarrativePhase.Reset, TransitionTriggerType.Threshold, "CLIMAX_TO_RESET")
                    : (false, state.CurrentPhase, TransitionTriggerType.Threshold, "NO_TRANSITION");
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

            return (false, state.CurrentPhase, TransitionTriggerType.Threshold, "NO_TRANSITION");
        }

        return (false, state.CurrentPhase, TransitionTriggerType.Threshold, "NO_TRANSITION");
    }

    private static decimal GetAverageStat(AdaptiveScenarioState state, Func<CharacterStatProfileV2, int> selector)
    {
        // Only scenario-bound characters (those with a CharacterRole) contribute to averages.
        // Incidental characters mentioned in narrative have no role and must not affect gating.
        var tracked = state.CharacterSnapshots
            .Where(x => !string.IsNullOrEmpty(x.CharacterRole))
            .ToList();

        if (tracked.Count == 0)
        {
            return 50m;
        }

        return (decimal)tracked.Average(x => selector(x));
    }

    private static int GetPhaseOrder(NarrativePhase phase)
        => phase switch
        {
            NarrativePhase.Opening => -1,
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
