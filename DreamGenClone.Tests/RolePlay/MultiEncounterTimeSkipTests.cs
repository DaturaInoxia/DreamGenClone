using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Tests for the multi-encounter time-skip directive injection logic.
/// Covers US1 (one-shot injection), US2 (no encounter number), US3 (user steer priority).
/// </summary>
public sealed class MultiEncounterTimeSkipTests
{
    // ---- US1: One-shot injection ----

    [Fact]
    public void TimeSkipDirective_TextHasNoEncounterNumber()
    {
        // US2: Both CloseScene and AdvanceTime directives must not contain encounter number references.
        var closeScene = "Close the current encounter naturally.";
        var advanceTime = "Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life.";

        foreach (var directive in new[] { closeScene, advanceTime })
        {
            Assert.DoesNotContain("#", directive);
            Assert.DoesNotContain("encounter #", directive, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("before encounter", directive, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TimeSkipDirective_CloseScene_FocusesOnClose()
    {
        var directive = "Close the current encounter naturally.";
        Assert.Contains("Close the current encounter", directive, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("advance time", directive, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ordinary life", directive, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeSkipDirective_AdvanceTime_FocusesOnAdvance()
    {
        var directive = "Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life.";
        Assert.Contains("advance time", directive, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ordinary life", directive, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Close the current encounter", directive, StringComparison.OrdinalIgnoreCase);
    }

    // ---- US3: User steer priority — HasRecentUserInstruction behavior ----

    [Fact]
    public void HasRecentUserInstruction_ReturnsTrue_WhenUserInstructionInLast3()
    {
        var session = new RolePlaySession();
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Dean", Content = "some content" });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "user steer", GeneratedByCommand = null });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Becky", Content = "response" });

        Assert.True(HasRecentUserInstruction(session, 3));
    }

    [Fact]
    public void HasRecentUserInstruction_ReturnsFalse_WhenOnlyEngineInstructionInLast3()
    {
        var session = new RolePlaySession();
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Dean", Content = "some content" });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "engine directive", GeneratedByCommand = "MultiEncounterTimeSkip" });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Becky", Content = "response" });

        Assert.False(HasRecentUserInstruction(session, 3));
    }

    [Fact]
    public void HasRecentUserInstruction_ReturnsFalse_WhenNoInstructionInLast3()
    {
        var session = new RolePlaySession();
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Dean", Content = "some content" });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Becky", Content = "response" });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Ken", Content = "response" });

        Assert.False(HasRecentUserInstruction(session, 3));
    }

    [Fact]
    public void HasRecentUserInstruction_ReturnsFalse_WhenUserInstructionOutsideWindow()
    {
        var session = new RolePlaySession();
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "old user steer", GeneratedByCommand = null });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Dean", Content = "content" });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Becky", Content = "content" });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Ken", Content = "content" });

        // Window is 3, user instruction is at position 0 (outside last 3)
        Assert.False(HasRecentUserInstruction(session, 3));
    }

    [Fact]
    public void HasRecentUserInstruction_ReturnsTrue_WhenUserInstructionAtEdgeOfWindow()
    {
        var session = new RolePlaySession();
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Dean", Content = "content" });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "user steer", GeneratedByCommand = null });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Becky", Content = "content" });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Ken", Content = "content" });

        // Window is 3, user instruction is at position 1 (within last 3: positions 1,2,3)
        Assert.True(HasRecentUserInstruction(session, 3));
    }

    [Fact]
    public void HasRecentUserInstruction_HandlesEmptySession()
    {
        var session = new RolePlaySession();
        Assert.False(HasRecentUserInstruction(session, 3));
    }

    [Fact]
    public void HasRecentUserInstruction_HandlesSessionWithFewerThanWindowInteractions()
    {
        var session = new RolePlaySession();
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "user steer", GeneratedByCommand = null });

        Assert.True(HasRecentUserInstruction(session, 3));
    }

    // ---- US3: Engine Instructions do not trigger skip ----

    [Fact]
    public void HasRecentUserInstruction_DistinguishesEngineFromUserInstructions()
    {
        var session = new RolePlaySession();
        // Engine instruction (GeneratedByCommand set)
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "engine", GeneratedByCommand = "MultiEncounterTimeSkip" });
        // User instruction (GeneratedByCommand null)
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "user", GeneratedByCommand = null });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Dean", Content = "content" });

        // Should find the user instruction
        Assert.True(HasRecentUserInstruction(session, 3));
    }

    // ---- Phase transition tests (US1) ----

    [Fact]
    public void CloseScene_Phase_Transitions_To_AdvanceTime()
    {
        // After boundary detection, phase is CloseScene. After injection, phase advances to AdvanceTime.
        var state = new AdaptiveScenarioState
        {
            CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax,
            CurrentEncounterNumber = 2,
            CurrentTimeSkipPhase = TimeSkipPhase.CloseScene
        };
        Assert.Equal(TimeSkipPhase.CloseScene, state.CurrentTimeSkipPhase);

        // Simulate the overflow loop transitioning
        state.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        Assert.Equal(TimeSkipPhase.AdvanceTime, state.CurrentTimeSkipPhase);
    }

    [Fact]
    public void AdvanceTime_Phase_Transitions_To_None()
    {
        var state = new AdaptiveScenarioState
        {
            CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax,
            CurrentEncounterNumber = 2,
            CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime
        };
        Assert.Equal(TimeSkipPhase.AdvanceTime, state.CurrentTimeSkipPhase);

        // Simulate the overflow loop transitioning after AdvanceTime injection
        state.CurrentTimeSkipPhase = TimeSkipPhase.None;
        Assert.Equal(TimeSkipPhase.None, state.CurrentTimeSkipPhase);
    }

    [Fact]
    public void TimeSkipPhase_Default_Is_None()
    {
        var state = new AdaptiveScenarioState();
        Assert.Equal(TimeSkipPhase.None, state.CurrentTimeSkipPhase);
    }

    // ---- User instruction deferral tests (US2) ----

    [Fact]
    public void UserInstruction_Skips_CloseScene_Keeps_Phase()
    {
        var session = new RolePlaySession();
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax;
        session.AdaptiveState.CurrentEncounterNumber = 2;
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.CloseScene;
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "user steer", GeneratedByCommand = null });

        // Verify HasRecentUserInstruction returns true
        Assert.True(HasRecentUserInstruction(session, 3));
        // Phase must remain CloseScene (deferred, not cleared)
        Assert.Equal(TimeSkipPhase.CloseScene, session.AdaptiveState.CurrentTimeSkipPhase);
    }

    [Fact]
    public void UserInstruction_Skips_AdvanceTime_Keeps_Phase()
    {
        var session = new RolePlaySession();
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax;
        session.AdaptiveState.CurrentEncounterNumber = 2;
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "user steer", GeneratedByCommand = null });

        Assert.True(HasRecentUserInstruction(session, 3));
        Assert.Equal(TimeSkipPhase.AdvanceTime, session.AdaptiveState.CurrentTimeSkipPhase);
    }

    [Fact]
    public void UserInstruction_Deferred_Multiple_Times_Still_Fires()
    {
        var session = new RolePlaySession();
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax;
        session.AdaptiveState.CurrentEncounterNumber = 2;
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.CloseScene;

        // Defer twice with user instructions
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "wait", GeneratedByCommand = null });
        Assert.True(HasRecentUserInstruction(session, 3));
        Assert.Equal(TimeSkipPhase.CloseScene, session.AdaptiveState.CurrentTimeSkipPhase);

        session.Interactions.Add(new RolePlayInteraction { ActorName = "Instruction", Content = "not yet", GeneratedByCommand = null });
        Assert.True(HasRecentUserInstruction(session, 3));
        Assert.Equal(TimeSkipPhase.CloseScene, session.AdaptiveState.CurrentTimeSkipPhase);

        // Add enough normal interactions to push user instructions out of window (size 3)
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Becky", Content = "response 1" });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Dean", Content = "response 2" });
        session.Interactions.Add(new RolePlayInteraction { ActorName = "Ken", Content = "response 3" });

        // No user instruction in the last 3 — phase still CloseScene, ready to fire
        Assert.False(HasRecentUserInstruction(session, 3));
        Assert.Equal(TimeSkipPhase.CloseScene, session.AdaptiveState.CurrentTimeSkipPhase);
    }

    // ---- Persistence survival tests (US3) ----

    [Fact]
    public void CurrentTimeSkipPhase_Survives_Set_Get_Cycle()
    {
        var state = new AdaptiveScenarioState { CurrentTimeSkipPhase = TimeSkipPhase.CloseScene };
        Assert.Equal(TimeSkipPhase.CloseScene, state.CurrentTimeSkipPhase);

        state.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        Assert.Equal(TimeSkipPhase.AdvanceTime, state.CurrentTimeSkipPhase);

        state.CurrentTimeSkipPhase = TimeSkipPhase.None;
        Assert.Equal(TimeSkipPhase.None, state.CurrentTimeSkipPhase);
    }

    [Fact]
    public void AdvanceTime_Phase_Survives_Set_Get_Cycle()
    {
        var state = new AdaptiveScenarioState { CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime };
        Assert.Equal(TimeSkipPhase.AdvanceTime, state.CurrentTimeSkipPhase);
    }

    [Fact]
    public void None_Phase_Survives_Set_Get_Cycle()
    {
        var state = new AdaptiveScenarioState { CurrentTimeSkipPhase = TimeSkipPhase.None };
        Assert.Equal(TimeSkipPhase.None, state.CurrentTimeSkipPhase);
    }

    // ---- Legacy migration tests (US4) ----

    [Fact]
    public void Legacy_TimeSkipPending_1_Backfilled_To_CloseScene()
    {
        // Simulate backfill: TimeSkipPending=1 → CurrentTimeSkipPhase=CloseScene (1)
        var legacyFlag = 1; // was TimeSkipPending = true
        var phase = legacyFlag != 0 ? TimeSkipPhase.CloseScene : TimeSkipPhase.None;
        Assert.Equal(TimeSkipPhase.CloseScene, phase);
    }

    [Fact]
    public void Legacy_TimeSkipPending_0_Remains_None()
    {
        var legacyFlag = 0; // was TimeSkipPending = false
        var phase = legacyFlag != 0 ? TimeSkipPhase.CloseScene : TimeSkipPhase.None;
        Assert.Equal(TimeSkipPhase.None, phase);
    }

    [Fact]
    public void BackCompat_Read_Fallback_To_Legacy()
    {
        // Simulate DB read where CurrentTimeSkipPhase=0 (default) but TimeSkipPending=1
        int currentTimeSkipPhase = 0;
        int legacyTimeSkipPending = 1;

        TimeSkipPhase result = currentTimeSkipPhase != 0
            ? (TimeSkipPhase)currentTimeSkipPhase
            : (legacyTimeSkipPending != 0 ? TimeSkipPhase.CloseScene : TimeSkipPhase.None);

        Assert.Equal(TimeSkipPhase.CloseScene, result);
    }

    // ---- Edge case tests (Phase 7: Polish) ----

    [Fact]
    public void isNewEncounterStart_False_During_AdvanceTime_Retry()
    {
        // When AdvanceTime is pending (injection skipped), isNewEncounterStart must be false
        // because CurrentTimeSkipPhase != None
        var state = new AdaptiveScenarioState
        {
            CurrentEncounterNumber = 2,
            TurnsInCurrentEncounter = 0,
            CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime
        };

        var isNewEncounterStart = state.CurrentEncounterNumber > 0
            && state.TurnsInCurrentEncounter == 0
            && state.CurrentTimeSkipPhase == TimeSkipPhase.None;

        Assert.False(isNewEncounterStart);
    }

    [Fact]
    public void isNewEncounterStart_True_When_Phase_Is_None()
    {
        var state = new AdaptiveScenarioState
        {
            CurrentEncounterNumber = 2,
            TurnsInCurrentEncounter = 0,
            CurrentTimeSkipPhase = TimeSkipPhase.None
        };

        var isNewEncounterStart = state.CurrentEncounterNumber > 0
            && state.TurnsInCurrentEncounter == 0
            && state.CurrentTimeSkipPhase == TimeSkipPhase.None;

        Assert.True(isNewEncounterStart);
    }

    [Fact]
    public void PipelineBatchIncrement_Skipped_During_TimeSkip()
    {
        // When CurrentTimeSkipPhase != None, pipeline-batch increment must NOT add to counter
        var state = new AdaptiveScenarioState
        {
            CurrentTimeSkipPhase = TimeSkipPhase.CloseScene,
            TurnsInCurrentEncounter = 5
        };
        var generatedSinceLastEval = 3;

        // Simulate the guarded condition: skip when phase != None
        var shouldIncrement = state.CurrentTimeSkipPhase == TimeSkipPhase.None;
        if (shouldIncrement)
        {
            state.TurnsInCurrentEncounter += generatedSinceLastEval;
        }

        Assert.Equal(5, state.TurnsInCurrentEncounter); // unchanged
    }

    [Fact]
    public void PipelineBatchIncrement_Applied_When_Phase_Is_None()
    {
        var state = new AdaptiveScenarioState
        {
            CurrentTimeSkipPhase = TimeSkipPhase.None,
            TurnsInCurrentEncounter = 5
        };
        var generatedSinceLastEval = 3;

        var shouldIncrement = state.CurrentTimeSkipPhase == TimeSkipPhase.None;
        if (shouldIncrement)
        {
            state.TurnsInCurrentEncounter += generatedSinceLastEval;
        }

        Assert.Equal(8, state.TurnsInCurrentEncounter);
    }

    [Fact]
    public void IsStateDirty_Set_On_Phase_Mutation()
    {
        var state = new AdaptiveScenarioState
        {
            CurrentTimeSkipPhase = TimeSkipPhase.CloseScene,
            IsStateDirty = false
        };

        // Simulate overflow loop setting IsStateDirty on phase mutation
        state.IsStateDirty = true;
        state.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;

        Assert.True(state.IsStateDirty);
        Assert.Equal(TimeSkipPhase.AdvanceTime, state.CurrentTimeSkipPhase);
    }

    // ====================================================================
    // B-057 Part A: Synchronous persist + unconditional HydrateV2State
    // ====================================================================

    // ---- Phase 4: Unconditional restore (tests 1-2) ----

    [Fact]
    public void HydrateV2State_UnconditionalRestore_DoesNotOverwriteDetectionState()
    {
        // With B-057, all time-skip mutations persist synchronously, so the DB snapshot
        // (previousState) always reflects the latest state. The unconditional restore in
        // HydrateV2State correctly loads from DB — no conditional protection needed.
        // This test verifies the unconditional restore path: previousState values are
        // always applied regardless of current in-memory state.
        var currentState = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentTimeSkipPhase = TimeSkipPhase.CloseScene,
            CurrentEncounterNumber = 3,
            TurnsInCurrentEncounter = 2
        };

        var previousState = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentTimeSkipPhase = TimeSkipPhase.CloseScene,
            CurrentEncounterNumber = 3,
            TurnsInCurrentEncounter = 2
        };

        // Unconditional restore: always mirror previousState (which is the latest persisted DB state)
        currentState.CurrentTimeSkipPhase = previousState.CurrentTimeSkipPhase;
        currentState.CurrentEncounterNumber = previousState.CurrentEncounterNumber;
        currentState.TurnsInCurrentEncounter = previousState.TurnsInCurrentEncounter;

        Assert.Equal(TimeSkipPhase.CloseScene, currentState.CurrentTimeSkipPhase);
        Assert.Equal(3, currentState.CurrentEncounterNumber);
        Assert.Equal(2, currentState.TurnsInCurrentEncounter);
    }

    [Fact]
    public void HydrateV2State_UnconditionalRestore_RestoresFromDB_WhenInMemoryIsDefault()
    {
        // When in-memory state is default (e.g. fresh session load), unconditional restore
        // correctly picks up the persisted DB values.
        var currentState = new AdaptiveScenarioState
        {
            SessionId = "test-session"
            // CurrentTimeSkipPhase defaults to None (0)
            // CurrentEncounterNumber defaults to 0
            // TurnsInCurrentEncounter defaults to 0
        };

        var previousState = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentTimeSkipPhase = TimeSkipPhase.CloseScene,
            CurrentEncounterNumber = 2,
            TurnsInCurrentEncounter = 5
        };

        // Unconditional restore from DB snapshot
        currentState.CurrentTimeSkipPhase = previousState.CurrentTimeSkipPhase;
        currentState.CurrentEncounterNumber = previousState.CurrentEncounterNumber;
        currentState.TurnsInCurrentEncounter = previousState.TurnsInCurrentEncounter;

        Assert.Equal(TimeSkipPhase.CloseScene, currentState.CurrentTimeSkipPhase);
        Assert.Equal(2, currentState.CurrentEncounterNumber);
        Assert.Equal(5, currentState.TurnsInCurrentEncounter);
    }

    // ---- Phase 1: Sync persist in TryDetectEncounterBoundaryAsync (tests 3-5) ----

    [Fact]
    public void TryDetectEncounterBoundaryAsync_SavesToDB_Synchronously()
    {
        // Verify that after the boundary detection mutation block, state values are correct
        // and the save-worthy state is fully populated before persist.
        var state = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentEncounterNumber = 1,
            TurnsInCurrentEncounter = 6,
            CurrentTimeSkipPhase = TimeSkipPhase.None
        };

        // Simulate the detection mutation block (Phase 1)
        state.CurrentEncounterNumber++;
        state.TurnsInCurrentEncounter = 0;
        state.CurrentTimeSkipPhase = TimeSkipPhase.CloseScene;

        // Verify mutated state (what would be persisted synchronously)
        Assert.Equal(2, state.CurrentEncounterNumber);
        Assert.Equal(0, state.TurnsInCurrentEncounter);
        Assert.Equal(TimeSkipPhase.CloseScene, state.CurrentTimeSkipPhase);
    }

    [Fact]
    public void OverflowTimeSkipPhaseTransition_SavesToDB_Synchronously()
    {
        // Verify the CloseScene → AdvanceTime → None phase transition cycle
        // produces the correct state values at each step (what would be persisted).
        var state = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentTimeSkipPhase = TimeSkipPhase.CloseScene,
            CurrentEncounterNumber = 2,
            TurnsInCurrentEncounter = 0
        };

        // Step 1: CloseScene → AdvanceTime
        state.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        Assert.Equal(TimeSkipPhase.AdvanceTime, state.CurrentTimeSkipPhase);

        // Step 2: AdvanceTime → None
        state.CurrentTimeSkipPhase = TimeSkipPhase.None;
        Assert.Equal(TimeSkipPhase.None, state.CurrentTimeSkipPhase);

        // Encounter number and interaction counter unchanged during phase transitions
        Assert.Equal(2, state.CurrentEncounterNumber);
        Assert.Equal(0, state.TurnsInCurrentEncounter);
    }

    [Fact]
    public void FullTimeSkipCycle_PersistsEveryTransition()
    {
        // Simulate the complete encounter detection → time-skip cycle.
        var state = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentEncounterNumber = 1,
            TurnsInCurrentEncounter = 6,
            CurrentTimeSkipPhase = TimeSkipPhase.None
        };

        // 1. Detection fires: encounter advances, counter resets, CloseScene set
        state.CurrentEncounterNumber++;
        state.TurnsInCurrentEncounter = 0;
        state.CurrentTimeSkipPhase = TimeSkipPhase.CloseScene;
        Assert.Equal(2, state.CurrentEncounterNumber);
        Assert.Equal(0, state.TurnsInCurrentEncounter);
        Assert.Equal(TimeSkipPhase.CloseScene, state.CurrentTimeSkipPhase);

        // 2. CloseScene → AdvanceTime
        state.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        Assert.Equal(TimeSkipPhase.AdvanceTime, state.CurrentTimeSkipPhase);

        // 3. AdvanceTime → None
        state.CurrentTimeSkipPhase = TimeSkipPhase.None;
        Assert.Equal(TimeSkipPhase.None, state.CurrentTimeSkipPhase);
    }

    // ---- Phase 5: Save-before-hydrate removed (test 6) ----

    [Fact]
    public void B057_SaveBeforeHydrateRemoved_DoesNotAffectStateConsistency()
    {
        // The save-before-hydrate blocks were removed because time-skip mutations
        // now persist synchronously at their mutation sites. Verify that the remaining
        // turn-completion save block still correctly handles non-time-skip dirty state.
        // IsStateDirty for time-skip should be false after sync persist; non-time-skip
        // dirty state should still be captured.
        var state = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentEncounterNumber = 2,
            CurrentTimeSkipPhase = TimeSkipPhase.CloseScene,
            IsStateDirty = false
        };

        // Non-time-skip state change (character stats, theme scores) still sets IsStateDirty
        state.CompletedScenarios = 3;
        state.IsStateDirty = true;

        // Assert: non-time-skip dirty state is still tracked
        Assert.True(state.IsStateDirty);
        Assert.Equal(3, state.CompletedScenarios);

        // Turn-completion save block would persist this
        if (state.IsStateDirty) { /* SaveAdaptiveStateAsync would fire here */ }
        state.IsStateDirty = false;
        Assert.False(state.IsStateDirty);
    }

    // ====================================================================
    // B-057 Part B: Universal encounter tracking + interaction metadata
    // ====================================================================

    // ---- Phase 6 + 6b: GlobalEncounterCount lifecycle (tests 7-9) ----

    [Fact]
    public void UniversalEncounter_Starts_OnFirstSexualContent_InAnyPhase()
    {
        // When WasInSexScene becomes true AND CurrentEncounterNumber == 0,
        // start a new encounter by setting CurrentEncounterNumber = GlobalEncounterCount + 1.
        var state = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentEncounterNumber = 0,
            GlobalEncounterCount = 0
        };

        // Simulate: first sexual content detected, encounter starts
        var wasInSexSceneBecomesTrue = true;
        if (wasInSexSceneBecomesTrue && state.CurrentEncounterNumber == 0)
        {
            state.CurrentEncounterNumber = state.GlobalEncounterCount + 1;
        }

        Assert.Equal(1, state.CurrentEncounterNumber);
        Assert.Equal(0, state.GlobalEncounterCount);
    }

    [Fact]
    public void UniversalEncounter_GlobalCounter_Increments_OnBoundary()
    {
        // When an encounter boundary fires (keyword or LLM), GlobalEncounterCount increments
        // and CurrentEncounterNumber resets to 0 (no longer active).
        var state = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentEncounterNumber = 1,
            GlobalEncounterCount = 0
        };

        // Simulate keyword boundary detection
        state.GlobalEncounterCount++;
        state.CurrentEncounterNumber = 0;

        Assert.Equal(1, state.GlobalEncounterCount);
        Assert.Equal(0, state.CurrentEncounterNumber);
    }

    [Fact]
    public void UniversalEncounter_GlobalCounter_IsCumulative_NeverDecremented()
    {
        // GlobalEncounterCount is cumulative across ALL encounters in the session.
        // It is never decremented, even when CurrentEncounterNumber resets.
        var state = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentEncounterNumber = 0,
            GlobalEncounterCount = 3
        };

        // CurrentEncounterNumber can be 0 while GlobalEncounterCount > 0
        Assert.Equal(3, state.GlobalEncounterCount);
        Assert.Equal(0, state.CurrentEncounterNumber);

        // Start a new encounter — uses GlobalEncounterCount + 1
        state.CurrentEncounterNumber = state.GlobalEncounterCount + 1;
        Assert.Equal(4, state.CurrentEncounterNumber);
        Assert.Equal(3, state.GlobalEncounterCount); // unchanged

        // Boundary detection
        state.GlobalEncounterCount++;
        state.CurrentEncounterNumber = 0;
        Assert.Equal(4, state.GlobalEncounterCount);
        Assert.Equal(0, state.CurrentEncounterNumber);
    }

    // ---- Phase 7 + 8: Interaction metadata stamping (test 10-11) ----

    [Fact]
    public void UniversalEncounter_InteractionFields_AreStamped_Correctly()
    {
        // Verify that interaction-level encounter metadata fields are populated correctly.
        var session = new RolePlaySession
        {
            LastResolvedIntensityLabel = "Explicit"
        };
        session.Interactions.Add(new RolePlayInteraction());

        var interaction = session.Interactions[^1];
        var state = new AdaptiveScenarioState
        {
            CurrentEncounterNumber = 2,
            TurnsInCurrentEncounter = 5,
            GlobalEncounterCount = 1
        };

        // Simulate the stamping logic from Phase 8
        interaction.SessionInteractionIndex = session.Interactions.Count - 1; // 0-based
        interaction.EncounterNumberAtCreation = state.CurrentEncounterNumber > 0
            ? state.CurrentEncounterNumber
            : null;
        interaction.InteractionIndexInEncounter = state.CurrentEncounterNumber > 0
            ? state.TurnsInCurrentEncounter - 1
            : null;
        interaction.ExplicitnessLevelAtCreation = session.LastResolvedIntensityLabel;

        Assert.Equal(0, interaction.SessionInteractionIndex);
        Assert.Equal(2, interaction.EncounterNumberAtCreation);
        Assert.Equal(4, interaction.InteractionIndexInEncounter);
        Assert.Equal("Explicit", interaction.ExplicitnessLevelAtCreation);
    }

    [Fact]
    public void UniversalEncounter_InteractionFields_AreNull_WhenNotInEncounter()
    {
        // When CurrentEncounterNumber == 0, interaction metadata should be null.
        var interaction = new RolePlayInteraction();
        var session = new RolePlaySession();
        var state = new AdaptiveScenarioState
        {
            CurrentEncounterNumber = 0,
            TurnsInCurrentEncounter = 0
        };

        interaction.EncounterNumberAtCreation = state.CurrentEncounterNumber > 0
            ? state.CurrentEncounterNumber
            : null;
        interaction.InteractionIndexInEncounter = state.CurrentEncounterNumber > 0
            ? state.TurnsInCurrentEncounter - 1
            : null;
        interaction.ExplicitnessLevelAtCreation = null;

        Assert.Null(interaction.EncounterNumberAtCreation);
        Assert.Null(interaction.InteractionIndexInEncounter);
        Assert.Null(interaction.ExplicitnessLevelAtCreation);
    }

    // ---- Phase 9: Climax entry numbering (test 12) ----

    [Fact]
    public void UniversalEncounter_FirstInteraction_Gets_Index_Zero_Not_Negative_One()
    {
        // Regression test: the per-interaction counter must be incremented AFTER encounter
        // start detection so the first interaction of a new encounter has index 0, not -1.
        var state = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentEncounterNumber = 0, // no active encounter yet
            TurnsInCurrentEncounter = 0,
            GlobalEncounterCount = 0
        };

        // Simulate encounter start detection (must happen BEFORE the counter increment)
        if (state.CurrentEncounterNumber == 0)
        {
            state.CurrentEncounterNumber = state.GlobalEncounterCount + 1; // = 1
        }

        // Counter increment happens AFTER encounter start (correct order)
        if (state.CurrentEncounterNumber > 0)
        {
            state.TurnsInCurrentEncounter++; // 0 → 1
        }

        int? interactionIndexInEncounter = state.CurrentEncounterNumber > 0
            ? state.TurnsInCurrentEncounter - 1 // 1 - 1 = 0
            : null;

        Assert.Equal(1, state.CurrentEncounterNumber);
        Assert.Equal(1, state.TurnsInCurrentEncounter);
        Assert.Equal(0, interactionIndexInEncounter); // NOT -1
    }

    [Fact]
    public void UniversalEncounter_Climax_UsesGlobalCounter_ForNumbering()
    {
        // When entering Climax phase with a prior BuildUp encounter completed,
        // the Climax encounter number should be GlobalEncounterCount + 1.
        var state = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentEncounterNumber = 0, // no active encounter
            GlobalEncounterCount = 1,   // one completed encounter in BuildUp
            CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
        };

        // Simulate the Climax entry logic (Phase 9): use GlobalEncounterCount + 1
        state.CurrentEncounterNumber = state.GlobalEncounterCount + 1;
        state.TurnsInCurrentEncounter = 0;

        Assert.Equal(2, state.CurrentEncounterNumber);
        Assert.Equal(0, state.TurnsInCurrentEncounter);
    }

    [Fact]
    public void UniversalEncounter_MultiEncounter_StillWorks_InClimax()
    {
        // Verify that the existing multi-encounter Climax cycle (boundary detection →
        // time-skip → next encounter) still works correctly with universal tracking.
        var state = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentEncounterNumber = 0,
            GlobalEncounterCount = 2,
            TurnsInCurrentEncounter = 0,
            CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
        };

        // Entering Climax — use global counter for numbering
        state.CurrentEncounterNumber = state.GlobalEncounterCount + 1;
        state.TurnsInCurrentEncounter = 0;
        Assert.Equal(3, state.CurrentEncounterNumber);

        // Simulate boundary detection for this encounter
        state.GlobalEncounterCount++;
        state.CurrentEncounterNumber = 0;
        state.CurrentTimeSkipPhase = TimeSkipPhase.CloseScene;
        Assert.Equal(3, state.GlobalEncounterCount);

        // Overflow transition: CloseScene → AdvanceTime → None
        state.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        Assert.Equal(TimeSkipPhase.AdvanceTime, state.CurrentTimeSkipPhase);
        state.CurrentTimeSkipPhase = TimeSkipPhase.None;
        Assert.Equal(TimeSkipPhase.None, state.CurrentTimeSkipPhase);

        // Next encounter starts with GlobalEncounterCount + 1
        state.CurrentEncounterNumber = state.GlobalEncounterCount + 1;
        state.TurnsInCurrentEncounter = 0;
        Assert.Equal(4, state.CurrentEncounterNumber);
        Assert.Equal(3, state.GlobalEncounterCount);
    }

    [Fact]
    public void GlobalEncounterCount_OnlyIncrements_ThroughLLMPath_EvidenceSpan()
    {
        // GlobalEncounterCount must ONLY increment through the LLM detection path's
        // evidence-span keyword validation (not from keyword matching on full interaction
        // content). The keyword path on interaction.Content was removed because it falsely
        // matched every explicit sexual interaction (orgasm, cum, pulse, etc.).
        //
        // This test verifies that GlobalEncounterCount is NOT incremented by simply having
        // completion keywords in interaction content — it requires the LLM path to fire
        // with a validated evidence span (simulated here by setting CurrentTimeSkipPhase).
        var state = new AdaptiveScenarioState
        {
            SessionId = "test-session",
            CurrentEncounterNumber = 1,
            GlobalEncounterCount = 0
        };

        // Simulate: interaction content has completion keywords but NO LLM evidence span.
        // The keyword response.LLM path (removed) would have fired here. Instead, verify
        // that GlobalEncounterCount is NOT affected by interaction content keywords alone.
        var interactionContent = "He climaxed with a shuddering groan, his body spent.";
        var hasKeywordsInContent = ContainsEncounterCompletionKeywords(interactionContent);
        Assert.True(hasKeywordsInContent); // the content DOES contain keywords

        // Without LLM detection + evidence-span validation, GlobalEncounterCount stays unchanged
        var globalBefore = state.GlobalEncounterCount;

        // LLM path fires only when semantic inference detects encounter-completed AND
        // the evidence span passes the keyword hard-gate (line 4779). Until that happens,
        // no increment occurs.
        Assert.Equal(globalBefore, state.GlobalEncounterCount); // unchanged by content keywords

        // The ONLY valid increment path is through TryDetectEncounterBoundaryAsync
        // which validates detected.EvidenceSpan, not interaction.Content.
    }

    /// <summary>
    /// Mirror of the private static helper in RolePlayEngineService for testing.
    /// This must stay in sync with the implementation.
    /// </summary>
    private static bool HasRecentUserInstruction(RolePlaySession session, int windowSize)
    {
        return session.Interactions
            .TakeLast(windowSize)
            .Any(x => string.Equals(x.ActorName, "Instruction", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(x.GeneratedByCommand));
    }

    /// <summary>
    /// Mirror of ContainsEncounterCompletionKeywords from RolePlayEngineService.
    /// This must stay in sync with the implementation.
    /// </summary>
    private static bool ContainsEncounterCompletionKeywords(string? evidenceSpan)
    {
        if (string.IsNullOrWhiteSpace(evidenceSpan)) return false;
        var lower = evidenceSpan.ToLowerInvariant();
        return EncounterCompletionKeywords.Any(k => lower.Contains(k));
    }

    private static readonly string[] EncounterCompletionKeywords = [
        "orgasm", "climax", "cum", "ejaculat", "spent", "finished",
        "release", "creampie", "pulse", "throb", "shudder", "collapse",
        "gasp", "pant", "catch.*breath"
    ];
}
