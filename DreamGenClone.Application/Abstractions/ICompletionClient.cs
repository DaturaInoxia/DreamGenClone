using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// Thrown when a provider aborts a streaming completion after it has already emitted text. Carries the partial
/// output so a caller (Prompt Tester) can still show what arrived; engine callers treat it as a failed
/// generation and must not persist the partial text as a finished scene.
/// </summary>
public sealed class IncompleteCompletionException : Exception
{
    public IncompleteCompletionException(string providerMessage, string partialContent, string? partialReasoning, string? finishReason = null)
        : base(providerMessage)
    {
        PartialContent = partialContent;
        PartialReasoning = partialReasoning;
        FinishReason = finishReason;
    }

    public string PartialContent { get; }
    public string? PartialReasoning { get; }
    public string? FinishReason { get; }
}

public interface ICompletionClient
{
    Task<string> GenerateAsync(
        string prompt,
        ResolvedModel resolved,
        CancellationToken cancellationToken = default);

    Task<string> GenerateAsync(
        string systemMessage,
        string userMessage,
        ResolvedModel resolved,
        CancellationToken cancellationToken = default);

    Task<string> StreamGenerateAsync(
        string prompt,
        ResolvedModel resolved,
        Func<string, Task> onChunk,
        CancellationToken cancellationToken = default);

    Task<string> StreamGenerateAsync(
        string systemMessage,
        string userMessage,
        ResolvedModel resolved,
        Func<string, Task> onChunk,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Provider reachability probe. Never requests a completion: it reads the provider's model list, which is
    /// free and reports 401/403 for a bad key, and falls back to a plain ping when no list exists.
    /// </summary>
    Task<bool> CheckHealthAsync(
        string providerBaseUrl,
        int timeoutSeconds,
        string? decryptedApiKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Model health probe. Never requests a completion — the model list proves the key and the model's
    /// presence; a bad key or empty balance is left to fail at generation time.
    /// </summary>
    Task<(bool Success, string Message)> CheckModelHealthAsync(
        string providerBaseUrl,
        string chatCompletionsPath,
        int timeoutSeconds,
        string? decryptedApiKey,
        string modelIdentifier,
        CancellationToken cancellationToken = default);

    Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(
        string prompt,
        ResolvedModel resolved,
        CancellationToken cancellationToken = default);

    Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(
        string systemMessage,
        string userMessage,
        ResolvedModel resolved,
        CancellationToken cancellationToken = default);

    Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(
        string prompt,
        ResolvedModel resolved,
        Func<string, Task> onChunk,
        CancellationToken cancellationToken = default);

    Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(
        string systemMessage,
        string userMessage,
        ResolvedModel resolved,
        Func<string, Task> onChunk,
        CancellationToken cancellationToken = default);
}
