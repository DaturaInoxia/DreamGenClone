using DreamGenClone.Domain.StoryParser;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.StoryParser;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.StoryParser;

public sealed class CollectionMatchingTests
{
    // ── ExtractBaseSlug ──

    [Theory]
    [InlineData("https://www.literotica.com/s/my-story-ch-5", "my-story")]
    [InlineData("https://www.literotica.com/s/my-story-chapter-12", "my-story")]
    [InlineData("https://www.literotica.com/s/my-story-pt-3", "my-story")]
    [InlineData("https://www.literotica.com/s/my-story-part-4", "my-story")]
    [InlineData("https://www.literotica.com/s/my-story", "my-story")]
    public void ExtractBaseSlug_HandlesChapterSuffixes(string url, string expected)
    {
        var result = CollectionMatchingService.ExtractBaseSlug(url);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void ExtractBaseSlug_ReturnsNull_ForInvalidUrls(string url)
    {
        Assert.Null(CollectionMatchingService.ExtractBaseSlug(url));
    }

    // ── StripTitleChapterSuffix ──

    [Theory]
    [InlineData("My Story Ch. 5", "My Story")]
    [InlineData("My Story Chapter 12", "My Story")]
    [InlineData("My Story Part 3", "My Story")]
    [InlineData("My Story - Ch. 1", "My Story")]
    [InlineData("My Story", "My Story")]
    public void StripTitleChapterSuffix_Works(string title, string expected)
    {
        var result = CollectionMatchingService.StripTitleChapterSuffix(title);
        Assert.Equal(expected, result);
    }

    // ── SlugToTitle ──

    [Theory]
    [InlineData("my-timid-new-girlfriend", "My Timid New Girlfriend")]
    [InlineData("single", "Single")]
    public void SlugToTitle_ConvertsCorrectly(string slug, string expected)
    {
        var result = CollectionMatchingService.SlugToTitle(slug);
        Assert.Equal(expected, result);
    }

    // ── ExtractChapterNumber (from StoryCollectionService) ──

    [Theory]
    [InlineData("https://www.literotica.com/s/my-story-ch-5", null, 5)]
    [InlineData("https://www.literotica.com/s/my-story-chapter-12", null, 12)]
    [InlineData("https://www.literotica.com/s/my-story-pt-3", null, 3)]
    [InlineData("https://www.literotica.com/s/my-story-part-4", null, 4)]
    [InlineData("https://www.literotica.com/s/my-story", "My Story Ch. 7", 7)]
    [InlineData("https://www.literotica.com/s/my-story", "My Story Chapter 10", 10)]
    [InlineData("https://www.literotica.com/s/my-story", "My Story Part 2", 2)]
    [InlineData("https://www.literotica.com/s/my-story", null, null)]
    [InlineData("https://www.literotica.com/s/my-story", "My Story", null)]
    public void ExtractChapterNumber_Works(string url, string? title, int? expected)
    {
        var result = StoryCollectionService.ExtractChapterNumber(url, title);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractChapterNumber_UrlTakesPrecedenceOverTitle()
    {
        // URL says ch-5 but title says Chapter 10 — URL should win
        var result = StoryCollectionService.ExtractChapterNumber(
            "https://www.literotica.com/s/my-story-ch-5", "My Story Chapter 10");
        Assert.Equal(5, result);
    }

    [Fact]
    public async Task FindMatchesAsync_DoesNotSelfMatch_FirstChapter()
    {
        // Arrange: single chapter story already in DB
        var persistence = await CreatePersistenceAsync();
        var story = new ParsedStoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceUrl = "https://www.literotica.com/s/the-wife-games-ch-01",
            SourceDomain = "www.literotica.com",
            Title = "The Wife Games Ch. 01",
            ParsedUtc = DateTime.UtcNow,
            PageCount = 2,
            CombinedText = "Text",
            StructuredPayloadJson = "[]",
            ParseStatus = ParseStatus.Success,
            DiagnosticsSummaryJson = "{}"
        };
        await persistence.SaveParsedStoryAsync(story);

        var service = new CollectionMatchingService(persistence, NullLogger<CollectionMatchingService>.Instance);

        // Act
        var result = await service.FindMatchesAsync(story.SourceUrl, story.Title);

        // Assert: no matches and no orphan sibling — the story should not match itself
        Assert.Empty(result.Matches);
        Assert.Null(result.OrphanSiblingStoryId);
    }

    [Fact]
    public async Task FindMatchesAsync_FindsSibling_SecondChapter()
    {
        // Arrange: two chapters in DB
        var persistence = await CreatePersistenceAsync();
        var ch1 = new ParsedStoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceUrl = "https://www.literotica.com/s/the-wife-games-ch-01",
            SourceDomain = "www.literotica.com",
            Title = "The Wife Games Ch. 01",
            ParsedUtc = DateTime.UtcNow.AddMinutes(-5),
            PageCount = 2,
            CombinedText = "Text",
            StructuredPayloadJson = "[]",
            ParseStatus = ParseStatus.Success,
            DiagnosticsSummaryJson = "{}"
        };
        await persistence.SaveParsedStoryAsync(ch1);

        var ch2Url = "https://www.literotica.com/s/the-wife-games-ch-02";
        var ch2 = new ParsedStoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceUrl = ch2Url,
            SourceDomain = "www.literotica.com",
            Title = "The Wife Games Ch. 02",
            ParsedUtc = DateTime.UtcNow,
            PageCount = 2,
            CombinedText = "Text",
            StructuredPayloadJson = "[]",
            ParseStatus = ParseStatus.Success,
            DiagnosticsSummaryJson = "{}"
        };
        await persistence.SaveParsedStoryAsync(ch2);

        var service = new CollectionMatchingService(persistence, NullLogger<CollectionMatchingService>.Instance);

        // Act: simulate importing ch-02 after ch-01 already exists
        var result = await service.FindMatchesAsync(ch2Url, ch2.Title);

        // Assert: should find ch-01 as an orphan sibling
        Assert.NotNull(result.OrphanSiblingStoryId);
        Assert.Equal(ch1.Id, result.OrphanSiblingStoryId);
        Assert.Equal("The Wife Games", result.SuggestedCollectionName);
    }

    private static async Task<ISqlitePersistence> CreatePersistenceAsync()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"matching-tests-{Guid.NewGuid():N}.db");
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
