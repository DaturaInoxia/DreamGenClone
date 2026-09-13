using DreamGenClone.Web.Application.RolePlay.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The produced image's own size. A crop trims and an enhance resamples, so the record has to describe
/// the bytes that exist rather than the source's dimensions (debug record 053 follow-up: crop and enhance
/// rows reported 1024x1024 for files that were 826x611 and 1024x757).
/// </summary>
public sealed class MediaEditProducedImageTests
{
    [Fact]
    public async Task SizeOf_ReportsTheProducedBytesDimensions()
    {
        var bytes = await CreatePngAsync(819, 1024);

        Assert.Equal("819x1024", MediaEditProducedImage.SizeOf(bytes));
    }

    [Fact]
    public async Task SizeOf_NonSquareLandscape_IsNotTransposed()
    {
        var bytes = await CreatePngAsync(1024, 757);

        Assert.Equal("1024x757", MediaEditProducedImage.SizeOf(bytes));
    }

    [Fact]
    public void SizeOf_BytesWithoutAnIdentifiableImage_FailsInsteadOfGuessing()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => MediaEditProducedImage.SizeOf([1, 2, 3, 4, 5, 6, 7, 8]));

        Assert.Contains("could not be identified", error.Message, StringComparison.Ordinal);
    }

    private static async Task<byte[]> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var buffer = new MemoryStream();
        await image.SaveAsPngAsync(buffer);
        return buffer.ToArray();
    }
}
