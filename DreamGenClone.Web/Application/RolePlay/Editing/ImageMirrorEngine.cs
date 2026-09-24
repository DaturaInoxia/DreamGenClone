using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Mirrors image bytes horizontally. Separated from the pipeline the same way the crop engine is, so the
/// deterministic operation can be tested without the store/queue plumbing.
/// </summary>
public interface IImageMirrorEngine
{
    Task<byte[]> MirrorAsync(Stream source, CancellationToken cancellationToken = default);
}

public sealed class ImageMirrorEngine : IImageMirrorEngine
{
    public async Task<byte[]> MirrorAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var image = await Image.LoadAsync<Rgba32>(source, cancellationToken);
        image.Mutate(context => context.Flip(FlipMode.Horizontal));

        await using var output = new MemoryStream();
        await image.SaveAsync(output, new PngEncoder(), cancellationToken);
        return output.ToArray();
    }
}
