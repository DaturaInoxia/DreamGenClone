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
    private readonly string _model;

    private const string SystemMessage =
        """
        You are a content classifier that determines whether a specific theme is present in a section of a story.

        Read the provided story text carefully and check whether it contains the described theme.

        MATCHING RULES:
        - A theme is "detected" when the text depicts the specific behavior or content described in the theme description.
        - Match based on what actually happens in the text, not on surface-level word overlap.
          Example: A story mentioning "brother" in a family argument does NOT match a theme about incest. The text must actually depict sexual contact between family members.
          Example: A character watching TV does NOT match a voyeurism theme. The text must depict the specific voyeuristic behavior described.
        - If the text contains scenes or events that clearly match the theme description, mark detected as true even if the theme is not the central focus.

        Intensity levels (only when detected is true):
        - "Minor": briefly mentioned or implied in a single passage
        - "Moderate": appears in multiple scenes but is not the main focus
        - "Major": a significant recurring element of the text
        - "Central": the primary focus of the text

        Confidence: your certainty that the theme is truly present (0.0 = guessing, 1.0 = absolutely certain).
        Be honest — if the match is ambiguous or based on inference rather than explicit content, use a low confidence value.

        Respond ONLY with valid JSON:
        {"detected": true/false, "intensity": "None/Minor/Moderate/Major/Central", "confidence": 0.0, "evidence": "your reasoning"}
        When detected is false, set intensity to "None" and confidence to 0.0.
        When detected is true, describe the specific content that matches the theme.
        Do not include any text outside the JSON object.
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
        _model = _options.Model ?? _lmOptions.Model;
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

        // Split the story into chunks so the entire text is analyzed
        var chunks = ChunkStoryText(storyText, _options.MaxStoryTextLength);
        _logger.LogInformation("Story {ParsedStoryId} split into {ChunkCount} chunk(s) ({TotalLength} chars, chunk size {ChunkSize})",
            parsedStoryId, chunks.Count, storyText.Length, _options.MaxStoryTextLength);

        // Build theme snapshot
        var snapshotItems = themes.Select(t => new { themeId = t.Id, name = t.Name, description = t.Description, tier = t.Tier.ToString() }).ToList();
        var themeSnapshotJson = JsonSerializer.Serialize(snapshotItems);

        // Evaluate each theme across all chunks, keeping the strongest detection
        var detections = new List<ThemeDetection>();
        var errors = new Dictionary<string, string>();
        var sw = Stopwatch.StartNew();

        foreach (var theme in themes)
        {
            ThemeDetection bestDetection = UndetectedTheme(theme);

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunkLabel = chunks.Count > 1 ? $" (part {i + 1} of {chunks.Count})" : "";
                var userMessage = $"""
                    Theme to detect:
                    Name: {theme.Name}
                    Description: {theme.Description}

                    Does the story text below contain the theme described above? Base your answer on what actually happens in the text.

                    Story text{chunkLabel}:
                    {chunks[i]}
                    """;

                try
                {
                    var response = await _lmClient.GenerateAsync(
                        SystemMessage,
                        userMessage,
                        _model,
                        _options.RankTemperature,
                        0.5,
                        200,
                        cancellationToken);

                    var json = StripMarkdownFences(response?.Trim() ?? string.Empty);
                    _logger.LogDebug("Theme '{ThemeName}' chunk {Chunk}/{Total} LLM response ({Length} chars): {Response}",
                        theme.Name, i + 1, chunks.Count, json.Length, json);

                    var detection = ParseSingleDetection(json, theme);

                    // Keep the strongest detection across chunks
                    if (detection.Detected && detection.Intensity > bestDetection.Intensity)
                    {
                        bestDetection = detection;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LLM call failed for theme '{ThemeName}' chunk {Chunk}/{Total}", theme.Name, i + 1, chunks.Count);
                    if (!errors.ContainsKey(theme.Name))
                        errors[theme.Name] = ex.Message;
                }
            }

            detections.Add(bestDetection);
            _logger.LogInformation("Theme '{ThemeName}' final result: detected={Detected}, intensity={Intensity}, confidence={Confidence}",
                theme.Name, bestDetection.Detected, bestDetection.Intensity, bestDetection.Confidence);
        }

        // Filter out low-confidence detections
        foreach (var detection in detections)
        {
            if (detection.Detected && detection.Confidence < _options.RankConfidenceThreshold)
            {
                _logger.LogWarning("Theme '{ThemeName}' detection filtered: confidence {Confidence:F2} below threshold {Threshold:F2}",
                    detection.ThemeName, detection.Confidence, _options.RankConfidenceThreshold);
                detection.Detected = false;
                detection.Intensity = ThemeIntensity.None;
            }
        }

        sw.Stop();
        _logger.LogInformation("All {ThemeCount} theme evaluations across {ChunkCount} chunk(s) completed in {Duration}ms",
            themes.Count, chunks.Count, sw.ElapsedMilliseconds);

        var (score, isDisqualified, disqualifyingThemes) = ThemeScoreCalculator.Calculate(detections);

        var result = new ThemeRankResult
        {
            Success = errors.Count == 0,
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
                confidence = d.Confidence,
                evidence = d.Evidence
            })),
            ThemeSnapshotJson = themeSnapshotJson,
            Errors = errors
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

    private static ThemeDetection ParseSingleDetection(string json, ThemePreference theme)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return UndetectedTheme(theme);
            }

            using var doc = JsonDocument.Parse(json);
            var elem = doc.RootElement;

            if (elem.ValueKind != JsonValueKind.Object)
            {
                return UndetectedTheme(theme);
            }

            var detected = elem.TryGetProperty("detected", out var detProp) && detProp.GetBoolean();
            var intensityStr = elem.TryGetProperty("intensity", out var intProp) ? intProp.GetString() ?? "None" : "None";
            var evidence = elem.TryGetProperty("evidence", out var evProp) ? evProp.GetString() ?? string.Empty : string.Empty;
            var confidence = elem.TryGetProperty("confidence", out var confProp) && confProp.TryGetDouble(out var confVal)
                ? Math.Clamp(confVal, 0.0, 1.0)
                : 0.0;

            var intensity = Enum.TryParse<ThemeIntensity>(intensityStr, ignoreCase: true, out var parsed)
                ? parsed
                : ThemeIntensity.None;

            // Normalize: if not detected, intensity should be None
            if (!detected) intensity = ThemeIntensity.None;

            return new ThemeDetection
            {
                ThemeId = theme.Id,
                ThemeName = theme.Name,
                Tier = theme.Tier,
                Detected = detected,
                Intensity = intensity,
                Confidence = confidence,
                Evidence = evidence
            };
        }
        catch (JsonException)
        {
            return UndetectedTheme(theme);
        }
    }

    private static ThemeDetection UndetectedTheme(ThemePreference theme) => new()
    {
        ThemeId = theme.Id,
        ThemeName = theme.Name,
        Tier = theme.Tier,
        Detected = false,
        Intensity = ThemeIntensity.None,
        Confidence = 0.0,
        Evidence = string.Empty
    };

    /// <summary>
    /// Splits story text into chunks of approximately <paramref name="chunkSize"/> characters,
    /// breaking at paragraph boundaries to avoid splitting mid-sentence.
    /// </summary>
    internal static List<string> ChunkStoryText(string text, int chunkSize)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= chunkSize)
            return [text];

        var chunks = new List<string>();
        var position = 0;

        while (position < text.Length)
        {
            var remaining = text.Length - position;
            if (remaining <= chunkSize)
            {
                chunks.Add(text[position..]);
                break;
            }

            // Take chunkSize chars, then look backwards for a paragraph break
            var end = position + chunkSize;
            var breakPoint = text.LastIndexOf("\n\n", end, Math.Min(chunkSize, end), StringComparison.Ordinal);

            if (breakPoint <= position)
            {
                // No paragraph break found, try a single newline
                breakPoint = text.LastIndexOf('\n', end, Math.Min(chunkSize, end));
            }

            if (breakPoint <= position)
            {
                // No newline at all, just cut at chunkSize
                breakPoint = end;
            }

            chunks.Add(text[position..breakPoint]);
            position = breakPoint;

            // Skip the newline characters at the break point
            while (position < text.Length && text[position] is '\n' or '\r')
                position++;
        }

        return chunks;
    }
}
