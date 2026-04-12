using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.Administration;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.StoryAnalysis;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class ThemeCatalogServiceTests
{
    private static ThemeCatalogService CreateService(InMemoryThemeCatalogPersistence? persistence = null)
    {
        var store = persistence ?? new InMemoryThemeCatalogPersistence();
        return new ThemeCatalogService(store, NullLogger<ThemeCatalogService>.Instance);
    }

    [Fact]
    public async Task SeedDefaultsAsync_Creates10BuiltInEntries()
    {
        var persistence = new InMemoryThemeCatalogPersistence();
        var service = CreateService(persistence);

        await service.SeedDefaultsAsync();

        var all = await service.GetAllAsync(includeDisabled: true);
        Assert.Equal(10, all.Count);
        Assert.All(all, e => Assert.True(e.IsBuiltIn));
        Assert.All(all, e => Assert.True(e.IsEnabled));
    }

    [Fact]
    public async Task SeedDefaultsAsync_IsIdempotent()
    {
        var persistence = new InMemoryThemeCatalogPersistence();
        var service = CreateService(persistence);

        await service.SeedDefaultsAsync();
        await service.SeedDefaultsAsync();

        var all = await service.GetAllAsync(includeDisabled: true);
        Assert.Equal(10, all.Count);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsEntry_WhenExists()
    {
        var service = CreateService();
        await service.SeedDefaultsAsync();

        var entry = await service.GetByIdAsync("intimacy");

        Assert.NotNull(entry);
        Assert.Equal("Intimacy", entry.Label);
        Assert.NotEmpty(entry.Keywords);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var service = CreateService();

        var entry = await service.GetByIdAsync("nonexistent");

        Assert.Null(entry);
    }

    [Fact]
    public async Task GetAllAsync_ExcludesDisabled_ByDefault()
    {
        var persistence = new InMemoryThemeCatalogPersistence();
        var service = CreateService(persistence);
        await service.SeedDefaultsAsync();

        // Disable one entry
        var intimacy = await service.GetByIdAsync("intimacy");
        intimacy!.IsEnabled = false;
        await service.SaveAsync(intimacy);

        var enabled = await service.GetAllAsync(includeDisabled: false);
        var all = await service.GetAllAsync(includeDisabled: true);

        Assert.Equal(9, enabled.Count);
        Assert.Equal(10, all.Count);
    }

    [Fact]
    public async Task SaveAsync_ClampsWeight()
    {
        var service = CreateService();

        var entry = new ThemeCatalogEntry
        {
            Id = "test-theme",
            Label = "Test",
            Keywords = ["test"],
            Weight = 15
        };
        await service.SaveAsync(entry);

        var loaded = await service.GetByIdAsync("test-theme");
        Assert.Equal(10, loaded!.Weight);
    }

    [Fact]
    public async Task SaveAsync_RejectsInvalidId()
    {
        var service = CreateService();

        var entry = new ThemeCatalogEntry { Id = "INVALID ID!", Label = "Bad" };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(entry));
    }

    [Fact]
    public async Task SaveAsync_RejectsEmptyId()
    {
        var service = CreateService();

        var entry = new ThemeCatalogEntry { Id = "", Label = "Empty" };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(entry));
    }

    [Fact]
    public async Task SaveAsync_RejectsEnabledEntryWithoutKeywords()
    {
        var service = CreateService();

        var entry = new ThemeCatalogEntry { Id = "no-keywords", Label = "No Keywords", IsEnabled = true };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(entry));
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntry()
    {
        var persistence = new InMemoryThemeCatalogPersistence();
        var service = CreateService(persistence);

        var entry = new ThemeCatalogEntry { Id = "custom-theme", Label = "Custom", Keywords = ["custom"], IsBuiltIn = false };
        await service.SaveAsync(entry);

        await service.DeleteAsync("custom-theme");

        var loaded = await service.GetByIdAsync("custom-theme");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsForBuiltIn()
    {
        var service = CreateService();
        await service.SeedDefaultsAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync("intimacy"));
    }

    [Fact]
    public async Task DeleteAsync_DoesNotThrow_WhenNotFound()
    {
        var service = CreateService();

        var ex = await Record.ExceptionAsync(() => service.DeleteAsync("nonexistent"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task SeedDefaultsAsync_PopulatesStatAffinities()
    {
        var service = CreateService();
        await service.SeedDefaultsAsync();

        var intimacy = await service.GetByIdAsync("intimacy");
        Assert.NotNull(intimacy);
        Assert.NotEmpty(intimacy.StatAffinities);
        Assert.True(intimacy.StatAffinities.ContainsKey("Desire"));
        Assert.True(intimacy.StatAffinities.ContainsKey("Connection"));
    }

    /// <summary>
    /// Minimal in-memory ISqlitePersistence stub for theme catalog operations only.
    /// </summary>
    internal sealed class InMemoryThemeCatalogPersistence : ISqlitePersistence
    {
        private readonly Dictionary<string, ThemeCatalogEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public Task<DatabaseBackup> CreateDatabaseBackupAsync(string? displayName, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveThemeCatalogEntryAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default)
        {
            _entries[entry.Id] = entry;
            return Task.CompletedTask;
        }

        public Task<ThemeCatalogEntry?> LoadThemeCatalogEntryAsync(string id, CancellationToken cancellationToken = default)
        {
            _entries.TryGetValue(id, out var entry);
            return Task.FromResult(entry);
        }

        public Task<List<ThemeCatalogEntry>> LoadAllThemeCatalogEntriesAsync(bool includeDisabled, CancellationToken cancellationToken = default)
        {
            var entries = includeDisabled
                ? _entries.Values.ToList()
                : _entries.Values.Where(e => e.IsEnabled).ToList();
            return Task.FromResult(entries);
        }

        public Task<bool> DeleteThemeCatalogEntryAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_entries.Remove(id));

        // Not used by ThemeCatalogService — stubs only
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveScenarioAsync(string id, string name, string payloadJson, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(string Id, string Name, string PayloadJson, string UpdatedUtc)?> LoadScenarioAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<(string Id, string Name, string PayloadJson, string UpdatedUtc)>> LoadAllScenariosAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteScenarioAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveParsedStoryAsync(DreamGenClone.Domain.StoryParser.ParsedStoryRecord record, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.StoryParser.ParsedStoryRecord?> LoadParsedStoryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<DreamGenClone.Domain.StoryParser.ParsedStoryRecord>> LoadParsedStoriesAsync(DreamGenClone.Domain.StoryParser.CatalogSortMode sortMode, int? limit = null, int? offset = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<DreamGenClone.Domain.StoryParser.ParsedStoryRecord>> LoadParsedStoriesAsync(DreamGenClone.Domain.StoryParser.CatalogSortMode sortMode, bool includeArchived, int? limit = null, int? offset = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<DreamGenClone.Domain.StoryParser.ParsedStoryRecord>> SearchParsedStoriesAsync(string query, DreamGenClone.Domain.StoryParser.CatalogSortMode sortMode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteParsedStoryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ArchiveParsedStoryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UnarchiveParsedStoryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> PurgeParsedStoryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UpdateCombinedTextAsync(string id, string combinedText, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<DreamGenClone.Domain.StoryParser.ParsedStoryRecord>> FindBySourceUrlAsync(string sourceUrl, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveStorySummaryAsync(StorySummary summary, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorySummary?> LoadStorySummaryAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveStoryAnalysisAsync(StoryAnalysisResult analysis, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StoryAnalysisResult?> LoadStoryAnalysisAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveThemePreferenceAsync(ThemePreference preference, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ThemePreference?> LoadThemePreferenceAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ThemePreference>> LoadAllThemePreferencesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ThemePreference>> LoadThemePreferencesByProfileAsync(string profileId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteThemePreferenceAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
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
        public Task SaveStoryRankingAsync(StoryRankingResult ranking, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StoryRankingResult?> LoadStoryRankingAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StoryRankingResult?> LoadStoryRankingByProfileAsync(string parsedStoryId, string profileId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<StoryRankingResult>> LoadStoryRankingsAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveStoryCollectionAsync(DreamGenClone.Domain.StoryParser.StoryCollection collection, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DreamGenClone.Domain.StoryParser.StoryCollection?> LoadStoryCollectionAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<DreamGenClone.Domain.StoryParser.StoryCollection>> LoadAllStoryCollectionsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteStoryCollectionAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<DreamGenClone.Domain.StoryParser.StoryCollection>> SearchStoryCollectionsAsync(string query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveStoryCollectionMemberAsync(DreamGenClone.Domain.StoryParser.StoryCollectionMembership membership, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<DreamGenClone.Domain.StoryParser.StoryCollectionMembership>> LoadCollectionMembersAsync(string collectionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<DreamGenClone.Domain.StoryParser.StoryCollection>> LoadCollectionsForStoryAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteStoryCollectionMemberAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteStoryCollectionMemberByStoryAsync(string collectionId, string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveUserStoryRatingAsync(UserStoryRating rating, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<UserStoryRating?> LoadUserStoryRatingAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteUserStoryRatingAsync(string parsedStoryId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Dictionary<string, UserStoryRating>> LoadUserStoryRatingsBatchAsync(IEnumerable<string> parsedStoryIds, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
