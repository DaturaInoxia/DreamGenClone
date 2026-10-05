using DreamGenClone.Web.Application.RolePlay.Evaluation.Gates;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B135-019 (P3): the sanitisation gate must reproduce <c>tools/consistency-scoring/scoring/sanitisation.py</c>.
/// These pin the skin-tone screen on solid, hand-computable images.
/// </summary>
public sealed class SanitisationGateTests
{
    [Fact]
    public void SolidSkinTone_IsNotSuspected()
    {
        // (180,140,100): R/G/B inside the broad bounds, R-G = 40 >= 15, and the PIL YCbCr (147,101,151) inside its bounds.
        var image = Solid(8, 8, 180, 140, 100);

        var result = SanitisationGate.Measure(image);

        Assert.False(result.SuspectedSanitised);
        Assert.Equal(64, result.SkinPixels);
        Assert.Equal(64, result.TotalPixels);
        Assert.Equal(1.0, result.SkinFraction);
    }

    [Fact]
    public void NonSkin_IsSuspected()
    {
        // Pure black: R = 0 is below the 95 floor, so no pixel passes the screen.
        var image = Solid(8, 8, 0, 0, 0);

        var result = SanitisationGate.Measure(image);

        Assert.True(result.SuspectedSanitised);
        Assert.Equal(0, result.SkinPixels);
        Assert.Equal(0.0, result.SkinFraction);
    }

    [Fact]
    public void RedGreenDifferenceAtFourteen_IsNotSkin()
    {
        // R - G = 14 is one below the 15 floor: channels are in range but the difference gate refuses it.
        var image = Solid(8, 8, 200, 186, 100);

        var result = SanitisationGate.Measure(image);

        Assert.Equal(0, result.SkinPixels);
        Assert.True(result.SuspectedSanitised);
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
}
