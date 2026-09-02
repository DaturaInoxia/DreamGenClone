namespace DreamGenClone.Domain.ModelManager;

/// <summary>Whether a provider exposes an image-generation endpoint.</summary>
public enum ImageProviderCapability
{
    /// <summary>Text chat only (default for existing LM Studio / OpenRouter rows).</summary>
    None = 0,

    /// <summary>Hosts both chat and image models (e.g. Together AI).</summary>
    TextAndImage = 1,

    /// <summary>Dedicated image endpoint/provider.</summary>
    ImageOnly = 2
}
