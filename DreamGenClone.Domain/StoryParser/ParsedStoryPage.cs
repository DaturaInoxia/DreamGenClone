namespace DreamGenClone.Domain.StoryParser;

public sealed class ParsedStoryPage
{
    public int Sequence { get; set; }

    public string PageUrl { get; set; } = string.Empty;

    public string ExtractedText { get; set; } = string.Empty;

    public bool IsTerminalPage { get; set; }

    public List<string> Warnings { get; set; } = [];
}
