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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reachability/health probe for an image model. POSTs a minimal image request to the provider's
    /// image-generation path (Split-image models don't answer on the chat path). Returns a success
    /// flag plus a user-facing message. Used by the Model Manager "Test Connection" for image-kind
    /// models.
    /// </summary>
    Task<(bool Success, string Message)> CheckImageModelHealthAsync(
        string providerBaseUrl,
        string imageGenerationPath,
        int timeoutSeconds,
        string? decryptedApiKey,
        string modelIdentifier,
        CancellationToken cancellationToken = default);
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
