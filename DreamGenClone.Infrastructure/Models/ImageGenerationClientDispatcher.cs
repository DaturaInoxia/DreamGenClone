using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// Routes image-generation calls to the correct client based on the provider's
/// <see cref="ImageProtocol"/>. Keeps <see cref="IImageGenerationClient"/> as the single
/// model-agnostic boundary while supporting both the OpenAI-compatible images endpoint and
/// ComfyUI.
/// </summary>
public sealed class ImageGenerationClientDispatcher : IImageGenerationClient
{
    private readonly ImageGenerationClient _openAiClient;
    private readonly ComfyUIImageClient _comfyUiClient;

    public ImageGenerationClientDispatcher(
        ImageGenerationClient openAiClient,
        ComfyUIImageClient comfyUiClient)
    {
        _openAiClient = openAiClient;
        _comfyUiClient = comfyUiClient;
    }

    public Task<byte[]?> GenerateAsync(
        ResolvedImageModel model,
        string prompt,
        string? size,
        string? negativePrompt = null,
        long? seed = null,
        CancellationToken cancellationToken = default)
        => model.ImageProtocol == ImageProtocol.ComfyUi
            ? _comfyUiClient.GenerateAsync(model, prompt, size, negativePrompt, seed, cancellationToken)
            : _openAiClient.GenerateAsync(model, prompt, size, negativePrompt, seed, cancellationToken);

    public Task<(bool Success, string Message)> CheckImageModelHealthAsync(
        string providerBaseUrl,
        string imageGenerationPath,
        int timeoutSeconds,
        string? decryptedApiKey,
        string modelIdentifier,
        ImageContentPolicy contentPolicy,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Health check protocol selection requires the resolved provider; use the concrete client directly.");
}
