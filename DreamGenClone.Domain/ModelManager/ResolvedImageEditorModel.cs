namespace DreamGenClone.Domain.ModelManager;

/// <summary>Fully configured Qwen source-image editing model and workflow settings.</summary>
/// <remarks>
/// <see cref="ComfyUiUrl"/> is the provider base URL for both protocols (a ComfyUI pod origin for
/// <see cref="ImageProtocol.ComfyUi"/>, or the RunPod serverless <c>/v2/&#123;endpointId&#125;</c> base
/// for <see cref="ImageProtocol.ComfyUiServerless"/>). The editing client dispatcher selects the
/// transport from <see cref="ImageProtocol"/>, and the ComfyUI HTTP client selects the graph shape
/// from <see cref="GraphKind"/> (configured in Model Manager, never guessed).
/// </remarks>
public sealed record ResolvedImageEditorModel(
    string ComfyUiUrl,
    int ProviderTimeoutSeconds,
    string? ApiKeyEncrypted,
    string ModelIdentifier,
    string ProviderName,
    ImageContentPolicy ContentPolicy,
    string DiffusionModel,
    string TextEncoder,
    string Vae,
    int Steps,
    double Cfg,
    string Sampler,
    string Scheduler,
    double Denoise,
    double AuraFlowShift,
    double CfgNormStrength,
    ImageProtocol ImageProtocol = ImageProtocol.ComfyUi,
    string? RegisteredModelId = null,
    ImageEditorGraphKind? GraphKind = null,
    string? LoraName = null,
    double? LoraStrength = null,

    /// <summary>
    /// <c>TextEncodeQwenImage21.resolution</c> for <see cref="ImageEditorGraphKind.QwenImage21Native"/>:
    /// a total pixel BUDGET (not a dimension) used to resize the source and every reference. Read from
    /// the model's NativeMultiReference qualification; null for every other graph kind.
    /// </summary>
    int? ResolutionBudget = null,

    /// <summary>
    /// Scene LoRAs the operator selected for THIS edit, resolved through the same scene-LoRA catalogue validation
    /// the compose path uses (family match, enabled row, explicit strength). Null or empty means NO scene LoRA -
    /// a configured state, not a fallback: the edit graph then emits no loader node and the render is identical to
    /// one made before edit-mode LoRA selection existed. Distinct from <see cref="LoraName"/>, which is the single
    /// per-model editor LoRA persisted on the model row in Model Manager.
    /// </summary>
    IReadOnlyList<ResolvedSceneLora>? SceneLoras = null,

    /// <summary>
    /// The editor row's <see cref="SceneImageModelFamily"/>. A scene-LoRA selection is refused against a family the
    /// LoRA was not trained for, so the edit path needs the same declaration the compose path has - and
    /// <c>Unknown</c> is a real value that REFUSES a selection rather than skipping the check.
    /// </summary>
    SceneImageModelFamily SceneImageModelFamily = SceneImageModelFamily.Unknown);