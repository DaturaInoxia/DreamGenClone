using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts;

/// <summary>
/// Orchestrates the 17-slot prompt architecture. Sorts slots by Zone then Order,
/// runs each slot's <c>ShouldWrite</c>/<c>WriteAsync</c>, enforces the token budget,
/// and logs at Information/Warning/Critical per FR-030/FR-037.
/// </summary>
public sealed class RolePlayPromptBuilder
{
    private readonly IReadOnlyList<IPromptSlot> _sortedSlots;
    private readonly PromptBudgetEnforcer _budgetEnforcer;
    private readonly ILogger<RolePlayPromptBuilder> _logger;

    public RolePlayPromptBuilder(
        IEnumerable<IPromptSlot> slots,
        PromptBudgetEnforcer budgetEnforcer,
        ILogger<RolePlayPromptBuilder> logger)
    {
        _budgetEnforcer = budgetEnforcer;
        _logger = logger;

        _sortedSlots = slots
            .OrderBy(s => s.Zone)
            .ThenBy(s => s.Order)
            .ToList()
            .AsReadOnly();

        // Startup validation: validate Zone/Order for registered slots.
        // The full 17-slot count is enforced at feature completion (Phase 9),
        // not during incremental development.
        foreach (var slot in _sortedSlots)
        {
            var expectedZone = GetExpectedZone(slot.Id);
            var expectedOrder = GetExpectedOrder(slot.Id);

            if (slot.Zone != expectedZone)
            {
                throw new InvalidOperationException(
                    $"RolePlayPromptBuilder: Slot {slot.Id} has Zone={slot.Zone}, expected Zone={expectedZone}.");
            }

            if (slot.Order != expectedOrder)
            {
                throw new InvalidOperationException(
                    $"RolePlayPromptBuilder: Slot {slot.Id} has Order={slot.Order}, expected Order={expectedOrder}.");
            }
        }

        // Verify no duplicate Ids.
        var ids = _sortedSlots.Select(s => s.Id).ToList();
        if (ids.Distinct().Count() != ids.Count)
        {
            var duplicates = ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key);
            throw new InvalidOperationException(
                $"RolePlayPromptBuilder: Duplicate slot IDs detected: {string.Join(", ", duplicates)}");
        }
    }

    /// <summary>
    /// Builds the full prompt by running all slots against the given context.
    /// </summary>
    public async Task<string> BuildAsync(PromptBuildContext context, CancellationToken ct)
    {
        // Fail-fast on missing MaxPromptChars (FR-004).
        if (context.MaxPromptChars <= 0)
        {
            throw new InvalidOperationException(
                $"MissingPromptConfig: session '{context.Session.Id}' MaxPromptChars must be a positive integer; " +
                "no hardcoded default is permitted (FR-004).");
        }

        var slotTexts = new List<SlotText>();
        var slotsFired = 0;

        foreach (var slot in _sortedSlots)
        {
            if (!slot.ShouldWrite(context))
                continue;

            var text = await slot.WriteAsync(context, ct);
            if (string.IsNullOrEmpty(text))
                continue;

            slotTexts.Add(new SlotText(slot.Id, text, slot.IsTrimEligible));
            slotsFired++;
        }

        // Phase 1: assemble all slot text.
        var assembledChars = slotTexts.Sum(s => s.Text.Length);

        // Phase 2: enforce budget (R7).
        var result = _budgetEnforcer.Enforce(slotTexts, context.MaxPromptChars);

        // ── Logging per FR-030 / FR-037 ────────────────────────

        // Critical overflow: mandatory slots exceed budget (FR-030).
        if (result.PostTrimChars > context.MaxPromptChars)
        {
            _logger.LogCritical(
                "Prompt budget overflow: mandatory slots exceed MaxPromptChars={MaxPromptChars}. SessionId={SessionId} Actor={Actor} MandatoryChars={MandatoryChars}",
                context.MaxPromptChars,
                context.Session.Id,
                context.ActorProfile.ActorName,
                result.PostTrimChars);
        }
        // Warning on trim (FR-030).
        else if (result.PreTrimChars > result.PostTrimChars && result.TrimmedSlots.Count > 0)
        {
            _logger.LogWarning(
                "Prompt trimmed: SessionId={SessionId} Actor={Actor} PreTrimChars={PreTrimChars} PostTrimChars={PostTrimChars} TrimmedSlots={TrimmedSlots}",
                context.Session.Id,
                context.ActorProfile.ActorName,
                result.PreTrimChars,
                result.PostTrimChars,
                string.Join(",", result.TrimmedSlots));
        }

        // Information log on every build (FR-037).
        _logger.LogInformation(
            "Prompt built: SessionId={SessionId} Actor={Actor} Phase={Phase} Chars={Chars} SlotsFired={SlotsFired} PreTrim={PreTrim} PostTrim={PostTrim}",
            context.Session.Id,
            context.ActorProfile.ActorName,
            context.Phase,
            result.PostTrimChars,
            slotsFired,
            result.PreTrimChars,
            result.PostTrimChars);

        return result.FinalText;
    }

    // ── Frozen Zone/Order contract ─────────────────────────────

    private static PromptZone GetExpectedZone(PromptSlotId id) => id switch
    {
        PromptSlotId.WritingStyle => PromptZone.C,
        <= PromptSlotId.WorldState => PromptZone.A,
        <= PromptSlotId.SceneContinuityAnchor => PromptZone.B,
        _ => PromptZone.C,
    };

    private static int GetExpectedOrder(PromptSlotId id) => id switch
    {
        PromptSlotId.SystemPrimer => 0,
        PromptSlotId.SceneAnchor => 1,
        PromptSlotId.ActorAssignment => 2,
        PromptSlotId.TurnContext => 3,
        PromptSlotId.SceneLocationLock => 4,
        PromptSlotId.WorldState => 4,
        PromptSlotId.CharacterData => 5,
        PromptSlotId.ScenarioContext => 6,
        PromptSlotId.CurrentLocation => 7,
        PromptSlotId.WritingStyle => 18,
        PromptSlotId.InteractionHistory => 9,
        PromptSlotId.SessionMemory => 10,
        PromptSlotId.SceneContinuityAnchor => 11,
        PromptSlotId.ThemeContract => 12,
        PromptSlotId.BehavioralFrames => 13,
        PromptSlotId.ScenarioGuidance => 14,
        PromptSlotId.IntensityPacing => 15,
        PromptSlotId.UserDirection => 16,
        PromptSlotId.FinalInstruction => 17,
        PromptSlotId.PinnedContext => 8,
        PromptSlotId.StagedDirections => 9,
        PromptSlotId.ContinuationOverride => 19,
        _ => int.MaxValue,
    };
}
