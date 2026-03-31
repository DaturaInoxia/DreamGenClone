using DreamGenClone.Application.StoryParser.Models;

namespace DreamGenClone.Application.StoryParser.Models;

public enum CollectionMatchReason
{
    UrlPattern = 0,
    TitleSimilarity = 1
}

public enum CatalogDisplayItemType
{
    StandaloneStory = 0,
    Collection = 1
}

public sealed class CollectionMatch
{
    public string CollectionId { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public CollectionMatchReason MatchReason { get; set; }
    public double Confidence { get; set; }
}

public sealed class CollectionMatchResult
{
    public List<CollectionMatch> Matches { get; set; } = [];
    public string? SuggestedCollectionName { get; set; }

    /// <summary>
    /// When an orphan sibling is detected, this is the existing story that shares a base slug or title.
    /// </summary>
    public string? OrphanSiblingStoryId { get; set; }
    public string? OrphanSiblingStoryTitle { get; set; }
}

public sealed class CollectionDisplayItem
{
    public CatalogDisplayItemType ItemType { get; set; }

    // For Collection items
    public string? CollectionId { get; set; }
    public string? CollectionName { get; set; }
    public string? CollectionDescription { get; set; }
    public int MemberCount { get; set; }
    public List<StoryCatalogEntry> Members { get; set; } = [];
    public DateTime LatestParsedUtc { get; set; }

    // For StandaloneStory items
    public StoryCatalogEntry? Story { get; set; }

    public DateTime SortDate => ItemType == CatalogDisplayItemType.Collection ? LatestParsedUtc : (Story?.ParsedUtc ?? DateTime.MinValue);
}
