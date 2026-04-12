using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class ScenarioStateModelTests
{
    [Fact]
    public void RolePlayAdaptiveState_DefaultsToBuildUpAndEmptyHistory()
    {
        var state = new RolePlayAdaptiveState();

        Assert.Equal(NarrativePhase.BuildUp, state.CurrentNarrativePhase);
        Assert.Equal(0, state.CompletedScenarios);
        Assert.Empty(state.ScenarioHistory);
        Assert.Null(state.ActiveScenarioId);
    }

    [Fact]
    public void ThemeTrackerItem_DefaultCandidateFieldsAreSafe()
    {
        var item = new ThemeTrackerItem();

        Assert.False(item.IsScenarioCandidate);
        Assert.Equal(0, item.NarrativeFitScore);
        Assert.Null(item.LastCandidateEvaluationTimeUtc);
    }

    [Fact]
    public void ScenarioMetadata_StoresExpectedValues()
    {
        var completedAt = DateTime.UtcNow;
        var metadata = new ScenarioMetadata
        {
            ScenarioId = "scenario-a",
            CompletedAtUtc = completedAt,
            InteractionCount = 6,
            PeakThemeScore = 88,
            PeakDesireLevel = 79,
            AverageRestraintLevel = 34.5,
            Notes = "cycle complete"
        };

        Assert.Equal("scenario-a", metadata.ScenarioId);
        Assert.Equal(completedAt, metadata.CompletedAtUtc);
        Assert.Equal(6, metadata.InteractionCount);
        Assert.Equal(88, metadata.PeakThemeScore);
        Assert.Equal(79, metadata.PeakDesireLevel);
        Assert.Equal(34.5, metadata.AverageRestraintLevel);
        Assert.Equal("cycle complete", metadata.Notes);
    }
}
