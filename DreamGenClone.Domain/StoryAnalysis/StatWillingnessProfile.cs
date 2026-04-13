namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class StatWillingnessProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string TargetStatName { get; set; } = "Desire";

    public bool IsDefault { get; set; }

    public List<WillingnessThreshold> Thresholds { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class WillingnessThreshold
{
    public int SortOrder { get; set; }

    public int MinValue { get; set; }

    public int MaxValue { get; set; }

    public string ExplicitnessLevel { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PromptGuideline { get; set; } = string.Empty;

    public List<string> ExampleScenarios { get; set; } = [];
}
