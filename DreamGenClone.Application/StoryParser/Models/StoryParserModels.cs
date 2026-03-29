using DreamGenClone.Domain.StoryParser;

namespace DreamGenClone.Application.StoryParser.Models;

public sealed class StoryParseRequest
{
    public string SourceUrl { get; set; } = string.Empty;

    public ParseErrorMode? ErrorMode { get; set; }
}

public sealed class StoryParseResult
{
    public string? ParsedStoryId { get; set; }

    public string SourceUrl { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Author { get; set; }

    public ParseStatus ParseStatus { get; set; } = ParseStatus.Failed;

    public string CombinedText { get; set; } = string.Empty;

    public int PageCount { get; set; }

    public List<ParsedStoryPage> Pages { get; set; } = [];

    public ParseDiagnostics Diagnostics { get; set; } = new();
}

public sealed class StoryCatalogQuery
{
    public CatalogSortMode SortMode { get; set; } = CatalogSortMode.NewestFirst;

    public int? Limit { get; set; }

    public int? Offset { get; set; }
}

public sealed class StoryCatalogSearch
{
    public string Query { get; set; } = string.Empty;

    public CatalogSortMode SortMode { get; set; } = CatalogSortMode.NewestFirst;
}

public sealed class StoryCatalogEntry
{
    public string Id { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Author { get; set; }

    public string SourceUrl { get; set; } = string.Empty;

    public string SourceDomain { get; set; } = string.Empty;

    public DateTime ParsedUtc { get; set; }

    public ParseStatus ParseStatus { get; set; }

    public int PageCount { get; set; }
}

public sealed class ParsedStoryDetail
{
    public string Id { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Author { get; set; }

    public string SourceUrl { get; set; } = string.Empty;

    public DateTime ParsedUtc { get; set; }

    public int PageCount { get; set; }

    public string CombinedText { get; set; } = string.Empty;

    public ParseStatus ParseStatus { get; set; }

    public ParseDiagnostics Diagnostics { get; set; } = new();
}
