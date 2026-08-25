namespace DreamGenClone.Domain.ModelManager;

public enum ModelLifecycleStrategy
{
    Unknown = 0,
    ScheduledSinglePod = 1,
    AlwaysOnSeparateProvider = 2,
    ManagedDedicatedPod = 3
}

public sealed record ResolvedMultimodalModel(
    string ProviderId,
    string ModelId,
    string ProviderBaseUrl,
    string ChatCompletionsPath,
    string ReadinessPath,
    string ReadinessSuccessContractJson,
    int RequestTimeoutSeconds,
    int TransitionTimeoutSeconds,
    int TransitionMarginSeconds,
    string CredentialReference,
    string? ApiKeyEncrypted,
    string ModelIdentifier,
    string ProviderName,
    ImageContentPolicy ContentPolicy,
    ModelLifecycleStrategy LifecycleStrategy,
    int MaximumInputImages,
    long MaximumInputImageBytes,
    long MaximumInputImagePixels,
    int MaximumInputImageDimension,
    IReadOnlySet<string> AcceptedInputMediaTypes,
    long MaximumResponseBytes,
    int MaximumActiveRequests,
    int QueueCapacity,
    double Temperature,
    double TopP,
    int MaxTokens,
    string? RuntimeRevision,
    string? ArtifactRevision);