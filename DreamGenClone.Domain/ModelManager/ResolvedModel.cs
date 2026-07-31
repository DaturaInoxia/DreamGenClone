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
    bool IsSessionOverride)
{
    /// <summary>
    /// When true, the completion request sends a thinking/chain-of-thought suppression
    /// parameter (chat_template_kwargs.thinking=false) so the model emits a direct answer
    /// instead of a long reasoning trace. Currently scoped to RolePlaySemanticAnalysis only:
    /// its event-extraction task is deterministic JSON output and extended reasoning was
    /// producing 30K+ char traces that failed JSON parsing.
    /// </summary>
    public bool DisableThinking { get; init; }
}
