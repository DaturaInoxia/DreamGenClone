using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class TieWindowScenarioTieBreakStrategy : IScenarioTieBreakStrategy
{
    public const string StrategyKey = "tie-window";

    public string Key => StrategyKey;

    public ScenarioTieDecision Evaluate(
        IReadOnlyList<ScenarioScoreResult> rankedCandidates,
        ScenarioSelectionContext context,
        double tieDeltaThreshold)
    {
        var normalizedTieDeltaThreshold = Math.Clamp(tieDeltaThreshold, 0.0, 1.0);
        var deferredForTie = rankedCandidates.Count >= 2
            && rankedCandidates[0].FitScore - rankedCandidates[1].FitScore <= normalizedTieDeltaThreshold;

        return deferredForTie
            ? new ScenarioTieDecision(true, "Top candidates tied, defer commitment")
            : new ScenarioTieDecision(false, "No tie deferral");
    }
}
