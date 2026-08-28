namespace DreamGenClone.Infrastructure.Persistence;

/// <summary>
/// Repository for phase Rule-of-Thumb text (FR-014). Each narrative phase has a single
/// row of writing-style guidance that the <c>WritingStyleSlot</c> reads at prompt-build time.
/// </summary>
public interface IPhaseRuleOfThumbRepository
{
    /// <summary>
    /// Returns the Rule-of-Thumb text for the given phase, or null if the phase row is missing.
    /// </summary>
    Task<PhaseRuleOfThumbRow?> GetByPhaseAsync(string phase, CancellationToken ct = default);
}

/// <summary>
/// Row shape for the PhaseRuleOfThumb config table.
/// </summary>
public sealed record PhaseRuleOfThumbRow(string Id, string Phase, string RuleOfThumbText);
