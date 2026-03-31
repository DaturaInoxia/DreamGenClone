using System.Diagnostics;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class StorySummaryService : IStorySummaryService
{
    private readonly ILmStudioClient _lmClient;
    private readonly ISqlitePersistence _persistence;
    private readonly StoryAnalysisOptions _options;
    private readonly LmStudioOptions _lmOptions;
    private readonly ILogger<StorySummaryService> _logger;
    private readonly string _model;

    private const string ChunkSystemMessage =
        """
        You are a literary analyst. Provide a concise synopsis of the following section of a story.
        Capture the key events, characters introduced, and any important developments.
        Write 1-2 paragraphs in plain text. Do not use bullet points or headings.
        """;

    private const string ConsolidateSystemMessage =
        """
        You are a literary analyst. You have been given summaries of consecutive sections of a single story.
        Combine them into one cohesive synopsis that captures the main characters, central conflict, key plot points, and resolution.
        Write 2-3 paragraphs in plain text. Do not use bullet points or headings.
        Do not mention that the story was split into sections.
        """;

    private const string SingleSystemMessage =
        """
        You are a literary analyst. Provide a concise synopsis of the following story.
        The summary should capture the main characters, central conflict, key plot points, and resolution.
        Write 2-3 paragraphs in plain text. Do not use bullet points or headings.
        """;

    public StorySummaryService(
        ILmStudioClient lmClient,
        ISqlitePersistence persistence,
        IOptions<StoryAnalysisOptions> options,
        IOptions<LmStudioOptions> lmOptions,
        ILogger<StorySummaryService> logger)
    {
        _lmClient = lmClient;
        _persistence = persistence;
        _options = options.Value;
        _lmOptions = lmOptions.Value;
        _logger = logger;
        _model = _options.Model ?? _lmOptions.Model;
    }

    public async Task<SummarizeResult> SummarizeAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Summarize invoked for story {ParsedStoryId}", parsedStoryId);

        var story = await _persistence.LoadParsedStoryAsync(parsedStoryId, cancellationToken);
        if (story is null)
        {
            return new SummarizeResult { Success = false, ErrorMessage = $"Story not found: {parsedStoryId}" };
        }

        var storyText = story.CombinedText;
        if (string.IsNullOrWhiteSpace(storyText))
        {
            return new SummarizeResult { Success = false, ErrorMessage = "Story text is empty" };
        }

        var sw = Stopwatch.StartNew();
        string finalSummary;

        var chunks = StoryRankingService.ChunkStoryText(storyText, _options.MaxStoryTextLength);
        _logger.LogInformation("Story {ParsedStoryId} split into {ChunkCount} chunk(s) for summarization ({TotalLength} chars)",
            parsedStoryId, chunks.Count, storyText.Length);

        try
        {
            if (chunks.Count == 1)
            {
                // Single chunk — summarize directly
                var response = await _lmClient.GenerateAsync(
                    SingleSystemMessage,
                    $"Story text:\n{chunks[0]}",
                    _model,
                    _options.SummarizeTemperature,
                    0.9,
                    _options.SummarizeMaxTokens,
                    cancellationToken);

                finalSummary = response?.Trim() ?? string.Empty;
            }
            else
            {
                // Multi-chunk: summarize each chunk, then consolidate
                var chunkSummaries = new List<string>();
                for (int i = 0; i < chunks.Count; i++)
                {
                    _logger.LogInformation("Summarizing chunk {Chunk}/{Total} for story {ParsedStoryId}", i + 1, chunks.Count, parsedStoryId);

                    var response = await _lmClient.GenerateAsync(
                        ChunkSystemMessage,
                        $"Story text (part {i + 1} of {chunks.Count}):\n{chunks[i]}",
                        _model,
                        _options.SummarizeTemperature,
                        0.9,
                        _options.SummarizeMaxTokens,
                        cancellationToken);

                    var chunkSummary = response?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(chunkSummary))
                    {
                        chunkSummaries.Add(chunkSummary);
                        _logger.LogInformation("Chunk {Chunk} summary: {Length} chars", i + 1, chunkSummary.Length);
                    }
                    else
                    {
                        _logger.LogWarning("Chunk {Chunk} returned empty summary", i + 1);
                    }
                }

                if (chunkSummaries.Count == 0)
                {
                    return new SummarizeResult { Success = false, ErrorMessage = "All chunk summaries were empty" };
                }

                // Consolidate chunk summaries into a single summary
                _logger.LogInformation("Consolidating {Count} chunk summaries for story {ParsedStoryId}", chunkSummaries.Count, parsedStoryId);
                var consolidateInput = string.Join("\n\n---\n\n", chunkSummaries.Select((s, i) => $"Section {i + 1}:\n{s}"));

                var consolidateResponse = await _lmClient.GenerateAsync(
                    ConsolidateSystemMessage,
                    $"Section summaries to consolidate:\n\n{consolidateInput}",
                    _model,
                    _options.SummarizeTemperature,
                    0.9,
                    _options.SummarizeMaxTokens * 2,
                    cancellationToken);

                finalSummary = consolidateResponse?.Trim() ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "LLM error during summarization of story {ParsedStoryId}", parsedStoryId);
            return new SummarizeResult { Success = false, ErrorMessage = $"LLM call failed: {ex.Message}" };
        }

        sw.Stop();
        _logger.LogInformation("Summarization completed in {Duration}ms for story {ParsedStoryId}", sw.ElapsedMilliseconds, parsedStoryId);

        if (finalSummary.Length < 50)
        {
            _logger.LogWarning("Validation failure: summary too short ({Length} chars) for story {ParsedStoryId}", finalSummary.Length, parsedStoryId);
            return new SummarizeResult { Success = false, ErrorMessage = $"Summary too short ({finalSummary.Length} chars, minimum 50)" };
        }

        var summary = new StorySummary
        {
            ParsedStoryId = parsedStoryId,
            SummaryText = finalSummary,
            GeneratedUtc = DateTime.UtcNow
        };

        await _persistence.SaveStorySummaryAsync(summary, cancellationToken);
        _logger.LogInformation("Summary persisted for story {ParsedStoryId}", parsedStoryId);

        return new SummarizeResult { Success = true, SummaryText = finalSummary };
    }

    public async Task<StorySummary?> GetSummaryAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadStorySummaryAsync(parsedStoryId, cancellationToken);
    }
}
