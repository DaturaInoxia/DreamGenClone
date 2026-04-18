using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IScenarioTieBreakStrategy
{
    string Key { get; }

    ScenarioTieDecision Evaluate(
        IReadOnlyList<ScenarioScoreResult> rankedCandidates,
        ScenarioSelectionContext context,
        double tieDeltaThreshold);
}
