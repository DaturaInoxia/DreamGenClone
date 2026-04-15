namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class BaseStatProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string TargetGender { get; set; } = "Any";

    public string TargetRole { get; set; } = "Unknown";

    public Dictionary<string, int> DefaultStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
