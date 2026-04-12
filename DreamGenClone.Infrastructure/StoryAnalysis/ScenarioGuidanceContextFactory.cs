using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class ScenarioGuidanceContextFactory : IScenarioGuidanceContextFactory
{
    public Task<ScenarioGuidanceContext> CreateAsync(
        ScenarioGuidanceInput input,
        CancellationToken cancellationToken = default)
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
            "Climax" => $"Deliver a high-intensity culmination that is explicitly framed around '{scenarioLabel}'.",
            "Reset" => "Transition to reflective tone and prepare for next build-up.",
            _ => "Maintain coherent narrative progression."
        };

        return Task.FromResult(new ScenarioGuidanceContext(
            input.CurrentPhase,
            input.ActiveScenarioId,
            guidance,
            input.SuppressedScenarioIds));
    }
}
