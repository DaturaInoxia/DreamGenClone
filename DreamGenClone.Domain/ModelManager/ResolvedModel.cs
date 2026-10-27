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
    public bool SupportsThinkingControl { get; init; }
    public ThinkingMode ThinkingMode { get; init; }

    /// <summary>
    /// Provider sampling seed, or null to let the provider choose. Set it to make two runs of the same prompt
    /// reproducible — the only way to tell a wording change from sampling noise when comparing prose.
    /// </summary>
    public long? Seed { get; init; }

    /// <summary>
    /// The function's configured repetition penalty, or null when it configures none. B-167: a compiler that restates
    /// one fact several ways is a repetition problem, and the provider applies the penalty natively — but the value is a
    /// DECLARED setting on the function default row (Model Manager), never a code constant, so a prompt compiled with a
    /// penalty nobody chose cannot be compared to one that was.
    /// </summary>
    public double? PresencePenalty { get; init; }
}
