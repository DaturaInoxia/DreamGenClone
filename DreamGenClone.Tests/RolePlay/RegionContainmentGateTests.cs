using DreamGenClone.Web.Application.RolePlay.Evaluation.Gates;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B135-017 (P3): the region-containment gate must reproduce <c>tools/qwen-region-proof/measure_region.py</c>
/// arithmetic exactly. These pin the port on hand-computable synthetic images: a contained case, a
/// non-contained case, and the per-channel vs per-pixel-max distinction the tool makes.
/// </summary>
public sealed class RegionContainmentGateTests
{
    [Fact]
    public void Contained_MaskedPreservesSourceOutsideAndChangesInside_ReturnsContained()
    {
        var source = Solid(10, 10, 100, 100, 100);
        var masked = Solid(10, 10, 100, 100, 100);
        FillRect(masked, 2, 2, 8, 8, 255, 255, 255);
        var control = Solid(10, 10, 255, 255, 255);

        var result = RegionContainmentGate.Measure(source, masked, control, xPct: 20, yPct: 20, widthPct: 60, heightPct: 60, band: 0);

        Assert.True(result.Contained);
        Assert.True(result.OutsidePreservedByMask);
        Assert.True(result.InsideChanged);

        // Outside, the masked run sits on the source (delta 0); the control drifts 155.
        Assert.Equal(0.0, result.OutsideSourceVsMasked.MeanAbsDiff);
        Assert.Equal(0.0, result.OutsideSourceVsMasked.PctPixelsChanged);
        Assert.Equal(155.0, result.OutsideSourceVsControl.MeanAbsDiff);

        // Inside, the masked run changed (delta 155).
        Assert.Equal(155.0, result.InsideSourceVsMasked.MeanAbsDiff);
        Assert.Equal(100.0, result.InsideSourceVsMasked.PctPixelsChanged);

        // Rectangle derivation: 20% of 10 = 2; +60% = 8. No band, no blur.
        Assert.Equal(2, result.X0);
        Assert.Equal(2, result.Y0);
        Assert.Equal(8, result.X1);
        Assert.Equal(8, result.Y1);
        Assert.Equal(0, result.MarginPixels);

        // Outside diff is zero, so the ratio is undefined (mirrors the tool's null).
        Assert.Null(result.OutsideRatioControlOverMasked);
    }

    [Fact]
    public void NotContained_MaskedDriftsLikeTheControl_ReturnsNotContained()
    {
        var source = Solid(10, 10, 100, 100, 100);
        var masked = Solid(10, 10, 255, 255, 255);   // same as control: no containment at all
        var control = Solid(10, 10, 255, 255, 255);

        var result = RegionContainmentGate.Measure(source, masked, control, xPct: 20, yPct: 20, widthPct: 60, heightPct: 60, band: 0);

        Assert.False(result.Contained);
        Assert.False(result.OutsidePreservedByMask);

        // outMasked * 2 (310) is not < outControl (155), so the ratio is 1.0.
        Assert.Equal(1.0, result.OutsideRatioControlOverMasked);
    }

    [Fact]
    public void Stats_SeparatePerChannelMeanFromPerPixelMaxChannel()
    {
        // A pure red change: per-channel mean is 255/3, the per-pixel max is 255.
        var source = Solid(10, 10, 0, 0, 0);
        var masked = Solid(10, 10, 255, 0, 0);
        var control = Solid(10, 10, 0, 0, 0);

        var result = RegionContainmentGate.Measure(source, masked, control, xPct: 20, yPct: 20, widthPct: 60, heightPct: 60, band: 0);

        Assert.Equal(85.0, result.InsideSourceVsMasked.MeanAbsDiff);
        Assert.Equal(255.0, result.InsideSourceVsMasked.MeanMaxChannelDiff);
        Assert.Equal(100.0, result.InsideSourceVsMasked.PctPixelsChanged);
    }

    [Fact]
    public void MismatchedSizes_AreRefused()
    {
        var source = Solid(10, 10, 0, 0, 0);
        var masked = Solid(8, 8, 0, 0, 0);
        var control = Solid(10, 10, 0, 0, 0);

        Assert.Throws<InvalidOperationException>(() =>
            RegionContainmentGate.Measure(source, masked, control, 20, 20, 60, 60));
    }

    private static Image<Rgb24> Solid(int width, int height, byte r, byte g, byte b)
    {
        var image = new Image<Rgb24>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = new Rgb24(r, g, b);
            }
        }
        return image;
    }

    private static void FillRect(Image<Rgb24> image, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                image[x, y] = new Rgb24(r, g, b);
            }
        }
    }
}
