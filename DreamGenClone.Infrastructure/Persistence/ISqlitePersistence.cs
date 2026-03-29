namespace DreamGenClone.Infrastructure.Persistence;

using DreamGenClone.Domain.StoryParser;

public interface ISqlitePersistence
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    // Scenario operations
    Task SaveScenarioAsync(string id, string name, string payloadJson, CancellationToken cancellationToken = default);
    Task<(string Id, string Name, string PayloadJson, string UpdatedUtc)?> LoadScenarioAsync(string id, CancellationToken cancellationToken = default);
    Task<List<(string Id, string Name, string PayloadJson, string UpdatedUtc)>> LoadAllScenariosAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteScenarioAsync(string id, CancellationToken cancellationToken = default);

    // Parsed story operations
    Task SaveParsedStoryAsync(ParsedStoryRecord record, CancellationToken cancellationToken = default);
    Task<ParsedStoryRecord?> LoadParsedStoryAsync(string id, CancellationToken cancellationToken = default);
    Task<List<ParsedStoryRecord>> LoadParsedStoriesAsync(CatalogSortMode sortMode, int? limit = null, int? offset = null, CancellationToken cancellationToken = default);
    Task<List<ParsedStoryRecord>> SearchParsedStoriesAsync(string query, CatalogSortMode sortMode, CancellationToken cancellationToken = default);
    Task<bool> DeleteParsedStoryAsync(string id, CancellationToken cancellationToken = default);
}
