using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Infrastructure.StoryAnalysis;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class NarrativePhaseManagerTests
{
    [Fact]
    public async Task EvaluateTransitionAsync_MovesCommittedToApproaching_WhenThresholdsMet()
    {
        var manager = new NarrativePhaseManager();

        var result = await manager.EvaluateTransitionAsync(
            new AdaptiveScenarioSnapshot("dominance", "Committed", 10, 65, 45, 60),
            new NarrativeSignalSnapshot(
                InteractionsSinceCommitment: 3,
                InteractionsInApproaching: 0,
                ExplicitClimaxRequested: false,
                ClimaxCompletionDetected: false,
                ManualScenarioOverrideRequested: false,
                ManualOverrideScenarioId: null));

        Assert.True(result.Transitioned);
        Assert.Equal("Approaching", result.NextPhase);
    }

    [Fact]
    public async Task EvaluateTransitionAsync_MovesApproachingToClimax_WhenThresholdsMet()
    {
        var manager = new NarrativePhaseManager();

        var result = await manager.EvaluateTransitionAsync(
            new AdaptiveScenarioSnapshot("dominance", "Approaching", 12, 75, 35, 80),
            new NarrativeSignalSnapshot(
                InteractionsSinceCommitment: 5,
                InteractionsInApproaching: 2,
                ExplicitClimaxRequested: false,
                ClimaxCompletionDetected: false,
                ManualScenarioOverrideRequested: false,
                ManualOverrideScenarioId: null));

        Assert.True(result.Transitioned);
        Assert.Equal("Climax", result.NextPhase);
    }

    [Fact]
    public async Task EvaluateTransitionAsync_AllowsExplicitClimaxOverride_FromApproaching()
    {
        var manager = new NarrativePhaseManager();

        var result = await manager.EvaluateTransitionAsync(
            new AdaptiveScenarioSnapshot("dominance", "Approaching", 12, 55, 55, 30),
            new NarrativeSignalSnapshot(
                InteractionsSinceCommitment: 1,
                InteractionsInApproaching: 0,
                ExplicitClimaxRequested: true,
                ClimaxCompletionDetected: false,
                ManualScenarioOverrideRequested: false,
                ManualOverrideScenarioId: null));

        Assert.True(result.Transitioned);
        Assert.Equal("Climax", result.NextPhase);
    }

    [Fact]
    public async Task EvaluateTransitionAsync_AppliesCycleEasing_ForApproachingToClimax()
    {
        var manager = new NarrativePhaseManager();

        var result = await manager.EvaluateTransitionAsync(
            new AdaptiveScenarioSnapshot("dominance", "Approaching", 24, 63, 47, 64),
            new NarrativeSignalSnapshot(
                InteractionsSinceCommitment: 6,
                InteractionsInApproaching: 2,
                ExplicitClimaxRequested: false,
                ClimaxCompletionDetected: false,
                ManualScenarioOverrideRequested: false,
                ManualOverrideScenarioId: null,
                CompletedScenarios: 2));

        Assert.True(result.Transitioned);
        Assert.Equal("Climax", result.NextPhase);
    }
}
