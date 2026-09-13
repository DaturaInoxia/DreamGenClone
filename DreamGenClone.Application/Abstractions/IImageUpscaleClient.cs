using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// Enhances an image with a ComfyUI upscale model (<c>UpscaleModelLoader</c> + <c>ImageUpscaleWithModel</c>).
///
/// The endpoint is taken from a resolved image-editor model because the upscaler runs on the SAME ComfyUI
/// instance the editor runs on: it needs that machine's URL, timeout and credentials, not an editor model
/// of its own. The upscale model itself is named by configuration (<c>UpscalerModelName</c>).
/// </summary>
public interface IImageUpscaleClient
{
    Task<byte[]> UpscaleAsync(
        ResolvedImageEditorModel endpoint,
        string upscalerModelName,
        Stream sourceImage,
        string sourceFileName,
        CancellationToken cancellationToken = default);
}
