using System.Text.Json;
using DreamGenClone.Domain.Administration;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.StoryAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class StyleProfileAffinityTests
{
    [Fact]
    public void StyleProfile_ThemeAffinities_DefaultsToEmpty()
    {
        var profile = new SteeringProfile();
        Assert.NotNull(profile.ThemeAffinities);
        Assert.Empty(profile.ThemeAffinities);
    }

    [Fact]
    public void StyleProfile_EscalatingThemeIds_DefaultsToEmpty()
    {
        var profile = new SteeringProfile();
        Assert.NotNull(profile.EscalatingThemeIds);
        Assert.Empty(profile.EscalatingThemeIds);
    }

    [Fact]
    public void StyleProfile_StatBias_DefaultsToEmpty()
    {
        var profile = new SteeringProfile();
        Assert.NotNull(profile.StatBias);
        Assert.Empty(profile.StatBias);
    }

    [Fact]
    public void ThemeAffinities_RoundTripsViaJson()
    {
        var profile = new SteeringProfile
        {
            ThemeAffinities = new(StringComparer.OrdinalIgnoreCase)
            {
                ["intimacy"] = 2,
                ["romantic-tension"] = 2,
                ["emotional-vulnerability"] = 1
            }
        };

        var json = JsonSerializer.Serialize(profile);
        var deserialized = JsonSerializer.Deserialize<SteeringProfile>(json)!;

        Assert.Equal(3, deserialized.ThemeAffinities.Count);
        Assert.Equal(2, deserialized.ThemeAffinities["intimacy"]);
        Assert.Equal(2, deserialized.ThemeAffinities["romantic-tension"]);
        Assert.Equal(1, deserialized.ThemeAffinities["emotional-vulnerability"]);
    }

    [Fact]
    public void EscalatingThemeIds_RoundTripsViaJson()
    {
        var profile = new SteeringProfile
        {
            EscalatingThemeIds = ["dominance", "power-dynamics", "forbidden-risk", "humiliation", "infidelity"]
        };

        var json = JsonSerializer.Serialize(profile);
        var deserialized = JsonSerializer.Deserialize<SteeringProfile>(json)!;

        Assert.Equal(5, deserialized.EscalatingThemeIds.Count);
        Assert.Contains("dominance", deserialized.EscalatingThemeIds);
        Assert.Contains("infidelity", deserialized.EscalatingThemeIds);
    }

    [Fact]
    public void StatBias_RoundTripsViaJson()
    {
        var profile = new SteeringProfile
        {
            StatBias = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 1,
                ["Connection"] = 1
            }
        };

        var json = JsonSerializer.Serialize(profile);
        var deserialized = JsonSerializer.Deserialize<SteeringProfile>(json)!;

        Assert.Equal(2, deserialized.StatBias.Count);
        Assert.Equal(1, deserialized.StatBias["Desire"]);
        Assert.Equal(1, deserialized.StatBias["Connection"]);
    }

    [Fact]
    public async Task SultryDefault_HasExpectedAffinities()
    {
        var persistence = new InMemoryStyleProfilePersistence();
        var service = new SteeringProfileService(persistence, NullLogger<SteeringProfileService>.Instance);

        var profiles = await service.ListAsync();

        var sultry = Assert.Single(profiles, p => p.Name == "Sultry");

        Assert.Equal(5, sultry.ThemeAffinities["intimacy"]);
        Assert.Equal(4, sultry.ThemeAffinities["voyeurism"]);
        Assert.Equal(2, sultry.ThemeAffinities["forbidden-risk"]);

        Assert.Contains("dominance", sultry.EscalatingThemeIds);
        Assert.Contains("power-dynamics", sultry.EscalatingThemeIds);
        Assert.Contains("forbidden-risk", sultry.EscalatingThemeIds);
        Assert.Contains("humiliation", sultry.EscalatingThemeIds);
        Assert.Contains("infidelity", sultry.EscalatingThemeIds);
        Assert.Equal(5, sultry.EscalatingThemeIds.Count);

        Assert.Equal(5, sultry.StatBias["Desire"]);
        Assert.Equal(5, sultry.StatBias["Restraint"]);
    }

    private sealed class InMemoryStyleProfilePersistence : ISqlitePersistence
    {
        private readonly Dictionary<string, SteeringProfile> _styleProfiles = new(StringComparer.OrdinalIgnoreCase);

        public Task<DatabaseBackup> CreateDatabaseBackupAsync(string? displayName, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task SaveStyleProfileAsync(SteeringProfile profile, CancellationToken ct = default)
        {
            _styleProfiles[profile.Id] = profile;
            return Task.CompletedTask;
        }

        public Task<SteeringProfile?> LoadStyleProfileAsync(string id, CancellationToken ct = default)
        {
            _styleProfiles.TryGetValue(id, out var profile);
            return Task.FromResult(profile);
        }

        public Task<List<SteeringProfile>> LoadAllStyleProfilesAsync(CancellationToken ct = default)
            => Task.FromResult(_styleProfiles.Values.ToList());

        public Task<bool> DeleteStyleProfileAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_styleProfiles.Remove(id));

        // Stubs for remaining ISqlitePersistence members
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
        public Task SaveThemeCatalogEntryAsync(ThemeCatalogEntry entry, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ThemeCatalogEntry?> LoadThemeCatalogEntryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ThemeCatalogEntry>> LoadAllThemeCatalogEntriesAsync(bool includeDisabled, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteThemeCatalogEntryAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
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