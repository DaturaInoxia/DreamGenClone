using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Builds the region mask a confined edit reads: WHITE where the instruction may change the picture, BLACK where the
/// source must survive (CASE-21 - the masked latent is what pins everything outside the region).
///
/// The geometry is the operator's rectangle in PERCENT, converted to pixels at the source's own size. The mask is an
/// artifact of the operation, not of the UI: an operation record carries the rectangle, and this is what turns it into
/// the image the graph needs - which is also why a region drawn in the browser, a region stored on a run, and a region
/// replayed by a proof are the same numbers.
/// </summary>
public interface IImageRegionMaskEngine
{
    /// <summary>
    /// The mask for <paramref name="region"/> at a frame of <paramref name="width"/> x <paramref name="height"/> pixels,
    /// as a PNG. Fails fast on geometry that cannot be honoured rather than emitting a mask that means something else.
    /// </summary>
    byte[] Build(MediaEditRegionOperation region, int width, int height);
}

/// <summary>
/// The one implementation of "turn a region into a mask". Pixel work belongs here and nowhere else, for the same
/// reason <see cref="IImageMirrorEngine"/> exists: a second implementation is how two paths come to disagree about
/// which pixels a rectangle actually covers.
/// </summary>
public sealed class ImageRegionMaskEngine : IImageRegionMaskEngine
{
    public byte[] Build(MediaEditRegionOperation region, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(region);
        region.Validate();

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"A region mask needs a positive frame size, but got {width}x{height}.");
        }

        var left = ToPixels(width, region.LeftPercent);
        var top = ToPixels(height, region.TopPercent);
        var right = ToPixels(width, region.LeftPercent + region.WidthPercent);
        var bottom = ToPixels(height, region.TopPercent + region.HeightPercent);

        // A region that rounds away to nothing is a mistake to report, not a mask to emit: the graph would receive an
        // all-black mask, which pins the ENTIRE frame and returns the source unchanged - a render that looks like a
        // success and did nothing.
        if (right <= left || bottom <= top)
        {
            throw new InvalidOperationException(
                $"The region rounds away to nothing at {width}x{height}: {region.Describe()}. Draw a larger region, or "
                + "edit the whole frame.");
        }

        using var image = new Image<L8>(width, height, new L8(0));
        image.ProcessPixelRows(accessor =>
        {
            for (var y = top; y < bottom; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = left; x < right; x++)
                {
                    row[x] = new L8(255);
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    private static int ToPixels(int extent, double percent)
        => (int)Math.Round(extent * percent / 100d, MidpointRounding.AwayFromZero);
}
