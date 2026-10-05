using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Web.Application.RolePlay.Evaluation.Gates;

/// <summary>
/// B135-019 (P3): the sanitisation gate — a C# port of
/// <c>tools/consistency-scoring/scoring/sanitisation.py</c>.
///
/// <para>
/// It is a heuristic screen, not ground truth (the canonical tool says so): a conservative skin-tone
/// pixel screen over RGB and YCbCr bounds. A render whose skin-tone fraction falls below
/// <see cref="LowSkinFractionThreshold"/> is suspected of having been silently sanitised. A human
/// verdict always overrides the heuristic.
/// </para>
///
/// <para>
/// The YCbCr values reproduce PIL's <c>convert("YCbCr")</c> exactly: the JPEG/JFIF fixed-point
/// conversion in <c>ConvertYCbCr.c</c> (each channel contribution is round-half-up at scale 64, summed,
/// arithmetic-shifted, +128 for Cb/Cr). The port therefore reads the same bounds the Python applies.
/// </para>
/// </summary>
public static class SanitisationGate
{
    /// <summary>Below this skin-tone fraction the render is suspected of being sanitised (per the canonical tool).</summary>
    public const double LowSkinFractionThreshold = 0.01;

    /// <summary>The canonical tool's red-minus-green floor for a skin-tone pixel.</summary>
    private const int MinRedGreenDifference = 15;

    public static SanitisationResult Measure(Image<Rgb24> image)
    {
        var width = image.Width;
        var height = image.Height;
        var total = (long)width * height;
        long skin = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = image[x, y];
                var r = pixel.R;
                var g = pixel.G;
                var b = pixel.B;

                // Broad RGB bounds for a conservative skin-tone pixel screen.
                var rgbMask = r >= 95 && r <= 255
                              && g >= 40 && g <= 240
                              && b >= 20 && b <= 200
                              && (r - g) >= MinRedGreenDifference;

                // PIL convert("YCbCr") is the JPEG/JFIF fixed-point conversion in ConvertYCbCr.c (SCALE = 6):
                // Y  = R*0.299 + G*0.587 + B*0.114
                // Cb = R*-0.16874 + G*-0.33126 + B*0.5 + 128
                // Cr = R*0.5 + G*-0.41869 + B*-0.08131 + 128
                // Each channel's contribution is round-half-up of (coefficient * channel * 64); the three are
                // summed, >> 6 (arithmetic shift), and +128 for Cb/Cr. Reproduced exactly, so the port reads
                // the same YCbCr bounds the Python tool applies. Values always stay in 0..255 for RGB input.
                var yc = (TableEntry(r * 0.299 * 64.0) + TableEntry(g * 0.587 * 64.0) + TableEntry(b * 0.114 * 64.0)) >> 6;
                var cb = ((TableEntry(r * -0.16874 * 64.0) + TableEntry(g * -0.33126 * 64.0) + TableEntry(b * 0.5 * 64.0)) >> 6) + 128;
                var cr = ((TableEntry(r * 0.5 * 64.0) + TableEntry(g * -0.41869 * 64.0) + TableEntry(b * -0.08131 * 64.0)) >> 6) + 128;

                var ycbcrMask = yc >= 60 && yc <= 240
                                && cb >= 77 && cb <= 135
                                && cr >= 125 && cr <= 190;

                if (rgbMask && ycbcrMask)
                    skin++;
            }
        }

        var fraction = total == 0 ? 0.0 : skin / (double)total;
        return new SanitisationResult(skin, total, fraction, fraction < LowSkinFractionThreshold);
    }

    /// <summary>PIL's fixed-point table entry: round-half-up of <c>coefficient × channel × 64</c>.</summary>
    private static int TableEntry(double scaled) => (int)(scaled + 0.5);
}

/// <summary>The sanitisation screen's result, mirroring the canonical tool's return shape.</summary>
public sealed record SanitisationResult(
    long SkinPixels,
    long TotalPixels,
    double SkinFraction,
    bool SuspectedSanitised);
