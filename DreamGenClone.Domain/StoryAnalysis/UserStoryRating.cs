namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class UserStoryRating
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ParsedStoryId { get; set; } = string.Empty;

    public int Stars { get; set; } = 3;

    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
