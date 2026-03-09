namespace DreamGenClone.Infrastructure.Configuration;

public sealed class LmStudioOptions
{
    public const string SectionName = "LmStudio";

    public string BaseUrl { get; set; } = "http://127.0.0.1:1234";

    public string ChatCompletionsPath { get; set; } = "/v1/chat/completions";

    public int TimeoutSeconds { get; set; } = 120;
}
