using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// Routes reference-conditioned generation to the client that can execute it, by provider protocol.
/// Mirrors <see cref="ImageGenerationClientDispatcher"/> so the new boundary is model-agnostic too: a
/// protocol with no reference-conditioned implementation fails fast instead of silently degrading to a
/// prompt-only render (which would drop the user's references without telling them).
/// </summary>
public sealed class ReferenceConditionedImageClientDispatcher : IReferenceConditionedImageClient
{
    private readonly ComfyUIImageClient _comfyUiClient;

    public ReferenceConditionedImageClientDispatcher(ComfyUIImageClient comfyUiClient)
    {
        _comfyUiClient = comfyUiClient;
    }

    public Task<byte[]> GenerateWithReferencesAsync(
        ResolvedImageModel model,
        ReferenceConditionedImageRequest request,
        CancellationToken cancellationToken = default)
        => model.ImageProtocol switch
        {
            ImageProtocol.ComfyUi => _comfyUiClient.GenerateWithReferencesAsync(model, request, cancellationToken),
            _ => throw new ImageGenerationException(
                $"Provider protocol '{model.ImageProtocol}' cannot run reference-conditioned generation. "
                + "Select a ComfyUI-backed model that declares the NativeMultiReference strategy.",
                model.ProviderName,
                reasonCode: "unsupported_reference_image_protocol")
        };
}
