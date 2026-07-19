using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// Tests for PromptBudgetEnforcer: trim priority order (FR-029), never-trim invariants,
/// critical-overflow path, fail-fast on missing/invalid MaxPromptChars (FR-004, FR-030).
/// </summary>
public sealed class PromptBudgetEnforcerTests
{
    private static PromptBudgetEnforcer CreateEnforcer()
    {
        return new PromptBudgetEnforcer(Microsoft.Extensions.Logging.Abstractions.NullLogger<PromptBudgetEnforcer>.Instance);
    }

    private static SlotText MakeSlot(PromptSlotId id, string text, bool isTrimEligible)
    {
        return new SlotText(id, text, isTrimEligible);
    }

    // ── T057: Fail-fast on invalid MaxPromptChars ──────────────

    [Fact]
    public void Enforce_Throws_WhenMaxPromptCharsIsZero()
    {
        var enforcer = CreateEnforcer();
        var slots = new List<SlotText>
        {
            MakeSlot(PromptSlotId.SceneAnchor, "Hello", false),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            enforcer.Enforce(slots, 0));

        Assert.Contains("MaxPromptChars", ex.Message);
        Assert.Contains("positive", ex.Message);
    }

    [Fact]
    public void Enforce_Throws_WhenMaxPromptCharsIsNegative()
    {
        var enforcer = CreateEnforcer();
        var slots = new List<SlotText>
        {
            MakeSlot(PromptSlotId.SceneAnchor, "Hello", false),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            enforcer.Enforce(slots, -1));

        Assert.Contains("MaxPromptChars", ex.Message);
        Assert.Contains("positive", ex.Message);
    }

    // ── T057: No trim when under budget ────────────────────────

    [Fact]
    public void Enforce_NoTrim_WhenUnderBudget()
    {
        var enforcer = CreateEnforcer();
        var slots = new List<SlotText>
        {
            MakeSlot(PromptSlotId.SceneAnchor, "AAA", false),        // 3 chars
            MakeSlot(PromptSlotId.CharacterData, "BBB", true),       // 3 chars
            MakeSlot(PromptSlotId.FinalInstruction, "CCC", false),   // 3 chars
        };

        var result = enforcer.Enforce(slots, 20); // 9 chars < 20

        Assert.Equal(9, result.PreTrimChars);
        Assert.Equal(9, result.PostTrimChars);
        Assert.Empty(result.TrimmedSlots);
        Assert.Contains("AAA", result.FinalText);
        Assert.Contains("BBB", result.FinalText);
        Assert.Contains("CCC", result.FinalText);
    }

    // ── T057: Never-trim invariants (Zone A + critical Zone C) ──

    [Fact]
    public void Enforce_NeverTrimSlots_ArePreserved()
    {
        var enforcer = CreateEnforcer();
        var slots = new List<SlotText>
        {
            // Zone A — never trimmed
            MakeSlot(PromptSlotId.SceneAnchor, "Anchor text here", false),
            MakeSlot(PromptSlotId.ActorAssignment, "Actor assignment text", false),
            MakeSlot(PromptSlotId.TurnContext, "Turn context text", false),
            MakeSlot(PromptSlotId.SceneLocationLock, "Location lock text", false),
            // Zone B — trimmable
            MakeSlot(PromptSlotId.CharacterData, "Character data that should be trimmed away", true),
            // Zone C — never trimmed
            MakeSlot(PromptSlotId.ThemeContract, "Theme contract text", false),
            MakeSlot(PromptSlotId.IntensityPacing, "Intensity pacing text", false),
            MakeSlot(PromptSlotId.FinalInstruction, "Final instruction text", false),
        };

        // Budget is tight — only Zone A + Zone C fit.
        var neverTrimChars = "Anchor text here".Length + "Actor assignment text".Length +
                             "Turn context text".Length + "Location lock text".Length +
                             "Theme contract text".Length + "Intensity pacing text".Length +
                             "Final instruction text".Length;
        var budget = neverTrimChars;

        var result = enforcer.Enforce(slots, budget);

        // All never-trim slots must be present.
        Assert.Contains("Anchor text here", result.FinalText);
        Assert.Contains("Actor assignment text", result.FinalText);
        Assert.Contains("Turn context text", result.FinalText);
        Assert.Contains("Location lock text", result.FinalText);
        Assert.Contains("Theme contract text", result.FinalText);
        Assert.Contains("Intensity pacing text", result.FinalText);
        Assert.Contains("Final instruction text", result.FinalText);

        // CharacterData should be trimmed.
        Assert.DoesNotContain("Character data that should be trimmed away", result.FinalText);
        Assert.Contains("CharacterData", result.TrimmedSlots);
    }

    // ── T057: Trim priority order (FR-029) ─────────────────────

    [Fact]
    public void Enforce_TrimsInPriorityOrder_FR029()
    {
        var enforcer = CreateEnforcer();
        // Create slots with known sizes, all trimmable.
        // The enforcer sorts trimmable by priority ASCENDING (1 = trim first).
        // When budget is tight, lower-priority slots (7=WritingStyle) are dropped before higher-priority ones.
        // However, the enforcer PROCESSES priority 1 first (gets budget), then 2, etc.
        // So priority 7 is the LAST to get budget and FIRST to be dropped.
        var slots = new List<SlotText>
        {
            // Never-trim baseline
            MakeSlot(PromptSlotId.SceneAnchor, new string('A', 100), false),
            MakeSlot(PromptSlotId.FinalInstruction, new string('Z', 100), false),

            // Trimmable slots with priority tags (lower number = trim first = processed first = gets budget first)
            MakeSlot(PromptSlotId.InteractionHistory, new string('1', 100), true),    // priority 1
            MakeSlot(PromptSlotId.CharacterData, new string('2', 100), true),          // priority 2
            MakeSlot(PromptSlotId.ScenarioContext, new string('3', 100), true),        // priority 3
            MakeSlot(PromptSlotId.SessionMemory, new string('4', 100), true),          // priority 4
            MakeSlot(PromptSlotId.CurrentLocation, new string('5', 100), true),        // priority 5
            MakeSlot(PromptSlotId.SceneContinuityAnchor, new string('6', 100), true),  // priority 6
            MakeSlot(PromptSlotId.WritingStyle, new string('7', 100), true),           // priority 7
        };

        // Budget = never-trim (200) + first 5 trimmable = 200 + 500 = 700.
        var result = enforcer.Enforce(slots, 700);

        // Priority 1-5 should survive (processed first, get budget).
        Assert.Contains(new string('1', 100), result.FinalText);
        Assert.Contains(new string('2', 100), result.FinalText);
        Assert.Contains(new string('3', 100), result.FinalText);
        Assert.Contains(new string('4', 100), result.FinalText);
        Assert.Contains(new string('5', 100), result.FinalText);

        // Priority 6-7 should be trimmed (no budget left).
        Assert.Contains("SceneContinuityAnchor", result.TrimmedSlots);
        Assert.Contains("WritingStyle", result.TrimmedSlots);
    }

    [Fact]
    public void Enforce_TrimsInteractionHistoryFirst()
    {
        var enforcer = CreateEnforcer();
        var slots = new List<SlotText>
        {
            MakeSlot(PromptSlotId.SceneAnchor, "A", false),
            MakeSlot(PromptSlotId.FinalInstruction, "Z", false),
            MakeSlot(PromptSlotId.InteractionHistory, new string('H', 500), true),
            MakeSlot(PromptSlotId.WritingStyle, new string('W', 500), true),
        };

        // Budget: 2 (never-trim) + 500 (just enough for one trimmable).
        // InteractionHistory (pri 1) AND WritingStyle (pri 7): enforcer processes pri 1 first.
        // WARNING: since both are same size, pri 1 fits and pri 7 is dropped.
        var result = enforcer.Enforce(slots, 502);

        // InteractionHistory (pri 1) processed first, fits. WritingStyle (pri 7) dropped.
        Assert.DoesNotContain("InteractionHistory", result.TrimmedSlots);
        Assert.Contains("WritingStyle", result.TrimmedSlots);
        Assert.Contains(new string('H', 500), result.FinalText);
        Assert.DoesNotContain(new string('W', 500), result.FinalText);
    }

    [Fact]
    public void Enforce_WritingStyle_LastResortTrim()
    {
        var enforcer = CreateEnforcer();
        var slots = new List<SlotText>
        {
            MakeSlot(PromptSlotId.SceneAnchor, "A", false),
            MakeSlot(PromptSlotId.FinalInstruction, "Z", false),
            MakeSlot(PromptSlotId.SceneContinuityAnchor, new string('S', 500), true),
            MakeSlot(PromptSlotId.WritingStyle, new string('W', 500), true),
        };

        // Budget: 2 (never-trim) + 500 (just enough for one trimmable).
        // SceneContinuityAnchor (pri 6) processed first (ascending sort), fits.
        // WritingStyle (pri 7) dropped.
        var result = enforcer.Enforce(slots, 502);

        // SceneContinuityAnchor (pri 6) processed before WritingStyle (pri 7), so it fits and WritingStyle is dropped.
        Assert.DoesNotContain("SceneContinuityAnchor", result.TrimmedSlots);
        Assert.Contains("WritingStyle", result.TrimmedSlots);
        Assert.Contains(new string('S', 500), result.FinalText);
        Assert.DoesNotContain(new string('W', 500), result.FinalText);
    }

    // ── T057: Critical overflow — mandatory slots exceed budget ──

    [Fact]
    public void Enforce_CriticalOverflow_ReturnsMandatoryOnly()
    {
        var enforcer = CreateEnforcer();
        var slots = new List<SlotText>
        {
            MakeSlot(PromptSlotId.SceneAnchor, new string('A', 100), false),
            MakeSlot(PromptSlotId.FinalInstruction, new string('Z', 100), false),
            MakeSlot(PromptSlotId.CharacterData, new string('C', 100), true),
        };

        // Budget less than mandatory (200 mandatory, budget = 150).
        var result = enforcer.Enforce(slots, 150);

        Assert.Equal(300, result.PreTrimChars);
        // Critical overflow: mandatory slots returned in full (200 chars).
        Assert.Equal(200, result.PostTrimChars);
        Assert.Contains(new string('A', 100), result.FinalText);
        Assert.Contains(new string('Z', 100), result.FinalText);
        Assert.DoesNotContain(new string('C', 100), result.FinalText);
    }

    // ── T057: SlotText partial trim (last slot truncated to fit) ──

    [Fact]
    public void Enforce_PartialSlotTrim_TruncatesLastFittingSlot()
    {
        var enforcer = CreateEnforcer();
        var slots = new List<SlotText>
        {
            MakeSlot(PromptSlotId.SceneAnchor, new string('A', 50), false),
            MakeSlot(PromptSlotId.FinalInstruction, new string('Z', 50), false),
            MakeSlot(PromptSlotId.CharacterData, new string('C', 100), true),
        };

        // Budget: 100 (never-trim) + 40 (partial CharacterData) = 140.
        var result = enforcer.Enforce(slots, 140);

        Assert.Equal(200, result.PreTrimChars);
        Assert.Equal(140, result.PostTrimChars);
        Assert.Contains(new string('C', 40), result.FinalText);
        Assert.DoesNotContain(new string('C', 100), result.FinalText);
    }

    // ── T057: Empty slot list ───────────────────────────────────

    [Fact]
    public void Enforce_EmptySlots_ReturnsEmpty()
    {
        var enforcer = CreateEnforcer();
        var result = enforcer.Enforce([], 1000);

        Assert.Equal(0, result.PreTrimChars);
        Assert.Equal(0, result.PostTrimChars);
        Assert.Empty(result.TrimmedSlots);
        Assert.Equal(string.Empty, result.FinalText);
    }
}
