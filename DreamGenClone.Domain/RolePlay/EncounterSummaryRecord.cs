namespace DreamGenClone.Domain.RolePlay;

public enum EncounterSummaryType
{
    PhaseMilestone,        // Template-only; written at every non-ArcCompletion transition
    ArcCompletion,        // LLM-enriched; written at Climax→Reset
    EncounterCompletion   // LLM-enriched; written at every encounter boundary detection (any phase)
}

public sealed class EncounterSummaryRecord
{
    /// <summary>GUID (no dashes), primary key.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string SessionId { get; set; } = string.Empty;

    /// <summary>The character whose perspective this summary represents.</summary>
    public string CharacterId { get; set; } = string.Empty;

    public EncounterSummaryType SummaryType { get; set; }

    /// <summary>Which arc (0-based) this transition belongs to.</summary>
    public int CycleIndex { get; set; }

    public NarrativePhase FromPhase { get; set; }

    public NarrativePhase ToPhase { get; set; }

    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

    /// <summary>How many turns occurred in FromPhase before this transition.</summary>
    public int TurnCountInPhase { get; set; }

    /// <summary>
    /// Encounter sequence number for <see cref="EncounterSummaryType.EncounterCompletion"/> rows
    /// (1-based index within the session, stamped from
    /// <see cref="AdaptiveScenarioState.GlobalEncounterCount"/> at detection time).
    /// Default 0 for other summary types.
    /// </summary>
    public int EncounterNumber { get; set; }

    /// <summary>
    /// Raw detection-text span produced by the encounter-boundary semantic inference
    /// for <see cref="EncounterSummaryType.EncounterCompletion"/> rows. Null for other
    /// summary types. Persisted for diagnostics and for B-056's aftermath injector
    /// (fallback if <see cref="LlmSummary"/> is not yet populated).
    /// </summary>
    public string? DetectionEvidence { get; set; }

    /// <summary>
    /// For <see cref="EncounterSummaryType.EncounterCompletion"/> rows: starting index
    /// (inclusive) of this encounter's interactions within
    /// <c>RolePlaySession.Interactions</c>. Default 0 for other summary types.
    /// </summary>
    public int StartInteractionIndex { get; set; }

    /// <summary>
    /// For <see cref="EncounterSummaryType.EncounterCompletion"/> rows: ending index
    /// (inclusive) of this encounter's interactions within
    /// <c>RolePlaySession.Interactions</c>. Default 0 for other summary types.
    /// </summary>
    public int EndInteractionIndex { get; set; }

    public string? SceneLocation { get; set; }

    public string? ActiveThemeId { get; set; }

    /// <summary>Finishing move ID if one was reached (ArcCompletion only).</summary>
    public string? FinishingMoveId { get; set; }

    /// <summary>JSON array of position IDs used in the arc (ArcCompletion only).</summary>
    public string? PositionIdsJson { get; set; }

    /// <summary>
    /// JSON of this character's stats at transition: {"Desire":N,"Restraint":N,"Tension":N,"Connection":N}
    /// </summary>
    public string CharacterStatsSnapshotJson { get; set; } = "{}";

    /// <summary>Deterministic template-generated text, written synchronously at transition.</summary>
    public string TemplateSummary { get; set; } = string.Empty;

    /// <summary>LLM-generated prose, written asynchronously by the job handler.</summary>
    public string? LlmSummary { get; set; }

    /// <summary>UTC timestamp when LlmSummary was written.</summary>
    public DateTime? LlmEnhancedUtc { get; set; }

    /// <summary>Full enrichment prompt sent to the LLM. Null until enhanced.</summary>
    public string? EnrichmentPrompt { get; set; }

    /// <summary>Returns LLM prose if available, otherwise template text.</summary>
    public string ActiveSummary => LlmSummary ?? TemplateSummary;

    /// <summary>True if LLM enhancement has been applied.</summary>
    public bool IsEnhanced => LlmSummary is not null;
}
