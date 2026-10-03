using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Builds the two images a confined edit reads: the MASK the host confines itself with, and the COMPOSITE ALPHA the
/// render is blended back over the source with (CASE-21 / CASE-25).
///
/// The mask is WHITE where the instruction may change the picture and BLACK where the source must survive. It is
/// HARD-EDGED deliberately, because the host rounds it to 0/1 before it confines anything
/// (<c>VAEEncodeForInpaint.encode</c> rounds both the pixel path and the sampler's <c>noise_mask</c>), so a soft ramp
/// painted into the mask is DISCARDED and the edit is confined to a rectangle whose edge shows in the render as a
/// one-pixel step. Its white area therefore covers the rectangle grown by grow + feather - the extra feather being the
/// room the fade needs - and the softening is applied AFTER the render, by blending the result over the source through
/// <see cref="BuildCompositeAlpha"/>.
///
/// The geometry is the operator's rectangle in PERCENT, converted to pixels at the source's own size. The mask is an
/// artifact of the operation, not of the UI: an operation record carries the rectangle, and this is what turns it into
/// the images the graph and the compositor need - which is also why a region drawn in the browser, a region stored on a
/// run, and a region replayed by a proof are the same numbers.
/// </summary>
public interface IImageRegionMaskEngine
{
    /// <summary>
    /// The mask for <paramref name="region"/> at a frame of <paramref name="width"/> x <paramref name="height"/> pixels,
    /// as a PNG. Fails fast on geometry that cannot be honoured rather than emitting a mask that means something else.
    /// </summary>
    byte[] Build(MediaEditRegionOperation region, int width, int height);

    /// <summary>
    /// The alpha the rendered frame is blended back over the source with, at the same frame size: fully opaque inside
    /// the drawn rectangle grown by <c>GrowMaskBy</c>, fading linearly to fully transparent across
    /// <c>FeatherPixels</c> beyond it. Its transparent end meets the mask's white edge, so the host's hard step lands
    /// where the blend has already given the source back.
    /// </summary>
    byte[] BuildCompositeAlpha(MediaEditRegionOperation region, int width, int height);
}

/// <summary>
/// The one implementation of "turn a region into the two images a confined edit needs". Pixel work belongs here and
/// nowhere else, for the same reason <see cref="IImageMirrorEngine"/> exists: a second implementation is how two paths
/// come to disagree about which pixels a rectangle actually covers.
/// </summary>
public sealed class ImageRegionMaskEngine : IImageRegionMaskEngine
{
    public byte[] Build(MediaEditRegionOperation region, int width, int height)
    {
        var geometry = GeometryOf(region, width, height);

        // Hard-edged, because the host will round it to 0/1 anyway: painting a ramp here would only pretend the edge
        // was soft while the graph confined the edit to the rectangle's step. The softness lives in the composite.
        return Paint(width, height, (x, y) => geometry.Generated.Contains(x, y) ? (byte)255 : (byte)0);
    }

    public byte[] BuildCompositeAlpha(MediaEditRegionOperation region, int width, int height)
    {
        var geometry = GeometryOf(region, width, height);
        var feather = region.FeatherPixels;

        // Opaque over everything the operator may change, then a linear fade to nothing across the feather. The fade
        // ends exactly on the mask's white edge - the hard step the host leaves in the render - so that step is
        // multiplied by zero, and the source is handed back gradually instead of at a line.
        return Paint(width, height, (x, y) =>
        {
            var distance = geometry.Editable.Distance(x, y);
            if (distance <= 0)
                return (byte)255;

            return (byte)Math.Round(Math.Clamp(1.0 - (double)distance / feather, 0.0, 1.0) * 255.0);
        });
    }

    private static byte[] Paint(int width, int height, Func<int, int, byte> value)
    {
        using var image = new Image<L8>(width, height, new L8(0));
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    row[x] = new L8(value(x, y));
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// The rectangles one region means, in pixels at the frame's own size: what the operator DREW, the area the host may
    /// change (the drawn rectangle grown by the operator's grow), and the mask's white area (grown by grow + feather,
    /// because the fade has to lie over pixels the host is allowed to generate - fading pinned pixels is fading the
    /// source into itself, which softens nothing).
    /// </summary>
    private static RegionGeometry GeometryOf(MediaEditRegionOperation region, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(region);
        region.Validate();

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"A region mask needs a positive frame size, but got {width}x{height}.");
        }

        var drawn = new PixelRect(
            ToPixels(width, region.LeftPercent),
            ToPixels(height, region.TopPercent),
            ToPixels(width, region.LeftPercent + region.WidthPercent),
            ToPixels(height, region.TopPercent + region.HeightPercent));

        // A region that rounds away to nothing is a mistake to report, not a mask to emit: the graph would receive an
        // all-black mask, which pins the ENTIRE frame and returns the source unchanged - a render that looks like a
        // success and did nothing.
        if (drawn.IsEmpty)
        {
            throw new InvalidOperationException(
                $"The region rounds away to nothing at {width}x{height}: {region.Describe()}. Draw a larger region, or "
                + "edit the whole frame.");
        }

        var editable = drawn.Grow(region.GrowMaskBy, width, height);
        return new RegionGeometry(editable, editable.Grow(region.FeatherPixels, width, height));
    }

    private static int ToPixels(int extent, double percent)
        => (int)Math.Round(extent * percent / 100d, MidpointRounding.AwayFromZero);

    private readonly record struct RegionGeometry(PixelRect Editable, PixelRect Generated);

    /// <summary>A pixel rectangle, half open: <see cref="Right"/> and <see cref="Bottom"/> are the first excluded pixel.</summary>
    private readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
    {
        public bool IsEmpty => Right <= Left || Bottom <= Top;

        public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

        /// <summary>Zero inside the rectangle, and how many pixels outside it otherwise - the larger of the two axes.</summary>
        public int Distance(int x, int y)
        {
            if (Contains(x, y))
                return 0;

            return Math.Max(
                Math.Max(Left - x, x - (Right - 1)),
                Math.Max(Top - y, y - (Bottom - 1)));
        }

        /// <summary>The rectangle widened by <paramref name="pixels"/> on every side, clamped to the frame.</summary>
        public PixelRect Grow(int pixels, int width, int height)
            => new(
                Math.Max(0, Left - pixels),
                Math.Max(0, Top - pixels),
                Math.Min(width, Right + pixels),
                Math.Min(height, Bottom + pixels));
    }
}
