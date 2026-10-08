namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// Immutable value object describing how to call a video-generation model, resolved from the Model Manager at
/// call time.
/// </summary>
/// <remarks>
/// The video sibling of <see cref="ResolvedImageModel"/>. MiniMax H3 is the only video family today, so the
/// resolved record carries <see cref="MiniMaxH3Refs"/> rather than a family switch - but the provider fields are
/// the same shape, and the base URL is taken from the model's own provider row (never a hardcoded host) exactly
/// as the image path does.
/// </remarks>
/// <param name="ProviderBaseUrl">Provider base URL from the model's provider row.</param>
/// <param name="ProviderTimeoutSeconds">
/// Per-request timeout. A trained-range render runs ~25 to ~100 minutes, so this must be configured large enough
/// for the poll cycle; the durable executor's lease renewal (not this timeout) protects the job.
/// </param>
/// <param name="ApiKeyEncrypted">Encrypted provider key, when the provider requires one.</param>
/// <param name="ModelIdentifier">The registered model identifier (the DiT filename).</param>
/// <param name="ProviderName">Provider display name, for diagnostics.</param>
/// <param name="Family">The model family (the H3 family value).</param>
/// <param name="PromptDialect">The model's prompt dialect (must be compatible with the family).</param>
/// <param name="ImageProtocol">The ComfyUI protocol; H3 is always a ComfyUI graph.</param>
/// <param name="H3">The artifacts and envelope the graph builder requires.</param>
/// <param name="ComfyUiUrl">Explicit ComfyUI URL when the provider row overrides the base URL.</param>
/// <param name="RegisteredModelId">The registered model row this resolution came from, for provenance.</param>
public sealed record ResolvedVideoModel(
    string ProviderBaseUrl,
    int ProviderTimeoutSeconds,
    string? ApiKeyEncrypted,
    string ModelIdentifier,
    string ProviderName,
    SceneImageModelFamily Family,
    SceneImagePromptDialect PromptDialect,
    ImageProtocol ImageProtocol,
    MiniMaxH3Refs H3,
    string? ComfyUiUrl = null,
    string? RegisteredModelId = null);
