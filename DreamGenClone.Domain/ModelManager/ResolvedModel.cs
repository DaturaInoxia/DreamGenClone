namespace DreamGenClone.Domain.ModelManager;

public sealed record ResolvedModel(
    string ProviderBaseUrl,
    string ChatCompletionsPath,
    int ProviderTimeoutSeconds,
    string? ApiKeyEncrypted,
    string ModelIdentifier,
    double Temperature,
    double TopP,
    int MaxTokens,
    string ProviderName,
    bool IsSessionOverride);
