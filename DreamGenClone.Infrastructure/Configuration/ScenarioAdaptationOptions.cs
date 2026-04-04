namespace DreamGenClone.Infrastructure.Configuration;

public sealed class ScenarioAdaptationOptions
{
    public const string SectionName = "ScenarioAdaptation";

    /// <summary>
    /// LLM model to use for scenario preview and adaptation.
    /// Falls back to LmStudioOptions.Model if not set.
    /// </summary>
    public string? Model { get; set; }

    public double PreviewTemperature { get; set; } = 0.6;

    public double PreviewTopP { get; set; } = 0.95;

    public int PreviewMaxTokens { get; set; } = 1200;

    public double AdaptTemperature { get; set; } = 0.5;

    public double AdaptTopP { get; set; } = 0.95;

    public int AdaptMaxTokens { get; set; } = 2000;
}
