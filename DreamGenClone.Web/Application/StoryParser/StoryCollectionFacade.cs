using DreamGenClone.Application.StoryParser;
using DreamGenClone.Application.StoryParser.Models;
using DreamGenClone.Domain.StoryParser;

namespace DreamGenClone.Web.Application.StoryParser;

public sealed class StoryCollectionFacade
{
    private readonly IStoryCollectionService _collectionService;
    private readonly ICollectionMatchingService _matchingService;
    private readonly IStoryCatalogService _catalogService;

    public StoryCollectionFacade(
        IStoryCollectionService collectionService,
        ICollectionMatchingService matchingService,
        IStoryCatalogService catalogService)
    {
        _collectionService = collectionService;
        _matchingService = matchingService;
        _catalogService = catalogService;
    }

    // --- Collection CRUD ---
    public Task<StoryCollection> CreateCollectionAsync(string name, string? description, CancellationToken ct = default)
        => _collectionService.CreateCollectionAsync(name, description, ct);

    public Task<StoryCollection> UpdateCollectionAsync(string id, string name, string? description, CancellationToken ct = default)
        => _collectionService.UpdateCollectionAsync(id, name, description, ct);

    public Task<bool> DeleteCollectionAsync(string id, CancellationToken ct = default)
        => _collectionService.DeleteCollectionAsync(id, ct);

    public Task<StoryCollection?> GetCollectionAsync(string id, CancellationToken ct = default)
        => _collectionService.GetCollectionAsync(id, ct);

    public Task<IReadOnlyList<StoryCollection>> ListCollectionsAsync(CancellationToken ct = default)
        => _collectionService.ListCollectionsAsync(ct);

    public Task<IReadOnlyList<StoryCollection>> SearchCollectionsAsync(string query, CancellationToken ct = default)
        => _collectionService.SearchCollectionsAsync(query, ct);

    // --- Membership ---
    public Task<StoryCollectionMembership> AddStoryToCollectionAsync(string collectionId, string parsedStoryId, int? sortOrder = null, CancellationToken ct = default)
        => _collectionService.AddStoryToCollectionAsync(collectionId, parsedStoryId, sortOrder, ct);

    public Task<bool> RemoveStoryFromCollectionAsync(string collectionId, string parsedStoryId, CancellationToken ct = default)
        => _collectionService.RemoveStoryFromCollectionAsync(collectionId, parsedStoryId, ct);

    public Task<IReadOnlyList<StoryCollectionMembership>> GetCollectionMembersAsync(string collectionId, CancellationToken ct = default)
        => _collectionService.GetCollectionMembersAsync(collectionId, ct);

    public Task<IReadOnlyList<StoryCollection>> GetCollectionsForStoryAsync(string parsedStoryId, CancellationToken ct = default)
        => _collectionService.GetCollectionsForStoryAsync(parsedStoryId, ct);

    public Task ReorderCollectionMembersAsync(string collectionId, List<string> parsedStoryIdsInOrder, CancellationToken ct = default)
        => _collectionService.ReorderCollectionMembersAsync(collectionId, parsedStoryIdsInOrder, ct);

    // --- Matching ---
    public Task<CollectionMatchResult> FindMatchesAsync(string sourceUrl, string? title, CancellationToken ct = default)
        => _matchingService.FindMatchesAsync(sourceUrl, title, ct);

    // --- Unified Catalog ---
    public async Task<List<CollectionDisplayItem>> BuildUnifiedCatalogAsync(
        CatalogSortMode sortMode, bool includeArchived, string? searchQuery, CancellationToken ct = default)
    {
        // Load all stories
        IReadOnlyList<StoryCatalogEntry> stories;
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            stories = await _catalogService.ListAsync(
                new StoryCatalogQuery { SortMode = sortMode }, includeArchived, ct);
        }
        else
        {
            stories = await _catalogService.SearchAsync(
                new StoryCatalogSearch { Query = searchQuery, SortMode = sortMode }, ct);
        }

        // Load all collections with their members
        var allCollections = await _collectionService.ListCollectionsAsync(ct);
        var collectionMembers = new Dictionary<string, List<StoryCollectionMembership>>();
        foreach (var col in allCollections)
        {
            var members = await _collectionService.GetCollectionMembersAsync(col.Id, ct);
            collectionMembers[col.Id] = members.ToList();
        }

        // Build set of story IDs that belong to a collection
        var storyToCollections = new Dictionary<string, string>();
        foreach (var (colId, members) in collectionMembers)
        {
            foreach (var m in members)
            {
                // A story in multiple collections: first one wins for grouping purposes
                storyToCollections.TryAdd(m.ParsedStoryId, colId);
            }
        }

        var storiesById = stories.ToDictionary(s => s.Id);
        var result = new List<CollectionDisplayItem>();
        var processedStoryIds = new HashSet<string>();

        // Build collection display items
        foreach (var col in allCollections)
        {
            var members = collectionMembers[col.Id];
            var memberEntries = new List<StoryCatalogEntry>();

            foreach (var m in members)
            {
                if (storiesById.TryGetValue(m.ParsedStoryId, out var entry))
                {
                    memberEntries.Add(entry);
                    processedStoryIds.Add(m.ParsedStoryId);
                }
            }

            // If searching, only include collection if it matches or has matching members
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var collectionNameMatches = col.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)
                    || (col.Description?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ?? false);

                if (!collectionNameMatches && memberEntries.Count == 0)
                    continue;
            }

            if (memberEntries.Count == 0 && string.IsNullOrWhiteSpace(searchQuery))
                continue; // Don't show empty collections in normal view

            result.Add(new CollectionDisplayItem
            {
                ItemType = CatalogDisplayItemType.Collection,
                CollectionId = col.Id,
                CollectionName = col.Name,
                CollectionDescription = col.Description,
                MemberCount = members.Count,
                Members = memberEntries,
                LatestParsedUtc = memberEntries.Count > 0
                    ? memberEntries.Max(e => e.ParsedUtc)
                    : col.CreatedUtc
            });
        }

        // Add standalone stories (not in any collection)
        foreach (var story in stories)
        {
            if (!processedStoryIds.Contains(story.Id))
            {
                result.Add(new CollectionDisplayItem
                {
                    ItemType = CatalogDisplayItemType.StandaloneStory,
                    Story = story
                });
            }
        }

        // Sort by date (same as catalog sort mode)
        result = sortMode switch
        {
            CatalogSortMode.NewestFirst => result.OrderByDescending(i => i.SortDate).ToList(),
            CatalogSortMode.UrlTitleAsc => result.OrderBy(i =>
                i.ItemType == CatalogDisplayItemType.Collection
                    ? i.CollectionName ?? ""
                    : (i.Story?.Title ?? i.Story?.SourceUrl ?? ""))
                .ToList(),
            _ => result
        };

        return result;
    }
}
