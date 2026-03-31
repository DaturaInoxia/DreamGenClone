using DreamGenClone.Domain.StoryParser;

namespace DreamGenClone.Application.StoryParser;

public interface IStoryCollectionService
{
    Task<StoryCollection> CreateCollectionAsync(string name, string? description, CancellationToken cancellationToken = default);
    Task<StoryCollection> UpdateCollectionAsync(string id, string name, string? description, CancellationToken cancellationToken = default);
    Task<bool> DeleteCollectionAsync(string id, CancellationToken cancellationToken = default);
    Task<StoryCollection?> GetCollectionAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoryCollection>> ListCollectionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoryCollection>> SearchCollectionsAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a story to a collection. If sortOrder is null, auto-extracts chapter number
    /// from the story's URL or title. Falls back to max existing SortOrder + 1.
    /// </summary>
    Task<StoryCollectionMembership> AddStoryToCollectionAsync(string collectionId, string parsedStoryId, int? sortOrder = null, CancellationToken cancellationToken = default);
    Task<bool> RemoveStoryFromCollectionAsync(string collectionId, string parsedStoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoryCollectionMembership>> GetCollectionMembersAsync(string collectionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoryCollection>> GetCollectionsForStoryAsync(string parsedStoryId, CancellationToken cancellationToken = default);
    Task ReorderCollectionMembersAsync(string collectionId, List<string> parsedStoryIdsInOrder, CancellationToken cancellationToken = default);
}
