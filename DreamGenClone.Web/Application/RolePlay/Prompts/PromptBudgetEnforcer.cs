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
    /// </summary>
    public BudgetEnforcementResult Enforce(
        IReadOnlyList<SlotText> slotTexts,
        int maxPromptChars)
    {
        if (maxPromptChars <= 0)
        {
            throw new InvalidOperationException(
                $"PromptBudgetEnforcer: MaxPromptChars must be a positive integer, got {maxPromptChars}.");
        }

        var totalChars = slotTexts.Sum(s => s.Text.Length);
        if (totalChars <= maxPromptChars)
        {
            return new BudgetEnforcementResult(
                string.Join("\n\n", slotTexts.Select(s => s.Text)),
                totalChars,
                totalChars,
                []);
        }

        // Two categories: never-trim and trimmable (in priority order).
        var neverTrim = new List<SlotText>();
        var trimmable = new List<SlotText>();

        foreach (var st in slotTexts)
        {
            if (st.IsTrimEligible)
                trimmable.Add(st);
            else
                neverTrim.Add(st);
        }

        // Sort trimmable by FR-029 priority (ascending = trim first).
        trimmable.Sort((a, b) => GetTrimPriority(a.SlotId).CompareTo(GetTrimPriority(b.SlotId)));

        var mandatoryChars = neverTrim.Sum(s => s.Text.Length);
        if (mandatoryChars > maxPromptChars)
        {
            _logger.LogCritical(
                "Prompt budget overflow: mandatory slots exceed MaxPromptChars={MaxPromptChars}. MandatoryChars={MandatoryChars}",
                maxPromptChars, mandatoryChars);

            // Critical overflow: return mandatory slots only.
            var mandatoryText = string.Join("\n\n", neverTrim.Select(s => s.Text));
            return new BudgetEnforcementResult(mandatoryText, totalChars, mandatoryText.Length, []);
        }

        var remainingBudget = maxPromptChars - mandatoryChars;
        var trimmedSlotIds = new List<string>();
        var partialTrims = new Dictionary<string, string>(StringComparer.Ordinal); // slotId -> trimmed text

        foreach (var st in trimmable)
        {
            if (remainingBudget <= 0)
            {
                trimmedSlotIds.Add(st.SlotId.ToString());
                continue;
            }

            if (st.Text.Length <= remainingBudget)
            {
                remainingBudget -= st.Text.Length;
            }
            else
            {
                // Partially trim this slot to fit — keep the truncated text.
                var trimmedText = st.Text[..remainingBudget];
                trimmedSlotIds.Add(st.SlotId.ToString());
                partialTrims[st.SlotId.ToString()] = trimmedText;
                remainingBudget = 0;
            }
        }

        // Rebuild: never-trim slots first (Zone order), then trimmable slots that fit or were partially trimmed.
        var finalSlots = new List<SlotText>();
        finalSlots.AddRange(neverTrim);

        foreach (var st in trimmable)
        {
            var slotIdStr = st.SlotId.ToString();
            if (trimmedSlotIds.Contains(slotIdStr) && !partialTrims.ContainsKey(slotIdStr))
                continue; // Fully dropped.

            // Use partially trimmed text if available, otherwise original.
            var text = partialTrims.TryGetValue(slotIdStr, out var pt) ? pt : st.Text;
            finalSlots.Add(new SlotText(st.SlotId, text, st.IsTrimEligible));
        }

        finalSlots.Sort((a, b) =>
        {
            var zoneCmp = GetSlotZone(a.SlotId).CompareTo(GetSlotZone(b.SlotId));
            if (zoneCmp != 0) return zoneCmp;
            return GetSlotOrder(a.SlotId).CompareTo(GetSlotOrder(b.SlotId));
        });

        var finalText = string.Join("\n\n", finalSlots.Select(s => s.Text));
        var postChars = finalText.Length;

        _logger.LogWarning(
            "Prompt trimmed: PreTrimChars={PreTrimChars} PostTrimChars={PostTrimChars} TrimmedSlots={TrimmedSlots}",
            totalChars, postChars, string.Join(",", trimmedSlotIds));

        return new BudgetEnforcementResult(finalText, totalChars, postChars, trimmedSlotIds);
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
        PromptSlotId.WritingStyle => 7,             // last resort
        _ => int.MaxValue,                          // never trimmed
    };

    private static PromptZone GetSlotZone(PromptSlotId slotId) => slotId switch
    {
        <= PromptSlotId.WorldState => PromptZone.A,
        <= PromptSlotId.SceneContinuityAnchor => PromptZone.B,
        _ => PromptZone.C,
    };

    private static int GetSlotOrder(PromptSlotId slotId) => slotId switch
    {
        PromptSlotId.SceneAnchor => 1,
        PromptSlotId.ActorAssignment => 2,
        PromptSlotId.TurnContext => 3,
        PromptSlotId.SceneLocationLock => 4,
        PromptSlotId.WorldState => 4,     // 4a — conditional sub-slot
        PromptSlotId.CharacterData => 5,
        PromptSlotId.ScenarioContext => 6,
        PromptSlotId.CurrentLocation => 7,
        PromptSlotId.WritingStyle => 8,
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
