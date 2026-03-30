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

public sealed class StoryAnalysisService : IStoryAnalysisService
{
    private readonly ILmStudioClient _lmClient;
    private readonly ISqlitePersistence _persistence;
    private readonly StoryAnalysisOptions _options;
    private readonly LmStudioOptions _lmOptions;
    private readonly ILogger<StoryAnalysisService> _logger;

    private static readonly Dictionary<AnalysisDimension, string> DimensionSystemMessages = new()
    {
        [AnalysisDimension.Characters] = """
            You are a literary analyst. Extract the characters from the following story.
            Respond ONLY with valid JSON in this exact format:
            {
              "characters": [
                { "name": "character name", "role": "protagonist/antagonist/supporting/minor", "description": "brief character description" }
              ]
            }
            Do not include any text outside the JSON object.
            """,
        [AnalysisDimension.Themes] = """
            You are a literary analyst. Identify the major themes in the following story.
            Important rules:
            - Do NOT list themes that are contradictory or mutually exclusive with each other.
            - If two potential themes are very similar or overlapping, merge them into one.
            - Only include themes that are clearly supported by the text.
            - Be precise: distinguish between e.g. "consensual non-monogamy" vs "infidelity" — pick only the one the story actually depicts.
            Respond ONLY with valid JSON in this exact format:
            {
              "themes": [
                { "name": "theme name", "description": "how this theme manifests in the story", "prevalence": "primary/secondary/minor" }
              ]
            }
            Do not include any text outside the JSON object.
            """,
        [AnalysisDimension.PlotStructure] = """
            You are a literary analyst. Analyze the plot structure of the following story.
            Respond ONLY with valid JSON in this exact format:
            {
              "exposition": "description of the story setup and initial situation",
              "risingAction": "description of the building tension and complications",
              "climax": "description of the turning point or peak conflict",
              "fallingAction": "description of events after the climax",
              "resolution": "description of how the story concludes"
            }
            Do not include any text outside the JSON object.
            """,
        [AnalysisDimension.WritingStyle] = """
            You are a literary analyst. Assess the writing style of the following story.
            Respond ONLY with valid JSON in this exact format:
            {
              "tone": "description of the overall tone",
              "perspective": "narrative perspective (first person, third person limited, etc.)",
              "pacing": "description of the story's pacing",
              "languageComplexity": "assessment of vocabulary and sentence complexity",
              "notableDevices": ["literary device 1", "literary device 2"]
            }
            Do not include any text outside the JSON object.
            """
    };

    private static readonly Dictionary<AnalysisDimension, string[]> RequiredKeys = new()
    {
        [AnalysisDimension.Characters] = ["characters"],
        [AnalysisDimension.Themes] = ["themes"],
        [AnalysisDimension.PlotStructure] = ["exposition", "risingAction", "climax", "fallingAction", "resolution"],
        [AnalysisDimension.WritingStyle] = ["tone", "perspective", "pacing", "languageComplexity", "notableDevices"]
    };

    public StoryAnalysisService(
        ILmStudioClient lmClient,
        ISqlitePersistence persistence,
        IOptions<StoryAnalysisOptions> options,
        IOptions<LmStudioOptions> lmOptions,
        ILogger<StoryAnalysisService> logger)
    {
        _lmClient = lmClient;
        _persistence = persistence;
        _options = options.Value;
        _lmOptions = lmOptions.Value;
        _logger = logger;
    }

    public async Task<AnalyzeResult> AnalyzeAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyze invoked for story {ParsedStoryId}", parsedStoryId);

        var story = await _persistence.LoadParsedStoryAsync(parsedStoryId, cancellationToken);
        if (story is null)
        {
            return new AnalyzeResult { Success = false, DimensionErrors = { [AnalysisDimension.Characters] = $"Story not found: {parsedStoryId}" } };
        }

        var storyText = story.CombinedText;
        if (string.IsNullOrWhiteSpace(storyText))
        {
            return new AnalyzeResult { Success = false, DimensionErrors = { [AnalysisDimension.Characters] = "Story text is empty" } };
        }

        string userMessage;
        if (storyText.Length > _options.MaxStoryTextLength)
        {
            _logger.LogInformation("Text truncation applied: original {OriginalLength} chars, truncated to {TruncatedLength} chars",
                storyText.Length, _options.MaxStoryTextLength);
            var truncated = storyText[.._options.MaxStoryTextLength];
            userMessage = $"Story text (truncated to first {_options.MaxStoryTextLength} characters — full story is {storyText.Length} characters):\n{truncated}";
        }
        else
        {
            userMessage = $"Story text:\n{storyText}";
        }

        var result = new AnalyzeResult();
        var dimensions = Enum.GetValues<AnalysisDimension>();
        int successCount = 0;

        foreach (var dimension in dimensions)
        {
            _logger.LogInformation("Analyzing dimension {Dimension} for story {ParsedStoryId}", dimension, parsedStoryId);
            var sw = Stopwatch.StartNew();

            try
            {
                var response = await _lmClient.GenerateAsync(
                    DimensionSystemMessages[dimension],
                    userMessage,
                    _lmOptions.Model,
                    _options.AnalyzeTemperature,
                    0.9,
                    _options.AnalyzeMaxTokens,
                    cancellationToken);
                sw.Stop();
                _logger.LogInformation("Dimension {Dimension} LLM call completed in {Duration}ms", dimension, sw.ElapsedMilliseconds);

                var json = StripMarkdownFences(response?.Trim() ?? string.Empty);
                if (string.IsNullOrWhiteSpace(json))
                {
                    result.DimensionErrors[dimension] = "Empty response from LLM";
                    _logger.LogWarning("JSON validation failed for dimension {Dimension}: empty response", dimension);
                    continue;
                }

                if (!ValidateJson(json, dimension))
                {
                    result.DimensionErrors[dimension] = "Invalid JSON schema";
                    _logger.LogWarning("JSON validation failed for dimension {Dimension}: invalid schema", dimension);
                    continue;
                }

                switch (dimension)
                {
                    case AnalysisDimension.Characters: result.CharactersJson = json; break;
                    case AnalysisDimension.Themes: result.ThemesJson = json; break;
                    case AnalysisDimension.PlotStructure: result.PlotStructureJson = json; break;
                    case AnalysisDimension.WritingStyle: result.WritingStyleJson = json; break;
                }
                successCount++;
                _logger.LogInformation("JSON validation passed for dimension {Dimension}", dimension);
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.DimensionErrors[dimension] = $"LLM call failed: {ex.Message}";
                _logger.LogError(ex, "Dimension {Dimension} LLM call failed for story {ParsedStoryId}", dimension, parsedStoryId);
            }
        }

        if (successCount == 0)
        {
            result.Success = false;
            return result;
        }

        result.Success = true;

        var analysis = new StoryAnalysisResult
        {
            ParsedStoryId = parsedStoryId,
            CharactersJson = result.CharactersJson,
            ThemesJson = result.ThemesJson,
            PlotStructureJson = result.PlotStructureJson,
            WritingStyleJson = result.WritingStyleJson,
            GeneratedUtc = DateTime.UtcNow
        };

        await _persistence.SaveStoryAnalysisAsync(analysis, cancellationToken);
        _logger.LogInformation("Analysis persisted for story {ParsedStoryId}, successful dimensions: {SuccessCount}/4", parsedStoryId, successCount);

        return result;
    }

    public async Task<StoryAnalysisResult?> GetAnalysisAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadStoryAnalysisAsync(parsedStoryId, cancellationToken);
    }

    private static string StripMarkdownFences(string text)
    {
        var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.None, TimeSpan.FromSeconds(2));
        return match.Success ? match.Groups[1].Value.Trim() : text;
    }

    private bool ValidateJson(string json, AnalysisDimension dimension)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            foreach (var key in RequiredKeys[dimension])
            {
                if (!root.TryGetProperty(key, out _))
                    return false;
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
