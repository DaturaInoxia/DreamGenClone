namespace DreamGenClone.Web.Domain.Story;

public sealed class StorySession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Title { get; set; } = "Untitled Story";

    public string? ScenarioId { get; set; }

    public List<StoryBlock> Blocks { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}
