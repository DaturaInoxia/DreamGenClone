using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class DecisionPointService : IDecisionPointService
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, int>> OptionDeltas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lean-in"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 6, ["Tension"] = 4, ["Restraint"] = -3 },
        ["hold-back"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Restraint"] = 5, ["Tension"] = -2, ["SelfRespect"] = 2 },
        ["seek-connection"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Connection"] = 5, ["Loyalty"] = 3 },
        ["test-boundary"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 5, ["Restraint"] = -2, ["Tension"] = 3 },
        ["redirect"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Restraint"] = 4, ["Connection"] = 2 },
        ["observe"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 1, ["Restraint"] = 2 }
    };

    private static readonly string[] DefaultOptionOrder = ["lean-in", "hold-back", "seek-connection"];
    private static readonly string[] HighDesireOptionOrder = ["lean-in", "test-boundary", "seek-connection"];
    private static readonly string[] HighRestraintOptionOrder = ["hold-back", "redirect", "observe"];

    private readonly ILogger<DecisionPointService> _logger;

    public DecisionPointService(ILogger<DecisionPointService> logger)
    {
        _logger = logger;
    }

    public Task<DecisionPoint?> TryCreateDecisionPointAsync(
        AdaptiveScenarioState state,
        DecisionTrigger trigger,
        DecisionGenerationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var shouldCreate = trigger == DecisionTrigger.PhaseChanged
            || trigger == DecisionTrigger.SignificantStatChange
            || (trigger == DecisionTrigger.InteractionStart && state.InteractionCountInPhase > 0 && state.InteractionCountInPhase % 3 == 0)
            || trigger == DecisionTrigger.ManualOverride;

        if (!shouldCreate || string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            return Task.FromResult<DecisionPoint?>(null);
        }

        var decisionPoint = new DecisionPoint
        {
            DecisionPointId = Guid.NewGuid().ToString("N"),
            SessionId = state.SessionId,
            ScenarioId = state.ActiveScenarioId,
            Phase = state.CurrentPhase,
            TriggerSource = trigger.ToString(),
            ContextSummary = context?.PromptSnippet ?? string.Empty,
            AskingActorName = context?.AskingActorName ?? string.Empty,
            TargetActorId = context?.TargetActorId ?? string.Empty,
            TransparencyMode = ResolveTransparencyMode(state, trigger),
            OptionIds = BuildContextualOptions(state, context),
            CreatedUtc = DateTime.UtcNow
        };

        return Task.FromResult<DecisionPoint?>(decisionPoint);
    }

    public Task<DecisionOutcome> ApplyDecisionAsync(
        AdaptiveScenarioState state,
        DecisionSubmission submission,
        string? targetActorId = null,
        CancellationToken cancellationToken = default)
    {
        var optionId = submission.OptionId;
        var deltas = OptionDeltas.TryGetValue(optionId, out var presetDeltas)
            ? new Dictionary<string, int>(presetDeltas, StringComparer.OrdinalIgnoreCase)
            : ParseCustomDeltas(submission.CustomResponseText);

        if (deltas.Count == 0)
        {
            deltas["Connection"] = 1;
        }

        var resolvedTargetActorId = ResolveTargetActorId(state, submission, targetActorId);
        ApplyDeltasToTarget(state, deltas, resolvedTargetActorId);

        var resolvedTransparency = ResolveTransparencyMode(state, ParseTrigger(submission));

        var auditPayload = JsonSerializer.Serialize(new
        {
            submission.ActorName,
            resolvedTargetActorId,
            submission.DecisionPointId,
            optionId,
            appliedAtUtc = DateTime.UtcNow,
            deltas
        });

        _logger.LogInformation(
            RolePlayV2LogEvents.DecisionOutcomeApplied,
            state.SessionId,
            submission.DecisionPointId,
            optionId,
            resolvedTransparency);

        return Task.FromResult(new DecisionOutcome
        {
            Applied = true,
            DecisionPointId = submission.DecisionPointId,
            OptionId = optionId,
            TargetActorId = resolvedTargetActorId,
            AppliedStatDeltas = deltas,
            AuditMetadataJson = auditPayload,
            TransparencyMode = resolvedTransparency,
            Summary = "Decision applied and canonical stat deltas persisted."
        });
    }

    private static List<string> BuildContextualOptions(AdaptiveScenarioState state, DecisionGenerationContext? context)
    {
        var focusActor = ResolveFocusActor(state, context);
        var desiredSet = SelectOptionSet(focusActor, state.CurrentPhase, context?.PromptSnippet);

        var options = desiredSet
            .Where(OptionDeltas.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        if (options.Count == 0)
        {
            options = [.. DefaultOptionOrder];
        }

        if (!options.Contains("custom", StringComparer.OrdinalIgnoreCase))
        {
            options.Add("custom");
        }

        return options;
    }

    private static IEnumerable<string> SelectOptionSet(CharacterStatProfileV2? focusActor, NarrativePhase phase, string? promptSnippet)
    {
        var desire = focusActor?.Desire ?? 50;
        var restraint = focusActor?.Restraint ?? 50;
        var tension = focusActor?.Tension ?? 50;

        if (phase == NarrativePhase.BuildUp && restraint >= 65)
        {
            return HighRestraintOptionOrder;
        }

        if (phase == NarrativePhase.Approaching || phase == NarrativePhase.Climax)
        {
            return HighDesireOptionOrder;
        }

        if (desire >= 65 || tension >= 70)
        {
            return HighDesireOptionOrder;
        }

        if (!string.IsNullOrWhiteSpace(promptSnippet)
            && promptSnippet.Contains("risk", StringComparison.OrdinalIgnoreCase))
        {
            return ["observe", "hold-back", "seek-connection"];
        }

        return DefaultOptionOrder;
    }

    private static CharacterStatProfileV2? ResolveFocusActor(AdaptiveScenarioState state, DecisionGenerationContext? context)
    {
        if (!string.IsNullOrWhiteSpace(context?.TargetActorId))
        {
            return state.CharacterSnapshots.FirstOrDefault(x =>
                string.Equals(x.CharacterId, context.TargetActorId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(context?.AskingActorName))
        {
            return state.CharacterSnapshots.FirstOrDefault(x =>
                string.Equals(x.CharacterId, context.AskingActorName, StringComparison.OrdinalIgnoreCase));
        }

        return state.CharacterSnapshots.FirstOrDefault();
    }

    private static void ApplyDeltasToTarget(
        AdaptiveScenarioState state,
        IReadOnlyDictionary<string, int> deltas,
        string? targetActorId)
    {
        if (string.IsNullOrWhiteSpace(targetActorId))
        {
            foreach (var snapshot in state.CharacterSnapshots)
            {
                ApplyDeltas(snapshot, deltas);
            }

            return;
        }

        var target = state.CharacterSnapshots.FirstOrDefault(x =>
            string.Equals(x.CharacterId, targetActorId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            return;
        }

        ApplyDeltas(target, deltas);
    }

    private static string? ResolveTargetActorId(AdaptiveScenarioState state, DecisionSubmission submission, string? fallbackTargetActorId)
    {
        if (!string.IsNullOrWhiteSpace(submission.TargetActorId))
        {
            return submission.TargetActorId;
        }

        if (!string.IsNullOrWhiteSpace(fallbackTargetActorId))
        {
            return fallbackTargetActorId;
        }

        if (!string.IsNullOrWhiteSpace(submission.ActorName))
        {
            var actorByName = state.CharacterSnapshots.FirstOrDefault(x =>
                string.Equals(x.CharacterId, submission.ActorName, StringComparison.OrdinalIgnoreCase));
            if (actorByName is not null)
            {
                return actorByName.CharacterId;
            }
        }

        return state.CharacterSnapshots.Count == 1
            ? state.CharacterSnapshots[0].CharacterId
            : null;
    }

    private static DecisionTrigger ParseTrigger(DecisionSubmission submission)
    {
        return submission.OptionId switch
        {
            "custom" => DecisionTrigger.ManualOverride,
            _ => DecisionTrigger.SignificantStatChange
        };
    }

    private static TransparencyMode ResolveTransparencyMode(AdaptiveScenarioState state, DecisionTrigger trigger)
    {
        if (trigger == DecisionTrigger.ManualOverride)
        {
            return TransparencyMode.Explicit;
        }

        return state.CurrentPhase switch
        {
            NarrativePhase.BuildUp => TransparencyMode.Hidden,
            NarrativePhase.Committed => TransparencyMode.Directional,
            NarrativePhase.Approaching => TransparencyMode.Directional,
            NarrativePhase.Climax => TransparencyMode.Explicit,
            _ => TransparencyMode.Directional
        };
    }

    private static Dictionary<string, int> ParseCustomDeltas(string? customResponseText)
    {
        var parsed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(customResponseText))
        {
            return parsed;
        }

        foreach (var segment in customResponseText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            if (int.TryParse(parts[1], out var delta))
            {
                parsed[parts[0]] = delta;
            }
        }

        return parsed;
    }

    private static void ApplyDeltas(CharacterStatProfileV2 profile, IReadOnlyDictionary<string, int> deltas)
    {
        foreach (var (stat, delta) in deltas)
        {
            switch (stat)
            {
                case "Desire":
                    profile.Desire = Math.Clamp(profile.Desire + delta, 0, 100);
                    break;
                case "Restraint":
                    profile.Restraint = Math.Clamp(profile.Restraint + delta, 0, 100);
                    break;
                case "Tension":
                    profile.Tension = Math.Clamp(profile.Tension + delta, 0, 100);
                    break;
                case "Connection":
                    profile.Connection = Math.Clamp(profile.Connection + delta, 0, 100);
                    break;
                case "Dominance":
                    profile.Dominance = Math.Clamp(profile.Dominance + delta, 0, 100);
                    break;
                case "Loyalty":
                    profile.Loyalty = Math.Clamp(profile.Loyalty + delta, 0, 100);
                    break;
                case "SelfRespect":
                    profile.SelfRespect = Math.Clamp(profile.SelfRespect + delta, 0, 100);
                    break;
            }
        }
    }
}
