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
/// into the mask is discarded and the confined area is a rectangle with a decoded step at its edge. The softening
/// therefore lives in the alpha, whose transparent end meets the mask's white edge - and stops short of it by the
/// host's own boundary band, because the host decodes that boundary as an ~8 px smear.
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
        var region = new MediaEditRegionOperation(25, 25, 50, 50, GrowMaskBy: 5, FeatherPixels: 9);

        using var mask = Image.Load<L8>(_engine.Build(region, 100, 100));

        Assert.Equal(255, mask[11, 11].PackedValue);    // grew 5px past the drawn corner, plus the feather
        Assert.Equal(0, mask[10, 10].PackedValue);
        Assert.Equal(255, mask[50, 50].PackedValue);    // the middle is still white
    }

    /// <summary>
    /// The alpha is what the operator's feather actually means: opaque over everything the region may change (the drawn
    /// rectangle grown by the grow value), then a linear fade to nothing. The fade stops SHORT of the mask's white edge
    /// by the host's own boundary band, because the host decodes that boundary as an ~8 px smear and ramping across it
    /// would blend the smear back in - which is the seam CASE-25's follow-up measured on a real outpaint.
    /// </summary>
    [Fact]
    public void TheAlphaIsOpaqueInsideTheRegionAndFadesToNothingBeforeTheMasksEdge()
    {
        // Drawn 25..75, so the host may change 20..80 and the mask is white over 4..96. A feather of 16 leaves a
        // ramp of 8 (feather - the host's 8 px band), so every step of the ramp is 255/8.
        var region = new MediaEditRegionOperation(25, 25, 50, 50, GrowMaskBy: 5, FeatherPixels: 16);

        using var alpha = Image.Load<L8>(_engine.BuildCompositeAlpha(region, 100, 100));

        Assert.Equal(255, alpha[50, 50].PackedValue);   // deep inside the region
        Assert.Equal(255, alpha[20, 20].PackedValue);   // the last fully opaque pixel
        Assert.Equal(223, alpha[19, 19].PackedValue);   // one pixel into the fade
        Assert.Equal(191, alpha[18, 18].PackedValue);
        Assert.Equal(159, alpha[17, 17].PackedValue);
        Assert.Equal(128, alpha[16, 16].PackedValue);
        Assert.Equal(96, alpha[15, 15].PackedValue);
        Assert.Equal(64, alpha[14, 14].PackedValue);
        Assert.Equal(32, alpha[13, 13].PackedValue);
        Assert.Equal(0, alpha[12, 12].PackedValue);     // the fade is over; the host's band starts here
        Assert.Equal(0, alpha[11, 11].PackedValue);     // inside the host's boundary band
        Assert.Equal(0, alpha[4, 4].PackedValue);       // the mask's white edge
        Assert.Equal(0, alpha[0, 0].PackedValue);       // and everything outside it stays the source
        Assert.Equal(0, alpha[99, 99].PackedValue);
    }

    /// <summary>
    /// The host's boundary band is the whole reason the ramp is shortened: the fade is over BEFORE the mask's white edge,
    /// so the smear the host leaves at that edge is never multiplied into the result.
    /// </summary>
    [Fact]
    public void TheAlphaIsZeroAcrossTheHostsBoundaryBandAtTheMasksEdge()
    {
        // Drawn 20..80 on a 200px frame, grown by 0, so the editable rectangle is 40..160 and the mask is white over
        // 16..184. A feather of 24 leaves a ramp of 16, so the fade ends at 160 + 16 - 1 = 175 and the white area keeps
        // 8 transparent pixels past it - one host latent block.
        var region = new MediaEditRegionOperation(20, 20, 60, 60, GrowMaskBy: 0, FeatherPixels: 24);

        using var alpha = Image.Load<L8>(_engine.BuildCompositeAlpha(region, 200, 200));
        using var mask = Image.Load<L8>(_engine.Build(region, 200, 200));

        Assert.Equal(255, mask[183, 100].PackedValue);  // the mask's last white pixel on the right
        Assert.Equal(0, mask[184, 100].PackedValue);

        Assert.Equal(255, alpha[159, 100].PackedValue); // the last fully opaque pixel (the editable rectangle ends at 160)
        Assert.Equal(239, alpha[160, 100].PackedValue); // one pixel into the fade
        Assert.Equal(16, alpha[174, 100].PackedValue);  // its last non-zero step
        for (var x = 175; x < 184; x++)
        {
            Assert.Equal(0, alpha[x, 100].PackedValue); // transparent across the host's band, though the mask is white
        }
    }

    /// <summary>
    /// A region flush with the frame edge still fades INWARD: the fade has nowhere to go past the edge of the frame, and
    /// inventing room for it would change pixels the operator never drew.
    /// </summary>
    [Fact]
    public void ARegionFlushWithTheFrameEdgeFadesInwardOnly()
    {
        var region = new MediaEditRegionOperation(0, 0, 50, 50, GrowMaskBy: 0, FeatherPixels: 16);

        using var alpha = Image.Load<L8>(_engine.BuildCompositeAlpha(region, 100, 100));

        Assert.Equal(255, alpha[0, 0].PackedValue);     // the drawn edge itself is still fully the render
        Assert.Equal(255, alpha[49, 49].PackedValue);
        Assert.Equal(223, alpha[50, 50].PackedValue);   // 255 * (1 - 1/8), fading into the untouched side
        Assert.Equal(191, alpha[51, 51].PackedValue);
        Assert.Equal(96, alpha[54, 54].PackedValue);
        Assert.Equal(0, alpha[58, 58].PackedValue);     // inside the host's boundary band
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
        var region = new MediaEditRegionOperation(10, 10, 0.1, 0.1, GrowMaskBy: 0, FeatherPixels: 16);

        var exception = Assert.Throws<InvalidOperationException>(() => _engine.Build(region, 32, 32));

        Assert.Contains("rounds away to nothing", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A feather that does not clear the host's own boundary band is refused, by name: the host rounds the mask it
    /// confines with to 0/1 and decodes its edge as a smear about one latent block wide, so a fade that starts inside
    /// that band leaves the edge visible as a seam (CASE-25). Refusing it here means the operator hears about it before
    /// a render is paid for, rather than seeing the seam in the picture.
    /// </summary>
    [Fact]
    public void AConfinedEditWithoutAFeatherIsRefusedByName()
    {
        var region = new MediaEditRegionOperation(25, 25, 50, 50, GrowMaskBy: 5, FeatherPixels: 0);

        var exception = Assert.Throws<InvalidOperationException>(() => _engine.Build(region, 100, 100));

        Assert.Contains("RegionFeatherPixels", exception.Message, StringComparison.Ordinal);
        Assert.Contains("seam", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFrameWithoutASizeIsRefused()
    {
        var region = new MediaEditRegionOperation(10, 10, 10, 10, GrowMaskBy: 0, FeatherPixels: 16);

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
