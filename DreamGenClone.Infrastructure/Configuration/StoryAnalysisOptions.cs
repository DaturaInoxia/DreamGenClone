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
}
