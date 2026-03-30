namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class StoryAnalysisResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ParsedStoryId { get; set; } = string.Empty;

    public string? CharactersJson { get; set; }

    public string? ThemesJson { get; set; }

    public string? PlotStructureJson { get; set; }

    public string? WritingStyleJson { get; set; }

    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
