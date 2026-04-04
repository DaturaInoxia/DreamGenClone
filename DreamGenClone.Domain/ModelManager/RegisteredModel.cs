namespace DreamGenClone.Domain.ModelManager;

public sealed class RegisteredModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProviderId { get; set; } = string.Empty;
    public string ModelIdentifier { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>
    /// Provider name, populated from JOIN queries (not persisted separately).
    /// </summary>
    public string? ProviderName { get; set; }
}
