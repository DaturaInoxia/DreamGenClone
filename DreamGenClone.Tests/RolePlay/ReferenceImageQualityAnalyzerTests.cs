using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ReferenceImageQualityAnalyzerTests
{
    private readonly ReferenceImageQualityAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_HighRes_ReturnsGood()
    {
        var (rating, notes) = _analyzer.Analyze(null, 1280, 960, 400_000);
        Assert.Equal(SceneImageReferenceQuality.Good, rating);
        Assert.Contains("resolution", notes);
        Assert.Contains("good", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_LowRes_ReturnsNotGood()
    {
        var (rating, notes) = _analyzer.Analyze(null, 365, 547, 21_000);
        Assert.Equal(SceneImageReferenceQuality.NotGood, rating);
        Assert.Contains("Low resolution", notes);
    }

    [Fact]
    public void Analyze_ModerateRes_ReturnsOk()
    {
        var (rating, notes) = _analyzer.Analyze(null, 513, 596, 42_000);
        Assert.Equal(SceneImageReferenceQuality.Ok, rating);
        Assert.Contains("Moderate resolution", notes);
    }

    [Fact]
    public void Analyze_Overcompressed_FlagsAsNotGood()
    {
        // 403x496 at ~0.05 B/px (clearly tiny)
        var (rating, notes) = _analyzer.Analyze(null, 403, 496, 10_000);
        Assert.Equal(SceneImageReferenceQuality.NotGood, rating);
        Assert.Contains("compressed", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_MissingDimensions_ReturnsNotGood()
    {
        var (rating, _) = _analyzer.Analyze(null, 0, 0, 0);
        Assert.Equal(SceneImageReferenceQuality.NotGood, rating);
    }

    [Fact]
    public void Analyze_SharpImage_StaysGood()
    {
        // Random noise is maximally sharp (high Laplacian variance) and incompressible (high density).
        using var stream = new MemoryStream(GenerateNoisePng(1024, 1024, seed: 42));
        var (rating, notes) = _analyzer.Analyze(stream, 1024, 1024, stream.Length);
        Assert.Equal(SceneImageReferenceQuality.Good, rating);
        Assert.Contains("Sharp", notes);
    }

    [Fact]
    public void Analyze_BlurrySolidImage_FlagsNotGood()
    {
        // Solid grey image -> variance of Laplacian ~0 -> blurry.
        using var stream = new MemoryStream(GeneratePng(1024, 1024, (_, _) => true));
        var (rating, notes) = _analyzer.Analyze(stream, 1024, 1024, stream.Length);
        Assert.Equal(SceneImageReferenceQuality.NotGood, rating);
        Assert.Contains("Blurry", notes);
    }

    private static byte[] GenerateNoisePng(int width, int height, int seed)
    {
        var rng = new Random(seed);
        using var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte v = (byte)rng.Next(256);
                image[x, y] = new Rgba32(v, v, v);
            }
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static byte[] GeneratePng(int width, int height, Func<int, int, bool> dark)
    {
        using var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image[x, y] = dark(x, y) ? new Rgba32(0, 0, 0) : new Rgba32(255, 255, 255);
            }
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}

