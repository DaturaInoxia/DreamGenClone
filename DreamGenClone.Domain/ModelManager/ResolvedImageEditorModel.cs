namespace DreamGenClone.Domain.ModelManager;

/// <summary>Fully configured Qwen source-image editing model and workflow settings.</summary>
/// <remarks>
/// <see cref="ComfyUiUrl"/> is the provider base URL for both protocols (a ComfyUI pod origin for
/// <see cref="ImageProtocol.ComfyUi"/>, or the RunPod serverless <c>/v2/&#123;endpointId&#125;</c> base
/// for <see cref="ImageProtocol.ComfyUiServerless"/>). The editing client dispatcher selects the
/// transport from <see cref="ImageProtocol"/>.
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
    ImageProtocol ImageProtocol = ImageProtocol.ComfyUi);