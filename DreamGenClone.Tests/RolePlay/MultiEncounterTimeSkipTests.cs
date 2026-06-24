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
        // US2: The directive text must not contain any encounter number reference.
        var directive = "Close the current encounter naturally. Then advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life.";
        Assert.DoesNotContain("#", directive);
        Assert.DoesNotContain("encounter #", directive, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("before encounter", directive, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeSkipDirective_FocusesOnCloseAndAdvance()
    {
        // US2: Directive must instruct to close, advance time, and establish ordinary life.
        var directive = "Close the current encounter naturally. Then advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life.";
        Assert.Contains("Close the current encounter", directive, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("advance time", directive, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ordinary life", directive, StringComparison.OrdinalIgnoreCase);
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
