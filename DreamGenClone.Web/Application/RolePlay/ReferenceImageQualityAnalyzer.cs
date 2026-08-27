using DreamGenClone.Domain.RolePlay;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Automatic quality assessment for reference images. Non-blocking — it never gates approval or
/// rendering; it only informs the curator. Combines metadata (face-region resolution, file density,
/// aspect ratio) with a real pixel-level sharpness metric (variance of the Laplacian on a downscaled
/// grayscale image) to flag low-resolution, over-compressed, and blurry references.
/// </summary>
public interface IReferenceImageQualityAnalyzer
{
    (SceneImageReferenceQuality Rating, string Notes) Analyze(
        Stream? imageStream, int width, int height, long byteLength);
}

/// <summary>
/// Metadata + sharpness ranking. A small face downscales poorly through IP-Adapter's 224px encoder,
/// so resolution is the primary signal; blur (variance of Laplacian) catches soft/fuzzy references
/// that metadata alone cannot see. Thresholds are tunable constants.
/// </summary>
public sealed class ReferenceImageQualityAnalyzer : IReferenceImageQualityAnalyzer
{
    private const int GoodMinDim = 768;
    private const int OkMinDim = 384;
    private const double LowDensityPerPixel = 0.08;
    private const double OkDensityPerPixel = 0.15;
    private const double MaxSaneAspectRatio = 2.4;
    private const double BlurrySharpness = 60;
    private const double OkSharpness = 250;

    public (SceneImageReferenceQuality Rating, string Notes) Analyze(
        Stream? imageStream, int width, int height, long byteLength)
    {
        if (width <= 0 || height <= 0)
        {
            return (SceneImageReferenceQuality.NotGood, "Missing dimensions — cannot assess.");
        }

        var reasons = new List<string>();
        int minDim = Math.Min(width, height);
        int maxDim = Math.Max(width, height);
        double density = (double)byteLength / ((long)width * height);
        double aspect = (double)maxDim / Math.Max(1, minDim);

        var rating = minDim switch
        {
            >= GoodMinDim => SceneImageReferenceQuality.Good,
            >= OkMinDim => SceneImageReferenceQuality.Ok,
            _ => SceneImageReferenceQuality.NotGood
        };

        reasons.Add(minDim switch
        {
            >= GoodMinDim => $"{width}×{height} resolution.",
            >= OkMinDim => $"Moderate resolution {width}×{height} (≥{GoodMinDim}px preferred).",
            _ => $"Low resolution {width}×{height} (aim ≥{GoodMinDim}px on the face)."
        });

        if (density < LowDensityPerPixel)
        {
            rating = SceneImageReferenceQuality.NotGood;
            reasons.Add($"Heavily compressed (~{density:0.00} B/px) — likely lacks detail.");
        }
        else if (density < OkDensityPerPixel)
        {
            if (rating == SceneImageReferenceQuality.Good) rating = SceneImageReferenceQuality.Ok;
            reasons.Add($"Somewhat compressed (~{density:0.00} B/px).");
        }

        if (aspect > MaxSaneAspectRatio)
        {
            if (rating == SceneImageReferenceQuality.Good) rating = SceneImageReferenceQuality.Ok;
            reasons.Add($"Extreme aspect ratio {width}×{height} (very wide/tall).");
        }

        double? sharpness = ComputeSharpness(imageStream);
        if (sharpness is not null)
        {
            if (sharpness < BlurrySharpness)
            {
                rating = SceneImageReferenceQuality.NotGood;
                reasons.Add($"Blurry (sharpness {sharpness:0}).");
            }
            else if (sharpness < OkSharpness)
            {
                if (rating == SceneImageReferenceQuality.Good) rating = SceneImageReferenceQuality.Ok;
                reasons.Add($"Somewhat soft (sharpness {sharpness:0}).");
            }
            else
            {
                reasons.Add($"Sharp (sharpness {sharpness:0}).");
            }
        }

        if (rating == SceneImageReferenceQuality.Good)
        {
            reasons.Add("Looks good for conditioning.");
        }

        return (rating, string.Join(" ", reasons));
    }

    /// <summary>Variance of the Laplacian over a 256px-wide grayscale image. Higher = sharper.</summary>
    private static double? ComputeSharpness(Stream? stream)
    {
        if (stream is null)
        {
            return null;
        }

        try
        {
            using var image = Image.Load<Rgba32>(stream);
            image.Mutate(x => x.Resize(256, 0, KnownResamplers.Lanczos3));
            image.Mutate(x => x.Grayscale());

            double sum = 0;
            double sumSq = 0;
            long count = 0;
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 1; y < accessor.Height - 1; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    var up = accessor.GetRowSpan(y - 1);
                    var down = accessor.GetRowSpan(y + 1);
                    for (int x = 1; x < row.Length - 1; x++)
                    {
                        var laplacian = -4 * row[x].R
                                        + row[x - 1].R + row[x + 1].R
                                        + up[x].R + down[x].R;
                        sum += laplacian;
                        sumSq += (double)laplacian * laplacian;
                        count++;
                    }
                }
            });

            if (count == 0)
            {
                return null;
            }

            double mean = sum / count;
            return sumSq / count - mean * mean;
        }
        catch
        {
            // Unreadable/unsupported image — fall back to metadata-only assessment.
            return null;
        }
    }
}

