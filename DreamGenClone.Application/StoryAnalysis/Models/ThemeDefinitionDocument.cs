namespace DreamGenClone.Application.StoryAnalysis.Models;

public sealed class ThemeDefinitionDocument
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int Weight { get; set; }

    public string SourceFilePath { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public string RawContent { get; set; } = string.Empty;

    public IReadOnlyList<string> ParseWarnings { get; set; } = [];
}
