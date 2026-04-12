using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IScenarioSelectionEngine
{
    Task<ScenarioSelectionResult> EvaluateAsync(
        AdaptiveScenarioSnapshot adaptiveState,
        IReadOnlyList<ScenarioCandidateInput> candidates,
        ScenarioSelectionContext context,
        CancellationToken cancellationToken = default);
}
