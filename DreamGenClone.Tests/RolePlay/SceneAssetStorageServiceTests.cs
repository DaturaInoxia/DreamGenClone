using System.Buffers.Binary;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneAssetStorageServiceTests
{
    [Fact]
    public async Task Save_ComputesSha256_Length_AndPngDimensions_UnderAssetsSubtree()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scene-assets-{Guid.NewGuid():N}");
        var service = CreateService(root);
        try
        {
            var png = MinimalPng(320, 240);
            var expectedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)).ToUpperInvariant();

            await using var input = new MemoryStream(png);
            var stored = await service.SaveAsync("asset-1.png", input);

            Assert.Equal("assets/asset-1.png", stored.RelativePath);
            Assert.Equal(png.Length, stored.ByteLength);
            Assert.Equal(expectedSha, stored.Sha256);
            Assert.Equal("image/png", stored.MediaType);
            Assert.Equal(320, stored.Width);
            Assert.Equal(240, stored.Height);
            Assert.True(File.Exists(Path.Combine(root, "assets", "asset-1.png")));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task Save_RejectsNonImage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scene-assets-{Guid.NewGuid():N}");
        var service = CreateService(root);
        try
        {
            await using var input = new MemoryStream([0x00, 0x01, 0x02, 0x03]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync("bad.bin", input));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task OpenRead_ReturnsStoredBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scene-assets-{Guid.NewGuid():N}");
        var service = CreateService(root);
        try
        {
            var png = MinimalPng(10, 10);
            await using (var input = new MemoryStream(png))
            {
                await service.SaveAsync("a.png", input);
            }

            await using var stream = await service.OpenReadAsync("assets/a.png");
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            Assert.Equal(png, buffer.ToArray());
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scene-assets-{Guid.NewGuid():N}");
        var service = CreateService(root);
        try
        {
            var png = MinimalPng(10, 10);
            await using (var input = new MemoryStream(png))
            {
                await service.SaveAsync("a.png", input);
            }
            Assert.True(File.Exists(Path.Combine(root, "assets", "a.png")));

            await service.DeleteAsync("assets/a.png");
            Assert.False(File.Exists(Path.Combine(root, "assets", "a.png")));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private static SceneAssetStorageService CreateService(string root) =>
        new(Options.Create(new PersistenceOptions { SceneImageRoot = root }),
            NullLogger<SceneAssetStorageService>.Instance);

    private static void TryDeleteDir(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
    }

    private static byte[] MinimalPng(int width, int height)
    {
        var bytes = new byte[29];
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
        bytes[4] = 0x0D; bytes[5] = 0x0A; bytes[6] = 0x1A; bytes[7] = 0x0A;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        bytes[12] = 0x49; bytes[13] = 0x48; bytes[14] = 0x44; bytes[15] = 0x52;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        bytes[24] = 8; bytes[25] = 6; bytes[26] = 0; bytes[27] = 0; bytes[28] = 0;
        return bytes;
    }
}
