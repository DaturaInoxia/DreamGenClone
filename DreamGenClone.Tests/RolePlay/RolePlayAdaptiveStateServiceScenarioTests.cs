using DreamGenClone.Infrastructure.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public async Task UpdateFromInteractionAsync_StaleCommittedScenario_AllowsLatePivot()
    {
        var options = Options.Create(new StoryAnalysisOptions
        {
            ActiveScenarioNoHitStaleTurns = 2,
            PivotCommittedInteractionWindow = 3,
            PivotCommittedInteractionWindowWhenStale = 8,
            PivotOvertakeMarginDefault = 2.0,
            PivotOvertakeMarginWhenStale = 1.0,
            SuppressedEvidenceMultiplier = 0.20,
            SuppressedEvidencePerTurnCap = 1.5
        });

        var service = new RolePlayAdaptiveStateService(
            new FakeThemeCatalogService(),
            scenarioDefinitionService: null,
            scenarioSelectionEngine: new ScenarioSelectionEngine(),
            narrativePhaseManager: new NarrativePhaseManager(),
            themePreferenceService: new FakeThemePreferenceService(),
            rpThemeService: null,
            statKeywordCategoryService: null,
            styleProfileService: new FakeSteeringProfileService(),
            debugEventSink: new RolePlayTestFactory.NullRolePlayDebugEventSink(),
            logger: NullLogger<RolePlayAdaptiveStateService>.Instance,
            intensityProfileService: null,
            storyAnalysisOptions: options);

        var session = new RolePlaySession();
        var state = session.AdaptiveState;
        state.ActiveScenarioId = "dominance";
        state.CurrentNarrativePhase = DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Committed;
        state.InteractionsSinceCommitment = 5;

        state.ThemeTracker.Themes["dominance"] = new ThemeTrackerItem
        {
            ThemeId = "dominance",
            ThemeName = "Dominance",
            Score = 8,
            Breakdown = new ThemeScoreBreakdown { InteractionEvidenceSignal = 8 }
        };

        state.ThemeTracker.Themes["infidelity"] = new ThemeTrackerItem
        {
            ThemeId = "infidelity",
            ThemeName = "Infidelity",
            Score = 8,
            Breakdown = new ThemeScoreBreakdown { InteractionEvidenceSignal = 8 }
        };

        state.ThemeTracker.RecentEvidence.Add(new ThemeEvidenceEvent
        {
            InteractionId = "i-1",
            ThemeId = "infidelity",
            SignalType = "interaction-evidence",
            Delta = 2,
            Confidence = 0.7
        });
        state.ThemeTracker.RecentEvidence.Add(new ThemeEvidenceEvent
        {
            InteractionId = "i-2",
            ThemeId = "infidelity",
            SignalType = "interaction-evidence",
            Delta = 2,
            Confidence = 0.7
        });

        var updated = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "cheat betray affair husband wife"
        });

        Assert.Equal("infidelity", updated.ActiveScenarioId);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_NonStaleCommittedScenario_DoesNotLatePivot()
    {
        var options = Options.Create(new StoryAnalysisOptions
        {
            ActiveScenarioNoHitStaleTurns = 2,
            PivotCommittedInteractionWindow = 3,
            PivotCommittedInteractionWindowWhenStale = 8,
            PivotOvertakeMarginDefault = 2.0,
            PivotOvertakeMarginWhenStale = 1.0,
            SuppressedEvidenceMultiplier = 0.20,
            SuppressedEvidencePerTurnCap = 1.5
        });

        var service = new RolePlayAdaptiveStateService(
            new FakeThemeCatalogService(),
            scenarioDefinitionService: null,
            scenarioSelectionEngine: new ScenarioSelectionEngine(),
            narrativePhaseManager: new NarrativePhaseManager(),
            themePreferenceService: new FakeThemePreferenceService(),
            rpThemeService: null,
            statKeywordCategoryService: null,
            styleProfileService: new FakeSteeringProfileService(),
            debugEventSink: new RolePlayTestFactory.NullRolePlayDebugEventSink(),
            logger: NullLogger<RolePlayAdaptiveStateService>.Instance,
            intensityProfileService: null,
            storyAnalysisOptions: options);

        var session = new RolePlaySession();
        var state = session.AdaptiveState;
        state.ActiveScenarioId = "dominance";
        state.CurrentNarrativePhase = DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Committed;
        state.InteractionsSinceCommitment = 5;

        state.ThemeTracker.Themes["dominance"] = new ThemeTrackerItem
        {
            ThemeId = "dominance",
            ThemeName = "Dominance",
            Score = 8,
            Breakdown = new ThemeScoreBreakdown { InteractionEvidenceSignal = 8 }
        };

        state.ThemeTracker.Themes["infidelity"] = new ThemeTrackerItem
        {
            ThemeId = "infidelity",
            ThemeName = "Infidelity",
            Score = 8,
            Breakdown = new ThemeScoreBreakdown { InteractionEvidenceSignal = 8 }
        };

        state.ThemeTracker.RecentEvidence.Add(new ThemeEvidenceEvent
        {
            InteractionId = "i-1",
            ThemeId = "dominance",
            SignalType = "interaction-evidence",
            Delta = 2,
            Confidence = 0.7
        });
        state.ThemeTracker.RecentEvidence.Add(new ThemeEvidenceEvent
        {
            InteractionId = "i-2",
            ThemeId = "dominance",
            SignalType = "interaction-evidence",
            Delta = 2,
            Confidence = 0.7
        });

        var updated = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "cheat betray affair husband wife"
        });

        Assert.Equal("dominance", updated.ActiveScenarioId);
    }

    private sealed class FakeThemePreferenceService : IThemePreferenceService
    {
        public Task<ThemePreference> CreateAsync(string profileId, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken ct = default)
            => Task.FromResult(new ThemePreference { ProfileId = profileId, Name = name, Tier = tier });

        public Task<List<ThemePreference>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(new List<ThemePreference>());

        public Task<List<ThemePreference>> ListByProfileAsync(string profileId, CancellationToken ct = default)
            => Task.FromResult(new List<ThemePreference>());

        public Task<ThemePreference?> UpdateAsync(string id, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken ct = default)
            => Task.FromResult<ThemePreference?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<int> AutoLinkToCatalogAsync(CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private sealed class FakeSteeringProfileService : ISteeringProfileService
    {
        public Task<SteeringProfile> CreateAsync(string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken ct = default)
            => Task.FromResult(new SteeringProfile { Name = name });

        public Task<List<SteeringProfile>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(new List<SteeringProfile>());

        public Task<SteeringProfile?> GetAsync(string id, CancellationToken ct = default)
            => Task.FromResult<SteeringProfile?>(null);

        public Task<SteeringProfile?> UpdateAsync(string id, string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken ct = default)
            => Task.FromResult<SteeringProfile?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
            => Task.FromResult(false);
    }

}
