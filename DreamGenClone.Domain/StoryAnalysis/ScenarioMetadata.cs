namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class ScenarioMetadata
{
    public string ScenarioId { get; set; } = string.Empty;

    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;

    public int InteractionCount { get; set; }

    public int PeakThemeScore { get; set; }

    public int PeakDesireLevel { get; set; }

    public double AverageRestraintLevel { get; set; }

    public string? Notes { get; set; }
}
