using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using System.Reflection;

namespace DreamGenClone.Tests.RolePlay;

public sealed class PhaseLifecycleTransitionTests
{
    private readonly ScenarioLifecycleService _service = new(NullLogger<ScenarioLifecycleService>.Instance);

    private static ScenarioLifecycleService CreateServiceWithProfile()
    {
        var profileService = new StubNarrativeGateProfileService();
        return new ScenarioLifecycleService(NullLogger<ScenarioLifecycleService>.Instance, profileService);
    }

    [Fact]
    public async Task ValidLifecycleTransitionSequence_ProgressesInOrder()
    {
        // All phase transitions require a configured profile — no hardcoded fallbacks.
        var service = CreateServiceWithProfile();
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Committed;

        var toApproaching = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            TurnsSinceCommitment = 3,
            ActiveScenarioFitScore = 61m,
            ActiveScenarioConfidence = 0.8m
        });
        state.CurrentPhase = toApproaching.TargetPhase;

        var toClimax = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            TurnsSinceCommitment = 1,
            ActiveScenarioFitScore = 85m,
            ActiveScenarioConfidence = 0.9m
        });
        state.CurrentPhase = toClimax.TargetPhase;

        var toReset = await service.EvaluateTransitionAsync(state, new LifecycleInputs { ClimaxCompletionRequested = true });
        state.CurrentPhase = toReset.TargetPhase;

        var toBuildUp = await service.EvaluateTransitionAsync(state, new LifecycleInputs());

        Assert.Equal(NarrativePhase.Approaching, toApproaching.TargetPhase);
        Assert.Equal(NarrativePhase.Climax, toClimax.TargetPhase);
        Assert.Equal(NarrativePhase.Reset, toReset.TargetPhase);
        Assert.Equal(NarrativePhase.BuildUp, toBuildUp.TargetPhase);
    }

    [Fact]
    public async Task BuildUp_DoesNotTransitionToCommitted_ViaLifecycle()
    {
        var service = CreateServiceWithProfile();
        var state = CreateState();

        var result = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            ActiveScenarioConfidence = 0.99m,
            ActiveScenarioFitScore = 95m,
            TurnsSinceCommitment = 100
        });

        Assert.False(result.Transitioned);
        Assert.Equal(NarrativePhase.BuildUp, result.TargetPhase);
    }

    [Fact]
    public async Task Committed_DoesNotTransitionWithoutProfile()
    {
        // Without a profile, no fallback thresholds — transition is blocked.
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Committed;

        var result = await _service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            TurnsSinceCommitment = 100,
            ActiveScenarioConfidence = 0.99m,
            ActiveScenarioFitScore = 95m
        });

        Assert.False(result.Transitioned);
        Assert.Equal(NarrativePhase.Committed, result.TargetPhase);
    }

    [Fact]
    public async Task Climax_AlwaysTransitionsToReset()
    {
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Climax;

        // Climax → Reset is configuration-driven. Without configured Climax→Reset gate rules,
        // explicit climax completion must be requested to transition.
        var result = await _service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            TurnsSinceCommitment = 50,
            ActiveScenarioFitScore = 95m,
            ClimaxCompletionRequested = true
        });

        Assert.True(result.Transitioned);
        Assert.Equal(NarrativePhase.Reset, result.TargetPhase);
    }

    [Fact]
    public async Task BuildScenarioCandidates_ReordersPrimaryCandidate_WhenEvidenceScoresChange()
    {
        var engine = RolePlayTestFactory.CreateEngineService();
        var session = await engine.CreateSessionAsync("phase-ordering");

        session.AdaptiveState.ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
        {
            ["theme-a"] = new ThemeScoreState { ThemeId = "theme-a", ThemeName = "Theme A", Score = 80, Blocked = false },
            ["theme-b"] = new ThemeScoreState { ThemeId = "theme-b", ThemeName = "Theme B", Score = 20, Blocked = false }
        };

        var firstCandidates = await InvokeBuildScenarioCandidatesAsync(engine, session);
        Assert.Equal("theme-a", firstCandidates[0].ScenarioId);

        // Simulate semantic evidence shifting narrative momentum to theme-b.
        session.AdaptiveState.ThemeScores["theme-a"].Score = 20;
        session.AdaptiveState.ThemeScores["theme-b"].Score = 90;

        var secondCandidates = await InvokeBuildScenarioCandidatesAsync(engine, session);
        Assert.Equal("theme-b", secondCandidates[0].ScenarioId);
    }

    [Fact]
    public async Task BuildScenarioCandidates_ExcludesBlockedTheme_EvenWhenScoreIsHigher()
    {
        var engine = RolePlayTestFactory.CreateEngineService();
        var session = await engine.CreateSessionAsync("phase-lock-safe");

        session.AdaptiveState.ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
        {
            ["theme-safe"] = new ThemeScoreState { ThemeId = "theme-safe", ThemeName = "Theme Safe", Score = 25, Blocked = false },
            ["theme-blocked"] = new ThemeScoreState { ThemeId = "theme-blocked", ThemeName = "Theme Blocked", Score = 99, Blocked = true }
        };

        var candidates = await InvokeBuildScenarioCandidatesAsync(engine, session);

        Assert.DoesNotContain(candidates, x => string.Equals(x.ScenarioId, "theme-blocked", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("theme-safe", candidates[0].ScenarioId);
    }

    [Fact]
    public async Task CommittedPhase_TransitionsWhenPostSemanticDesireCrossesGateThreshold()
    {
        var service = CreateServiceWithProfile();
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Committed;
        state.CharacterSnapshots[0].Desire = 64;
        state.CharacterSnapshots[0].Restraint = 44;

        var beforeSemantic = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            TurnsSinceCommitment = 3,
            ActiveScenarioFitScore = 61m,
            ActiveScenarioConfidence = 0.8m
        });
        Assert.False(beforeSemantic.Transitioned);

        // Simulate semantic stat mapping delta being applied before gate evaluation.
        state.CharacterSnapshots[0].Desire = 66;

        var afterSemantic = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            TurnsSinceCommitment = 3,
            ActiveScenarioFitScore = 61m,
            ActiveScenarioConfidence = 0.8m
        });

        Assert.True(afterSemantic.Transitioned);
        Assert.Equal(NarrativePhase.Approaching, afterSemantic.TargetPhase);
    }

    [Fact]
    public async Task CommittedPhase_DoesNotTransitionWhenSemanticStatDeltaIsSuppressed()
    {
        var service = CreateServiceWithProfile();
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Committed;

        // Simulate suppression/cap outcome where applied semantic delta is effectively zero.
        state.CharacterSnapshots[0].Desire = 64;
        state.CharacterSnapshots[0].Restraint = 44;

        var result = await service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            TurnsSinceCommitment = 3,
            ActiveScenarioFitScore = 61m,
            ActiveScenarioConfidence = 0.8m
        });

        Assert.False(result.Transitioned);
        Assert.Equal(NarrativePhase.Committed, result.TargetPhase);
    }

    [Fact]
    public async Task IllegalTransitionRequest_IsRejected()
    {
        var state = CreateState();
        var result = await _service.EvaluateTransitionAsync(state, new LifecycleInputs { TurnsSinceCommitment = 5, ActiveScenarioFitScore = 90m });

        Assert.False(result.Transitioned);
        Assert.Equal(NarrativePhase.BuildUp, result.TargetPhase);
    }

    [Fact]
    public async Task ManualAdvanceTargetPhase_TransitionsImmediately()
    {
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Committed;

        var result = await _service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            ManualAdvanceTargetPhase = NarrativePhase.Approaching
        });

        Assert.True(result.Transitioned);
        Assert.Equal(NarrativePhase.Approaching, result.TargetPhase);
        Assert.Equal("MANUAL_NEXT_PHASE", result.Reason);
    }

    [Fact]
    public async Task ManualAdvanceTargetPhase_DoesNotAllowBackwardTransition()
    {
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Approaching;

        var result = await _service.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            ManualAdvanceTargetPhase = NarrativePhase.Committed
        });

        Assert.False(result.Transitioned);
        Assert.Equal(NarrativePhase.Approaching, result.TargetPhase);
    }

    [Fact]
    public async Task ResetToBuildUp_ExecuteResetPreservesContinuityRelevantState()
    {
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Reset;
        state.CycleIndex = 4;
        state.ActiveFormulaVersion = "custom-v2";
        var snapshot = state.CharacterSnapshots[0];

        var reset = await _service.ExecuteResetAsync(state, ResetReason.Completion);
        var decayed = reset.CharacterSnapshots[0];

        Assert.Equal(NarrativePhase.BuildUp, reset.CurrentPhase);
        Assert.Equal(5, reset.CycleIndex);
        Assert.Equal("custom-v2", reset.ActiveFormulaVersion);
        Assert.Single(reset.CharacterSnapshots);
        Assert.Equal(snapshot.CharacterId, reset.CharacterSnapshots[0].CharacterId);
        Assert.True(decayed.Desire < snapshot.Desire);
        Assert.Equal(snapshot.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50, decayed.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50);
        Assert.True(decayed.Dominance <= snapshot.Dominance);
        Assert.Equal(snapshot.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50, decayed.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50);
        Assert.Equal(snapshot.Loyalty, decayed.Loyalty);
        Assert.Equal(snapshot.SelfRespect, decayed.SelfRespect);
    }

    [Fact]
    public async Task ResetDecay_StrongerForHigherElevatedDesire()
    {
        var state = new AdaptiveScenarioState
        {
            SessionId = "session-2",
            CurrentPhase = NarrativePhase.Reset,
            ActiveFormulaVersion = "rpv2-default",
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "char-high",
                    Desire = 95,
                    Restraint = 20,
                    Dominance = 80,
                    Loyalty = 55,
                    SelfRespect = 58,
                    RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 85, ["Connection"] = 60 }
                },
                new CharacterStatProfileV2
                {
                    CharacterId = "char-mid",
                    Desire = 65,
                    Restraint = 80,
                    Dominance = 60,
                    Loyalty = 55,
                    SelfRespect = 58,
                    RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 60, ["Connection"] = 60 }
                }
            ]
        };

        var reset = await _service.ExecuteResetAsync(state, ResetReason.Completion);
        var high = reset.CharacterSnapshots.Single(x => x.CharacterId == "char-high");
        var mid = reset.CharacterSnapshots.Single(x => x.CharacterId == "char-mid");

        Assert.True((95 - high.Desire) > (65 - mid.Desire));
        Assert.Equal(30, high.Restraint);
        Assert.Equal(70, mid.Restraint);
    }

    [Fact]
    public async Task ResetDecay_PullTowardBaselineDecreasesAsCycleIncreases()
    {
        var earlyCycleState = new AdaptiveScenarioState
        {
            SessionId = "cycle-early",
            CurrentPhase = NarrativePhase.Reset,
            CycleIndex = 0,
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "char-a",
                    Desire = 90,
                    Restraint = 20,
                    Dominance = 70,
                    Loyalty = 50,
                    SelfRespect = 50,
                    RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 80, ["Connection"] = 55 }
                }
            ]
        };

        var laterCycleState = new AdaptiveScenarioState
        {
            SessionId = "cycle-late",
            CurrentPhase = NarrativePhase.Reset,
            CycleIndex = 5,
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "char-a",
                    Desire = 90,
                    Restraint = 20,
                    Dominance = 70,
                    Loyalty = 50,
                    SelfRespect = 50,
                    RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 80, ["Connection"] = 55 }
                }
            ]
        };

        var earlyReset = await _service.ExecuteResetAsync(earlyCycleState, ResetReason.Completion);
        var lateReset = await _service.ExecuteResetAsync(laterCycleState, ResetReason.Completion);

        var early = earlyReset.CharacterSnapshots[0];
        var late = lateReset.CharacterSnapshots[0];

        Assert.True(late.Desire > early.Desire);
        Assert.True(late.Dominance > early.Dominance);
        Assert.True(late.Restraint < early.Restraint);
    }

    [Fact]
    public async Task ResetDecay_UsesConfiguredDesireBaselinePullSchedule()
    {
        var options = Options.Create(new StoryAnalysisOptions
        {
            ResetStatBaselines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 40,
                ["Restraint"] = 50,
                ["Tension"] = 40,
                ["Connection"] = 50,
                ["Dominance"] = 50,
                ["Loyalty"] = 50,
                ["SelfRespect"] = 50
            },
            ResetStatBaselinePullSchedule = [0.8333, 0.5833, 0.3333, 0.2, 0.1667]
        });
        var service = new ScenarioLifecycleService(NullLogger<ScenarioLifecycleService>.Instance, storyAnalysisOptions: options);

        var expectedByCycle = new Dictionary<int, int>
        {
            [1] = 50,
            [2] = 65,
            [3] = 80,
            [4] = 88,
            [5] = 90
        };

        foreach (var (cycle, expectedDesire) in expectedByCycle)
        {
            var state = new AdaptiveScenarioState
            {
                SessionId = $"session-cycle-{cycle}",
                CurrentPhase = NarrativePhase.Reset,
                CycleIndex = cycle - 1,
                CharacterSnapshots =
                [
                    new CharacterStatProfileV2
                    {
                        CharacterId = "char-a",
                        Desire = 100,
                        Restraint = 20,
                        Dominance = 70,
                        Loyalty = 50,
                        SelfRespect = 50,
                        RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 80, ["Connection"] = 55 }
                    }
                ]
            };

            var reset = await service.ExecuteResetAsync(state, ResetReason.Completion);
            Assert.Equal(expectedDesire, reset.CharacterSnapshots[0].Desire);
        }

        var belowBaselineState = new AdaptiveScenarioState
        {
            SessionId = "session-below-baseline",
            CurrentPhase = NarrativePhase.Reset,
            CycleIndex = 0,
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "char-b",
                    Desire = 10,
                    Restraint = 20,
                    Dominance = 10,
                    Loyalty = 10,
                    SelfRespect = 10,
                    RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 10, ["Connection"] = 10 }
                }
            ]
        };

        var belowBaselineReset = await service.ExecuteResetAsync(belowBaselineState, ResetReason.Completion);
        var updated = belowBaselineReset.CharacterSnapshots[0];
        Assert.True(updated.Desire > 10);
        Assert.True(updated.Restraint > 20);
        Assert.True((updated.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50) > 10);
        Assert.True((updated.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50) > 10);
        Assert.True(updated.Dominance > 10);
        Assert.True(updated.Loyalty > 10);
        Assert.True(updated.SelfRespect > 10);
    }

    [Fact]
    public async Task ExecuteReset_ResetDecayReductionPerCycle_ReducesPullForLaterCycles()
    {
        // After multiple completed cycles, the stat pull should be proportionally smaller,
        // preserving more of the earned stat gains.
        var options = Options.Create(new StoryAnalysisOptions
        {
            ResetStatBaselinePullSchedule = [0.30, 0.25, 0.20, 0.15, 0.10],
            ResetDecayReductionPerCycle = 0.10,
            ResetDecayReductionCap = 0.60
        });
        var service = new ScenarioLifecycleService(NullLogger<ScenarioLifecycleService>.Instance, storyAnalysisOptions: options);

        // First cycle (CycleIndex=0): no reduction applied — CycleIndex > 0 check fails
        // statPull = schedule[0] = 0.30; MoveTowardBaseline(90, 50, 0.30) → delta=-40 → adj=-12 → 78
        var firstCycleState = new AdaptiveScenarioState
        {
            SessionId = "reset-reduction-first",
            CurrentPhase = NarrativePhase.Reset,
            CycleIndex = 0,
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "char-a",
                    Desire = 90,
                    Restraint = 50,
                    Dominance = 50,
                    Loyalty = 50,
                    SelfRespect = 50,
                    RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 50, ["Connection"] = 50 }
                }
            ]
        };

        // Fourth cycle (CycleIndex=3): reductionFraction = min(0.60, 3*0.10) = 0.30
        // statPull = schedule[3] = 0.15; scaled pull = 0.15 * 0.70 = 0.105
        // MoveTowardBaseline(90, 50, 0.105) → delta=-40 → adj=round(-4.2)=-4 → 86
        var laterCycleState = new AdaptiveScenarioState
        {
            SessionId = "reset-reduction-later",
            CurrentPhase = NarrativePhase.Reset,
            CycleIndex = 3,
            CharacterSnapshots =
            [
                new CharacterStatProfileV2
                {
                    CharacterId = "char-a",
                    Desire = 90,
                    Restraint = 50,
                    Dominance = 50,
                    Loyalty = 50,
                    SelfRespect = 50,
                    RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 50, ["Connection"] = 50 }
                }
            ]
        };

        var firstReset = await service.ExecuteResetAsync(firstCycleState, ResetReason.Completion);
        var laterReset = await service.ExecuteResetAsync(laterCycleState, ResetReason.Completion);

        var firstChar = firstReset.CharacterSnapshots[0];
        var laterChar = laterReset.CharacterSnapshots[0];

        Assert.Equal(78, firstChar.Desire);
        Assert.Equal(86, laterChar.Desire);
        Assert.True(laterChar.Desire > firstChar.Desire,
            $"Later cycles should retain more stat gains: later={laterChar.Desire} should > first={firstChar.Desire}");
    }

    [Fact]
    public async Task StatDecayOverride_ScaleZero_StatFrozenAfterReset()
    {
        // Desire override = 0.0 → decay pull is multiplied by 0 → stat should not move at all.
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Reset;
        state.CycleIndex = 0;
        var snapshot = state.CharacterSnapshots[0]; // Desire = 80, baseline = 50 by default

        var overrides = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["Desire"] = 0.0m
        };

        var reset = await _service.ExecuteResetAsync(state, ResetReason.Completion, statDecayScaleOverrides: overrides);
        var decayed = reset.CharacterSnapshots[0];

        Assert.Equal(snapshot.Desire, decayed.Desire);
    }

    [Fact]
    public async Task StatDecayOverride_ScaleHalf_StatMovesHalfwayToBaseline()
    {
        // At scale 1.0 Desire would fully decay to baseline.
        // At scale 0.5 it should move approximately halfway.
        // Use pull schedule = [1.0] and baselines = {Desire: 0} so the full-pull target is 0.
        var opts = Options.Create(new StoryAnalysisOptions
        {
            ResetStatBaselinePullSchedule = [1.0],
            ResetStatBaselines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 0 }
        });
        var service = new ScenarioLifecycleService(NullLogger<ScenarioLifecycleService>.Instance, null, opts);

        var state = new AdaptiveScenarioState
        {
            SessionId = "test",
            CurrentPhase = NarrativePhase.Reset,
            CycleIndex = 0,
            ActiveFormulaVersion = "rpv2-default",
            CharacterSnapshots = [new CharacterStatProfileV2 { CharacterId = "c", Desire = 100 }]
        };

        // Full pull (scale=1.0) → Desire should go to 0.
        var fullReset = await service.ExecuteResetAsync(state, ResetReason.Completion);
        Assert.Equal(0, fullReset.CharacterSnapshots[0].Desire);

        // Half pull (scale=0.5) → Desire should move halfway: 100 * (1 - 0.5) = 50.
        var halfOverrides = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 0.5m };
        var halfReset = await service.ExecuteResetAsync(state, ResetReason.Completion, statDecayScaleOverrides: halfOverrides);
        Assert.Equal(50, halfReset.CharacterSnapshots[0].Desire);
    }

    [Fact]
    public async Task StatDecayOverride_EmptyOverrides_NumericallyIdenticalToNoOverrides()
    {
        // Regression guard: passing an empty overrides dictionary must produce the same result as null.
        var state = CreateState();
        state.CurrentPhase = NarrativePhase.Reset;
        state.CycleIndex = 0;

        var withNull = await _service.ExecuteResetAsync(state, ResetReason.Completion, statDecayScaleOverrides: null);
        var withEmpty = await _service.ExecuteResetAsync(state, ResetReason.Completion,
            statDecayScaleOverrides: new Dictionary<string, decimal>());

        var nullSnap = withNull.CharacterSnapshots[0];
        var emptySnap = withEmpty.CharacterSnapshots[0];
        Assert.Equal(nullSnap.Desire, emptySnap.Desire);
        Assert.Equal(nullSnap.Restraint, emptySnap.Restraint);
        Assert.Equal(nullSnap.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50, emptySnap.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50);
        Assert.Equal(nullSnap.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50, emptySnap.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50);
        Assert.Equal(nullSnap.Dominance, emptySnap.Dominance);
        Assert.Equal(nullSnap.Loyalty, emptySnap.Loyalty);
        Assert.Equal(nullSnap.SelfRespect, emptySnap.SelfRespect);
    }

    [Fact]
    public async Task ThemeMachineResolution_ThrowsWhenNoActiveDefinitionExists()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dreamgenclone-machine-resolution-{Guid.NewGuid():N}.db");

        try
        {
            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();
            await EnsureThemeTableAsync(connection);
            await EnsureMachineTablesAsync(connection);
            await InsertThemeAsync(connection, "theme-1");
            await InsertMachineDefinitionAsync(connection, "definition-1", "theme-1", "machine-a", version: 1, isActive: false);

            var service = new ThemeMachineResolutionService(
                Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath}" }));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ResolveAsync("session-1", "theme-1", pinnedSnapshot: null));

            Assert.Contains("no active machine definition", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // SQLite can hold the file handle briefly after disposal on Windows.
                }
            }
        }
    }

    [Fact]
    public async Task ThemeMachineResolution_ThrowsWhenMultipleActiveDefinitionsExist()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dreamgenclone-machine-resolution-{Guid.NewGuid():N}.db");

        try
        {
            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();
            await EnsureThemeTableAsync(connection);
            await EnsureMachineTablesAsync(connection);
            await InsertThemeAsync(connection, "theme-1");
            await InsertMachineDefinitionAsync(connection, "definition-1", "theme-1", "machine-a", version: 1, isActive: true);
            await InsertMachineDefinitionAsync(connection, "definition-2", "theme-1", "machine-a", version: 2, isActive: true);

            var service = new ThemeMachineResolutionService(
                Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath}" }));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ResolveAsync("session-1", "theme-1", pinnedSnapshot: null));

            Assert.Contains("multiple active machine definitions", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // SQLite can hold the file handle briefly after disposal on Windows.
                }
            }
        }
    }

    private static async Task EnsureThemeTableAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS RPThemes (Id TEXT PRIMARY KEY);";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureMachineTablesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RPThemeMachineDefinitions (
                DefinitionId TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                MachineKey TEXT NOT NULL,
                Version INTEGER NOT NULL,
                Name TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 0,
                IsSeeded INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                UNIQUE (ThemeId, MachineKey, Version)
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertThemeAsync(SqliteConnection connection, string themeId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO RPThemes (Id) VALUES ($id);";
        command.Parameters.AddWithValue("$id", themeId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertMachineDefinitionAsync(
        SqliteConnection connection,
        string definitionId,
        string themeId,
        string machineKey,
        int version,
        bool isActive)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPThemeMachineDefinitions (
                DefinitionId, ThemeId, MachineKey, Version, Name, IsActive, IsSeeded, CreatedUtc, UpdatedUtc)
            VALUES (
                $definitionId, $themeId, $machineKey, $version, $name, $isActive, 0, $createdUtc, $updatedUtc);
            """;
        command.Parameters.AddWithValue("$definitionId", definitionId);
        command.Parameters.AddWithValue("$themeId", themeId);
        command.Parameters.AddWithValue("$machineKey", machineKey);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$name", machineKey);
        command.Parameters.AddWithValue("$isActive", isActive ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<ScenarioDefinition>> InvokeBuildScenarioCandidatesAsync(RolePlayEngineService engine, RolePlaySession session)
    {
        var method = typeof(RolePlayEngineService).GetMethod("BuildScenarioCandidatesAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildScenarioCandidatesAsync method not found.");

        var taskObject = method.Invoke(engine, [session, CancellationToken.None]) as Task
            ?? throw new InvalidOperationException("BuildScenarioCandidatesAsync invocation did not return a task.");

        await taskObject.ConfigureAwait(false);

        var resultProperty = taskObject.GetType().GetProperty("Result")
            ?? throw new InvalidOperationException("BuildScenarioCandidatesAsync task did not expose a Result property.");

        return (IReadOnlyList<ScenarioDefinition>?)resultProperty.GetValue(taskObject)
            ?? [];
    }

    private static AdaptiveScenarioState CreateState() => new()
    {
        SessionId = "session-1",
        CurrentPhase = NarrativePhase.BuildUp,
        ActiveFormulaVersion = "rpv2-default",
        CharacterSnapshots =
        [
            new CharacterStatProfileV2 { CharacterId = "char-a", Desire = 80, Restraint = 30, Dominance = 50, Loyalty = 50, SelfRespect = 50, RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 50, ["Connection"] = 55 } }
        ]
    };

    private sealed class StubNarrativeGateProfileService : INarrativeGateProfileService
    {
        private readonly NarrativeGateProfile _profile = new()
        {
            Id = "default-profile",
            Name = "Defaults",
            IsDefault = true,
            Rules =
            [
                new() { SortOrder = 1, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 60m },
                new() { SortOrder = 2, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.AverageDesire, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 65m },
                new() { SortOrder = 3, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.AverageRestraint, Comparator = NarrativeGateComparators.LessThanOrEqual, Threshold = 45m },
                new() { SortOrder = 4, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.TurnsSinceCommitment, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 3m },
                new() { SortOrder = 5, FromPhase = "Approaching", ToPhase = "Climax", MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 80m },
                new() { SortOrder = 6, FromPhase = "Approaching", ToPhase = "Climax", MetricKey = NarrativeGateMetricKeys.AverageDesire, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 75m },
                new() { SortOrder = 7, FromPhase = "Approaching", ToPhase = "Climax", MetricKey = NarrativeGateMetricKeys.AverageRestraint, Comparator = NarrativeGateComparators.LessThanOrEqual, Threshold = 35m },
                new() { SortOrder = 8, FromPhase = "Climax", ToPhase = "Reset", MetricKey = NarrativeGateMetricKeys.TurnsSinceCommitment, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 12m }
            ]
        };

        public Task<NarrativeGateProfile> SaveAsync(NarrativeGateProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(profile);

        public Task<List<NarrativeGateProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<NarrativeGateProfile> { _profile });

        public Task<NarrativeGateProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<NarrativeGateProfile?>(_profile);

        public Task<NarrativeGateProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<NarrativeGateProfile?>(_profile);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
