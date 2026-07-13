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
    /// Global confidence threshold for semantic encounter-start detection.
    /// Applied universally across all themes. Detection fires when the LLM
    /// inference confidence meets or exceeds this value.
    /// Default: 0.70.
    /// </summary>
    public decimal EncounterStartConfidenceThreshold { get; init; } = 0.70m;
}
