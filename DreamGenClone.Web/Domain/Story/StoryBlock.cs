namespace DreamGenClone.Web.Domain.Story;

public sealed class StoryBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public StoryBlockType BlockType { get; set; }

    public string Author { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
