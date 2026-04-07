namespace DreamGenClone.Domain.ModelManager;

/// <summary>Metadata retrieved from a provider's API about a specific model.</summary>
public sealed class ModelMetadata
{
    public string ModelId { get; set; } = string.Empty;
    public int ContextWindowSize { get; set; }
    public string ParameterCount { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;

    /// <summary>Raw JSON snippet from the provider for reference.</summary>
    public string RawProviderData { get; set; } = string.Empty;
}
