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

        var textWords = Regex.Matches(normalizedText, "[a-z0-9]+")
            .Select(x => x.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tokens = (theme.Name + " " + theme.Description)
            .Split([' ', ',', '.', ';', ':', '(', ')', '[', ']', '"', '\'', '/', '\\', '-', '_', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length >= 3)
            .Where(x => !StopWords.Contains(x))
            .Distinct()
            .ToList();

        if (tokens.Count == 0)
        {
            return false;
        }

        var tokenHits = tokens.Count(textWords.Contains);
        if (tokenHits >= 2)
        {
            return true;
        }

        var phraseCandidates = new[] { theme.Name, theme.Description }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim().ToLowerInvariant())
            .Where(x => x.Length >= 3)
            .Distinct();

        foreach (var phrase in phraseCandidates)
        {
            var pattern = $@"\b{Regex.Escape(phrase)}\b";
            if (Regex.IsMatch(normalizedText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }
}