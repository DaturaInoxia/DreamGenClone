using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Scales an image DOWN to a target long edge with Lanczos. Separate from the upscaler because the two do
/// different things: ComfyUI synthesises the extra detail, this only brings the result back to the size the
/// workflow asked for.
/// </summary>
public interface IImageResizeEngine
{
    Task<byte[]> ScaleToLongEdgeAsync(byte[] source, int targetLongEdge, CancellationToken cancellationToken = default);
}

public sealed class ImageResizeEngine : IImageResizeEngine
{
    /// <summary>
    /// The size a long edge of <paramref name="targetLongEdge"/> implies, preserving aspect and rounding to
    /// at least one pixel. Deliberately pure so the rule is testable as arithmetic.
    /// </summary>
    public static (int Width, int Height) ComputeTargetSize(int sourceWidth, int sourceHeight, int targetLongEdge)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new InvalidOperationException($"An image needs a positive size, but got {sourceWidth}x{sourceHeight}.");
        if (targetLongEdge <= 0)
            throw new InvalidOperationException($"A target long edge must be positive, but got {targetLongEdge}.");

        var longEdge = Math.Max(sourceWidth, sourceHeight);
        if (longEdge <= targetLongEdge)
            return (sourceWidth, sourceHeight);

        var scale = (double)targetLongEdge / longEdge;
        return (
            Math.Max(1, (int)Math.Round(sourceWidth * scale)),
            Math.Max(1, (int)Math.Round(sourceHeight * scale)));
    }

    public async Task<byte[]> ScaleToLongEdgeAsync(
        byte[] source, int targetLongEdge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length == 0)
            throw new InvalidOperationException("There are no image bytes to scale.");

        using var image = Image.Load<Rgba32>(source);
        var (width, height) = ComputeTargetSize(image.Width, image.Height, targetLongEdge);

        // Already at or below the target: there is nothing to bring down, so the bytes are returned as they
        // are rather than being upscaled — "down to" never means "up to".
        if (width == image.Width && height == image.Height)
            return source;

        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(width, height),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Lanczos3
        }));

        await using var output = new MemoryStream();
        await image.SaveAsync(output, new PngEncoder(), cancellationToken);
        return output.ToArray();
    }
}
