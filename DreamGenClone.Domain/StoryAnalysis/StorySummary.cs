namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class StorySummary
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ParsedStoryId { get; set; } = string.Empty;

    public string SummaryText { get; set; } = string.Empty;

    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
