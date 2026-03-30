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

    private const string SystemMessage =
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

        string response;
        var sw = Stopwatch.StartNew();
        try
        {
            response = await _lmClient.GenerateAsync(
                SystemMessage,
                userMessage,
                _lmOptions.Model,
                _options.SummarizeTemperature,
                0.9,
                _options.SummarizeMaxTokens,
                cancellationToken);
            sw.Stop();
            _logger.LogInformation("LLM call completed in {Duration}ms for story {ParsedStoryId}", sw.ElapsedMilliseconds, parsedStoryId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "LLM error during summarization of story {ParsedStoryId}", parsedStoryId);
            return new SummarizeResult { Success = false, ErrorMessage = $"LLM call failed: {ex.Message}" };
        }

        var trimmed = response?.Trim() ?? string.Empty;
        if (trimmed.Length < 50)
        {
            _logger.LogWarning("Validation failure: summary too short ({Length} chars) for story {ParsedStoryId}", trimmed.Length, parsedStoryId);
            return new SummarizeResult { Success = false, ErrorMessage = $"Summary too short ({trimmed.Length} chars, minimum 50)" };
        }

        var summary = new StorySummary
        {
            ParsedStoryId = parsedStoryId,
            SummaryText = trimmed,
            GeneratedUtc = DateTime.UtcNow
        };

        await _persistence.SaveStorySummaryAsync(summary, cancellationToken);
        _logger.LogInformation("Summary persisted for story {ParsedStoryId}", parsedStoryId);

        return new SummarizeResult { Success = true, SummaryText = trimmed };
    }

    public async Task<StorySummary?> GetSummaryAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadStorySummaryAsync(parsedStoryId, cancellationToken);
    }
}
