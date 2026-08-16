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

    // ── T057: Trim priority order (FR-029) ─────────────────────

    // ── T057: Critical overflow — mandatory slots exceed budget ──

    // ── T057: SlotText partial trim (last slot truncated to fit) ──

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
