using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Infrastructure.StoryAnalysis;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class ScenarioSelectionEngineTests
{
    [Fact]
    public async Task EvaluateAsync_RanksCandidatesByDeterministicFitScore()
    {
        var engine = new ScenarioSelectionEngine();

        var result = await engine.EvaluateAsync(
            new AdaptiveScenarioSnapshot(null, "BuildUp", 2, 50, 50, 0),
            [
                new ScenarioCandidateInput("a", 0.95, 0.90, 0.70, true),
                new ScenarioCandidateInput("b", 0.35, 0.30, 0.20, true)
            ],
            new ScenarioSelectionContext(2, false, null));

        Assert.False(result.DeferredForTie);
        Assert.Equal("a", result.RankedCandidates[0].ScenarioId);
        Assert.True(result.RankedCandidates[0].FitScore > result.RankedCandidates[1].FitScore);
    }

    [Fact]
    public async Task EvaluateAsync_DefersWhenTopCandidatesAreWithinTieDelta()
    {
        var engine = new ScenarioSelectionEngine();

        var result = await engine.EvaluateAsync(
            new AdaptiveScenarioSnapshot(null, "BuildUp", 3, 50, 50, 0),
            [
                new ScenarioCandidateInput("a", 0.70, 0.70, 0.70, true),
                new ScenarioCandidateInput("b", 0.68, 0.70, 0.70, true)
            ],
            new ScenarioSelectionContext(3, false, null));

        Assert.True(result.DeferredForTie);
        Assert.Null(result.SelectedScenarioId);
    }

    [Fact]
    public async Task EvaluateAsync_CommitsWhenThresholdAndBuildUpCountMet()
    {
        var engine = new ScenarioSelectionEngine();

        var result = await engine.EvaluateAsync(
            new AdaptiveScenarioSnapshot(null, "BuildUp", 2, 50, 50, 0),
            [
                new ScenarioCandidateInput("a", 0.95, 0.90, 0.90, true),
                new ScenarioCandidateInput("b", 0.40, 0.40, 0.40, true)
            ],
            new ScenarioSelectionContext(2, false, null));

        Assert.Equal("a", result.SelectedScenarioId);
        Assert.False(result.DeferredForTie);
    }
}
