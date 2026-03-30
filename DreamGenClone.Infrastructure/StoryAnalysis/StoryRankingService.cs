using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class StoryRankingService : IStoryRankingService
{
    private readonly ILmStudioClient _lmClient;
    private readonly ISqlitePersistence _persistence;
    private readonly IThemePreferenceService _themeService;
    private readonly StoryAnalysisOptions _options;
    private readonly LmStudioOptions _lmOptions;
    private readonly ILogger<StoryRankingService> _logger;

    private const string SystemMessage =
        """
        You are a literary analyst detecting thematic content in stories.
        You will be given a list of themes to check for. For each theme, determine:
        1. Whether the theme is present in the story
        2. The intensity of the theme: "None", "Minor", "Moderate", "Major", or "Central"
        3. Brief evidence from the story supporting your determination

        Respond ONLY with valid JSON as an array matching the themes provided, in this exact format:
        [
          {
            "themeId": "the id provided",
            "detected": true,
            "intensity": "Major",
            "evidence": "brief explanation"
          }
        ]
        Use intensity "None" when the theme is not detected (detected should be false in that case).
        Do not include any text outside the JSON array.
        """;

    public StoryRankingService(
        ILmStudioClient lmClient,
        ISqlitePersistence persistence,
        IThemePreferenceService themeService,
        IOptions<StoryAnalysisOptions> options,
        IOptions<LmStudioOptions> lmOptions,
        ILogger<StoryRankingService> logger)
    {
        _lmClient = lmClient;
        _persistence = persistence;
        _themeService = themeService;
        _options = options.Value;
        _lmOptions = lmOptions.Value;
        _logger = logger;
    }

    public async Task<ThemeRankResult> RankAsync(string parsedStoryId, string profileId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Theme rank invoked for story {ParsedStoryId} with profile {ProfileId}", parsedStoryId, profileId);

        var themes = await _themeService.ListByProfileAsync(profileId, cancellationToken);
        if (themes.Count == 0)
        {
            _logger.LogWarning("No theme preferences configured for profile {ProfileId}", profileId);
            return new ThemeRankResult { Success = false, Errors = { ["_global"] = "No theme preferences configured for this profile. Please add at least one theme." } };
        }

        _logger.LogInformation("Ranking story {ParsedStoryId} against {ThemeCount} themes in profile {ProfileId}", parsedStoryId, themes.Count, profileId);

        var story = await _persistence.LoadParsedStoryAsync(parsedStoryId, cancellationToken);
        if (story is null)
        {
            return new ThemeRankResult { Success = false, Errors = { ["_global"] = $"Story not found: {parsedStoryId}" } };
        }

        var storyText = story.CombinedText;
        if (string.IsNullOrWhiteSpace(storyText))
        {
            return new ThemeRankResult { Success = false, Errors = { ["_global"] = "Story text is empty" } };
        }

        string storyTextForPrompt;
        if (storyText.Length > _options.MaxStoryTextLength)
        {
            _logger.LogInformation("Text truncation applied: original {OriginalLength} chars, truncated to {TruncatedLength} chars",
                storyText.Length, _options.MaxStoryTextLength);
            storyTextForPrompt = storyText[.._options.MaxStoryTextLength];
        }
        else
        {
            storyTextForPrompt = storyText;
        }

        // Build theme snapshot
        var snapshotItems = themes.Select(t => new { themeId = t.Id, name = t.Name, description = t.Description, tier = t.Tier.ToString() }).ToList();
        var themeSnapshotJson = JsonSerializer.Serialize(snapshotItems);

        // Build theme list for the LLM prompt
        var themeDefinitions = themes.Select(t => new { themeId = t.Id, name = t.Name, description = t.Description }).ToList();
        var themeListJson = JsonSerializer.Serialize(themeDefinitions, new JsonSerializerOptions { WriteIndented = true });

        // Single LLM call for all themes
        var userMessage = $"Themes to detect:\n{themeListJson}\n\nStory text:\n{storyTextForPrompt}";
        var maxTokens = Math.Max(200, themes.Count * 50);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _lmClient.GenerateAsync(
                SystemMessage,
                userMessage,
                _lmOptions.Model,
                _options.RankTemperature,
                0.9,
                maxTokens,
                cancellationToken);
            sw.Stop();
            _logger.LogInformation("Theme detection LLM call completed in {Duration}ms", sw.ElapsedMilliseconds);

            var json = StripMarkdownFences(response?.Trim() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ThemeRankResult { Success = false, Errors = { ["_global"] = "Empty response from LLM" } };
            }

            var detections = ParseDetections(json, themes);

            var (score, isDisqualified, disqualifyingThemes) = ThemeScoreCalculator.Calculate(detections);

            var result = new ThemeRankResult
            {
                Success = true,
                Score = score,
                IsDisqualified = isDisqualified,
                DisqualifyingThemes = disqualifyingThemes,
                ThemeDetectionsJson = JsonSerializer.Serialize(detections.Select(d => new
                {
                    themeId = d.ThemeId,
                    themeName = d.ThemeName,
                    tier = d.Tier.ToString(),
                    detected = d.Detected,
                    intensity = d.Intensity.ToString(),
                    evidence = d.Evidence
                })),
                ThemeSnapshotJson = themeSnapshotJson
            };

            var ranking = new StoryRankingResult
            {
                ParsedStoryId = parsedStoryId,
                ProfileId = profileId,
                ThemeSnapshotJson = themeSnapshotJson,
                ThemeDetectionsJson = result.ThemeDetectionsJson,
                Score = score,
                IsDisqualified = isDisqualified,
                GeneratedUtc = DateTime.UtcNow
            };

            await _persistence.SaveStoryRankingAsync(ranking, cancellationToken);
            _logger.LogInformation("Ranking persisted for story {ParsedStoryId} profile {ProfileId}: score={Score}, disqualified={IsDisqualified}",
                parsedStoryId, profileId, score, isDisqualified);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Theme detection LLM call failed for story {ParsedStoryId}", parsedStoryId);
            return new ThemeRankResult { Success = false, Errors = { ["_global"] = $"LLM call failed: {ex.Message}" } };
        }
    }

    public async Task<StoryRankingResult?> GetRankingAsync(string parsedStoryId, string profileId, CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadStoryRankingByProfileAsync(parsedStoryId, profileId, cancellationToken);
    }

    public async Task<List<StoryRankingResult>> GetRankingsAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadStoryRankingsAsync(parsedStoryId, cancellationToken);
    }

    private static string StripMarkdownFences(string text)
    {
        var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.None, TimeSpan.FromSeconds(2));
        return match.Success ? match.Groups[1].Value.Trim() : text;
    }

    private static List<ThemeDetection> ParseDetections(string json, List<ThemePreference> themes)
    {
        var detections = new List<ThemeDetection>();
        var themeLookup = themes.ToDictionary(t => t.Id);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var array = doc.RootElement;

            if (array.ValueKind != JsonValueKind.Array)
            {
                // Return all themes as undetected if LLM response is malformed
                return themes.Select(t => new ThemeDetection
                {
                    ThemeId = t.Id,
                    ThemeName = t.Name,
                    Tier = t.Tier,
                    Detected = false,
                    Intensity = ThemeIntensity.None,
                    Evidence = string.Empty
                }).ToList();
            }

            var parsedIds = new HashSet<string>();
            foreach (var elem in array.EnumerateArray())
            {
                var themeId = elem.TryGetProperty("themeId", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                if (!themeLookup.TryGetValue(themeId, out var theme))
                    continue;

                parsedIds.Add(themeId);
                var detected = elem.TryGetProperty("detected", out var detProp) && detProp.GetBoolean();
                var intensityStr = elem.TryGetProperty("intensity", out var intProp) ? intProp.GetString() ?? "None" : "None";
                var evidence = elem.TryGetProperty("evidence", out var evProp) ? evProp.GetString() ?? string.Empty : string.Empty;

                var intensity = Enum.TryParse<ThemeIntensity>(intensityStr, ignoreCase: true, out var parsed)
                    ? parsed
                    : ThemeIntensity.None;

                // Normalize: if not detected, intensity should be None
                if (!detected) intensity = ThemeIntensity.None;

                detections.Add(new ThemeDetection
                {
                    ThemeId = themeId,
                    ThemeName = theme.Name,
                    Tier = theme.Tier,
                    Detected = detected,
                    Intensity = intensity,
                    Evidence = evidence
                });
            }

            // Add any themes the LLM missed as undetected
            foreach (var theme in themes)
            {
                if (!parsedIds.Contains(theme.Id))
                {
                    detections.Add(new ThemeDetection
                    {
                        ThemeId = theme.Id,
                        ThemeName = theme.Name,
                        Tier = theme.Tier,
                        Detected = false,
                        Intensity = ThemeIntensity.None,
                        Evidence = string.Empty
                    });
                }
            }
        }
        catch (JsonException)
        {
            return themes.Select(t => new ThemeDetection
            {
                ThemeId = t.Id,
                ThemeName = t.Name,
                Tier = t.Tier,
                Detected = false,
                Intensity = ThemeIntensity.None,
                Evidence = string.Empty
            }).ToList();
        }

        return detections;
    }
}
