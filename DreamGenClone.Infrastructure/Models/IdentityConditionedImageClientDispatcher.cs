using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// Routes identity-conditioned image calls to the correct client based on the provider's
/// <see cref="ImageProtocol"/>. Keeps <see cref="IIdentityConditionedImageClient"/> as the single
/// boundary while supporting both the pod ComfyUI client (multipart <c>/upload/image</c>) and the
/// RunPod serverless client (inline base64 <c>input.images</c>). No silent fallback — an unknown
/// protocol fails fast.
/// </summary>
public sealed class IdentityConditionedImageClientDispatcher : IIdentityConditionedImageClient
{
    private readonly ComfyUIIdentityConditionedClient _comfyUiClient;
    private readonly RunPodServerlessIdentityClient _serverlessClient;

    public IdentityConditionedImageClientDispatcher(
        ComfyUIIdentityConditionedClient comfyUiClient,
        RunPodServerlessIdentityClient serverlessClient)
    {
        _comfyUiClient = comfyUiClient;
        _serverlessClient = serverlessClient;
    }

    public Task<byte[]> GenerateAsync(
        ResolvedIdentityImageModel model,
        IdentityControlledImageRequest request,
        CancellationToken cancellationToken = default)
        => model.ImageProtocol switch
        {
            ImageProtocol.ComfyUi => _comfyUiClient.GenerateAsync(model, request, cancellationToken),
            ImageProtocol.ComfyUiServerless => _serverlessClient.GenerateAsync(model, request, cancellationToken),
            _ => throw new ImageGenerationException(
                $"Identity conditioning requires a ComfyUI provider (pod or serverless), but provider '{model.ProviderName}' uses protocol '{model.ImageProtocol}'.",
                model.ProviderName,
                reasonCode: "identity_requires_comfyui_protocol")
        };
}
