using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis.Models;

public sealed class AnalyzeResult
{
    public bool Success { get; set; }

    public string? CharactersJson { get; set; }

    public string? ThemesJson { get; set; }

    public string? PlotStructureJson { get; set; }

    public string? WritingStyleJson { get; set; }

    public Dictionary<AnalysisDimension, string> DimensionErrors { get; set; } = new();
}
