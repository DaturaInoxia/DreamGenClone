using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IStorySummaryService
{
    Task<SummarizeResult> SummarizeAsync(string parsedStoryId, CancellationToken cancellationToken = default);

    Task<StorySummary?> GetSummaryAsync(string parsedStoryId, CancellationToken cancellationToken = default);
}
