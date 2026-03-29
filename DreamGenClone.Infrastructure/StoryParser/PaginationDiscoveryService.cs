using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace DreamGenClone.Infrastructure.StoryParser;

public sealed class PaginationDiscoveryService
{
    public Uri? DiscoverNextPage(IHtmlDocument document, Uri currentUri)
    {
        var nextLink = document.QuerySelector("a[title='Next Page']")
            ?? document.QuerySelector("a[rel='next']")
            ?? document.QuerySelector("a._pagination__item--next_1392n_40");

        if (nextLink is IHtmlAnchorElement nextAnchor)
        {
            var rawHref = nextAnchor.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(rawHref) && TryResolveHttpUri(currentUri, rawHref, out var resolvedNext))
            {
                return resolvedNext;
            }
        }

        var currentPage = GetPageNumberFromQuery(currentUri) ?? 1;
        var pageAnchors = document.QuerySelectorAll("a[href*='?page=']")
            .OfType<IHtmlAnchorElement>()
            .ToList();

        foreach (var anchor in pageAnchors)
        {
            var rawHref = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(rawHref) || !TryResolveHttpUri(currentUri, rawHref, out var candidate))
            {
                continue;
            }

            var candidatePage = GetPageNumberFromQuery(candidate);
            if (candidatePage.HasValue && candidatePage.Value == currentPage + 1)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryResolveHttpUri(Uri baseUri, string relativeOrAbsolute, out Uri resolved)
    {
        resolved = null!;
        if (!Uri.TryCreate(baseUri, relativeOrAbsolute, out var candidate))
        {
            return false;
        }

        if (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        resolved = candidate;
        return true;
    }

    private static int? GetPageNumberFromQuery(Uri uri)
    {
        var query = uri.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var parts = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("page", StringComparison.OrdinalIgnoreCase) && int.TryParse(Uri.UnescapeDataString(kv[1]), out var page))
            {
                return page;
            }
        }

        return null;
    }
}
