using DreamGenClone.Application.StoryParser.Models;
using DreamGenClone.Infrastructure.StoryParser;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace DreamGenClone.Tests.StoryParser;

public sealed class ErrorHandlingTests
{
    [Fact]
    public async Task ParseFromUrl_ReturnsFailed_ForInvalidUrl()
    {
        var service = CreateService();
        var result = await service.ParseFromUrlAsync(new StoryParseRequest { SourceUrl = "not-a-url" });

        Assert.Equal(DreamGenClone.Domain.StoryParser.ParseStatus.Failed, result.ParseStatus);
        Assert.NotEmpty(result.Diagnostics.Errors);
    }

    [Fact]
    public async Task ParseFromUrl_ReturnsFailed_ForUnsupportedDomain()
    {
        var service = CreateService();
        var result = await service.ParseFromUrlAsync(new StoryParseRequest { SourceUrl = "https://example.com/story" });

        Assert.Equal(DreamGenClone.Domain.StoryParser.ParseStatus.Failed, result.ParseStatus);
        Assert.Contains(result.Diagnostics.Errors, e => e.Code == "unsupported_domain");
    }

    [Fact]
    public async Task ParseFromUrl_EmitsWarning_WhenMaxPageCountReached()
    {
        var service = CreateService(maxPageCount: 1);
        var result = await service.ParseFromUrlAsync(new StoryParseRequest { SourceUrl = "https://www.literotica.com/s/sample" });

        Assert.True(result.PageCount == 1);
        Assert.Contains(result.Diagnostics.Warnings, w => w.Code == "max_page_count_reached");
    }

    private static StoryParserService CreateService(int maxPageCount = 2)
    {
        var options = Options.Create(new DreamGenClone.Application.StoryParser.StoryParserOptions
        {
            SupportedDomains = ["www.literotica.com"],
            TimeoutSeconds = 2,
            MaxHtmlBytes = 1024,
            MaxPageCount = maxPageCount,
            ErrorModeDefault = "FailFast"
        });

        var client = new HttpClient(new FakeMessageHandler()) { Timeout = TimeSpan.FromSeconds(2) };
        var fetchClient = new HtmlFetchClient(client, options, NullLogger<HtmlFetchClient>.Instance);

        var tempDb = Path.Combine(Path.GetTempPath(), $"storyparser-tests-{Guid.NewGuid():N}.db");
        var persistenceOptions = Options.Create(new DreamGenClone.Infrastructure.Configuration.PersistenceOptions
        {
            ConnectionString = $"Data Source={tempDb}"
        });
        var persistence = new DreamGenClone.Infrastructure.Persistence.SqlitePersistence(
            persistenceOptions,
            NullLogger<DreamGenClone.Infrastructure.Persistence.SqlitePersistence>.Instance);
        persistence.InitializeAsync().GetAwaiter().GetResult();

        return new StoryParserService(
            fetchClient,
            new PaginationDiscoveryService(),
            new DomainStoryExtractor(),
            persistence,
            options,
            NullLogger<StoryParserService>.Instance);
    }

    private sealed class FakeMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body><article><h1>Title</h1><div itemprop='articleBody'><p>Body</p></div></article><a href='?page=2' title='Next Page'>Next</a></body></html>", Encoding.UTF8, "text/html")
            });
        }
    }
}
