using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.Administration;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Domain.StoryParser;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.StoryAnalysis;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class ThemePreferenceCatalogLinkTests
{
    private static readonly List<ThemeCatalogEntry> CatalogEntries =
    [
        new() { Id = "intimacy", Label = "Intimacy", Keywords = ["close", "touch"], Weight = 3, IsEnabled = true, IsBuiltIn = true },
        new() { Id = "power-dynamics", Label = "Power Dynamics", Keywords = ["control"], Weight = 4, IsEnabled = true, IsBuiltIn = true },
        new() { Id = "confession", Label = "Confession", Keywords = ["confess"], Weight = 3, IsEnabled = true, IsBuiltIn = true },
    ];

    private sealed class FakeCatalogService : IThemeCatalogService
    {
        public Task<ThemeCatalogEntry?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(CatalogEntries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<ThemeCatalogEntry>> GetAllAsync(bool includeDisabled = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ThemeCatalogEntry>>(CatalogEntries);

        public Task SaveAsync(ThemeCatalogEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SeedDefaultsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryPersistence : ISqlitePersistence
    {
        private readonly List<ThemePreference> _preferences = [];

        public Task<DatabaseBackup> CreateDatabaseBackupAsync(string? displayName, CancellationToken ct = default)
            => throw new NotImplementedException();

        public void Add(ThemePreference pref) => _preferences.Add(pref);

        public Task SaveThemePreferenceAsync(ThemePreference preference, CancellationToken ct = default)
        {
            var idx = _preferences.FindIndex(p => p.Id == preference.Id);
            if (idx >= 0) _preferences[idx] = preference;
            else _preferences.Add(preference);
            return Task.CompletedTask;
        }

        public Task<ThemePreference?> LoadThemePreferenceAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_preferences.FirstOrDefault(p => p.Id == id));

        public Task<List<ThemePreference>> LoadAllThemePreferencesAsync(CancellationToken ct = default)
            => Task.FromResult(_preferences.ToList());

        public Task<List<ThemePreference>> LoadThemePreferencesByProfileAsync(string profileId, CancellationToken ct = default)
            => Task.FromResult(_preferences.Where(p => p.ProfileId == profileId).ToList());

        public Task<bool> DeleteThemePreferenceAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_preferences.RemoveAll(p => p.Id == id) > 0);

        public Task SaveStatWillingnessProfileAsync(StatWillingnessProfile profile, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StatWillingnessProfile?> LoadStatWillingnessProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StatWillingnessProfile?> LoadDefaultStatWillingnessProfileAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<StatWillingnessProfile>> LoadAllStatWillingnessProfilesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteStatWillingnessProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveHusbandAwarenessProfileAsync(HusbandAwarenessProfile profile, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<HusbandAwarenessProfile?> LoadHusbandAwarenessProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<HusbandAwarenessProfile>> LoadAllHusbandAwarenessProfilesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteHusbandAwarenessProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveScenarioDefinitionAsync(ScenarioDefinitionEntity definition, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ScenarioDefinitionEntity?> LoadScenarioDefinitionAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ScenarioDefinitionEntity>> LoadAllScenarioDefinitionsAsync(bool includeDisabled = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteScenarioDefinitionAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();

        // Stubs for remaining ISqlitePersistence members
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveScenarioAsync(string id, string name, string payloadJson, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(string Id, string Name, string PayloadJson, string UpdatedUtc)?> LoadScenarioAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<(string Id, string Name, string PayloadJson, string UpdatedUtc)>> LoadAllScenariosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteScenarioAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveParsedStoryAsync(ParsedStoryRecord record, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ParsedStoryRecord?> LoadParsedStoryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ParsedStoryRecord>> LoadParsedStoriesAsync(CatalogSortMode sortMode, int? limit = null, int? offset = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ParsedStoryRecord>> LoadParsedStoriesAsync(CatalogSortMode sortMode, bool includeArchived, int? limit = null, int? offset = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ParsedStoryRecord>> SearchParsedStoriesAsync(string query, CatalogSortMode sortMode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteParsedStoryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ArchiveParsedStoryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UnarchiveParsedStoryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> PurgeParsedStoryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UpdateCombinedTextAsync(string id, string combinedText, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ParsedStoryRecord>> FindBySourceUrlAsync(string sourceUrl, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveStorySummaryAsync(StorySummary summary, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorySummary?> LoadStorySummaryAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveStoryAnalysisAsync(StoryAnalysisResult analysis, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StoryAnalysisResult?> LoadStoryAnalysisAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveThemeProfileAsync(ThemeProfile profile, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ThemeProfile?> LoadThemeProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ThemeProfile>> LoadAllThemeProfilesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteThemeProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetDefaultThemeProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ThemeProfile?> LoadDefaultThemeProfileAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveToneProfileAsync(IntensityProfile profile, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IntensityProfile?> LoadToneProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<IntensityProfile>> LoadAllToneProfilesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteToneProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveBaseStatProfileAsync(BaseStatProfile profile, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<BaseStatProfile?> LoadBaseStatProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<BaseStatProfile>> LoadAllBaseStatProfilesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteBaseStatProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveStyleProfileAsync(SteeringProfile profile, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SteeringProfile?> LoadStyleProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<SteeringProfile>> LoadAllStyleProfilesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteStyleProfileAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveThemeCatalogEntryAsync(ThemeCatalogEntry entry, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ThemeCatalogEntry?> LoadThemeCatalogEntryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ThemeCatalogEntry>> LoadAllThemeCatalogEntriesAsync(bool includeDisabled, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteThemeCatalogEntryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveStoryRankingAsync(StoryRankingResult ranking, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StoryRankingResult?> LoadStoryRankingAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StoryRankingResult?> LoadStoryRankingByProfileAsync(string parsedStoryId, string profileId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<StoryRankingResult>> LoadStoryRankingsAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveStoryCollectionAsync(StoryCollection collection, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StoryCollection?> LoadStoryCollectionAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<StoryCollection>> LoadAllStoryCollectionsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteStoryCollectionAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<StoryCollection>> SearchStoryCollectionsAsync(string query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveStoryCollectionMemberAsync(StoryCollectionMembership membership, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<StoryCollectionMembership>> LoadCollectionMembersAsync(string collectionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<StoryCollection>> LoadCollectionsForStoryAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteStoryCollectionMemberAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteStoryCollectionMemberByStoryAsync(string collectionId, string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveUserStoryRatingAsync(UserStoryRating rating, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<UserStoryRating?> LoadUserStoryRatingAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteUserStoryRatingAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Dictionary<string, UserStoryRating>> LoadUserStoryRatingsBatchAsync(IEnumerable<string> parsedStoryIds, CancellationToken ct = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task AutoLink_MatchesNameToLabel_SetsCatalogId()
    {
        var persistence = new InMemoryPersistence();
        var pref = new ThemePreference { ProfileId = "p1", Name = "Intimacy", CatalogId = "" };
        persistence.Add(pref);

        var service = new ThemePreferenceService(persistence, new FakeCatalogService(), NullLogger<ThemePreferenceService>.Instance);

        var linked = await service.AutoLinkToCatalogAsync();

        Assert.Equal(1, linked);
        var updated = await persistence.LoadThemePreferenceAsync(pref.Id);
        Assert.Equal("intimacy", updated!.CatalogId);
    }

    [Fact]
    public async Task AutoLink_MatchesById_WhenNameIsId()
    {
        var persistence = new InMemoryPersistence();
        var pref = new ThemePreference { ProfileId = "p1", Name = "power-dynamics", CatalogId = "" };
        persistence.Add(pref);

        var service = new ThemePreferenceService(persistence, new FakeCatalogService(), NullLogger<ThemePreferenceService>.Instance);

        var linked = await service.AutoLinkToCatalogAsync();

        Assert.Equal(1, linked);
        var updated = await persistence.LoadThemePreferenceAsync(pref.Id);
        Assert.Equal("power-dynamics", updated!.CatalogId);
    }

    [Fact]
    public async Task AutoLink_SkipsAlreadyLinked()
    {
        var persistence = new InMemoryPersistence();
        var pref = new ThemePreference { ProfileId = "p1", Name = "Intimacy", CatalogId = "intimacy" };
        persistence.Add(pref);

        var service = new ThemePreferenceService(persistence, new FakeCatalogService(), NullLogger<ThemePreferenceService>.Instance);

        var linked = await service.AutoLinkToCatalogAsync();

        Assert.Equal(0, linked);
    }

    [Fact]
    public async Task AutoLink_UnlinkedPreference_ReturnsZero_WhenNoMatch()
    {
        var persistence = new InMemoryPersistence();
        var pref = new ThemePreference { ProfileId = "p1", Name = "NonexistentTheme", CatalogId = "" };
        persistence.Add(pref);

        var service = new ThemePreferenceService(persistence, new FakeCatalogService(), NullLogger<ThemePreferenceService>.Instance);

        var linked = await service.AutoLinkToCatalogAsync();

        Assert.Equal(0, linked);
        var updated = await persistence.LoadThemePreferenceAsync(pref.Id);
        Assert.Equal(string.Empty, updated!.CatalogId);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesCatalogId_WhenRelinkingPreference()
    {
        var persistence = new InMemoryPersistence();
        var pref = new ThemePreference { ProfileId = "p1", Name = "Custom", Description = "desc", Tier = ThemeTier.NiceToHave, CatalogId = string.Empty };
        persistence.Add(pref);

        var service = new ThemePreferenceService(persistence, new FakeCatalogService(), NullLogger<ThemePreferenceService>.Instance);

        var updated = await service.UpdateAsync(pref.Id, "Intimacy", "updated", ThemeTier.MustHave, "intimacy");

        Assert.NotNull(updated);
        Assert.Equal("intimacy", updated!.CatalogId);
        var loaded = await persistence.LoadThemePreferenceAsync(pref.Id);
        Assert.Equal("intimacy", loaded!.CatalogId);
    }

    [Fact]
    public async Task CatalogId_PersistsCorrectly()
    {
        var persistence = new InMemoryPersistence();
        var pref = new ThemePreference { ProfileId = "p1", Name = "Confession", CatalogId = "confession" };

        await persistence.SaveThemePreferenceAsync(pref);

        var loaded = await persistence.LoadThemePreferenceAsync(pref.Id);
        Assert.Equal("confession", loaded!.CatalogId);
    }

    [Fact]
    public void CatalogId_DefaultsToEmpty()
    {
        var pref = new ThemePreference();
        Assert.Equal(string.Empty, pref.CatalogId);
    }
}
