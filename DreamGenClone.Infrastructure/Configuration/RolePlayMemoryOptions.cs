namespace DreamGenClone.Infrastructure.Configuration;

public sealed class RolePlayMemoryOptions
{
    public const string SectionName = "RolePlayMemory";

    /// <summary>
    /// Maximum number of PhaseMilestone entries from the current arc to inject
    /// into the continuation prompt.
    /// Default: 5.
    /// </summary>
    public int MaxMilestonesToInject { get; init; } = 5;

    /// <summary>
    /// Maximum number of ArcCompletion entries (most recent arcs) to inject
    /// into the continuation prompt.
    /// Default: 10.
    /// </summary>
    public int MaxArcCompletionsToInject { get; init; } = 10;

    /// <summary>
    /// Maximum number of EncounterCompletion entries (most recent encounter-boundary
    /// memories within the current arc) to inject. Written at every encounter
    /// boundary detection (any phase) and enriched by the LLM pipeline.
    /// Default: 5.
    /// </summary>
    public int MaxEncounterCompletionsToInject { get; init; } = 5;

    /// <summary>
    /// Dedicated model slot used by EncounterSummaryJobHandler for the LLM enrichment of
    /// ArcCompletion and EncounterCompletion summaries. Must match a slot registered in the
    /// model manager. Default: "roleplay-summary-enhancement".
    /// </summary>
    public string SummaryEnhancementModelSlot { get; init; } = "roleplay-summary-enhancement";

    /// <summary>
    /// When true, the EncounterSummaryJobHandler will call the LLM to generate
    /// per-character intimate act prose for ArcCompletion entries.
    /// When false, only TemplateSummary is used for all entries.
    /// Default: true.
    /// </summary>
    public bool EnableLlmSummaryEnhancement { get; init; } = true;

    /// <summary>
    /// Maximum allowed length (characters) of an LLM-enhanced memory summary.
    /// If the enrichment response exceeds this, the response is rejected (not
    /// persisted) and the template summary is used instead. Guards against
    /// chain-of-thought/reasoning leakage and runaway responses that would
    /// otherwise bloat the prompt (see session e12d27a6: a 35K reasoning dump
    /// ballooned the Recent Encounter Memories slot to 81K chars).
    /// Default: 4000.
    /// </summary>
    public int MaxLlmSummaryChars { get; init; } = 4000;

    /// <summary>
    /// Global confidence threshold for semantic encounter-start detection.
    /// Applied universally across all themes. Detection fires when the LLM
    /// inference confidence meets or exceeds this value.
    /// Default: 0.70.
    /// </summary>
    public decimal EncounterStartConfidenceThreshold { get; init; } = 0.70m;

    /// <summary>
    /// Number of prior interactions to include as context for semantic
    /// encounter-start and encounter-completed detection inference. The current
    /// interaction is always included separately as InteractionText. Keeping this
    /// small keeps the inference prompt fast enough for the semantic model.
    /// Default: 4.
    /// </summary>
    public int EncounterStartContextTurns { get; init; } = 4;

    /// <summary>
    /// Global confidence threshold for semantic encounter-completed (boundary) detection
    /// when the theme has no explicit encounter-completed mapping. Themes WITH a mapping
    /// use their per-theme ConfidenceMin/ConfidenceMax instead. Applied universally so
    /// encounter-end detection works for all themes.
    /// Default: 0.70.
    /// </summary>
    public decimal EncounterEndConfidenceThreshold { get; init; } = 0.70m;
}
