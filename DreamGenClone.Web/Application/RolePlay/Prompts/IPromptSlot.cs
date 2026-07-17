using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Prompts;

/// <summary>
/// Contract every prompt slot implements. The 17-slot architecture is frozen per spec —
/// implementation MUST NOT add, remove, reorder, or re-zone slots without a spec amendment.
/// </summary>
public interface IPromptSlot
{
    /// <summary>Unique slot identifier (matches <see cref="PromptSlotId"/> enum).</summary>
    PromptSlotId Id { get; }

    /// <summary>Attention zone: A (Primacy), B (Context), or C (Recency).</summary>
    PromptZone Zone { get; }

    /// <summary>Order within zone (1-based). Builder sorts by Zone then Order.</summary>
    int Order { get; }

    /// <summary>True if this slot's text can be trimmed when over budget (FR-029).</summary>
    bool IsTrimEligible { get; }

    /// <summary>
    /// Pure predicate. Returns true if this slot should emit text for the given context.
    /// Idempotent for identical context, no side effects.
    /// </summary>
    bool ShouldWrite(PromptBuildContext context);

    /// <summary>
    /// Produces the slot's text. MUST NOT throw for a context where <see cref="ShouldWrite"/>
    /// returned true. Result MUST NOT contain leading/trailing newlines — builder handles spacing.
    /// Exceptions propagate per fail-fast contract.
    /// </summary>
    Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct);

    /// <summary>
    /// Trims the slot's text to fit the remaining budget. MUST be idempotent.
    /// MUST NOT produce empty output from non-empty input.
    /// </summary>
    string Trim(string text, int maxChars);
}
