namespace DreamGenClone.Domain.ModelManager;

public sealed class Provider
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public ProviderType ProviderType { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string ChatCompletionsPath { get; set; } = "/v1/chat/completions";
    public int TimeoutSeconds { get; set; } = 120;
    public string? ApiKeyEncrypted { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public string UpdatedUtc { get; set; } = DateTime.UtcNow.ToString("o");
}
