namespace DreamGenClone.Infrastructure.Configuration;

/// <summary>
/// Recommended initial values for RP prompt configuration.
/// Used ONLY by the session-creation seeder — NEVER by the runtime prompt builder.
/// The runtime always reads from persisted session config and fails fast on missing values (FR-004).
/// </summary>
public sealed class RolePlayPromptOptions
{
    public const string SectionName = "RolePlayPrompt";

    /// <summary>
    /// Recommended initial max prompt characters for new sessions (~8,750 tokens at ~4 chars/token,
    /// leaving ~1,250 tokens for output within an 8K window).
    /// </summary>
    public int RecommendedInitialMaxPromptChars { get; init; } = 35000;

    /// <summary>Recommended initial context window turns for new sessions.</summary>
    public int RecommendedInitialContextWindowTurns { get; init; } = 8;

    /// <summary>Recommended turn threshold after which scenario context compresses.</summary>
    public int RecommendedInitialScenarioCompressionTurnThreshold { get; init; } = 10;

    /// <summary>Recommended number of recent turns with full detail.</summary>
    public int RecommendedInitialHistoryFullDetailTurnBand { get; init; } = 3;

    /// <summary>Recommended number of middle turns with narrative-only summaries.</summary>
    public int RecommendedInitialHistoryNarrativeOnlyTurnBand { get; init; } = 3;

    /// <summary>Recommended turn threshold for long-term session memory compression.</summary>
    public int RecommendedInitialSessionMemoryLongTermTurnThreshold { get; init; } = 10;
}
