using DreamGenClone.Application.StoryParser;
using DreamGenClone.Application.StoryParser.Models;
using DreamGenClone.Domain.StoryParser;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.StoryParser;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.StoryParser;

public sealed class CatalogIntegrationTests
{
    [Fact]
    public async Task StoryParserService_ListAndSearch_ReturnEntries()
    {
        var persistence = await CreatePersistenceAsync();
        await persistence.SaveParsedStoryAsync(new ParsedStoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceUrl = "https://www.literotica.com/s/zeta",
            SourceDomain = "www.literotica.com",
            Title = "Zeta",
            ParsedUtc = DateTime.UtcNow,
            PageCount = 1,
            CombinedText = "Text",
            StructuredPayloadJson = "[]",
            ParseStatus = ParseStatus.Success,
            DiagnosticsSummaryJson = "{}"
        });

        var parserOptions = Options.Create(new StoryParserOptions());
        var service = new StoryParserService(
            new HtmlFetchClient(new HttpClient(new FakeHandler()), parserOptions, NullLogger<HtmlFetchClient>.Instance),
            new PaginationDiscoveryService(),
            new DomainStoryExtractor(),
            persistence,
            parserOptions,
            NullLogger<StoryParserService>.Instance);

        var listed = await service.ListAsync(new StoryCatalogQuery { SortMode = CatalogSortMode.NewestFirst });
        var searched = await service.SearchAsync(new StoryCatalogSearch { Query = "Zeta", SortMode = CatalogSortMode.NewestFirst });

        Assert.NotEmpty(listed);
        Assert.Single(searched);
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

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>ok</body></html>")
            });
        }
    }
}
