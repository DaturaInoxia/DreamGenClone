using DreamGenClone.Domain.StoryParser;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.StoryParser;

public sealed class CatalogPersistenceTests
{
    [Fact]
    public async Task SaveAndLoadParsedStory_Works()
    {
        var persistence = await CreatePersistenceAsync();
        var record = new ParsedStoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceUrl = "https://www.literotica.com/s/sample",
            SourceDomain = "www.literotica.com",
            Title = "Sample Story",
            ParsedUtc = DateTime.UtcNow,
            PageCount = 1,
            CombinedText = "Sample",
            StructuredPayloadJson = "[]",
            ParseStatus = ParseStatus.Success,
            DiagnosticsSummaryJson = "{}"
        };

        await persistence.SaveParsedStoryAsync(record);
        var loaded = await persistence.LoadParsedStoryAsync(record.Id);

        Assert.NotNull(loaded);
        Assert.Equal(record.SourceUrl, loaded!.SourceUrl);
        Assert.Equal(record.PageCount, loaded.PageCount);
    }

    [Fact]
    public async Task SearchParsedStories_FiltersByMetadata()
    {
        var persistence = await CreatePersistenceAsync();

        await persistence.SaveParsedStoryAsync(new ParsedStoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceUrl = "https://www.literotica.com/s/a",
            SourceDomain = "www.literotica.com",
            Title = "Alpha Story",
            ParsedUtc = DateTime.UtcNow,
            PageCount = 1,
            CombinedText = "A",
            StructuredPayloadJson = "[]",
            ParseStatus = ParseStatus.Success,
            DiagnosticsSummaryJson = "{}"
        });

        await persistence.SaveParsedStoryAsync(new ParsedStoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceUrl = "https://www.literotica.com/s/b",
            SourceDomain = "www.literotica.com",
            Title = "Beta Story",
            ParsedUtc = DateTime.UtcNow,
            PageCount = 1,
            CombinedText = "B",
            StructuredPayloadJson = "[]",
            ParseStatus = ParseStatus.Success,
            DiagnosticsSummaryJson = "{}"
        });

        var results = await persistence.SearchParsedStoriesAsync("Alpha", CatalogSortMode.NewestFirst);
        Assert.Single(results);
        Assert.Equal("Alpha Story", results[0].Title);
    }

    [Fact]
    public async Task LoadParsedStories_SortsByMode()
    {
        var persistence = await CreatePersistenceAsync();

        await persistence.SaveParsedStoryAsync(new ParsedStoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceUrl = "https://www.literotica.com/s/zeta",
            SourceDomain = "www.literotica.com",
            Title = "Zeta",
            ParsedUtc = DateTime.UtcNow.AddMinutes(-10),
            PageCount = 1,
            CombinedText = "Z",
            StructuredPayloadJson = "[]",
            ParseStatus = ParseStatus.Success,
            DiagnosticsSummaryJson = "{}"
        });

        await persistence.SaveParsedStoryAsync(new ParsedStoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceUrl = "https://www.literotica.com/s/alpha",
            SourceDomain = "www.literotica.com",
            Title = "Alpha",
            ParsedUtc = DateTime.UtcNow,
            PageCount = 1,
            CombinedText = "A",
            StructuredPayloadJson = "[]",
            ParseStatus = ParseStatus.Success,
            DiagnosticsSummaryJson = "{}"
        });

        var newestFirst = await persistence.LoadParsedStoriesAsync(CatalogSortMode.NewestFirst);
        var alphaSort = await persistence.LoadParsedStoriesAsync(CatalogSortMode.UrlTitleAsc);

        Assert.Equal("Alpha", newestFirst[0].Title);
        Assert.Equal("Alpha", alphaSort[0].Title);
        Assert.Equal("Zeta", alphaSort[1].Title);
    }

    private static async Task<ISqlitePersistence> CreatePersistenceAsync()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"storyparser-tests-{Guid.NewGuid():N}.db");
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
