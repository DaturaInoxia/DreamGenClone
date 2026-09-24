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
    SceneImageModelFamily SceneImageModelFamily,
    SceneImagePromptDialect PromptDialect,
    ImageProtocol ImageProtocol,
    string? ComfyUiUrl = null,

    /// <summary>
    /// Qwen-Image-2.1 artifacts and envelope, resolved from the model's
    /// <c>CapabilityQualificationsJson</c> when the family is
    /// <see cref="SceneImageModelFamily.QwenImage21"/>; null for every other family. The graph
    /// builder REQUIRES it for that family and fails fast when it is missing.
    /// </summary>
    QwenImage21Refs? QwenImage21 = null,

    /// <summary>
    /// The registered model row this resolution came from. Needed wherever a resolved value has to be
    /// checked against the model's own declared capabilities (reference strategies), and carried for
    /// provenance. Null when the caller resolved without a model row.
    /// </summary>
    string? RegisteredModelId = null);
