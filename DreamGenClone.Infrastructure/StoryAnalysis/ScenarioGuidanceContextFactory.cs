using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Abstractions;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class ScenarioGuidanceContextFactory : IScenarioGuidanceContextFactory
{
    private readonly IScenarioGuidanceGenerator? _scenarioGuidanceGenerator;
    private readonly IBehavioralFrameGenerator _frameGenerator;

    public ScenarioGuidanceContextFactory(
        IBehavioralFrameGenerator frameGenerator,
        IScenarioGuidanceGenerator? scenarioGuidanceGenerator = null)
    {
        _frameGenerator = frameGenerator;
        _scenarioGuidanceGenerator = scenarioGuidanceGenerator;
    }

    public Task<ScenarioGuidanceContext> CreateAsync(
        ScenarioGuidanceInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_scenarioGuidanceGenerator is not null)
        {
            return CreateFromGeneratorAsync(input, cancellationToken);
        }

        return CreateFallbackAsync(input, cancellationToken);
    }

    private async Task<ScenarioGuidanceContext> CreateFromGeneratorAsync(
        ScenarioGuidanceInput input,
        CancellationToken cancellationToken)
    {
        var generated = await _scenarioGuidanceGenerator!.GenerateGuidanceAsync(
            new ScenarioGuidanceRequest
            {
                SessionId = input.SessionId,
                CurrentPhase = input.CurrentPhase,
                ActiveScenarioId = input.ActiveScenarioId,
                VariantId = input.VariantId,
                AverageDesire = input.AverageDesire,
                AverageRestraint = input.AverageRestraint,
                AverageDominance = input.AverageDominance,
                AverageLoyalty = input.AverageLoyalty,
                SelectedWillingnessProfileId = input.SelectedWillingnessProfileId,
                SelectedResistanceProfileId = input.SelectedResistanceProfileId,
                CharacterEncounterProfileIds = input.CharacterEncounterProfileIds,
                Characters = input.Characters,
                SuppressedScenarioIds = input.SuppressedScenarioIds,
                CharacterRuntimeStats = input.CharacterRuntimeStats
            },
            cancellationToken);

        var mergedGuidanceText = generated.GuidanceText;
        if (generated.EmphasisPoints.Count > 0)
        {
            mergedGuidanceText += $" Emphasize: {string.Join(", ", generated.EmphasisPoints)}.";
        }

        if (generated.AvoidancePoints.Count > 0)
        {
            mergedGuidanceText += $" Avoid: {string.Join(", ", generated.AvoidancePoints)}.";
        }

        // T017: generate per-character behavioral frames with runtime stats override when available
        var characterBehavioralFrames = await _frameGenerator.GenerateFramesAsync(
            input.CharacterEncounterProfileIds,
            input.Characters,
            input.CharacterRuntimeStats,
            cancellationToken);

        var characterStatStateTexts = BuildCharacterStatStateTexts(input.CharacterRuntimeStats, input.Characters);

        return new ScenarioGuidanceContext(
            input.CurrentPhase,
            input.ActiveScenarioId,
            mergedGuidanceText,
            input.SuppressedScenarioIds,
            characterBehavioralFrames,
            characterStatStateTexts);
    }

    private async Task<ScenarioGuidanceContext> CreateFallbackAsync(ScenarioGuidanceInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var scenarioLabel = string.IsNullOrWhiteSpace(input.ActiveScenarioId)
            ? "current narrative direction"
            : input.ActiveScenarioId;

        var guidance = input.CurrentPhase switch
        {
            "BuildUp" => "Use subtle, exploratory cues and avoid hard commitment language.",
            "Committed" => $"Keep narrative choices anchored to '{scenarioLabel}' and avoid introducing conflicting scenario pivots.",
            "Approaching" => $"Increase anticipation and intensity while preserving coherence with '{scenarioLabel}'.",
            "Climax" => $"Write the physical culmination of the scene framed around '{scenarioLabel}' with explicit physical detail. Describe body positioning, movement, and sensation specifically. Spend multiple turns within the same position or act before any transition — advancing within a turn means richer sensory and physical writing, not a required position change. Narrative urgency raises writing intensity, not scene length abbreviation. By default, male characters do not orgasm until the user issues /endclimax; until then the scene always continues — unless the active steer or instruction explicitly directs it.",
            "Reset" => "Transition to reflective tone and prepare for next build-up.",
            _ => "Maintain coherent narrative progression."
        };

        var characterBehavioralFrames = await _frameGenerator.GenerateFramesAsync(
            input.CharacterEncounterProfileIds,
            input.Characters,
            input.CharacterRuntimeStats,
            cancellationToken);

        var characterStatStateTexts = BuildCharacterStatStateTexts(input.CharacterRuntimeStats, input.Characters);

        return new ScenarioGuidanceContext(
            input.CurrentPhase,
            input.ActiveScenarioId,
            guidance,
            input.SuppressedScenarioIds,
            characterBehavioralFrames,
            characterStatStateTexts);
    }

    private static Dictionary<string, string> BuildCharacterStatStateTexts(
        IReadOnlyDictionary<string, CharacterStatProfileV2>? characterRuntimeStats,
        IReadOnlyList<ScenarioCharacter> characters)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (characterRuntimeStats is null) return result;

        foreach (var (label, profile) in characterRuntimeStats)
        {
            var role = ResolveRoleFromLabel(label, characters);
            if (role is null) continue;

            var texts = new List<string>();
            foreach (var statName in AdaptiveStatCatalog.CanonicalStatNames)
            {
                var value = CharacterStatProfileV2Accessor.GetStatOrDefault(profile, statName);
                var text = CharacterStatTextCatalog.ResolveText(statName, role, value);
                if (!string.IsNullOrWhiteSpace(text)) texts.Add($"  {statName} — {text}");
            }

            if (texts.Count > 0) result[label] = string.Join('\n', texts);
        }

        return result;
    }

    private static string? ResolveRoleFromLabel(string label, IReadOnlyList<ScenarioCharacter> characters)
    {
        foreach (var c in characters)
        {
            if (string.Equals($"{c.Name} ({c.Role})", label, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Id, label, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Name, label, StringComparison.OrdinalIgnoreCase))
            {
                return c.Role;
            }
        }

        return null;
    }
}
