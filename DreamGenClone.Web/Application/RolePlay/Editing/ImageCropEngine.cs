using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Applies a crop to image bytes. Separated from the arithmetic so the framing rules can be tested
/// without decoding pixels, and so the store/queue plumbing never has to know about ImageSharp.
/// </summary>
public interface IImageCropEngine
{
    Task<byte[]> CropAsync(
        MediaEditCropOperation operation, Stream source, CancellationToken cancellationToken = default);
}

public sealed class ImageCropEngine : IImageCropEngine
{
    public async Task<byte[]> CropAsync(
        MediaEditCropOperation operation, Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(source);
        operation.Validate();

        using var image = await Image.LoadAsync<Rgba32>(source, cancellationToken);

        // The mode is an explicit alternative, never inferred: an unusable combination has already been
        // rejected by Validate(), and an unknown mode is rejected here rather than quietly framing.
        var rect = operation.Mode switch
        {
            ImageCropMode.Framing => ImageCropCalculator.Compute(image.Width, image.Height, operation.Settings),
            ImageCropMode.HeadAware => ImageCropCalculator.ComputeHeadAware(
                image.Width, image.Height, operation.Settings, operation.Measurement),
            ImageCropMode.HeadFramed => ImageCropCalculator.ComputeHeadFramed(
                image.Width, image.Height, operation.Settings, operation.Measurement),
            ImageCropMode.Manual => ImageCropCalculator.ComputeManual(image.Width, image.Height, operation.Rect),
            _ => throw new InvalidOperationException($"Unsupported crop mode '{operation.Mode}'.")
        };

        image.Mutate(context => context.Crop(new Rectangle(rect.X, rect.Y, rect.Width, rect.Height)));

        await using var output = new MemoryStream();
        await image.SaveAsync(output, new PngEncoder(), cancellationToken);
        return output.ToArray();
    }
}
