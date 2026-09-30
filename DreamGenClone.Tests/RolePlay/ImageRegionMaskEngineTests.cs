using DreamGenClone.Web.Application.RolePlay.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The mask is what makes an edit CONFINED (CASE-21): white where the instruction may change the picture, black where
/// the source must survive. These tests pin the pixel geometry, because a mask that covers the wrong rectangle is not a
/// visibly broken render - it is a correct-looking render of the wrong area.
/// </summary>
public sealed class ImageRegionMaskEngineTests
{
    private readonly ImageRegionMaskEngine _engine = new();

    [Fact]
    public void TheRegionIsWhiteAndEverythingElseIsBlack()
    {
        // A quarter of a 100x100 frame, so percent and pixels are the same number.
        var region = new MediaEditRegionOperation(25, 25, 50, 50, GrowMaskBy: 0, FeatherPixels: 0);

        var mask = _engine.Build(region, 100, 100);

        using var image = Image.Load<L8>(mask);
        Assert.Equal(100, image.Width);
        Assert.Equal(100, image.Height);
        Assert.Equal(255, image[50, 50].PackedValue);   // inside the region
        Assert.Equal(255, image[25, 25].PackedValue);   // its top-left pixel
        Assert.Equal(0, image[24, 24].PackedValue);     // one pixel outside, diagonally
        Assert.Equal(0, image[0, 0].PackedValue);       // the frame corner
        Assert.Equal(0, image[99, 99].PackedValue);     // the far corner
        Assert.Equal(255, image[74, 74].PackedValue);   // the last white pixel
        Assert.Equal(0, image[75, 75].PackedValue);     // one past it
    }

    /// <summary>A region flush with two edges is the panorama/outpaint shape and must be honoured as drawn.</summary>
    [Fact]
    public void ARegionFlushWithTwoEdgesIsWhiteFromTheEdge()
    {
        var region = new MediaEditRegionOperation(0, 0, 50, 20, GrowMaskBy: 0, FeatherPixels: 0);

        using var image = Image.Load<L8>(_engine.Build(region, 200, 100));

        Assert.Equal(255, image[0, 0].PackedValue);
        Assert.Equal(255, image[99, 19].PackedValue);
        Assert.Equal(0, image[100, 19].PackedValue);
        Assert.Equal(0, image[99, 20].PackedValue);
    }

    /// <summary>
    /// A region that rounds away to nothing would emit an all-black mask, which pins the WHOLE frame and returns the
    /// source unchanged. Refused instead: a render that looks like a success and did nothing is the worst outcome.
    /// </summary>
    [Fact]
    public void ARegionTooSmallForTheFrameIsRefusedRatherThanEmittedAsAnAllBlackMask()
    {
        var region = new MediaEditRegionOperation(10, 10, 0.1, 0.1, GrowMaskBy: 0, FeatherPixels: 0);

        var exception = Assert.Throws<InvalidOperationException>(() => _engine.Build(region, 32, 32));

        Assert.Contains("rounds away to nothing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFrameWithoutASizeIsRefused()
    {
        var region = new MediaEditRegionOperation(10, 10, 10, 10, GrowMaskBy: 0, FeatherPixels: 0);

        Assert.Throws<InvalidOperationException>(() => _engine.Build(region, 0, 100));
    }
}
