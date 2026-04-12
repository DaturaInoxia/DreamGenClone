using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class DecisionPointService : IDecisionPointService
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, int>> DefaultOptionDeltas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lean-in"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 6, ["Tension"] = 4, ["Restraint"] = -3 },
        ["hold-back"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Restraint"] = 5, ["Tension"] = -2, ["SelfRespect"] = 2 },
        ["seek-connection"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Connection"] = 5, ["Loyalty"] = 3 }
    };

    private readonly ILogger<DecisionPointService> _logger;

    public DecisionPointService(ILogger<DecisionPointService> logger)
    {
        _logger = logger;
    }

    public Task<DecisionPoint?> TryCreateDecisionPointAsync(
        AdaptiveScenarioState state,
        DecisionTrigger trigger,
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
            TransparencyMode = TransparencyMode.Directional,
            OptionIds = [.. DefaultOptionDeltas.Keys],
            CreatedUtc = DateTime.UtcNow
        };

        return Task.FromResult<DecisionPoint?>(decisionPoint);
    }

    public Task<DecisionOutcome> ApplyDecisionAsync(
        AdaptiveScenarioState state,
        DecisionSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var optionId = submission.OptionId;
        var deltas = DefaultOptionDeltas.TryGetValue(optionId, out var presetDeltas)
            ? new Dictionary<string, int>(presetDeltas, StringComparer.OrdinalIgnoreCase)
            : ParseCustomDeltas(submission.CustomResponseText);

        if (deltas.Count == 0)
        {
            deltas["Connection"] = 1;
        }

        foreach (var snapshot in state.CharacterSnapshots)
        {
            ApplyDeltas(snapshot, deltas);
        }

        var auditPayload = JsonSerializer.Serialize(new
        {
            submission.ActorName,
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
            TransparencyMode.Directional);

        return Task.FromResult(new DecisionOutcome
        {
            Applied = true,
            DecisionPointId = submission.DecisionPointId,
            OptionId = optionId,
            AppliedStatDeltas = deltas,
            AuditMetadataJson = auditPayload,
            TransparencyMode = TransparencyMode.Directional,
            Summary = "Decision applied and canonical stat deltas persisted."
        });
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
