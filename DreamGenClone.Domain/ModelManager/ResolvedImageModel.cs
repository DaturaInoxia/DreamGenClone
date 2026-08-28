namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// Immutable value object describing how to call an image-generation model, resolved from the
/// Model Manager at call time. Mirrors <see cref="ResolvedModel"/> but for the images endpoint.
/// </summary>
public sealed record ResolvedImageModel(
    string ProviderBaseUrl,
    string ImageGenerationPath,
    int ProviderTimeoutSeconds,
    string? ApiKeyEncrypted,
    string ModelIdentifier,
    ImageContentPolicy ContentPolicy,
    string ProviderName,
    bool IsSessionOverride,
    ImageProtocol ImageProtocol = ImageProtocol.OpenAiImages,
    string? ComfyUiUrl = null);
