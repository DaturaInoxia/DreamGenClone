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
}
