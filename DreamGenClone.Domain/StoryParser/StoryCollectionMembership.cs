namespace DreamGenClone.Domain.StoryParser;

public sealed class StoryCollectionMembership
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string CollectionId { get; set; } = string.Empty;

    public string ParsedStoryId { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
}
