using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

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

    Task<bool> CheckHealthAsync(
        string providerBaseUrl,
        int timeoutSeconds,
        string? decryptedApiKey,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> CheckModelHealthAsync(
        string providerBaseUrl,
        string chatCompletionsPath,
        int timeoutSeconds,
        string? decryptedApiKey,
        string modelIdentifier,
        CancellationToken cancellationToken = default);
}
