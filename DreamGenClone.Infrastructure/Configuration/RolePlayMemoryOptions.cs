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
    /// When true, the EncounterSummaryJobHandler will call the LLM to generate
    /// per-character intimate act prose for ArcCompletion entries.
    /// When false, only TemplateSummary is used for all entries.
    /// Default: true.
    /// </summary>
    public bool EnableLlmSummaryEnhancement { get; init; } = true;
}
