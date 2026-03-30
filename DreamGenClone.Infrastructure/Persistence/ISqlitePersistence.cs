namespace DreamGenClone.Infrastructure.Persistence;

using DreamGenClone.Domain.StoryAnalysis;
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
    Task<List<ParsedStoryRecord>> LoadParsedStoriesAsync(CatalogSortMode sortMode, bool includeArchived, int? limit = null, int? offset = null, CancellationToken cancellationToken = default);
    Task<List<ParsedStoryRecord>> SearchParsedStoriesAsync(string query, CatalogSortMode sortMode, CancellationToken cancellationToken = default);
    Task<bool> DeleteParsedStoryAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> ArchiveParsedStoryAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> UnarchiveParsedStoryAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> PurgeParsedStoryAsync(string id, CancellationToken cancellationToken = default);
    Task<List<ParsedStoryRecord>> FindBySourceUrlAsync(string sourceUrl, CancellationToken cancellationToken = default);

    // Story summary operations
    Task SaveStorySummaryAsync(StorySummary summary, CancellationToken cancellationToken = default);
    Task<StorySummary?> LoadStorySummaryAsync(string parsedStoryId, CancellationToken cancellationToken = default);

    // Story analysis operations
    Task SaveStoryAnalysisAsync(StoryAnalysisResult analysis, CancellationToken cancellationToken = default);
    Task<StoryAnalysisResult?> LoadStoryAnalysisAsync(string parsedStoryId, CancellationToken cancellationToken = default);

    // Theme preference operations
    Task SaveThemePreferenceAsync(ThemePreference preference, CancellationToken cancellationToken = default);
    Task<ThemePreference?> LoadThemePreferenceAsync(string id, CancellationToken cancellationToken = default);
    Task<List<ThemePreference>> LoadAllThemePreferencesAsync(CancellationToken cancellationToken = default);
    Task<List<ThemePreference>> LoadThemePreferencesByProfileAsync(string profileId, CancellationToken cancellationToken = default);
    Task<bool> DeleteThemePreferenceAsync(string id, CancellationToken cancellationToken = default);

    // Ranking profile operations
    Task SaveRankingProfileAsync(RankingProfile profile, CancellationToken cancellationToken = default);
    Task<RankingProfile?> LoadRankingProfileAsync(string id, CancellationToken cancellationToken = default);
    Task<List<RankingProfile>> LoadAllRankingProfilesAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteRankingProfileAsync(string id, CancellationToken cancellationToken = default);
    Task SetDefaultRankingProfileAsync(string id, CancellationToken cancellationToken = default);
    Task<RankingProfile?> LoadDefaultRankingProfileAsync(CancellationToken cancellationToken = default);

    // Story ranking operations
    Task SaveStoryRankingAsync(StoryRankingResult ranking, CancellationToken cancellationToken = default);
    Task<StoryRankingResult?> LoadStoryRankingAsync(string parsedStoryId, CancellationToken cancellationToken = default);
    Task<StoryRankingResult?> LoadStoryRankingByProfileAsync(string parsedStoryId, string profileId, CancellationToken cancellationToken = default);
    Task<List<StoryRankingResult>> LoadStoryRankingsAsync(string parsedStoryId, CancellationToken cancellationToken = default);
}
