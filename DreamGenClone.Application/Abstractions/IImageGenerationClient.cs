using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// OpenAI-compatible image generation client for the <c>/v1/images/generations</c> endpoint.
/// Mirrors <see cref="ICompletionClient"/> as the model-agnostic boundary for image calls.
/// </summary>
public interface IImageGenerationClient
{
    /// <summary>
    /// Generate an image. Returns the decoded image bytes, or null if the provider returned no
    /// image data. Throws <see cref="ImageGenerationException"/> on HTTP/policy errors.
    /// </summary>
    Task<byte[]?> GenerateAsync(
        ResolvedImageModel model,
        string prompt,
        string? size,
        string? negativePrompt = null,
        long? seed = null,
        CancellationToken cancellationToken = default,
        SceneImageGenerationOptions? options = null);

    /// <summary>
    /// Health probe for an image model. Must never generate: this is a GET on the provider's model list,
    /// which is free and whose 401/403 still proves the API key. Providers that expose no list are reported
    /// as reachable-but-unverified; a bad key or an empty balance surfaces as a generation failure instead.
    /// Used by the Model Manager "Test" button and "Re-check All".
    /// </summary>
    Task<(bool Success, string Message)> CheckImageModelHealthAsync(
        string providerBaseUrl,
        string imageGenerationPath,
        int timeoutSeconds,
        string? decryptedApiKey,
        string modelIdentifier,
        ImageContentPolicy contentPolicy,
        CancellationToken cancellationToken = default,
        ImageProtocol imageProtocol = ImageProtocol.OpenAiImages);
}

/// <summary>Thrown when an image generation call fails (HTTP error or provider policy rejection).</summary>
public sealed class ImageGenerationException : Exception
{
    public ImageGenerationException(string message, string providerName, int? statusCode = null, string? reasonCode = null, Exception? inner = null)
        : base(message, inner)
    {
        ProviderName = providerName;
        StatusCode = statusCode;
        ReasonCode = reasonCode;
    }

    public string ProviderName { get; }
    public int? StatusCode { get; }
    public string? ReasonCode { get; }
}
