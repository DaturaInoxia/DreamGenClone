namespace DreamGenClone.Domain.ModelManager;

/// <summary>Classifies how a provider executes image-generation requests.</summary>
public enum ProviderExecutionClass
{
    /// <summary>OpenAiImages: no ComfyUI graph and no cold start.</summary>
    HostedApi = 1,

    /// <summary>ComfyUi: an always-on dedicated pod.</summary>
    DedicatedPod = 2,

    /// <summary>ComfyUiServerless: cold-starts on the first request.</summary>
    SelfBuiltServerless = 3
}