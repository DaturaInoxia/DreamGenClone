using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// Routes pose-conditioned image calls to the correct client based on the provider's
/// <see cref="ImageProtocol"/>. Keeps <see cref="IPoseConditionedImageClient"/> as the single
/// boundary. Only the local <see cref="ImageProtocol.ComfyUi"/> path is implemented today; serverless
/// has no pose worker and hosted APIs cannot execute ControlNet — both fail fast. No silent fallback.
/// </summary>
public sealed class PoseConditionedImageClientDispatcher : IPoseConditionedImageClient
{
    private readonly ComfyUIPoseConditionedImageClient _comfyUiClient;

    public PoseConditionedImageClientDispatcher(ComfyUIPoseConditionedImageClient comfyUiClient)
    {
        _comfyUiClient = comfyUiClient;
    }

    public Task<byte[]> GenerateAsync(
        ResolvedPoseImageModel model,
        PoseConditionedImageRequest request,
        CancellationToken cancellationToken = default)
        => model.ImageProtocol switch
        {
            ImageProtocol.ComfyUi => _comfyUiClient.GenerateAsync(model, request, cancellationToken),
            ImageProtocol.ComfyUiServerless => throw new ImageGenerationException(
                $"Pose conditioning (ControlNet) is not yet available on serverless ComfyUI provider '{model.ProviderName}'. Use the local ComfyUI provider.",
                model.ProviderName,
                reasonCode: "pose_requires_local_comfyui"),
            _ => throw new ImageGenerationException(
                $"Pose conditioning (ControlNet) requires the local ComfyUI protocol, but provider '{model.ProviderName}' uses protocol '{model.ImageProtocol}'.",
                model.ProviderName,
                reasonCode: "pose_requires_comfyui_protocol")
        };
}
