using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IScenarioFitScoreStrategy
{
    string Key { get; }

    ScenarioScoreResult ScoreCandidate(
        ScenarioCandidateInput candidate,
        AdaptiveScenarioSnapshot adaptiveState,
        ScenarioSelectionContext context);
}
