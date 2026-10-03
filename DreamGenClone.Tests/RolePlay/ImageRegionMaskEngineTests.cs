using DreamGenClone.Web.Application.RolePlay.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// One region becomes TWO images (CASE-21 / CASE-25): the MASK the host confines itself with, and the ALPHA the render
/// is blended back over the source with. These tests pin both geometries, because they are what keeps the operator's
/// rectangle honest - a mask that covers the wrong area is not a visibly broken render, it is a correct-looking render
/// of the wrong area, and an alpha that fades in the wrong place is the visible rectangle coming back.
///
/// The mask is HARD-EDGED and that is the point: the host rounds it to 0/1 before it confines anything with it
/// (<c>VAEEncodeForInpaint.encode</c> rounds both the pixel path and the sampler's noise mask), so a soft ramp painted
/// into the mask is discarded and the confined area is a rectangle with a one-pixel step at its edge. The softening
/// therefore lives in the alpha, whose transparent end meets the mask's white edge.
/// </summary>
public sealed class ImageRegionMaskEngineTests
{
    private readonly ImageRegionMaskEngine _engine = new();

    [Fact]
    public void TheMaskIsHardAndCoversTheDrawnRegionGrownByGrowAndFeather()
    {
        // A quarter of a 100x100 frame, so percent and pixels are the same number: drawn 25..75, grown by 5 + 10 = 15.
        var region = new MediaEditRegionOperation(25, 25, 50, 50, GrowMaskBy: 5, FeatherPixels: 10);

        using var mask = Image.Load<L8>(_engine.Build(region, 100, 100));

        Assert.Equal(100, mask.Width);
        Assert.Equal(100, mask.Height);
        Assert.Equal(255, mask[50, 50].PackedValue);   // inside the drawn region
        Assert.Equal(255, mask[10, 10].PackedValue);   // its first white pixel: 25 - 5 - 10
        Assert.Equal(0, mask[9, 9].PackedValue);       // one pixel outside
        Assert.Equal(255, mask[89, 89].PackedValue);   // its last white pixel: 75 + 5 + 10 - 1
        Assert.Equal(0, mask[90, 90].PackedValue);     // one past it
        Assert.Equal(0, mask[0, 0].PackedValue);       // the frame corner
        Assert.Equal(0, mask[99, 99].PackedValue);     // the far corner

        Assert.All(Values(mask), value => Assert.True(
            value is 0 or 255,
            $"The mask carries {value}, but the host rounds it to 0/1 - a ramp here is discarded, not honoured."));
    }

    /// <summary>A region flush with two edges is the panorama/outpaint shape and must stay white from the edge.</summary>
    [Fact]
    public void ARegionFlushWithTwoEdgesIsWhiteFromTheEdge()
    {
        var region = new MediaEditRegionOperation(0, 0, 50, 20, GrowMaskBy: 0, FeatherPixels: 10);

        using var mask = Image.Load<L8>(_engine.Build(region, 200, 100));

        Assert.Equal(255, mask[0, 0].PackedValue);
        Assert.Equal(255, mask[109, 29].PackedValue);   // 100 wide, 20 tall, grown by 10
        Assert.Equal(0, mask[110, 29].PackedValue);
        Assert.Equal(0, mask[109, 30].PackedValue);
    }

    [Fact]
    public void TheGrowValueWidensTheWhiteRectangle()
    {
        var region = new MediaEditRegionOperation(25, 25, 50, 50, GrowMaskBy: 5, FeatherPixels: 1);

        using var mask = Image.Load<L8>(_engine.Build(region, 100, 100));

        Assert.Equal(255, mask[19, 19].PackedValue);    // grew 5px past the drawn corner, plus the single feather pixel
        Assert.Equal(0, mask[18, 18].PackedValue);
        Assert.Equal(255, mask[50, 50].PackedValue);    // the middle is still white
    }

    /// <summary>
    /// The alpha is what the operator's feather actually means: opaque over everything the region may change (the drawn
    /// rectangle grown by the grow value), then a linear fade to nothing across the feather - reaching zero exactly on
    /// the mask's white edge, which is where the host's hard step sits.
    /// </summary>
    [Fact]
    public void TheAlphaIsOpaqueInsideTheRegionAndFadesToNothingAtTheMasksEdge()
    {
        // Drawn 25..75, so the host may change 20..80 and the mask is white over 15..85. A feather of 5 makes every
        // step of the ramp a whole number (255/5 = 51).
        var region = new MediaEditRegionOperation(25, 25, 50, 50, GrowMaskBy: 5, FeatherPixels: 5);

        using var alpha = Image.Load<L8>(_engine.BuildCompositeAlpha(region, 100, 100));

        Assert.Equal(255, alpha[50, 50].PackedValue);   // deep inside the region
        Assert.Equal(255, alpha[20, 20].PackedValue);   // the last fully opaque pixel
        Assert.Equal(204, alpha[19, 19].PackedValue);   // one pixel into the fade
        Assert.Equal(153, alpha[18, 18].PackedValue);
        Assert.Equal(102, alpha[17, 17].PackedValue);
        Assert.Equal(51, alpha[16, 16].PackedValue);
        Assert.Equal(0, alpha[15, 15].PackedValue);     // the mask's white edge: fully the source again
        Assert.Equal(0, alpha[0, 0].PackedValue);       // and everything outside it stays the source
        Assert.Equal(0, alpha[99, 99].PackedValue);
    }

    /// <summary>
    /// A region flush with the frame edge still fades INWARD: the fade has nowhere to go past the edge of the frame, and
    /// inventing room for it would change pixels the operator never drew.
    /// </summary>
    [Fact]
    public void ARegionFlushWithTheFrameEdgeFadesInwardOnly()
    {
        var region = new MediaEditRegionOperation(0, 0, 50, 50, GrowMaskBy: 0, FeatherPixels: 4);

        using var alpha = Image.Load<L8>(_engine.BuildCompositeAlpha(region, 100, 100));

        Assert.Equal(255, alpha[0, 0].PackedValue);     // the drawn edge itself is still fully the render
        Assert.Equal(255, alpha[49, 49].PackedValue);
        Assert.Equal(191, alpha[50, 50].PackedValue);   // 255 * (1 - 1/4), fading into the untouched side
        Assert.Equal(128, alpha[51, 51].PackedValue);
        Assert.Equal(0, alpha[54, 54].PackedValue);
        Assert.Equal(0, alpha[99, 99].PackedValue);
    }

    /// <summary>
    /// A feather wide enough to reach across the frame leaves nothing pinned - which is what the operator asked for by
    /// choosing it. It must not throw, and it must not ramp past pixels that do not exist.
    /// </summary>
    [Fact]
    public void AFeatherWiderThanTheFrameGivesTheHostTheWholeFrame()
    {
        var region = new MediaEditRegionOperation(25, 25, 50, 50, GrowMaskBy: 0, FeatherPixels: 400);

        using var mask = Image.Load<L8>(_engine.Build(region, 100, 100));
        using var alpha = Image.Load<L8>(_engine.BuildCompositeAlpha(region, 100, 100));

        Assert.All(Values(mask), value => Assert.Equal(255, value));
        Assert.All(Values(alpha), value => Assert.True(value > 0, "a feather this wide leaves the whole frame available"));
        Assert.Equal(255, alpha[25, 25].PackedValue);
    }

    /// <summary>
    /// A region that rounds away to nothing would emit an all-black mask, which pins the WHOLE frame and returns the
    /// source unchanged. Refused instead: a render that looks like a success and did nothing is the worst outcome.
    /// </summary>
    [Fact]
    public void ARegionTooSmallForTheFrameIsRefusedRatherThanEmittedAsAnAllBlackMask()
    {
        var region = new MediaEditRegionOperation(10, 10, 0.1, 0.1, GrowMaskBy: 0, FeatherPixels: 8);

        var exception = Assert.Throws<InvalidOperationException>(() => _engine.Build(region, 32, 32));

        Assert.Contains("rounds away to nothing", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A zero feather is refused, by name: the host rounds the mask it confines with to 0/1, so a zero feather confines
    /// the edit to a hard-edged rectangle and that edge shows in the render as an outline (CASE-25). Refusing it here
    /// means the operator hears about it before a render is paid for, rather than seeing the box in the picture.
    /// </summary>
    [Fact]
    public void AConfinedEditWithoutAFeatherIsRefusedByName()
    {
        var region = new MediaEditRegionOperation(25, 25, 50, 50, GrowMaskBy: 5, FeatherPixels: 0);

        var exception = Assert.Throws<InvalidOperationException>(() => _engine.Build(region, 100, 100));

        Assert.Contains("RegionFeatherPixels", exception.Message, StringComparison.Ordinal);
        Assert.Contains("outline", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFrameWithoutASizeIsRefused()
    {
        var region = new MediaEditRegionOperation(10, 10, 10, 10, GrowMaskBy: 0, FeatherPixels: 8);

        Assert.Throws<InvalidOperationException>(() => _engine.Build(region, 0, 100));
    }

    private static IEnumerable<byte> Values(Image<L8> image)
    {
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                yield return image[x, y].PackedValue;
            }
        }
    }
}
