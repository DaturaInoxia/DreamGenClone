using AngleSharp.Html.Parser;
using DreamGenClone.Application.StoryParser;
using DreamGenClone.Application.StoryParser.Models;
using DreamGenClone.Domain.StoryParser;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DreamGenClone.Infrastructure.StoryParser;

public sealed class StoryParserService : IStoryParserService, IStoryCatalogService
{
    private readonly HtmlFetchClient _htmlFetchClient;
    private readonly PaginationDiscoveryService _paginationDiscovery;
    private readonly DomainStoryExtractor _extractor;
    private readonly ISqlitePersistence _persistence;
    private readonly StoryParserOptions _options;
    private readonly ILogger<StoryParserService> _logger;
    private readonly HtmlParser _htmlParser = new();

    public StoryParserService(
        HtmlFetchClient htmlFetchClient,
        PaginationDiscoveryService paginationDiscovery,
        DomainStoryExtractor extractor,
        ISqlitePersistence persistence,
        IOptions<StoryParserOptions> options,
        ILogger<StoryParserService> logger)
    {
        _htmlFetchClient = htmlFetchClient;
        _paginationDiscovery = paginationDiscovery;
        _extractor = extractor;
        _persistence = persistence;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StoryParseResult> ParseFromUrlAsync(StoryParseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new ParseDiagnostics { StartedUtc = DateTime.UtcNow };
        var pages = new List<ParsedStoryPage>();
        var sourceUrl = request.SourceUrl?.Trim() ?? string.Empty;
        var status = ParseStatus.Failed;
        string? title = null;
        string? author = null;
        string? persistedId = null;

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var startUri)
            || (startUri.Scheme != Uri.UriSchemeHttps && startUri.Scheme != Uri.UriSchemeHttp))
        {
            diagnostics.Errors.Add(new DiagnosticItem
            {
                Code = "invalid_url",
                Message = "The provided URL is invalid.",
                Severity = "Error",
                Stage = "Validation",
                PageUrl = sourceUrl
            });

            diagnostics.CompletedUtc = DateTime.UtcNow;
            return BuildResult(sourceUrl, title, author, status, pages, diagnostics, persistedId);
        }

        if (_options.SupportedDomains.Count > 0 && !_options.SupportedDomains.Any(d => string.Equals(d, startUri.Host, StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Errors.Add(new DiagnosticItem
            {
                Code = "unsupported_domain",
                Message = $"Host '{startUri.Host}' is not in supported domains.",
                Severity = "Error",
                Stage = "Validation",
                PageUrl = sourceUrl
            });

            diagnostics.CompletedUtc = DateTime.UtcNow;
            return BuildResult(sourceUrl, title, author, status, pages, diagnostics, persistedId);
        }

        var errorMode = request.ErrorMode ?? ParseErrorModeOrDefault(_options.ErrorModeDefault);
        Uri? current = startUri;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (current is not null && pages.Count < _options.MaxPageCount)
        {
            if (!visited.Add(current.AbsoluteUri))
            {
                break;
            }

            try
            {
                _logger.LogInformation("Parsing story page {PageNumber} from {Url}", pages.Count + 1, current);

                var html = await _htmlFetchClient.FetchHtmlAsync(current, cancellationToken);
                var document = await _htmlParser.ParseDocumentAsync(html, cancellationToken);

                title ??= _extractor.ExtractTitle(document);
                author ??= _extractor.ExtractAuthor(document);
                var page = _extractor.ExtractPage(document, current, pages.Count + 1);
                pages.Add(page);

                current = _paginationDiscovery.DiscoverNextPage(document, current);
            }
            catch (Exception ex)
            {
                diagnostics.Errors.Add(new DiagnosticItem
                {
                    Code = "parse_error",
                    Message = ex.Message,
                    Severity = "Error",
                    Stage = "Extraction",
                    PageUrl = current?.AbsoluteUri
                });

                _logger.LogError(ex, "Story parse failure for {Url}", current);

                if (errorMode == ParseErrorMode.FailFast)
                {
                    break;
                }

                current = null;
            }
        }

        if (pages.Count >= _options.MaxPageCount && current is not null)
        {
            diagnostics.Warnings.Add(new DiagnosticItem
            {
                Code = "max_page_count_reached",
                Message = "Parsing stopped because max page count was reached.",
                Severity = "Warning",
                Stage = "Discovery",
                PageUrl = current.AbsoluteUri
            });
        }

        if (pages.Count > 0)
        {
            pages[^1].IsTerminalPage = true;
        }

        status = diagnostics.Errors.Count switch
        {
            0 when pages.Count > 0 => ParseStatus.Success,
            _ when pages.Count > 0 => ParseStatus.PartialSuccess,
            _ => ParseStatus.Failed
        };

        var combinedText = string.Join("\n\n", pages.Select(p => p.ExtractedText).Where(t => !string.IsNullOrWhiteSpace(t)));
        var record = new ParsedStoryRecord
        {
            SourceUrl = sourceUrl,
            SourceDomain = startUri.Host,
            Title = title,
            Author = author,
            ParsedUtc = DateTime.UtcNow,
            PageCount = pages.Count,
            CombinedText = combinedText,
            StructuredPayloadJson = JsonSerializer.Serialize(pages),
            ParseStatus = status,
            DiagnosticsSummaryJson = JsonSerializer.Serialize(diagnostics)
        };

        if (pages.Count > 0)
        {
            await _persistence.SaveParsedStoryAsync(record, cancellationToken);
            persistedId = record.Id;
            _logger.LogInformation("Story parse persisted with id {ParsedStoryId}", persistedId);
        }

        diagnostics.CompletedUtc = DateTime.UtcNow;
        return BuildResult(sourceUrl, title, author, status, pages, diagnostics, persistedId);
    }

    public async Task<ParsedStoryDetail?> GetParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        var record = await _persistence.LoadParsedStoryAsync(id, cancellationToken);
        if (record is null)
        {
            return null;
        }

        return new ParsedStoryDetail
        {
            Id = record.Id,
            Title = record.Title,
            Author = record.Author,
            SourceUrl = record.SourceUrl,
            ParsedUtc = record.ParsedUtc,
            PageCount = record.PageCount,
            CombinedText = record.CombinedText,
            ParseStatus = record.ParseStatus,
            Diagnostics = DeserializeDiagnostics(record.DiagnosticsSummaryJson)
        };
    }

    public async Task<bool> DeleteParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _persistence.DeleteParsedStoryAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<StoryCatalogEntry>> ListAsync(StoryCatalogQuery query, CancellationToken cancellationToken = default)
    {
        var records = await _persistence.LoadParsedStoriesAsync(query.SortMode, query.Limit, query.Offset, cancellationToken);
        return records.Select(ToCatalogEntry).ToList();
    }

    public async Task<IReadOnlyList<StoryCatalogEntry>> SearchAsync(StoryCatalogSearch query, CancellationToken cancellationToken = default)
    {
        var records = await _persistence.SearchParsedStoriesAsync(query.Query, query.SortMode, cancellationToken);
        return records.Select(ToCatalogEntry).ToList();
    }

    private static StoryCatalogEntry ToCatalogEntry(ParsedStoryRecord record)
    {
        return new StoryCatalogEntry
        {
            Id = record.Id,
            Title = record.Title,
            Author = record.Author,
            SourceUrl = record.SourceUrl,
            SourceDomain = record.SourceDomain,
            ParsedUtc = record.ParsedUtc,
            ParseStatus = record.ParseStatus,
            PageCount = record.PageCount
        };
    }

    private static ParseErrorMode ParseErrorModeOrDefault(string configured)
    {
        return Enum.TryParse<ParseErrorMode>(configured, ignoreCase: true, out var parsed)
            ? parsed
            : ParseErrorMode.FailFast;
    }

    private static ParseDiagnostics DeserializeDiagnostics(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ParseDiagnostics();
        }

        try
        {
            return JsonSerializer.Deserialize<ParseDiagnostics>(json) ?? new ParseDiagnostics();
        }
        catch
        {
            return new ParseDiagnostics();
        }
    }

    private static StoryParseResult BuildResult(
        string sourceUrl,
        string? title,
        string? author,
        ParseStatus status,
        List<ParsedStoryPage> pages,
        ParseDiagnostics diagnostics,
        string? parsedStoryId)
    {
        return new StoryParseResult
        {
            ParsedStoryId = parsedStoryId,
            SourceUrl = sourceUrl,
            Title = title,
            Author = author,
            ParseStatus = status,
            CombinedText = string.Join("\n\n", pages.Select(p => p.ExtractedText).Where(t => !string.IsNullOrWhiteSpace(t))),
            PageCount = pages.Count,
            Pages = pages,
            Diagnostics = diagnostics
        };
    }
}
