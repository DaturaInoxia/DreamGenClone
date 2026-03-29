namespace DreamGenClone.Application.StoryParser;

public sealed class StoryParserOptions
{
    public const string SectionName = "StoryParser";

    public int TimeoutSeconds { get; set; } = 10;

    public long MaxHtmlBytes { get; set; } = 5 * 1024 * 1024;

    public int MaxPageCount { get; set; } = 20;

    public string ErrorModeDefault { get; set; } = "FailFast";

    public List<string> SupportedDomains { get; set; } = ["www.literotica.com", "literotica.com"];
}
