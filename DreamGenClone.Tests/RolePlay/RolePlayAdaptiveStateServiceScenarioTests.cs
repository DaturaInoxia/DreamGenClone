using DreamGenClone.Infrastructure.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using static DreamGenClone.Tests.RolePlay.RolePlayTestFactory;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayAdaptiveStateServiceScenarioTests
{
    [Fact]
    public async Task UpdateFromInteractionAsync_CommitsScenario_WhenThresholdAndInteractionGateMet()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), new ScenarioSelectionEngine());
        var session = new RolePlaySession
        {
            Interactions =
            [
                new RolePlayInteraction
                {
                    ActorName = "Alex",
                    Content = "warm up interaction"
                }
            ]
        };

        var interaction = new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "dominate dominate dominate order kneel"
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.Equal("dominance", state.ActiveScenarioId);
        Assert.NotNull(state.ScenarioCommitmentTimeUtc);
        Assert.Equal(DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Committed, state.CurrentNarrativePhase);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_DoesNotCommit_WhenTieIsDetected()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), new ScenarioSelectionEngine());
        var session = new RolePlaySession
        {
            Interactions =
            [
                new RolePlayInteraction { ActorName = "Alex", Content = "warm up interaction" }
            ]
        };

        var interaction = new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "control command obey claim dominate"
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.Null(state.ActiveScenarioId);
        Assert.Equal(DreamGenClone.Domain.StoryAnalysis.NarrativePhase.BuildUp, state.CurrentNarrativePhase);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_ManualOverride_ResetsThenReturnsToBuildUp_WithRequestedPriority()
    {
        var service = new RolePlayAdaptiveStateService(
            new FakeThemeCatalogService(),
            new ScenarioSelectionEngine(),
            new NarrativePhaseManager());

        var session = new RolePlaySession
        {
            Interactions =
            [
                new RolePlayInteraction { ActorName = "Alex", Content = "warm up interaction" }
            ]
        };

        await service.UpdateFromInteractionAsync(
            session,
            new RolePlayInteraction { ActorName = "Alex", Content = "dominate dominate order kneel" });

        var stateAfterOverride = await service.UpdateFromInteractionAsync(
            session,
            new RolePlayInteraction { ActorName = "Alex", Content = "override infidelity now" });

        Assert.Null(stateAfterOverride.ActiveScenarioId);
        Assert.Equal(DreamGenClone.Domain.StoryAnalysis.NarrativePhase.BuildUp, stateAfterOverride.CurrentNarrativePhase);
        Assert.True(stateAfterOverride.CompletedScenarios >= 1);
        Assert.True(stateAfterOverride.ThemeTracker.Themes["infidelity"].Breakdown.ChoiceSignal >= 20);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_IsDeterministicAcrossReplay_ForMultiCycleSequence()
    {
        var sequence = new[]
        {
            "dominate dominate order kneel",
            "control command obey claim dominate",
            "override infidelity now",
            "cheat betray affair husband wife",
            "risk secret hide forbidden caught"
        };

        async Task<RolePlayAdaptiveState> RunReplayAsync()
        {
            var service = new RolePlayAdaptiveStateService(
                new FakeThemeCatalogService(),
                new ScenarioSelectionEngine(),
                new NarrativePhaseManager());

            var session = new RolePlaySession
            {
                Interactions =
                [
                    new RolePlayInteraction { ActorName = "Alex", Content = "warm up interaction" }
                ]
            };

            RolePlayAdaptiveState? state = null;
            foreach (var content in sequence)
            {
                state = await service.UpdateFromInteractionAsync(
                    session,
                    new RolePlayInteraction { ActorName = "Alex", Content = content });
            }

            return state!;
        }

        var first = await RunReplayAsync();
        var second = await RunReplayAsync();

        Assert.Equal(first.CurrentNarrativePhase, second.CurrentNarrativePhase);
        Assert.Equal(first.ActiveScenarioId, second.ActiveScenarioId);
        Assert.Equal(first.CompletedScenarios, second.CompletedScenarios);
        Assert.Equal(first.ScenarioHistory.Count, second.ScenarioHistory.Count);
    }

    [Fact]
    public async Task ApplyManualScenarioOverrideAsync_KeepsOverrideDuringLockWindow()
    {
        var service = new RolePlayAdaptiveStateService(
            new FakeThemeCatalogService(),
            new ScenarioSelectionEngine(),
            new NarrativePhaseManager());

        var session = new RolePlaySession
        {
            Interactions =
            [
                new RolePlayInteraction { ActorName = "Alex", Content = "warm up interaction" }
            ]
        };

        await service.UpdateFromInteractionAsync(
            session,
            new RolePlayInteraction { ActorName = "Alex", Content = "dominate dominate dominate order kneel" });

        var applied = await service.ApplyManualScenarioOverrideAsync(session, "infidelity");
        Assert.True(applied);
        Assert.Equal("infidelity", session.AdaptiveState.ActiveScenarioId);

        var afterCompetingSignal = await service.UpdateFromInteractionAsync(
            session,
            new RolePlayInteraction { ActorName = "Alex", Content = "dominate command obey control dominate" });

        Assert.Equal("infidelity", afterCompetingSignal.ActiveScenarioId);
        Assert.Equal("ManualOverride", afterCompetingSignal.ThemeTracker.ThemeSelectionRule);
    }

}
