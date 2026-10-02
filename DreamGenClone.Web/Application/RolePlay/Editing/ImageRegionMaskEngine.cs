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

        // Grow the rectangle before it is painted (B-130): a mask cut exactly on the drawn edge leaves a seam, so the
        // operator's grow value widens the white area first. Clamped to the frame, the same way the host's own grow
        // node is bounded by the image.
        var grow = Math.Max(0, region.GrowMaskBy);
        left = Math.Max(0, left - grow);
        top = Math.Max(0, top - grow);
        right = Math.Min(width, right + grow);
        bottom = Math.Min(height, bottom + grow);

        // Soften the edge by painting the falloff into the mask ITSELF (CASE-21): the host's FeatherMask node feathers
        // the mask TENSOR's outer frame border, not a region drawn inside the frame, so it can never soften an interior
        // rectangle's edge. The ramp below is what actually removes the visible seam, and it behaves the same on every
        // host and every frame size.
        var feather = Math.Min(Math.Max(0, region.FeatherPixels), Math.Min(width, height));

        using var image = new Image<L8>(width, height, new L8(0));
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    row[x] = new L8(MaskValue(x, y, left, top, right, bottom, feather));
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// The mask value for one pixel: fully white inside the grown rectangle, fully black outside it, and a linear ramp
    /// across <paramref name="feather"/> pixels on each side of the edge. A zero feather is a hard edge.
    /// </summary>
    private static byte MaskValue(int x, int y, int left, int top, int right, int bottom, int feather)
    {
        // Signed distance to the rectangle: negative inside, positive outside, 0 exactly on the edge.
        int signedDistance;
        if (x >= left && x < right && y >= top && y < bottom)
        {
            var insideX = Math.Min(x - left, right - 1 - x);
            var insideY = Math.Min(y - top, bottom - 1 - y);
            signedDistance = -Math.Min(insideX, insideY);
        }
        else
        {
            var outsideX = Math.Max(left - x, x - (right - 1));
            var outsideY = Math.Max(top - y, y - (bottom - 1));
            signedDistance = Math.Max(outsideX, outsideY);
        }

        if (feather <= 0)
            return signedDistance <= 0 ? (byte)255 : (byte)0;

        var t = Math.Clamp((feather - signedDistance) / (2.0 * feather), 0.0, 1.0);
        return (byte)Math.Round(t * 255);
    }

    private static int ToPixels(int extent, double percent)
        => (int)Math.Round(extent * percent / 100d, MidpointRounding.AwayFromZero);
}
