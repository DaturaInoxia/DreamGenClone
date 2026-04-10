using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using System.Text.RegularExpressions;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class PromptDealbreakerService : IPromptDealbreakerService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "your", "their", "they",
        "them", "have", "will", "would", "could", "should", "about", "same", "scene", "story",
        "style", "tone", "plot", "character", "characters", "context", "continue", "guidance"
    };

    private readonly IThemePreferenceService _themePreferenceService;

    public PromptDealbreakerService(IThemePreferenceService themePreferenceService)
    {
        _themePreferenceService = themePreferenceService;
    }

    public async Task<PromptDealbreakerResult> ValidateAsync(string text, string profileId, CancellationToken cancellationToken = default)
    {
        var result = new PromptDealbreakerResult();
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(profileId))
        {
            return result;
        }

        var normalized = text.ToLowerInvariant();
        var themes = await _themePreferenceService.ListByProfileAsync(profileId, cancellationToken);
        var dealbreakers = themes.Where(t => t.Tier == ThemeTier.HardDealBreaker).ToList();

        foreach (var theme in dealbreakers)
        {
            if (MatchesTheme(normalized, theme))
            {
                result.ViolatedThemes.Add(theme.Name);
            }
        }

        if (result.ViolatedThemes.Count > 0)
        {
            result.IsAllowed = false;
            result.Message = $"Prompt violates hard dealbreakers: {string.Join(", ", result.ViolatedThemes)}";
        }

        return result;
    }

    private static bool MatchesTheme(string normalizedText, ThemePreference theme)
    {
        if (string.IsNullOrWhiteSpace(theme.Name) && string.IsNullOrWhiteSpace(theme.Description))
        {
            return false;
        }

        var nameTokens = Tokenize(theme.Name);
        var descriptionTokens = Tokenize(theme.Description);
        var textWords = Regex.Matches(normalizedText, "[a-z0-9]+")
            .Select(x => x.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ContainsExactPhrase(normalizedText, theme.Name))
        {
            return true;
        }

        var phraseCandidates = BuildPhraseCandidates(nameTokens)
            .Concat(BuildPhraseCandidates(descriptionTokens))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var phrase in phraseCandidates)
        {
            if (ContainsExactPhrase(normalizedText, phrase))
            {
                return true;
            }
        }

        var tokens = nameTokens
            .Concat(descriptionTokens)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tokens.Count == 0)
        {
            return false;
        }

        var nameTokenHits = nameTokens.Count(textWords.Contains);
        var tokenHits = tokens.Count(textWords.Contains);

        if (nameTokens.Count > 0 && nameTokenHits > 0 && tokenHits >= 2)
        {
            return true;
        }

        if (nameTokens.Count == 0 && tokenHits >= 2)
        {
            return true;
        }

        return false;
    }

    private static List<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([' ', ',', '.', ';', ':', '(', ')', '[', ']', '"', '\'', '/', '\\', '-', '_', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length >= 3)
            .Where(x => !StopWords.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> BuildPhraseCandidates(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            yield break;
        }

        for (var i = 0; i < tokens.Count - 1; i++)
        {
            yield return $"{tokens[i]} {tokens[i + 1]}";
        }
    }

    private static bool ContainsExactPhrase(string normalizedText, string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        var trimmed = phrase.Trim().ToLowerInvariant();
        if (trimmed.Length < 3)
        {
            return false;
        }

        var pattern = $@"\b{Regex.Escape(trimmed)}\b";
        return Regex.IsMatch(normalizedText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}