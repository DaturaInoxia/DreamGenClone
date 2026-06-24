namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class StatResistanceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string TargetStatName { get; set; } = "Loyalty";

    public bool IsDefault { get; set; }

    public List<ResistanceThreshold> Thresholds { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ResistanceThreshold
{
    public int SortOrder { get; set; }

    public int MinValue { get; set; }

    public int MaxValue { get; set; }

    public string ResistanceLevel { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PromptDirective { get; set; } = string.Empty;

    public List<string> ExampleScenarios { get; set; } = [];
}
