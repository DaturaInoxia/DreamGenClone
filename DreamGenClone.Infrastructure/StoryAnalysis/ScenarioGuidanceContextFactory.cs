using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Application.RolePlay;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class ScenarioGuidanceContextFactory : IScenarioGuidanceContextFactory
{
    private readonly IScenarioGuidanceGenerator? _scenarioGuidanceGenerator;

    public ScenarioGuidanceContextFactory(IScenarioGuidanceGenerator? scenarioGuidanceGenerator = null)
    {
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

        return Task.FromResult(CreateFallback(input));
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
                AverageTension = input.AverageTension,
                AverageConnection = input.AverageConnection,
                AverageDominance = input.AverageDominance,
                AverageLoyalty = input.AverageLoyalty,
                SelectedWillingnessProfileId = input.SelectedWillingnessProfileId,
                HusbandAwarenessProfileId = input.HusbandAwarenessProfileId,
                SuppressedScenarioIds = input.SuppressedScenarioIds
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

        return new ScenarioGuidanceContext(
            input.CurrentPhase,
            input.ActiveScenarioId,
            mergedGuidanceText,
            input.SuppressedScenarioIds,
            generated.HusbandAwarenessFrame);
    }

    private static ScenarioGuidanceContext CreateFallback(ScenarioGuidanceInput input)
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

        return new ScenarioGuidanceContext(
            input.CurrentPhase,
            input.ActiveScenarioId,
            guidance,
            input.SuppressedScenarioIds);
    }
}
