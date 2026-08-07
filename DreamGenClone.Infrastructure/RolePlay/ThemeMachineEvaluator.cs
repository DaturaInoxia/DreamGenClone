using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ThemeMachineEvaluator : IThemeMachineEvaluator
{
    private readonly ILogger<ThemeMachineEvaluator> _logger;

    public ThemeMachineEvaluator(ILogger<ThemeMachineEvaluator> logger)
    {
        _logger = logger;
    }

    public Task<ThemeMachineEvaluationResult> EvaluateAsync(
        AdaptiveScenarioState adaptiveState,
        ThemeMachineEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adaptiveState);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.SessionId))
        {
            throw new InvalidOperationException("SessionId is required for theme machine evaluation.");
        }

        if (string.IsNullOrWhiteSpace(context.ThemeId))
        {
            throw new InvalidOperationException("ThemeId is required for theme machine evaluation.");
        }

        if (string.IsNullOrWhiteSpace(context.Snapshot.CurrentStateCode))
        {
            throw new InvalidOperationException("CurrentStateCode is required for theme machine evaluation.");
        }

        try
        {
            var now = DateTime.UtcNow;
            var snapshot = CloneSnapshot(context.Snapshot);
            var diagnostics = new List<ThemeMachineDiagnosticEvent>();

            var transitions = context.Transitions
                .Where(x => x.IsEnabled)
                .Where(x => string.Equals(x.FromStateCode, snapshot.CurrentStateCode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.TransitionId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            RPThemeMachineTransition? selectedTransition = null;
            string? blockedReasonCode = null;

            foreach (var transition in transitions)
            {
                var gateResult = EvaluateGate(transition, snapshot, context.SessionId);
                if (gateResult.Eligible)
                {
                    selectedTransition = transition;
                    break;
                }

                if (!string.IsNullOrWhiteSpace(gateResult.BlockReasonCode))
                {
                    blockedReasonCode = gateResult.BlockReasonCode;
                }
            }

            if (selectedTransition is not null)
            {
                var fromState = snapshot.CurrentStateCode;
                snapshot.CurrentStateCode = selectedTransition.ToStateCode;
                snapshot.TurnsInCurrentState = 0;
                if (string.Equals(selectedTransition.ToStateCode, "EncounterInProgress", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(selectedTransition.ToStateCode, "ReturnBeatRequired", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.ReturnBeatCompleted = false;
                }

                snapshot.LastTransitionId = selectedTransition.TransitionId;
                snapshot.LastTransitionUtc = now;
                snapshot.LastTransitionReasonCode = "ThemeMachineTransitionApplied";
                snapshot.LastEvaluatedUtc = now;

                diagnostics.Add(new ThemeMachineDiagnosticEvent
                {
                    SessionId = context.SessionId,
                    ThemeId = context.ThemeId,
                    MachineKey = snapshot.MachineKey,
                    DefinitionVersion = snapshot.DefinitionVersion,
                    EventType = "transition",
                    FromStateCode = fromState,
                    ToStateCode = selectedTransition.ToStateCode,
                    TransitionId = selectedTransition.TransitionId,
                    ReasonCode = "ThemeMachineTransitionApplied",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        triggerType = selectedTransition.TriggerType,
                        priority = selectedTransition.Priority,
                        machineKey = snapshot.MachineKey,
                        definitionId = snapshot.DefinitionId
                    }),
                    OccurredUtc = now
                });

                _logger.LogInformation(
                    "Theme machine transition applied: SessionId={SessionId} MachineKey={MachineKey} FromState={FromState} ToState={ToState} TransitionId={TransitionId} Priority={Priority}",
                    context.SessionId,
                    snapshot.MachineKey,
                    fromState,
                    selectedTransition.ToStateCode,
                    selectedTransition.TransitionId,
                    selectedTransition.Priority);

                return Task.FromResult(new ThemeMachineEvaluationResult
                {
                    UpdatedSnapshot = snapshot,
                    Directive = BuildDirective(context.SessionId, snapshot, ["ThemeMachineTransitionApplied"]),
                    Diagnostics = diagnostics,
                    TransitionApplied = true,
                    AppliedTransitionId = selectedTransition.TransitionId
                });
            }

            snapshot.TurnsInCurrentState = Math.Max(0, snapshot.TurnsInCurrentState) + 1;
            snapshot.LastEvaluatedUtc = now;

            var reasonCodes = new List<string>();
            if (!string.IsNullOrWhiteSpace(blockedReasonCode))
            {
                reasonCodes.Add(blockedReasonCode);
                diagnostics.Add(new ThemeMachineDiagnosticEvent
                {
                    SessionId = context.SessionId,
                    ThemeId = context.ThemeId,
                    MachineKey = snapshot.MachineKey,
                    DefinitionVersion = snapshot.DefinitionVersion,
                    EventType = "blocked",
                    FromStateCode = snapshot.CurrentStateCode,
                    ToStateCode = snapshot.CurrentStateCode,
                    TransitionId = null,
                    ReasonCode = blockedReasonCode,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        state = snapshot.CurrentStateCode,
                        machineKey = snapshot.MachineKey,
                        definitionId = snapshot.DefinitionId
                    }),
                    OccurredUtc = now
                });

                _logger.LogWarning(
                    "Theme machine transition blocked: SessionId={SessionId} MachineKey={MachineKey} State={State} ReasonCode={ReasonCode}",
                    context.SessionId,
                    snapshot.MachineKey,
                    snapshot.CurrentStateCode,
                    blockedReasonCode);
            }

            return Task.FromResult(new ThemeMachineEvaluationResult
            {
                UpdatedSnapshot = snapshot,
                Directive = BuildDirective(context.SessionId, snapshot, reasonCodes),
                Diagnostics = diagnostics,
                TransitionApplied = false,
                AppliedTransitionId = null
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(
                ex,
                "Theme machine evaluation failed: SessionId={SessionId} ThemeId={ThemeId} State={State}",
                context.SessionId,
                context.ThemeId,
                context.Snapshot.CurrentStateCode);
            throw;
        }
    }

    private static ThemeMachineSessionSnapshot CloneSnapshot(ThemeMachineSessionSnapshot source)
        => new()
        {
            MachineKey = source.MachineKey,
            ThemeId = source.ThemeId,
            DefinitionId = source.DefinitionId,
            DefinitionVersion = source.DefinitionVersion,
            CurrentStateCode = source.CurrentStateCode,
            TurnsInCurrentState = source.TurnsInCurrentState,
            ReturnBeatCompleted = source.ReturnBeatCompleted,
            LastTransitionId = source.LastTransitionId,
            LastTransitionUtc = source.LastTransitionUtc,
            LastTransitionReasonCode = source.LastTransitionReasonCode,
            LastEvaluatedUtc = source.LastEvaluatedUtc
        };

    private static ThemeMachineDirective BuildDirective(
        string sessionId,
        ThemeMachineSessionSnapshot snapshot,
        IReadOnlyList<string> reasonCodes)
    {
        var requiredNarrativeBeats = new List<string>();
        var promptConstraints = new List<string>();
        var blockDisappearanceCandidates = false;

        if (string.Equals(snapshot.CurrentStateCode, "ReturnBeatRequired", StringComparison.OrdinalIgnoreCase))
        {
            blockDisappearanceCandidates = true;
            requiredNarrativeBeats.Add("ReturnBeatRequired");
            promptConstraints.Add("Do not introduce a new disappearance beat until the return beat is completed.");
        }
        else if (string.Equals(snapshot.CurrentStateCode, "ReintegrationCooldown", StringComparison.OrdinalIgnoreCase))
        {
            blockDisappearanceCandidates = true;
            requiredNarrativeBeats.Add("ReintegrationCooldown");
            promptConstraints.Add("Maintain reintegration continuity until cooldown eligibility gates pass.");
        }

        var normalizedReasons = reasonCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ThemeMachineDirective
        {
            SessionId = sessionId,
            CurrentStateCode = snapshot.CurrentStateCode,
            BlockDisappearanceCandidates = blockDisappearanceCandidates,
            RequiredNarrativeBeats = requiredNarrativeBeats,
            PromptHardConstraints = promptConstraints,
            ReasonCodes = normalizedReasons
        };
    }

    private static GateEvaluationResult EvaluateGate(
        RPThemeMachineTransition transition,
        ThemeMachineSessionSnapshot snapshot,
        string sessionId)
    {
        var triggerType = (transition.TriggerType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(triggerType))
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': transition '{transition.TransitionId}' is missing TriggerType.");
        }

        if (string.Equals(triggerType, "always", StringComparison.OrdinalIgnoreCase))
        {
            return GateEvaluationResult.EligibleResult;
        }

        if (!string.Equals(triggerType, "cooldown-eligibility", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': transition '{transition.TransitionId}' has unsupported TriggerType '{transition.TriggerType}'.");
        }

        if (string.IsNullOrWhiteSpace(transition.GateConfigJson))
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': transition '{transition.TransitionId}' is missing required GateConfigJson.");
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(transition.GateConfigJson);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': transition '{transition.TransitionId}' has invalid GateConfigJson.",
                ex);
        }

        // Prefer the canonical post-migration turn threshold (minimumTurns). Legacy rows that
        // predate the B-044 interaction→turn migration store minimumInteractions; accept those
        // ÷3 ceiling (the one permitted runtime interaction→turn conversion, legacy-read path
        // only — spec 001-replace-interactions-turns R5/T026).
        int minimumTurns = -1;
        var hasCanonicalTurns = root.TryGetProperty("minimumTurns", out var minimumTurnsProperty)
            && minimumTurnsProperty.ValueKind == JsonValueKind.Number
            && minimumTurnsProperty.TryGetInt32(out minimumTurns)
            && minimumTurns >= 0;
        int legacyInteractions = -1;
        var hasLegacyInteractions = root.TryGetProperty("minimumInteractions", out var legacyInteractionsProperty)
            && legacyInteractionsProperty.ValueKind == JsonValueKind.Number
            && legacyInteractionsProperty.TryGetInt32(out legacyInteractions)
            && legacyInteractions >= 0;

        if (!hasCanonicalTurns && !hasLegacyInteractions)
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': cooldown transition '{transition.TransitionId}' is missing required integer minimumTurns >= 0.");
        }

        if (!hasCanonicalTurns)
        {
            minimumTurns = Math.Max(0, (legacyInteractions + 2) / 3);
        }

        if (!root.TryGetProperty("requireReturnBeatCompleted", out var requireReturnBeatCompletedProperty)
            || (requireReturnBeatCompletedProperty.ValueKind != JsonValueKind.True
                && requireReturnBeatCompletedProperty.ValueKind != JsonValueKind.False))
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': cooldown transition '{transition.TransitionId}' is missing required boolean requireReturnBeatCompleted.");
        }

        var requireReturnBeatCompleted = requireReturnBeatCompletedProperty.GetBoolean();
        if (requireReturnBeatCompleted)
        {
            _ = ResolveConfiguredReturnBeatCompletionSignals(root, transition.TransitionId, sessionId);
        }

        var turnsGatePassed = snapshot.TurnsInCurrentState >= minimumTurns;
        var returnBeatGatePassed = !requireReturnBeatCompleted || snapshot.ReturnBeatCompleted;
        if (turnsGatePassed && returnBeatGatePassed)
        {
            return GateEvaluationResult.EligibleResult;
        }

        return new GateEvaluationResult(false, transition.BlockReasonCode);
    }

    private static IReadOnlyList<string> ResolveConfiguredReturnBeatCompletionSignals(
        JsonElement gateConfig,
        string transitionId,
        string sessionId)
    {
        if (!gateConfig.TryGetProperty("returnBeatCompletionSignals", out var signalsProperty)
            || signalsProperty.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': cooldown transition '{transitionId}' is missing required string array returnBeatCompletionSignals.");
        }

        var signals = new List<string>();
        foreach (var element in signalsProperty.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    $"Theme machine evaluation failed for session '{sessionId}': cooldown transition '{transitionId}' has non-string returnBeatCompletionSignals entries.");
            }

            var signal = element.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(signal))
            {
                throw new InvalidOperationException(
                    $"Theme machine evaluation failed for session '{sessionId}': cooldown transition '{transitionId}' has blank returnBeatCompletionSignals entries.");
            }

            signals.Add(signal);
        }

        if (signals.Count == 0)
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': cooldown transition '{transitionId}' requires at least one returnBeatCompletionSignals entry.");
        }

        var transgressorRoleName = ResolveRequiredReturnBeatRoleName(
            gateConfig,
            transitionId,
            sessionId,
            "returnBeatTransgressorRole");
        var partnerRoleName = ResolveRequiredReturnBeatRoleName(
            gateConfig,
            transitionId,
            sessionId,
            "returnBeatPartnerRole");

        if (string.Equals(transgressorRoleName, partnerRoleName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': cooldown transition '{transitionId}' must configure distinct returnBeatTransgressorRole and returnBeatPartnerRole values.");
        }

        return signals
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveRequiredReturnBeatRoleName(
        JsonElement gateConfig,
        string transitionId,
        string sessionId,
        string propertyName)
    {
        if (!gateConfig.TryGetProperty(propertyName, out var roleProperty)
            || roleProperty.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': cooldown transition '{transitionId}' is missing required string {propertyName}.");
        }

        var rawRoleName = roleProperty.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(rawRoleName))
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': cooldown transition '{transitionId}' has blank {propertyName}.");
        }

        var normalizedRoleName = CharacterRoleCatalog.Normalize(rawRoleName);
        if (string.Equals(normalizedRoleName, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{sessionId}': cooldown transition '{transitionId}' has invalid {propertyName}='{rawRoleName}'.");
        }

        return normalizedRoleName;
    }

    private readonly record struct GateEvaluationResult(bool Eligible, string? BlockReasonCode)
    {
        public static GateEvaluationResult EligibleResult { get; } = new(true, null);
    }
}
