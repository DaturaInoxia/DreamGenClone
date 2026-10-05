using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Web.Application.RolePlay.Evaluation.Gates;

/// <summary>
/// B135-017 (P3): the region-containment gate — a pixel-exact C# port of
/// <c>tools/qwen-region-proof/measure_region.py</c>.
///
/// <para>
/// Three images are compared: the SOURCE that was edited, the MASKED run, and the CONTROL run (the
/// identical instruction and seed emitted without the mask). The masked run is contained when,
/// OUTSIDE the masked rectangle, it still sits on the source, because at denoise 1.0 the sampler
/// blends the original latent back there; the control has no such constraint. The decisive number is
/// therefore <c>mean|diff| source-vs-masked, outside</c> &lt;&lt; <c>mean|diff| source-vs-control, outside</c>.
/// A band (margin) around the rectangle is excluded from both sets so the soft mask edge cannot
/// flatter the comparison. This is a pixel measurement, not a semantic judgement: it says the edit was
/// contained, not that the contained edit was good.
/// </para>
///
/// <para>
/// The port reproduces the Python tool's arithmetic exactly: the same rectangle derivation (percent →
/// pixels, truncated), the same margin formula, the same per-channel mean and per-pixel max-channel
/// statistics, the same changed threshold (16/255), and the same verdict
/// (<c>outMasked * 2 &lt; outControl AND inMasked &gt; outMasked</c>).
/// </para>
/// </summary>
public static class RegionContainmentGate
{
    /// <summary>A pixel counts as changed beyond this per-channel delta (16/255, per the canonical tool).</summary>
    public const double ChangedThreshold = 16.0;

    /// <summary>Pixels of margin excluded on BOTH sides of the rectangle edge (per the canonical tool).</summary>
    public const int DefaultBand = 24;

    public static RegionContainmentResult Measure(
        Image<Rgb24> source,
        Image<Rgb24> masked,
        Image<Rgb24> control,
        double xPct,
        double yPct,
        double widthPct,
        double heightPct,
        double blurPct = 0.0,
        int band = DefaultBand)
    {
        if (source.Width != masked.Width || source.Height != masked.Height
            || source.Width != control.Width || source.Height != control.Height)
            throw new InvalidOperationException(
                "The source, masked and control images must share one size; compare renders of one source.");

        var width = source.Width;
        var height = source.Height;

        var sourceFlat = ToRgbFloat(source);
        var maskedFlat = ToRgbFloat(masked);
        var controlFlat = ToRgbFloat(control);

        // Same derivation the Python tool performs: percent of the frame, truncated to int.
        var x0 = (int)(xPct / 100.0 * width);
        var y0 = (int)(yPct / 100.0 * height);
        var x1 = x0 + (int)(widthPct / 100.0 * width);
        var y1 = y0 + (int)(heightPct / 100.0 * height);

        // The blur radius is given in percent units, so it is converted against the smaller edge.
        var margin = band + (int)(blurPct / 100.0 * Math.Min(width, height));

        var inside = BuildInsideMask(width, height, x0, y0, x1, y1, margin);
        var outside = BuildOutsideMask(width, height, x0, y0, x1, y1, margin);

        var insideSourceVsMasked = DiffStats(sourceFlat, maskedFlat, inside, width, height);
        var insideMaskedVsControl = DiffStats(maskedFlat, controlFlat, inside, width, height);
        var insideSourceVsControl = DiffStats(sourceFlat, controlFlat, inside, width, height);
        var outsideSourceVsMasked = DiffStats(sourceFlat, maskedFlat, outside, width, height);
        var outsideMaskedVsControl = DiffStats(maskedFlat, controlFlat, outside, width, height);
        var outsideSourceVsControl = DiffStats(sourceFlat, controlFlat, outside, width, height);

        var outMasked = outsideSourceVsMasked.MeanAbsDiff;
        var outControl = outsideSourceVsControl.MeanAbsDiff;
        var inMasked = insideSourceVsMasked.MeanAbsDiff;

        var outsidePreservedByMask = outMasked * 2.0 < outControl;
        var insideChanged = inMasked > outMasked;
        var contained = outsidePreservedByMask && insideChanged;

        return new RegionContainmentResult(
            Width: width,
            Height: height,
            XPct: xPct,
            YPct: yPct,
            WidthPct: widthPct,
            HeightPct: heightPct,
            BlurPct: blurPct,
            X0: x0,
            Y0: y0,
            X1: x1,
            Y1: y1,
            MarginPixels: margin,
            InsideSourceVsMasked: insideSourceVsMasked,
            InsideMaskedVsControl: insideMaskedVsControl,
            InsideSourceVsControl: insideSourceVsControl,
            OutsideSourceVsMasked: outsideSourceVsMasked,
            OutsideMaskedVsControl: outsideMaskedVsControl,
            OutsideSourceVsControl: outsideSourceVsControl,
            Contained: contained,
            OutsidePreservedByMask: outsidePreservedByMask,
            InsideChanged: insideChanged,
            OutsideRatioControlOverMasked: outMasked > 0 ? Math.Round(outControl / outMasked, 2) : null);
    }

    private static float[] ToRgbFloat(Image<Rgb24> image)
    {
        var flat = new float[image.Width * image.Height * 3];
        var i = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                flat[i++] = pixel.R;
                flat[i++] = pixel.G;
                flat[i++] = pixel.B;
            }
        }
        return flat;
    }

    private static bool[,] BuildInsideMask(int width, int height, int x0, int y0, int x1, int y1, int margin)
    {
        var mask = new bool[height, width];
        for (var y = y0 + margin; y < y1 - margin; y++)
        {
            for (var x = x0 + margin; x < x1 - margin; x++)
            {
                mask[y, x] = true;
            }
        }
        return mask;
    }

    private static bool[,] BuildOutsideMask(int width, int height, int x0, int y0, int x1, int y1, int margin)
    {
        var mask = new bool[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                mask[y, x] = true;
            }
        }

        var bandY0 = Math.Max(0, y0 - margin);
        var bandY1 = Math.Min(height, y1 + margin);
        var bandX0 = Math.Max(0, x0 - margin);
        var bandX1 = Math.Min(width, x1 + margin);
        for (var y = bandY0; y < bandY1; y++)
        {
            for (var x = bandX0; x < bandX1; x++)
            {
                mask[y, x] = false;
            }
        }
        return mask;
    }

    private static RegionDiffStats DiffStats(float[] a, float[] b, bool[,] region, int width, int height)
    {
        double channelSum = 0.0;
        double maxChannelSum = 0.0;
        long pixels = 0;
        long changed = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!region[y, x])
                    continue;

                var index = (y * width + x) * 3;
                var dr = Math.Abs(a[index] - b[index]);
                var dg = Math.Abs(a[index + 1] - b[index + 1]);
                var db = Math.Abs(a[index + 2] - b[index + 2]);
                var maxChannel = Math.Max(dr, Math.Max(dg, db));

                channelSum += dr + dg + db;
                maxChannelSum += maxChannel;
                pixels++;
                if (maxChannel > ChangedThreshold)
                    changed++;
            }
        }

        if (pixels == 0)
            throw new InvalidOperationException("The selected region is empty; check the rectangle and band.");

        return new RegionDiffStats(
            Pixels: pixels,
            MeanAbsDiff: Math.Round(channelSum / (pixels * 3), 3),
            MeanMaxChannelDiff: Math.Round(maxChannelSum / pixels, 3),
            PctPixelsChanged: Math.Round(changed / (double)pixels * 100.0, 3));
    }
}

/// <summary>Per-region pixel statistics, mirroring the canonical tool's <c>diff_stats</c>.</summary>
public sealed record RegionDiffStats(
    long Pixels,
    double MeanAbsDiff,
    double MeanMaxChannelDiff,
    double PctPixelsChanged);

/// <summary>The full containment report, mirroring the canonical tool's JSON (minus the file paths).</summary>
public sealed record RegionContainmentResult(
    int Width,
    int Height,
    double XPct,
    double YPct,
    double WidthPct,
    double HeightPct,
    double BlurPct,
    int X0,
    int Y0,
    int X1,
    int Y1,
    int MarginPixels,
    RegionDiffStats InsideSourceVsMasked,
    RegionDiffStats InsideMaskedVsControl,
    RegionDiffStats InsideSourceVsControl,
    RegionDiffStats OutsideSourceVsMasked,
    RegionDiffStats OutsideMaskedVsControl,
    RegionDiffStats OutsideSourceVsControl,
    bool Contained,
    bool OutsidePreservedByMask,
    bool InsideChanged,
    double? OutsideRatioControlOverMasked);
