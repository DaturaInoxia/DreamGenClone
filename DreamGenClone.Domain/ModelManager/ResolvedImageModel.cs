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
    /// Krea 2 artifacts and sampling envelope, resolved from the model's <c>CapabilityQualificationsJson</c> when
    /// the family is <see cref="SceneImageModelFamily.Krea2"/>; null for every other family. The graph builder
    /// REQUIRES it for that family and fails fast when it is missing.
    /// </summary>
    Krea2Refs? Krea2 = null,

    /// <summary>
    /// The registered model row this resolution came from. Needed wherever a resolved value has to be
    /// checked against the model's own declared capabilities (reference strategies), and carried for
    /// provenance. Null when the caller resolved without a model row.
    /// </summary>
    string? RegisteredModelId = null,

    /// <summary>
    /// The character LoRAs this render applies, one per bound actor, in chain order. Null/empty means NO LoRA:
    /// the graph then emits no <c>LoraLoader</c> node and every wire stays exactly as it was, so a render without
    /// a LoRA is identical to a pre-LoRA render. Populated by the render that knows the frame's cast; the
    /// resolution service itself cannot know it.
    /// </summary>
    IReadOnlyList<ResolvedCharacterLora>? Loras = null,

    /// <summary>
    /// The NON-IDENTITY LoRAs this render applies (unlock / act / anatomy / style), in the operator's chosen order.
    /// Null/empty means NO scene LoRA: the graph then emits no loader node for them and the render is identical to
    /// one made before the scene-LoRA catalog existed.
    ///
    /// <para>
    /// Populated from the render request's multi-select (<c>SceneImageStudioSettings.SceneLoras</c>), resolved
    /// against the <c>SceneLora</c> catalog and filtered to this model's family - NEVER from the model row, so no
    /// configuration can force a LoRA onto a render. <see cref="Loras"/> (character identity) is appended AFTER
    /// this list so identity stays closest to the subject.
    /// </para>
    /// </summary>
    IReadOnlyList<ResolvedSceneLora>? SceneLoras = null);
