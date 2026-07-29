using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts;

/// <summary>
/// Enforces the configurable <c>MaxPromptChars</c> budget (FR-004) by trimming Zone B slots
/// in the FR-029 priority order after all slots have produced text. Zone A and critical Zone C
/// slots are never trimmed.
/// </summary>
public sealed class PromptBudgetEnforcer
{
    private readonly ILogger<PromptBudgetEnforcer> _logger;

    public PromptBudgetEnforcer(ILogger<PromptBudgetEnforcer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Enforces the budget on assembled slot texts. Returns the final text and enforcement metadata.
    /// Budget enforcement disabled — full prompt always sent to the LLM.
    /// </summary>
    public BudgetEnforcementResult Enforce(
        IReadOnlyList<SlotText> slotTexts,
        int maxPromptChars)
    {
        var totalChars = slotTexts.Sum(s => s.Text.Length);
        var finalText = string.Join("\n\n", slotTexts.Select(s => s.Text));
        return new BudgetEnforcementResult(finalText, totalChars, totalChars, []);
    }

    // ── FR-029 Trim Priority (ascending = trim first) ──────────

    private static int GetTrimPriority(PromptSlotId slotId) => slotId switch
    {
        PromptSlotId.InteractionHistory => 1,       // oldest history first
        PromptSlotId.CharacterData => 2,            // non-present char data
        PromptSlotId.ScenarioContext => 3,          // scenario metadata
        PromptSlotId.SessionMemory => 4,            // session memory
        PromptSlotId.CurrentLocation => 5,          // location summaries
        PromptSlotId.SceneContinuityAnchor => 6,    // cross-perceptions
        _ => int.MaxValue,                          // never trimmed
    };

    private static PromptZone GetSlotZone(PromptSlotId slotId) => slotId switch
    {
        PromptSlotId.WritingStyle => PromptZone.C,
        <= PromptSlotId.WorldState => PromptZone.A,
        <= PromptSlotId.SceneContinuityAnchor => PromptZone.B,
        _ => PromptZone.C,
    };

    private static int GetSlotOrder(PromptSlotId slotId) => slotId switch
    {
        PromptSlotId.SystemPrimer => 0,
        PromptSlotId.SceneAnchor => 1,
        PromptSlotId.ActorAssignment => 2,
        PromptSlotId.TurnContext => 3,
        PromptSlotId.SceneLocationLock => 4,
        PromptSlotId.WorldState => 4,     // 4a — conditional sub-slot
        PromptSlotId.CharacterData => 5,
        PromptSlotId.ScenarioContext => 6,
        PromptSlotId.CurrentLocation => 7,
        PromptSlotId.InteractionHistory => 9,
        PromptSlotId.SessionMemory => 10,
        PromptSlotId.SceneContinuityAnchor => 11,
        PromptSlotId.ThemeContract => 12,
        PromptSlotId.BehavioralFrames => 13,
        PromptSlotId.ScenarioGuidance => 14,
        PromptSlotId.IntensityPacing => 15,
        PromptSlotId.UserDirection => 16,
        PromptSlotId.FinalInstruction => 17,
        _ => int.MaxValue,
    };
}

/// <summary>
/// A slot's produced text with trim eligibility metadata.
/// </summary>
public sealed record SlotText(PromptSlotId SlotId, string Text, bool IsTrimEligible);

/// <summary>
/// Result of budget enforcement: the final prompt text and metadata.
/// </summary>
/// <param name="FinalText">The assembled (possibly trimmed) prompt text.</param>
/// <param name="PreTrimChars">Total chars before enforcement.</param>
/// <param name="PostTrimChars">Total chars after enforcement.</param>
/// <param name="TrimmedSlots">Slot IDs that were trimmed or dropped.</param>
public sealed record BudgetEnforcementResult(
    string FinalText,
    int PreTrimChars,
    int PostTrimChars,
    IReadOnlyList<string> TrimmedSlots);
