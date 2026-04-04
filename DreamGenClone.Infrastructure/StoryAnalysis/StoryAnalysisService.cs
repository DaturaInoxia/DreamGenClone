using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class StoryAnalysisService : IStoryAnalysisService
{
    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolver;
    private readonly ISqlitePersistence _persistence;
    private readonly StoryAnalysisOptions _options;
    private readonly ILogger<StoryAnalysisService> _logger;

    private static readonly Dictionary<AnalysisDimension, string> DimensionSystemMessages = new()
    {
        [AnalysisDimension.Characters] = """
            You are a literary analyst. Extract the characters from the following story text.
            Respond ONLY with valid JSON in this exact format:
            {
              "characters": [
                { "name": "character name", "role": "protagonist/antagonist/supporting/minor", "description": "brief character description" }
              ]
            }
            Do not include any text outside the JSON object.
            """,
        [AnalysisDimension.Themes] = """
            You are a literary analyst. Identify the major themes in the following story text.
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
            You are a literary analyst. Analyze the plot structure of the following story text.
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
            You are a literary analyst. Assess the writing style of the following story text.
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

    private static readonly Dictionary<AnalysisDimension, string> ConsolidateSystemMessages = new()
    {
        [AnalysisDimension.Characters] = """
            You are a literary analyst. You have been given character lists extracted from consecutive sections of a single story.
            Merge them into one deduplicated character list. If the same character appears in multiple sections, combine their descriptions and use their most significant role.
            Respond ONLY with valid JSON in this exact format:
            {
              "characters": [
                { "name": "character name", "role": "protagonist/antagonist/supporting/minor", "description": "brief character description" }
              ]
            }
            Do not include any text outside the JSON object.
            """,
        [AnalysisDimension.Themes] = """
            You are a literary analyst. You have been given theme analyses from consecutive sections of a single story.
            Merge them into one consolidated theme list for the whole story. Deduplicate similar themes. Only include themes that have genuine support.
            Important: Do NOT list contradictory or mutually exclusive themes. Merge overlapping themes.
            Respond ONLY with valid JSON in this exact format:
            {
              "themes": [
                { "name": "theme name", "description": "how this theme manifests in the story", "prevalence": "primary/secondary/minor" }
              ]
            }
            Do not include any text outside the JSON object.
            """,
        [AnalysisDimension.PlotStructure] = """
            You are a literary analyst. You have been given plot structure analyses from consecutive sections of a single story.
            Combine them into one coherent plot structure covering the full story arc.
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
            You are a literary analyst. You have been given writing style assessments from consecutive sections of a single story.
            Synthesize them into one overall writing style assessment for the whole story.
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
        ICompletionClient completionClient,
        IModelResolutionService modelResolver,
        ISqlitePersistence persistence,
        IOptions<StoryAnalysisOptions> options,
        ILogger<StoryAnalysisService> logger)
    {
        _completionClient = completionClient;
        _modelResolver = modelResolver;
        _persistence = persistence;
        _options = options.Value;
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

        var chunks = StoryRankingService.ChunkStoryText(storyText, _options.MaxStoryTextLength);
        _logger.LogInformation("Story {ParsedStoryId} split into {ChunkCount} chunk(s) for analysis ({TotalLength} chars)",
            parsedStoryId, chunks.Count, storyText.Length);

        var result = new AnalyzeResult();
        var dimensions = Enum.GetValues<AnalysisDimension>();
        int successCount = 0;

        foreach (var dimension in dimensions)
        {
            _logger.LogInformation("Analyzing dimension {Dimension} for story {ParsedStoryId}", dimension, parsedStoryId);
            var sw = Stopwatch.StartNew();

            try
            {
                string json;
                if (chunks.Count == 1)
                {
                    // Single chunk — analyze directly
                    json = await AnalyzeChunkAsync(dimension, $"Story text:\n{chunks[0]}", cancellationToken);
                }
                else
                {
                    // Multi-chunk: analyze each chunk, then consolidate
                    var chunkResults = new List<string>();
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        _logger.LogInformation("Analyzing dimension {Dimension} chunk {Chunk}/{Total}", dimension, i + 1, chunks.Count);
                        var chunkJson = await AnalyzeChunkAsync(dimension, $"Story text (part {i + 1} of {chunks.Count}):\n{chunks[i]}", cancellationToken);

                        if (!string.IsNullOrWhiteSpace(chunkJson) && ValidateJson(chunkJson, dimension))
                        {
                            chunkResults.Add(chunkJson);
                        }
                        else
                        {
                            _logger.LogWarning("Chunk {Chunk} returned invalid result for dimension {Dimension}", i + 1, dimension);
                        }
                    }

                    if (chunkResults.Count == 0)
                    {
                        result.DimensionErrors[dimension] = "All chunks returned invalid results";
                        continue;
                    }

                    if (chunkResults.Count == 1)
                    {
                        json = chunkResults[0];
                    }
                    else
                    {
                        // Consolidate chunk results
                        _logger.LogInformation("Consolidating {Count} chunk results for dimension {Dimension}", chunkResults.Count, dimension);
                        var consolidateInput = string.Join("\n\n---\n\n", chunkResults.Select((r, i) => $"Section {i + 1} analysis:\n{r}"));

                        var consolidateResolved = await _modelResolver.ResolveAsync(AppFunction.StoryAnalyze, cancellationToken: cancellationToken);
                        var consolidateResponse = await _completionClient.GenerateAsync(
                            ConsolidateSystemMessages[dimension],
                            $"Section analyses to consolidate:\n\n{consolidateInput}",
                            consolidateResolved,
                            cancellationToken);

                        json = StripMarkdownFences(consolidateResponse?.Trim() ?? string.Empty);
                        _logger.LogDebug("Consolidated {Dimension} response ({Length} chars): {Json}", dimension, json.Length, json);
                    }
                }

                sw.Stop();
                _logger.LogInformation("Dimension {Dimension} completed in {Duration}ms", dimension, sw.ElapsedMilliseconds);

                if (string.IsNullOrWhiteSpace(json))
                {
                    result.DimensionErrors[dimension] = "Empty response from LLM";
                    continue;
                }

                if (!ValidateJson(json, dimension))
                {
                    result.DimensionErrors[dimension] = "Invalid JSON schema";
                    _logger.LogWarning("JSON validation failed for dimension {Dimension}: invalid schema. Response ({Length} chars): {Json}",
                        dimension, json.Length, json.Length > 500 ? json[..500] + "..." : json);
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
                _logger.LogInformation("Dimension {Dimension} success", dimension);
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.DimensionErrors[dimension] = $"LLM call failed: {ex.Message}";
                _logger.LogError(ex, "Dimension {Dimension} failed for story {ParsedStoryId}", dimension, parsedStoryId);
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

    private async Task<string> AnalyzeChunkAsync(AnalysisDimension dimension, string userMessage, CancellationToken cancellationToken)
    {
        var resolved = await _modelResolver.ResolveAsync(AppFunction.StoryAnalyze, cancellationToken: cancellationToken);
        var response = await _completionClient.GenerateAsync(
            DimensionSystemMessages[dimension],
            userMessage,
            resolved,
            cancellationToken);

        return StripMarkdownFences(response?.Trim() ?? string.Empty);
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
