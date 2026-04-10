using DreamGenClone.Domain.StoryParser;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.StoryParser;

public sealed class CollectionPersistenceTests
{
    [Fact]
    public async Task SaveAndLoadStoryCollection_Works()
    {
        var persistence = await CreatePersistenceAsync();
        var collection = new StoryCollection { Name = "My Series", Description = "A multi-chapter story" };

        await persistence.SaveStoryCollectionAsync(collection);
        var loaded = await persistence.LoadStoryCollectionAsync(collection.Id);

        Assert.NotNull(loaded);
        Assert.Equal("My Series", loaded!.Name);
        Assert.Equal("A multi-chapter story", loaded.Description);
    }

    [Fact]
    public async Task LoadAllStoryCollections_ReturnsAll()
    {
        var persistence = await CreatePersistenceAsync();
        await persistence.SaveStoryCollectionAsync(new StoryCollection { Name = "Collection A" });
        await persistence.SaveStoryCollectionAsync(new StoryCollection { Name = "Collection B" });

        var all = await persistence.LoadAllStoryCollectionsAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task SearchStoryCollections_FiltersByName()
    {
        var persistence = await CreatePersistenceAsync();
        await persistence.SaveStoryCollectionAsync(new StoryCollection { Name = "Alpha Series" });
        await persistence.SaveStoryCollectionAsync(new StoryCollection { Name = "Beta Series" });

        var results = await persistence.SearchStoryCollectionsAsync("Alpha");

        Assert.Single(results);
        Assert.Equal("Alpha Series", results[0].Name);
    }

    [Fact]
    public async Task DeleteStoryCollection_RemovesCollectionAndMembers()
    {
        var persistence = await CreatePersistenceAsync();
        var collection = new StoryCollection { Name = "To Delete" };
        await persistence.SaveStoryCollectionAsync(collection);

        var story = CreateStoryRecord("story-1");
        await persistence.SaveParsedStoryAsync(story);

        var membership = new StoryCollectionMembership
        {
            CollectionId = collection.Id,
            ParsedStoryId = story.Id,
            SortOrder = 0
        };
        await persistence.SaveStoryCollectionMemberAsync(membership);

        var deleted = await persistence.DeleteStoryCollectionAsync(collection.Id);

        Assert.True(deleted);
        Assert.Null(await persistence.LoadStoryCollectionAsync(collection.Id));
        Assert.Empty(await persistence.LoadCollectionMembersAsync(collection.Id));
    }

    [Fact]
    public async Task SaveAndLoadCollectionMember_Works()
    {
        var persistence = await CreatePersistenceAsync();
        var collection = new StoryCollection { Name = "Series" };
        await persistence.SaveStoryCollectionAsync(collection);

        var story = CreateStoryRecord("story-1");
        await persistence.SaveParsedStoryAsync(story);

        var membership = new StoryCollectionMembership
        {
            CollectionId = collection.Id,
            ParsedStoryId = story.Id,
            SortOrder = 5
        };
        await persistence.SaveStoryCollectionMemberAsync(membership);

        var members = await persistence.LoadCollectionMembersAsync(collection.Id);

        Assert.Single(members);
        Assert.Equal(story.Id, members[0].ParsedStoryId);
        Assert.Equal(5, members[0].SortOrder);
    }

    [Fact]
    public async Task LoadCollectionsForStory_ReturnsCorrectCollections()
    {
        var persistence = await CreatePersistenceAsync();
        var col1 = new StoryCollection { Name = "Series A" };
        var col2 = new StoryCollection { Name = "Series B" };
        await persistence.SaveStoryCollectionAsync(col1);
        await persistence.SaveStoryCollectionAsync(col2);

        var story = CreateStoryRecord("story-1");
        await persistence.SaveParsedStoryAsync(story);

        await persistence.SaveStoryCollectionMemberAsync(new StoryCollectionMembership
        {
            CollectionId = col1.Id, ParsedStoryId = story.Id, SortOrder = 0
        });
        await persistence.SaveStoryCollectionMemberAsync(new StoryCollectionMembership
        {
            CollectionId = col2.Id, ParsedStoryId = story.Id, SortOrder = 0
        });

        var collections = await persistence.LoadCollectionsForStoryAsync(story.Id);

        Assert.Equal(2, collections.Count);
    }

    [Fact]
    public async Task DeleteStoryCollectionMemberByStory_RemovesCorrectMember()
    {
        var persistence = await CreatePersistenceAsync();
        var collection = new StoryCollection { Name = "Series" };
        await persistence.SaveStoryCollectionAsync(collection);

        var story1 = CreateStoryRecord("story-1");
        var story2 = CreateStoryRecord("story-2");
        await persistence.SaveParsedStoryAsync(story1);
        await persistence.SaveParsedStoryAsync(story2);

        await persistence.SaveStoryCollectionMemberAsync(new StoryCollectionMembership
        {
            CollectionId = collection.Id, ParsedStoryId = story1.Id, SortOrder = 0
        });
        await persistence.SaveStoryCollectionMemberAsync(new StoryCollectionMembership
        {
            CollectionId = collection.Id, ParsedStoryId = story2.Id, SortOrder = 1
        });

        await persistence.DeleteStoryCollectionMemberByStoryAsync(collection.Id, story1.Id);

        var remaining = await persistence.LoadCollectionMembersAsync(collection.Id);
        Assert.Single(remaining);
        Assert.Equal(story2.Id, remaining[0].ParsedStoryId);
    }

    [Fact]
    public async Task SaveStoryCollectionMember_UpsertsSortOrder()
    {
        var persistence = await CreatePersistenceAsync();
        var collection = new StoryCollection { Name = "Series" };
        await persistence.SaveStoryCollectionAsync(collection);

        var story = CreateStoryRecord("story-1");
        await persistence.SaveParsedStoryAsync(story);

        await persistence.SaveStoryCollectionMemberAsync(new StoryCollectionMembership
        {
            CollectionId = collection.Id, ParsedStoryId = story.Id, SortOrder = 0
        });

        // Upsert with new SortOrder
        await persistence.SaveStoryCollectionMemberAsync(new StoryCollectionMembership
        {
            CollectionId = collection.Id, ParsedStoryId = story.Id, SortOrder = 10
        });

        var members = await persistence.LoadCollectionMembersAsync(collection.Id);
        Assert.Single(members);
        Assert.Equal(10, members[0].SortOrder);
    }

    private static ParsedStoryRecord CreateStoryRecord(string id) => new()
    {
        Id = id,
        SourceUrl = $"https://www.literotica.com/s/{id}",
        SourceDomain = "www.literotica.com",
        Title = $"Story {id}",
        ParsedUtc = DateTime.UtcNow,
        PageCount = 1,
        CombinedText = "Text",
        StructuredPayloadJson = "[]",
        ParseStatus = ParseStatus.Success,
        DiagnosticsSummaryJson = "{}"
    };

    private static async Task<ISqlitePersistence> CreatePersistenceAsync()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"collection-tests-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={tempDb}"
        });

        var persistence = new SqlitePersistence(
            options,
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
        await persistence.InitializeAsync();
        return persistence;
    }
}
