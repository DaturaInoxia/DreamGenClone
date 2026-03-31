using System.Globalization;
using System.Text.RegularExpressions;
using DreamGenClone.Application.StoryParser;
using DreamGenClone.Application.StoryParser.Models;
using DreamGenClone.Domain.StoryParser;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryParser;

public sealed class CollectionMatchingService : ICollectionMatchingService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<CollectionMatchingService> _logger;

    // Regex to strip chapter suffixes from URL slugs
    private static readonly Regex ChapterSuffixRegex = new(
        @"-(?:ch|chapter|pt|part)-\d+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Regex to strip chapter suffixes from titles
    private static readonly Regex TitleChapterSuffixRegex = new(
        @"\s*[-–—]?\s*(?:Ch\.\s*\d+|Chapter\s+\d+|Part\s+\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public CollectionMatchingService(ISqlitePersistence persistence, ILogger<CollectionMatchingService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<CollectionMatchResult> FindMatchesAsync(string sourceUrl, string? title, CancellationToken cancellationToken = default)
    {
        var result = new CollectionMatchResult();

        var baseSlug = ExtractBaseSlug(sourceUrl);
        if (baseSlug is null)
            return result;

        // Load all stories to find siblings with same base slug
        var allStories = await _persistence.LoadParsedStoriesAsync(
            CatalogSortMode.NewestFirst, includeArchived: true, cancellationToken: cancellationToken);

        var siblings = allStories
            .Where(s => !string.Equals(s.SourceUrl, sourceUrl, StringComparison.OrdinalIgnoreCase))
            .Where(s => IsBaseSlugMatch(s.SourceUrl, baseSlug))
            .ToList();

        if (siblings.Count > 0)
        {
            // Check if any sibling belongs to a collection
            foreach (var sibling in siblings)
            {
                var collections = await _persistence.LoadCollectionsForStoryAsync(sibling.Id, cancellationToken);
                foreach (var collection in collections)
                {
                    if (result.Matches.All(m => m.CollectionId != collection.Id))
                    {
                        result.Matches.Add(new CollectionMatch
                        {
                            CollectionId = collection.Id,
                            CollectionName = collection.Name,
                            MatchReason = CollectionMatchReason.UrlPattern,
                            Confidence = 1.0
                        });
                    }
                }
            }

            // If no collection found but siblings exist → orphan detection
            if (result.Matches.Count == 0)
            {
                var firstSibling = siblings[0];
                result.SuggestedCollectionName = SlugToTitle(baseSlug);
                result.OrphanSiblingStoryId = firstSibling.Id;
                result.OrphanSiblingStoryTitle = firstSibling.Title ?? firstSibling.SourceUrl;
            }

            return result;
        }

        // No URL slug match — try title similarity
        if (!string.IsNullOrEmpty(title))
        {
            var baseTitle = StripTitleChapterSuffix(title);
            if (!string.IsNullOrEmpty(baseTitle) && !string.Equals(baseTitle, title, StringComparison.OrdinalIgnoreCase))
            {
                // The title had a chapter suffix, so it's likely a chapter — look for similar titles
                foreach (var story in allStories)
                {
                    if (story.Title is null) continue;
                    // Skip the story being imported (same URL exclusion as slug matching)
                    if (string.Equals(story.SourceUrl, sourceUrl, StringComparison.OrdinalIgnoreCase)) continue;

                    var storyBaseTitle = StripTitleChapterSuffix(story.Title);
                    if (string.Equals(baseTitle, storyBaseTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        var collections = await _persistence.LoadCollectionsForStoryAsync(story.Id, cancellationToken);
                        foreach (var collection in collections)
                        {
                            if (result.Matches.All(m => m.CollectionId != collection.Id))
                            {
                                result.Matches.Add(new CollectionMatch
                                {
                                    CollectionId = collection.Id,
                                    CollectionName = collection.Name,
                                    MatchReason = CollectionMatchReason.TitleSimilarity,
                                    Confidence = 0.9
                                });
                            }
                        }

                        if (result.Matches.Count == 0 && result.OrphanSiblingStoryId is null)
                        {
                            result.SuggestedCollectionName = baseTitle;
                            result.OrphanSiblingStoryId = story.Id;
                            result.OrphanSiblingStoryTitle = story.Title;
                        }
                    }
                }
            }
        }

        return result;
    }

    internal static string? ExtractBaseSlug(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
            return null;

        // Get the last segment of the path (the slug)
        var path = uri.AbsolutePath.TrimEnd('/');
        var lastSlash = path.LastIndexOf('/');
        var slug = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;

        if (string.IsNullOrEmpty(slug))
            return null;

        // Strip chapter suffix
        var baseSlug = ChapterSuffixRegex.Replace(slug, string.Empty);
        return baseSlug;
    }

    private static bool IsBaseSlugMatch(string candidateUrl, string targetBaseSlug)
    {
        var candidateBase = ExtractBaseSlug(candidateUrl);
        return candidateBase is not null &&
               string.Equals(candidateBase, targetBaseSlug, StringComparison.OrdinalIgnoreCase);
    }

    internal static string StripTitleChapterSuffix(string title)
    {
        return TitleChapterSuffixRegex.Replace(title, string.Empty).Trim();
    }

    internal static string SlugToTitle(string slug)
    {
        // Convert "my-timid-new-girlfriend" → "My Timid New Girlfriend"
        var words = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w =>
            string.IsNullOrEmpty(w) ? w : char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
    }
}
