using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The downloader's refusals are the part that matters: it fetches a URL and writes into the repository, so a
/// refusal must happen before any network call and must leave nothing on disk.
/// </summary>
public sealed class PosePackDownloaderTests
{
    [Fact]
    public async Task Download_ANonHttpUrl_IsRefusedWithoutTouchingTheNetwork()
    {
        using var fixture = new PoseLibraryTestFixture();
        var downloader = fixture.Downloader();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync("file:///etc/passwd", "Sneaky pack"));

        Assert.Contains("http", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_WithNoName_IsRefused()
    {
        using var fixture = new PoseLibraryTestFixture();
        var downloader = fixture.Downloader();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync("https://example.invalid/pack.zip", "   "));

        Assert.Contains("name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_APackThatExists_IsRefusedRatherThanOverwritten()
    {
        using var fixture = new PoseLibraryTestFixture();
        var downloader = fixture.Downloader();

        // The bundled pack folder already exists in the fixture.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync("https://example.invalid/pack.zip", "Openpose NSFW"));

        Assert.Contains("already exists", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_WithoutASizeLimitConfigured_IsRefused()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.OptionsValue.DownloadMaxMegabytes = null;
        var downloader = fixture.Downloader();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync("https://example.invalid/pack.zip", "A new pack"));

        Assert.Contains("DownloadMaxMegabytes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_WithoutAPacksRootConfigured_IsRefused()
    {
        using var fixture = new PoseLibraryTestFixture();
        fixture.OptionsValue.PacksRoot = null;
        var downloader = fixture.Downloader();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync("https://example.invalid/pack.zip", "A new pack"));

        Assert.Contains("PacksRoot", error.Message, StringComparison.Ordinal);
    }
}
