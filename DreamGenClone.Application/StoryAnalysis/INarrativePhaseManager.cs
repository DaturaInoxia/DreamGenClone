using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Application.StoryAnalysis;

public interface INarrativePhaseManager
{
    Task<PhaseTransitionResult> EvaluateTransitionAsync(
        AdaptiveScenarioSnapshot adaptiveState,
        NarrativeSignalSnapshot signals,
        CancellationToken cancellationToken = default);

    Task<AdaptiveScenarioSnapshot> ApplyResetAsync(
        AdaptiveScenarioSnapshot adaptiveState,
        ResetTrigger trigger,
        CancellationToken cancellationToken = default);
}
