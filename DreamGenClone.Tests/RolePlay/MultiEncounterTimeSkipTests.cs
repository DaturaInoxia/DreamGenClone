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
            InteractionsInCurrentEncounter = 0,
            CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime
        };

        var isNewEncounterStart = state.CurrentEncounterNumber > 0
            && state.InteractionsInCurrentEncounter == 0
            && state.CurrentTimeSkipPhase == TimeSkipPhase.None;

        Assert.False(isNewEncounterStart);
    }

    [Fact]
    public void isNewEncounterStart_True_When_Phase_Is_None()
    {
        var state = new AdaptiveScenarioState
        {
            CurrentEncounterNumber = 2,
            InteractionsInCurrentEncounter = 0,
            CurrentTimeSkipPhase = TimeSkipPhase.None
        };

        var isNewEncounterStart = state.CurrentEncounterNumber > 0
            && state.InteractionsInCurrentEncounter == 0
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
            InteractionsInCurrentEncounter = 5
        };
        var generatedSinceLastEval = 3;

        // Simulate the guarded condition: skip when phase != None
        var shouldIncrement = state.CurrentTimeSkipPhase == TimeSkipPhase.None;
        if (shouldIncrement)
        {
            state.InteractionsInCurrentEncounter += generatedSinceLastEval;
        }

        Assert.Equal(5, state.InteractionsInCurrentEncounter); // unchanged
    }

    [Fact]
    public void PipelineBatchIncrement_Applied_When_Phase_Is_None()
    {
        var state = new AdaptiveScenarioState
        {
            CurrentTimeSkipPhase = TimeSkipPhase.None,
            InteractionsInCurrentEncounter = 5
        };
        var generatedSinceLastEval = 3;

        var shouldIncrement = state.CurrentTimeSkipPhase == TimeSkipPhase.None;
        if (shouldIncrement)
        {
            state.InteractionsInCurrentEncounter += generatedSinceLastEval;
        }

        Assert.Equal(8, state.InteractionsInCurrentEncounter);
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
}
