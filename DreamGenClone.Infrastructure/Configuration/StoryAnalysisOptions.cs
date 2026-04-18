namespace DreamGenClone.Infrastructure.Configuration;

public sealed class StoryAnalysisOptions
{
    public const string SectionName = "StoryAnalysis";

    public double SummarizeTemperature { get; set; } = 0.3;

    public int SummarizeMaxTokens { get; set; } = 500;

    public double AnalyzeTemperature { get; set; } = 0.3;

    public int AnalyzeMaxTokens { get; set; } = 800;

    public double RankTemperature { get; set; } = 0.1;

    public int RankMaxTokens { get; set; } = 200;

    public int MaxStoryTextLength { get; set; } = 12000;

    /// <summary>
    /// LLM model to use for analysis, ranking, and summarization.
    /// Falls back to LmStudioOptions.Model if not set.
    /// </summary>
    public string? Model { get; set; }

    public double RankConfidenceThreshold { get; set; } = 0.5;

    public bool UseScenarioDefinitionsForAdaptiveRuntime { get; set; }

    // Enables RP-only Theme/Profile subsystem for runtime candidate/guidance paths.
    public bool UseRpThemeSubsystem { get; set; } = true;

    // When true, RP subsystem only applies to sessions explicitly marked for it.
    public bool UseRpThemeSubsystemForNewSessionsOnly { get; set; } = true;

    // Markdown source folder used by RP theme sync controls.
    public string RpThemeMarkdownSourcePath { get; set; } = "specs/v2/ThemeDefinitaions";

    public int AdaptiveThemeAffinityStackLimit { get; set; } = 1;

    public int AdaptiveEarlyTurnInteractionThreshold { get; set; } = 3;

    public int AdaptiveEarlyTurnPerStatDeltaCap { get; set; } = 2;

    public int AdaptivePerInteractionTotalDeltaBudget { get; set; } = 10;

    public int AdaptiveThemeAffinityCapBuildUp { get; set; } = 0;

    public int AdaptiveThemeAffinityCapCommitted { get; set; } = 1;

    public int AdaptiveThemeAffinityCapApproaching { get; set; } = 1;

    public int AdaptiveThemeAffinityCapClimax { get; set; } = 2;

    public int AdaptiveThemeAffinityCapReset { get; set; } = 0;

    // BuildUp scenario selection fit scoring strategy key.
    public string BuildUpSelectionFitScoreStrategy { get; set; } = "weighted-blend";

    // BuildUp scenario selection tie-break strategy key.
    public string BuildUpSelectionTieBreakStrategy { get; set; } = "tie-window";

    // Tie delta threshold used by tie-break strategies.
    public double BuildUpSelectionTieDeltaThreshold { get; set; } = 0.10;

    // Commitment score threshold used after ranking.
    public double BuildUpSelectionCommitThreshold { get; set; } = 0.60;

    // Candidate gate strategy before scoring/ranking. Supported: legacy, dominant-role.
    public string BuildUpSelectionCandidateGateStrategy { get; set; } = "legacy";

    // Minimum per-role score required when dominant-role gate strategy is active.
    public double BuildUpSelectionDominantRoleMinScore { get; set; } = 0.85;
}
