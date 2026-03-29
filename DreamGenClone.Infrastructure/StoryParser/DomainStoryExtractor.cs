using AngleSharp.Html.Dom;
using DreamGenClone.Domain.StoryParser;

namespace DreamGenClone.Infrastructure.StoryParser;

public sealed class DomainStoryExtractor
{
    public string ExtractTitle(IHtmlDocument document)
    {
        var title = document.QuerySelector("article h1")?.TextContent
            ?? document.QuerySelector("h1")?.TextContent
            ?? document.Title;

        return NormalizeInline(title);
    }

    public string? ExtractAuthor(IHtmlDocument document)
    {
        var author = document.QuerySelector("a.y_eU[href*='/stories/member']")?.TextContent
            ?? document.QuerySelector("span.b-story-user-y a")?.TextContent
            ?? document.QuerySelector("a[class*='author']")?.TextContent
            ?? document.QuerySelector("a[href*='/memberpage']")?.TextContent;

        var normalized = NormalizeInline(author);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public ParsedStoryPage ExtractPage(IHtmlDocument document, Uri pageUri, int sequence)
    {
        var articleBody = document.QuerySelector("[itemprop='articleBody']")
            ?? document.QuerySelector("._article__content_nuz12_81")
            ?? document.QuerySelector("article");

        var paragraphs = articleBody?.QuerySelectorAll("p")?.Select(p => NormalizeInline(p.TextContent))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList() ?? [];

        var content = string.Join("\n\n", paragraphs);
        return new ParsedStoryPage
        {
            Sequence = sequence,
            PageUrl = pageUri.ToString(),
            ExtractedText = content,
            IsTerminalPage = false
        };
    }

    private static string NormalizeInline(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalized = input.Replace("\r", " ").Replace("\n", " ").Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return normalized;
    }
}
