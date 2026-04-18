using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class WeightedBlendScenarioFitScoreStrategy : IScenarioFitScoreStrategy
{
    public const string StrategyKey = "weighted-blend";

    public string Key => StrategyKey;

    public ScenarioScoreResult ScoreCandidate(
        ScenarioCandidateInput candidate,
        AdaptiveScenarioSnapshot adaptiveState,
        ScenarioSelectionContext context)
    {
        var fitScore = Clamp01(0.40 * candidate.CharacterAlignmentScore
            + 0.35 * candidate.NarrativeEvidenceScore
            + 0.25 * candidate.PreferencePriorityScore);

        return new ScenarioScoreResult(
            candidate.ScenarioId,
            fitScore,
            "Weighted deterministic fit score");
    }

    private static double Clamp01(double value) => Math.Clamp(Math.Round(value, 4), 0.0, 1.0);
}
