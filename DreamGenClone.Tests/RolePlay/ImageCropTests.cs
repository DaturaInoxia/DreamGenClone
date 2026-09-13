using DreamGenClone.Web.Application.RolePlay.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The deterministic crop core (B-121 Crop step): framing is arithmetic, so it is tested as
/// arithmetic — exact pixel windows — rather than by eyeballing output images.
/// </summary>
public sealed class ImageCropTests
{
    [Fact]
    public void SameAspectAsSource_KeepsTheWholeImage()
    {
        var rect = ImageCropCalculator.Compute(1920, 1080, new ImageCropSettings(1920.0 / 1080, 50, 50));

        Assert.Equal(new ImageCropRect(0, 0, 1920, 1080), rect);
    }

    [Fact]
    public void WiderSource_SquareTarget_TrimsSidesAndKeepsFullHeight()
    {
        var rect = ImageCropCalculator.Compute(40, 20, new ImageCropSettings(1.0, 50, 50));

        Assert.Equal(20, rect.Width);
        Assert.Equal(20, rect.Height);
        Assert.Equal(10, rect.X); // centred within the 20px of horizontal slack
        Assert.Equal(0, rect.Y);  // no vertical slack to distribute
    }

    [Fact]
    public void TallerSource_SquareTarget_TrimsTopAndBottom()
    {
        Assert.Equal(
            new ImageCropRect(0, 0, 20, 20),
            ImageCropCalculator.Compute(20, 40, new ImageCropSettings(1.0, 0, 50)));

        Assert.Equal(
            new ImageCropRect(0, 10, 20, 20),
            ImageCropCalculator.Compute(20, 40, new ImageCropSettings(1.0, 50, 50)));

        Assert.Equal(
            new ImageCropRect(0, 20, 20, 20),
            ImageCropCalculator.Compute(20, 40, new ImageCropSettings(1.0, 100, 50)));
    }

    [Fact]
    public void MoreHeadroom_MovesTheWindowDown_TrimmingTheSpaceAboveIt()
    {
        // A portrait 4:5 window inside a tall image: headroom 0 sits it at the top, 100 at the bottom,
        // which is the knob that keeps or trims space above the head.
        var none = ImageCropCalculator.Compute(100, 200, new ImageCropSettings(0.8, 0, 50));
        var all = ImageCropCalculator.Compute(100, 200, new ImageCropSettings(0.8, 100, 50));

        Assert.Equal(0, none.Y);
        Assert.Equal(200 - all.Height, all.Y);
        Assert.True(none.Y < all.Y);
        Assert.Equal(125, none.Height); // 100 / 0.8
    }

    [Fact]
    public void WindowNeverLeavesTheSource()
    {
        var rect = ImageCropCalculator.Compute(37, 53, new ImageCropSettings(1.3, 100, 100));

        Assert.InRange(rect.X, 0, 37 - rect.Width);
        Assert.InRange(rect.Y, 0, 53 - rect.Height);
        Assert.InRange(rect.X + rect.Width, 1, 37);
        Assert.InRange(rect.Y + rect.Height, 1, 53);
    }

    [Fact]
    public void AspectIsHonouredWithinARoundingPixel()
    {
        var rect = ImageCropCalculator.Compute(1000, 1000, new ImageCropSettings(16.0 / 9, 50, 50));

        Assert.InRange((double)rect.Width / rect.Height, (16.0 / 9) - 0.02, (16.0 / 9) + 0.02);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveAspect_FailsFast(double aspect)
        => Assert.Throws<InvalidOperationException>(() =>
            ImageCropCalculator.Compute(100, 100, new ImageCropSettings(aspect, 50, 50)));

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void HeadroomOutsideZeroToOneHundred_FailsFast(double headroom)
        => Assert.Throws<InvalidOperationException>(() =>
            ImageCropCalculator.Compute(100, 100, new ImageCropSettings(1, headroom, 50)));

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void HorizontalOffsetOutsideZeroToOneHundred_FailsFast(double offset)
        => Assert.Throws<InvalidOperationException>(() =>
            ImageCropCalculator.Compute(100, 100, new ImageCropSettings(1, 50, offset)));

    [Fact]
    public void NonPositiveSourceSize_FailsFast()
        => Assert.Throws<InvalidOperationException>(() =>
            ImageCropCalculator.Compute(0, 100, new ImageCropSettings(1, 50, 50)));

    [Fact]
    public async Task Engine_WritesExactlyTheComputedWindow()
    {
        var engine = new ImageCropEngine();
        await using var source = await CreatePngAsync(40, 20);

        var bytes = await engine.CropAsync(
            new MediaEditCropOperation(ImageCropMode.Framing, new ImageCropSettings(1.0, 50, 50), null), source);

        using var result = Image.Load<Rgba32>(bytes);
        Assert.Equal(20, result.Width);
        Assert.Equal(20, result.Height);
    }

    [Fact]
    public async Task Engine_IsDeterministic()
    {
        var engine = new ImageCropEngine();
        var operation = new MediaEditCropOperation(
            ImageCropMode.Framing, new ImageCropSettings(0.8, 25, 60), null);

        await using var first = await CreatePngAsync(64, 48);
        var a = await engine.CropAsync(operation, first);
        await using var second = await CreatePngAsync(64, 48);
        var b = await engine.CropAsync(operation, second);

        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Engine_HeadAware_UsesTheMeasuredHeadInsteadOfTheSlack()
    {
        var engine = new ImageCropEngine();
        await using var source = await CreatePngAsync(600, 1000);

        // 40% of the measured head height (400px) is 160px above the hairline at y=300 -> top at 140.
        var bytes = await engine.CropAsync(
            new MediaEditCropOperation(
                ImageCropMode.HeadAware,
                new ImageCropSettings(0.8, 40, 50),
                new ImageCropHeadMeasurement(300, 700)),
            source);

        using var result = Image.Load<Rgba32>(bytes);
        var expected = ImageCropCalculator.ComputeHeadAware(
            600, 1000, new ImageCropSettings(0.8, 40, 50), new ImageCropHeadMeasurement(300, 700));
        Assert.Equal(expected.Width, result.Width);
        Assert.Equal(expected.Height, result.Height);
    }

    [Fact]
    public async Task Engine_HeadAwareWithoutAMeasurement_FailsFast()
    {
        var engine = new ImageCropEngine();
        await using var source = await CreatePngAsync(40, 20);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.CropAsync(
            new MediaEditCropOperation(ImageCropMode.HeadAware, new ImageCropSettings(1.0, 50, 50), null), source));
    }

    [Fact]
    public void FramingModeThatCarriesAMeasurement_FailsFast()
    {
        var operation = new MediaEditCropOperation(
            ImageCropMode.Framing, new ImageCropSettings(1.0, 50, 50), new ImageCropHeadMeasurement(100, 400));

        Assert.Throws<InvalidOperationException>(operation.Validate);
    }

    [Fact]
    public void Operation_UnnamedKind_FailsFast()
    {
        var operation = new MediaEditOperation(MediaEditOperationKind.Unknown, null);

        Assert.Throws<InvalidOperationException>(operation.Validate);
    }

    [Fact]
    public void Operation_EditWithCropParameters_FailsFast()
    {
        var operation = new MediaEditOperation(
            MediaEditOperationKind.Edit,
            new MediaEditCropOperation(ImageCropMode.Framing, new ImageCropSettings(1.0, 50, 50), null));

        Assert.Throws<InvalidOperationException>(operation.Validate);
    }

    [Fact]
    public void Operation_CropWithoutParameters_FailsFast()
    {
        var operation = new MediaEditOperation(MediaEditOperationKind.Crop, null);

        Assert.Throws<InvalidOperationException>(operation.Validate);
    }

    [Fact]
    public void Operation_UnknownCropMode_FailsFast()
    {
        var operation = new MediaEditCropOperation(
            (ImageCropMode)99, new ImageCropSettings(1.0, 50, 50), null);

        Assert.Throws<InvalidOperationException>(operation.Validate);
    }

    [Fact]
    public void Operation_HeadFramedWithoutAFaceBox_FailsFast()
    {
        var operation = new MediaEditCropOperation(
            ImageCropMode.HeadFramed,
            new ImageCropSettings(1.0, 10, 50),
            new ImageCropHeadMeasurement(300, 700));

        var error = Assert.Throws<InvalidOperationException>(operation.Validate);
        Assert.Contains("face box", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Operation_ManualWithoutARect_FailsFast()
    {
        var operation = new MediaEditCropOperation(
            ImageCropMode.Manual, new ImageCropSettings(1.0, 10, 50), null);

        Assert.Throws<InvalidOperationException>(operation.Validate);
    }

    [Fact]
    public void Operation_DerivedModeCarryingARect_FailsFast()
    {
        var operation = new MediaEditCropOperation(
            ImageCropMode.Framing, new ImageCropSettings(1.0, 10, 50), null, new ImageCropRect(1, 1, 10, 10));

        Assert.Throws<InvalidOperationException>(operation.Validate);
    }

    [Fact]
    public void HeadFramed_WrapsTheMeasuredHeadWithMarginAndFitsTheAspect()
    {
        // 1000x1000 source; head 300..700 (400px tall), face 400..600 wide; 10% margin = 40px.
        var head = new ImageCropHeadMeasurement(300, 700, new ImageCropFaceBox(400, 300, 200, 400));

        var rect = ImageCropCalculator.ComputeHeadFramed(1000, 1000, new ImageCropSettings(1.0, 10, 50), head);

        Assert.Equal(new ImageCropRect(260, 260, 480, 480), rect);
        // The whole head is inside the window, which is the point of the mode.
        Assert.True(rect.X <= 400 && rect.X + rect.Width >= 600);
        Assert.True(rect.Y <= 300 && rect.Y + rect.Height >= 700);
    }

    [Fact]
    public void HeadFramed_HeadAtTheEdge_KeepsTheWindowInsideTheSource()
    {
        var head = new ImageCropHeadMeasurement(10, 110, new ImageCropFaceBox(0, 10, 100, 100));

        var rect = ImageCropCalculator.ComputeHeadFramed(1000, 1000, new ImageCropSettings(1.0, 10, 50), head);

        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.True(rect.X + rect.Width <= 1000 && rect.Y + rect.Height <= 1000);
    }

    [Fact]
    public void HeadFramed_WithoutAMeasurement_FailsFast()
    {
        Assert.Throws<InvalidOperationException>(() => ImageCropCalculator.ComputeHeadFramed(
            1000, 1000, new ImageCropSettings(1.0, 10, 50), null));
    }

    [Fact]
    public void Manual_UsesTheDraggedWindowExactly()
    {
        var rect = ImageCropCalculator.ComputeManual(1000, 1000, new ImageCropRect(10, 20, 100, 50));

        Assert.Equal(new ImageCropRect(10, 20, 100, 50), rect);
    }

    [Fact]
    public void Manual_OutsideTheSource_FailsFast()
    {
        Assert.Throws<InvalidOperationException>(() => ImageCropCalculator.ComputeManual(
            1000, 1000, new ImageCropRect(950, 0, 100, 100)));
    }

    [Fact]
    public void Translate_KeepsTheSizeAndStaysInsideTheSource()
    {
        var moved = ImageCropCalculator.Translate(new ImageCropRect(10, 10, 100, 100), 500, -500, 200, 200);

        Assert.Equal(new ImageCropRect(100, 0, 100, 100), moved);
    }

    [Fact]
    public void Resize_AnchorsTheOppositeCornerAndHonoursTheMinimum()
    {
        var grown = ImageCropCalculator.Resize(
            new ImageCropRect(100, 100, 100, 100), "br", 50, 50, 500, 500, 48);
        Assert.Equal(new ImageCropRect(100, 100, 150, 150), grown);

        var collapsed = ImageCropCalculator.Resize(
            new ImageCropRect(100, 100, 100, 100), "br", -500, -500, 500, 500, 48);
        Assert.Equal(48, collapsed.Width);
        Assert.Equal(48, collapsed.Height);
        Assert.Equal(100, collapsed.X);
        Assert.Equal(100, collapsed.Y);

        var fromTopLeft = ImageCropCalculator.Resize(
            new ImageCropRect(100, 100, 100, 100), "tl", 10, 10, 500, 500, 48);
        Assert.Equal(new ImageCropRect(110, 110, 90, 90), fromTopLeft);
    }

    [Fact]
    public void Resize_UnknownHandle_FailsFast()
    {
        Assert.Throws<InvalidOperationException>(() => ImageCropCalculator.Resize(
            new ImageCropRect(10, 10, 50, 50), "middle", 5, 5, 200, 200, 48));
    }

    [Fact]
    public void HeadAware_PlacesTheTopEdgeHeadroomAboveTheHairline()
    {
        // 600x1000 source, square window -> 600x600, leaving 400px of vertical slack.
        // Head measured hairline y=300, chin y=700 -> 400px tall; 50% headroom = 200px above the hairline.
        var rect = ImageCropCalculator.ComputeHeadAware(
            600, 1000, new ImageCropSettings(1.0, 50, 50), new ImageCropHeadMeasurement(300, 700));

        Assert.Equal(100, rect.Y); // 300 - 200
        Assert.Equal(600, rect.Width);
        Assert.Equal(600, rect.Height);
    }

    [Fact]
    public void HeadAware_IsClampedToTheImageTop()
    {
        // 100% headroom would start 100px above the image, so the frame stops at the top edge.
        var rect = ImageCropCalculator.ComputeHeadAware(
            600, 1000, new ImageCropSettings(1.0, 100, 50), new ImageCropHeadMeasurement(300, 700));

        Assert.Equal(0, rect.Y);
    }

    [Fact]
    public void HeadAware_KeepsExactlyTheFramingWindowSize()
    {
        // Switching mode must only move the frame, never resize it.
        var framing = ImageCropCalculator.Compute(600, 1000, new ImageCropSettings(0.8, 50, 50));
        var headAware = ImageCropCalculator.ComputeHeadAware(
            600, 1000, new ImageCropSettings(0.8, 40, 50), new ImageCropHeadMeasurement(300, 700));

        Assert.Equal(framing.X, headAware.X);
        Assert.Equal(framing.Width, headAware.Width);
        Assert.Equal(framing.Height, headAware.Height);
    }

    [Fact]
    public void HeadAware_WithoutAMeasurement_FailsFast()
        => Assert.Throws<InvalidOperationException>(() =>
            ImageCropCalculator.ComputeHeadAware(600, 1000, new ImageCropSettings(1.0, 50, 50), null));

    [Fact]
    public void HeadAware_WithAnUnusableHeadHeight_FailsFast()
        => Assert.Throws<InvalidOperationException>(() =>
            ImageCropCalculator.ComputeHeadAware(
                600, 1000, new ImageCropSettings(1.0, 50, 50), new ImageCropHeadMeasurement(700, 700)));

    /// <summary>A gradient, so cropping the wrong window changes the bytes rather than just the size.</summary>
    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = new Rgba32(
                    (byte)(x * 255 / Math.Max(1, width - 1)),
                    (byte)(y * 255 / Math.Max(1, height - 1)),
                    64);
            }
        }

        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
