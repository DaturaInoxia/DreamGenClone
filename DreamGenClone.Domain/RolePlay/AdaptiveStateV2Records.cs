namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// V2 theme tracker per-theme score row. One row per active theme per session, persisted in
/// <c>RolePlayV2ThemeScores</c>. Mirrors the per-theme content previously held in
/// <c>ThemeTrackerItem</c> (V1 web-domain), but is keyed and persisted relationally.
/// </summary>
public sealed class ThemeScoreState
{
    public string ThemeId { get; set; } = string.Empty;
    public string ThemeName { get; set; } = string.Empty;
    public string Intensity { get; set; } = "None";
    public double Score { get; set; }
    public ThemeScoreBreakdownV2 Breakdown { get; set; } = new();
    public bool Blocked { get; set; }
    public int SuppressedHitCount { get; set; }
    public bool IsScenarioCandidate { get; set; }
    public double NarrativeFitScore { get; set; }
    public DateTime? LastCandidateEvaluationTimeUtc { get; set; }
    public int CompletionCooldownInteractions { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ThemeScoreBreakdownV2
{
    public double ChoiceSignal { get; set; }
    public double CharacterStateSignal { get; set; }
    public double InteractionEvidenceSignal { get; set; }
    public double ScenarioPhaseSignal { get; set; }
    /// <summary>
    /// Direct FitScore additive bonus from successor causality links. Set when a predecessor
    /// scenario completes and this theme is listed as a successor. Applied directly to the
    /// gate-adjusted FitScore (0-100 scale) during candidate evaluation. Reset each semi-reset
    /// cycle so it does not stack across arcs.
    /// </summary>
    public double SuccessorCausalityBoost { get; set; }
    /// <summary>
    /// Flat FitScore point deduction applied to the just-completed theme after Reset.
    /// Subtracted directly from the gate-adjusted FitScore (0-100 scale). Set during
    /// <c>ApplyThemeSemiResetAsync</c> using the configured penalty value. Cleared on
    /// all themes at the start of the next semi-reset cycle.
    /// </summary>
    public double CompletionFitScorePenalty { get; set; }
}

/// <summary>
/// V2 theme evidence ring. Holds the most recent N evidence events that drove score deltas.
/// Persisted in <c>RolePlayV2ThemeTrackerMeta.RecentEvidenceJson</c>.
/// </summary>
public sealed class ThemeEvidenceRecord
{
    public string InteractionId { get; set; } = string.Empty;
    public string ThemeId { get; set; } = string.Empty;
    public string SignalType { get; set; } = string.Empty;
    public double Delta { get; set; }
    public double Confidence { get; set; }
    public string Rationale { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// V2 scenario history entry. One row per completed scenario, persisted in
/// <c>RolePlayV2ScenarioHistory</c>.
/// </summary>
public sealed class ScenarioHistoryEntry
{
    public string Id { get; set; } = string.Empty;
    public string ScenarioId { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
    public int InteractionCount { get; set; }
    public int PeakThemeScore { get; set; }
    public int PeakDesireLevel { get; set; }
    public double AverageRestraintLevel { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// V2 pairwise stat record. Holds a per-source/target/stat block.
/// Persisted in <c>RolePlayV2PairwiseStats</c> — one row per (SessionId, SourceCharacterId, TargetCharacterId).
/// </summary>
public sealed class PairwiseStatRecord
{
    public string SourceCharacterId { get; set; } = string.Empty;
    public string TargetCharacterId { get; set; } = string.Empty;
    public Dictionary<string, int> Stats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// V2 semantic event evidence record. Persisted in <c>RolePlayV2SemanticEvents</c>.
/// Mirrors the V1 <c>SemanticEventEvidenceRecord</c> shape.
/// </summary>
public sealed class SemanticEventRecord
{
    public string InteractionId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string MappingId { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public List<string> ThemeTargets { get; set; } = [];
    public DateTime ProcessedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// V2 semantic theme delta breakdown. Persisted in
/// <c>RolePlayV2AdaptiveStates.SemanticDeltaBreakdownsJson</c>.
/// </summary>
public sealed class SemanticThemeDeltaBreakdown
{
    public string InteractionId { get; set; } = string.Empty;
    public string ThemeId { get; set; } = string.Empty;
    public string SourceType { get; set; } = "semantic";
    public decimal RawDelta { get; set; }
    public decimal AppliedDelta { get; set; }
    public decimal CappedDelta { get; set; }
    public decimal SuppressedDelta { get; set; }
    public string? SuppressionReasonCode { get; set; }
}

/// <summary>
/// V2 semantic stat delta breakdown. Persisted in
/// <c>RolePlayV2AdaptiveStates.SemanticStatDeltaBreakdownsJson</c>.
/// </summary>
public sealed class SemanticStatDeltaRecord
{
    public string InteractionId { get; set; } = string.Empty;
    public string CharacterId { get; set; } = string.Empty;
    public string StatName { get; set; } = string.Empty;
    public string SourceType { get; set; } = "semantic";
    public decimal RawDelta { get; set; }
    public decimal AppliedDelta { get; set; }
    public decimal CappedDelta { get; set; }
    public decimal SuppressedDelta { get; set; }
    public string? SuppressionReasonCode { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
}
