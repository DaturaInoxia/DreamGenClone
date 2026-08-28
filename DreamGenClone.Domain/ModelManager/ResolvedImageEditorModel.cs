namespace DreamGenClone.Domain.ModelManager;

/// <summary>Fully configured Qwen source-image editing model and ComfyUI workflow settings.</summary>
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
    double CfgNormStrength);