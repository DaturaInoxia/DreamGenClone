using System.Text.RegularExpressions;
using DreamGenClone.Application.StoryParser;
using DreamGenClone.Domain.StoryParser;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryParser;

public sealed class StoryCollectionService : IStoryCollectionService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<StoryCollectionService> _logger;

    // URL slug chapter patterns: -ch-5, -chapter-12, -pt-3, -part-4
    private static readonly Regex UrlChapterRegex = new(
        @"-(?:ch|chapter|pt|part)-(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Title chapter patterns: Ch. 5, Chapter 12, Part 3
    private static readonly Regex TitleChapterRegex = new(
        @"(?:Ch\.\s*|Chapter\s+|Part\s+)(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public StoryCollectionService(ISqlitePersistence persistence, ILogger<StoryCollectionService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<StoryCollection> CreateCollectionAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        var collection = new StoryCollection
        {
            Name = name,
            Description = description
        };

        await _persistence.SaveStoryCollectionAsync(collection, cancellationToken);
        _logger.LogInformation("Created story collection {CollectionId}: {Name}", collection.Id, name);
        return collection;
    }

    public async Task<StoryCollection> UpdateCollectionAsync(string id, string name, string? description, CancellationToken cancellationToken = default)
    {
        var existing = await _persistence.LoadStoryCollectionAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Collection {id} not found");

        existing.Name = name;
        existing.Description = description;
        existing.UpdatedUtc = DateTime.UtcNow;

        await _persistence.SaveStoryCollectionAsync(existing, cancellationToken);
        _logger.LogInformation("Updated story collection {CollectionId}: {Name}", id, name);
        return existing;
    }

    public Task<bool> DeleteCollectionAsync(string id, CancellationToken cancellationToken = default)
    {
        return _persistence.DeleteStoryCollectionAsync(id, cancellationToken);
    }

    public Task<StoryCollection?> GetCollectionAsync(string id, CancellationToken cancellationToken = default)
    {
        return _persistence.LoadStoryCollectionAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<StoryCollection>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadAllStoryCollectionsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoryCollection>> SearchCollectionsAsync(string query, CancellationToken cancellationToken = default)
    {
        return await _persistence.SearchStoryCollectionsAsync(query, cancellationToken);
    }

    public async Task<StoryCollectionMembership> AddStoryToCollectionAsync(
        string collectionId, string parsedStoryId, int? sortOrder = null, CancellationToken cancellationToken = default)
    {
        var resolvedOrder = sortOrder;

        if (resolvedOrder is null)
        {
            // Try to auto-extract chapter number from story URL or title
            var story = await _persistence.LoadParsedStoryAsync(parsedStoryId, cancellationToken);
            if (story is not null)
            {
                resolvedOrder = ExtractChapterNumber(story.SourceUrl, story.Title);
            }

            // If still null, fall back to max+1
            if (resolvedOrder is null)
            {
                var members = await _persistence.LoadCollectionMembersAsync(collectionId, cancellationToken);
                resolvedOrder = members.Count > 0 ? members.Max(m => m.SortOrder) + 1 : 0;
            }
        }

        var membership = new StoryCollectionMembership
        {
            CollectionId = collectionId,
            ParsedStoryId = parsedStoryId,
            SortOrder = resolvedOrder.Value
        };

        await _persistence.SaveStoryCollectionMemberAsync(membership, cancellationToken);
        _logger.LogInformation("Added story {StoryId} to collection {CollectionId} at order {SortOrder}",
            parsedStoryId, collectionId, resolvedOrder.Value);

        return membership;
    }

    public Task<bool> RemoveStoryFromCollectionAsync(string collectionId, string parsedStoryId, CancellationToken cancellationToken = default)
    {
        return _persistence.DeleteStoryCollectionMemberByStoryAsync(collectionId, parsedStoryId, cancellationToken);
    }

    public async Task<IReadOnlyList<StoryCollectionMembership>> GetCollectionMembersAsync(string collectionId, CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadCollectionMembersAsync(collectionId, cancellationToken);
    }

    public async Task<IReadOnlyList<StoryCollection>> GetCollectionsForStoryAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadCollectionsForStoryAsync(parsedStoryId, cancellationToken);
    }

    public async Task ReorderCollectionMembersAsync(string collectionId, List<string> parsedStoryIdsInOrder, CancellationToken cancellationToken = default)
    {
        var members = await _persistence.LoadCollectionMembersAsync(collectionId, cancellationToken);
        var membersByStory = members.ToDictionary(m => m.ParsedStoryId);

        for (var i = 0; i < parsedStoryIdsInOrder.Count; i++)
        {
            if (membersByStory.TryGetValue(parsedStoryIdsInOrder[i], out var member))
            {
                member.SortOrder = i;
                await _persistence.SaveStoryCollectionMemberAsync(member, cancellationToken);
            }
        }
    }

    internal static int? ExtractChapterNumber(string? sourceUrl, string? title)
    {
        if (!string.IsNullOrEmpty(sourceUrl))
        {
            var urlMatch = UrlChapterRegex.Match(sourceUrl);
            if (urlMatch.Success && int.TryParse(urlMatch.Groups[1].Value, out var urlNum))
            {
                return urlNum;
            }
        }

        if (!string.IsNullOrEmpty(title))
        {
            var titleMatch = TitleChapterRegex.Match(title);
            if (titleMatch.Success && int.TryParse(titleMatch.Groups[1].Value, out var titleNum))
            {
                return titleNum;
            }
        }

        return null;
    }
}
