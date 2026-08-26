using System.Buffers.Binary;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CharacterImageAssetStorageServiceTests
{
    [Fact]
    public async Task Save_ComputesSha256_Length_AndPngDimensions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-assets-{Guid.NewGuid():N}");
        var service = CreateService(root);
        try
        {
            var png = MinimalPng(320, 240);
            var expectedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)).ToUpperInvariant();

            await using var input = new MemoryStream(png);
            var stored = await service.SaveAsync("char-123", "face.png", input);

            Assert.Equal("identity/char-123/face.png", stored.RelativePath);
            Assert.Equal(png.Length, stored.ByteLength);
            Assert.Equal(expectedSha, stored.Sha256);
            Assert.Equal("image/png", stored.MediaType);
            Assert.Equal(320, stored.Width);
            Assert.Equal(240, stored.Height);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task Save_DetectsJpegDimensions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-assets-{Guid.NewGuid():N}");
        var service = CreateService(root);
        try
        {
            var jpeg = MinimalJpeg(16, 32);
            await using var input = new MemoryStream(jpeg);
            var stored = await service.SaveAsync("char-123", "wardrobe.jpg", input);

            Assert.Equal("image/jpeg", stored.MediaType);
            Assert.Equal(16, stored.Width);
            Assert.Equal(32, stored.Height);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task Save_DetectsWebpVp8Dimensions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-assets-{Guid.NewGuid():N}");
        var service = CreateService(root);
        try
        {
            var webp = MinimalWebpVp8(320, 240);
            await using var input = new MemoryStream(webp);
            var stored = await service.SaveAsync("char-123", "face.webp", input);

            Assert.Equal("image/webp", stored.MediaType);
            Assert.Equal(320, stored.Width);
            Assert.Equal(240, stored.Height);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task Save_DetectsWebpVp8LDimensions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-assets-{Guid.NewGuid():N}");
        var service = CreateService(root);
        try
        {
            var webp = MinimalWebpVp8L(64, 48);
            await using var input = new MemoryStream(webp);
            var stored = await service.SaveAsync("char-123", "face.webp", input);

            Assert.Equal("image/webp", stored.MediaType);
            Assert.Equal(64, stored.Width);
            Assert.Equal(48, stored.Height);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task Save_Open_Delete_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-assets-{Guid.NewGuid():N}");
        var service = CreateService(root);
        try
        {
            var bytes = MinimalPng(64, 64);
            await using var input = new MemoryStream(bytes);
            var stored = await service.SaveAsync("char-123", "face.png", input);

            {
                await using var opened = await service.OpenReadAsync(stored.RelativePath);
                using var buffer = new MemoryStream();
                await opened.CopyToAsync(buffer);
                Assert.Equal(bytes, buffer.ToArray());
            }

            await service.DeleteAsync(stored.RelativePath);
            Assert.False(File.Exists(Path.Combine(root, stored.RelativePath)));

            // Idempotent delete must not throw.
            await service.DeleteAsync(stored.RelativePath);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task Save_RejectsNonImageContent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-assets-{Guid.NewGuid():N}");
        var service = CreateService(root);
        try
        {
            await using var input = new MemoryStream("this is not an image"u8.ToArray());
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync("char-123", "face.png", input));

            // Rejection must not leave a file behind.
            Assert.False(Directory.Exists(Path.Combine(root, "identity", "char-123")));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task Save_SanitizesSegments()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-assets-{Guid.NewGuid():N}");
        var service = CreateService(root);
        try
        {
            await using var input = new MemoryStream(MinimalPng(8, 8));
            var stored = await service.SaveAsync("../evil/char", "face.png", input);

            Assert.False(stored.RelativePath.Contains("..", StringComparison.Ordinal));
            Assert.StartsWith("identity/", stored.RelativePath, StringComparison.Ordinal);
            Assert.EndsWith("/face.png", stored.RelativePath, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private static CharacterImageAssetStorageService CreateService(string root) =>
        new(Options.Create(new PersistenceOptions { SceneImageRoot = root }),
            NullLogger<CharacterImageAssetStorageService>.Instance);

    /// <summary>Minimal WebP container with a lossy VP8 chunk carrying width/height at 26..29.</summary>
    private static byte[] MinimalWebpVp8(int width, int height)
    {
        var bytes = new byte[30];
        bytes[0] = 0x52; bytes[1] = 0x49; bytes[2] = 0x46; bytes[3] = 0x46; // "RIFF"
        bytes[8] = 0x57; bytes[9] = 0x45; bytes[10] = 0x42; bytes[11] = 0x50; // "WEBP"
        bytes[12] = (byte)'V'; bytes[13] = (byte)'P'; bytes[14] = (byte)'8'; bytes[15] = (byte)' ';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 10); // chunk size
        bytes[20] = 0x90; bytes[21] = 0xD2; bytes[22] = 0x04; // frame tag
        bytes[23] = 0x9D; bytes[24] = 0x01; bytes[25] = 0x2A; // start code
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(26, 4), ((uint)height << 16) | (uint)width);
        return bytes;
    }

    /// <summary>Minimal WebP container with a lossless VP8L chunk carrying width/height at 20..23.</summary>
    private static byte[] MinimalWebpVp8L(int width, int height)
    {
        var bytes = new byte[24];
        bytes[0] = 0x52; bytes[1] = 0x49; bytes[2] = 0x46; bytes[3] = 0x46; // "RIFF"
        bytes[8] = 0x57; bytes[9] = 0x45; bytes[10] = 0x42; bytes[11] = 0x50; // "WEBP"
        bytes[12] = (byte)'V'; bytes[13] = (byte)'P'; bytes[14] = (byte)'8'; bytes[15] = (byte)'L';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 4); // chunk size
        // 32-bit bitfield: signature(1) | width-1(14) | height-1(14) | alpha(3).
        var value = ((uint)(height - 1) << 15) | ((uint)(width - 1) << 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), value);
        return bytes;
    }

    private static void TryDeleteDir(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
    }

    private static byte[] MinimalPng(int width, int height)
    {
        var bytes = new byte[29];
        // PNG signature.
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
        bytes[4] = 0x0D; bytes[5] = 0x0A; bytes[6] = 0x1A; bytes[7] = 0x0A;
        // IHDR chunk length (13).
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        // "IHDR".
        bytes[12] = 0x49; bytes[13] = 0x48; bytes[14] = 0x44; bytes[15] = 0x52;
        // Width + height.
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        // Bit depth, color type, compression, filter, interlace.
        bytes[24] = 8; bytes[25] = 6; bytes[26] = 0; bytes[27] = 0; bytes[28] = 0;
        return bytes;
    }

    private static byte[] MinimalJpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        // APP0 segment.
        bytes.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 });
        // SOF0 segment: length 17, precision 8, height, width, 3 components.
        bytes.Add(0xFF); bytes.Add(0xC0);
        bytes.Add(0x00); bytes.Add(0x11);
        bytes.Add(0x08);
        bytes.AddRange(new byte[] { (byte)(height >> 8), (byte)height });
        bytes.AddRange(new byte[] { (byte)(width >> 8), (byte)width });
        bytes.Add(0x03);
        bytes.AddRange(new byte[] { 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01 });
        // EOI.
        bytes.Add(0xFF); bytes.Add(0xD9);
        return bytes.ToArray();
    }
}
