namespace DreamGenClone.Application.StoryAnalysis.Models;

public sealed class SummarizeResult
{
    public bool Success { get; set; }

    public string? SummaryText { get; set; }

    public string? ErrorMessage { get; set; }
}
