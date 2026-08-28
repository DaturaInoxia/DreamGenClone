using System.Buffers.Binary;
using System.Security.Cryptography;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.Storage;

/// <summary>
/// Local-disk storage for character identity reference assets. Writes under
/// <c>{SceneImageRoot}/identity/{characterProfileId}/{fileName}</c> and computes the SHA-256, byte
/// length, media type, and dimensions required at ingest. Unsupported or non-image content is
/// rejected before any database row can be created.
/// </summary>
public sealed class CharacterImageAssetStorageService : ICharacterImageAssetStorageService
{
    // Enough of the file head to locate a PNG IHDR chunk or a JPEG SOF segment for dimension
    // probing. Dimensions are nullable, so a JPEG whose SOF lies beyond this window still ingests
    // with unknown dimensions rather than failing.
    private const int MaxProbeBytes = 256 * 1024;

    private readonly PersistenceOptions _options;
    private readonly ILogger<CharacterImageAssetStorageService> _logger;

    public CharacterImageAssetStorageService(
        IOptions<PersistenceOptions> options,
        ILogger<CharacterImageAssetStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StoredCharacterImageAsset> SaveAsync(
        string characterProfileId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterProfileId))
            throw new InvalidOperationException("A character profile id is required to store a reference asset.");
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("A file name is required to store a reference asset.");

        var safeProfile = SanitizeSegment(characterProfileId);
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            throw new InvalidOperationException("A valid file name is required to store a reference asset.");

        var targetDirectory = Path.Combine(Path.GetFullPath(_options.SceneImageRoot), "identity", safeProfile);
        Directory.CreateDirectory(targetDirectory);

        var fullPath = Path.Combine(targetDirectory, safeName);
        var relativePath = $"identity/{safeProfile}/{safeName}";

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var prefix = new MemoryStream();
        var buffer = new byte[81920];
        long byteLength = 0;

        try
        {
            await using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    byteLength += read;

                    if (prefix.Length < MaxProbeBytes)
                    {
                        var remaining = (int)Math.Min(read, MaxProbeBytes - prefix.Length);
                        prefix.Write(buffer, 0, remaining);
                    }
                }
            }

            var (mediaType, width, height) = ProbeImage(prefix.GetBuffer(), (int)prefix.Length);

            var sha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToUpperInvariant();
            _logger.LogInformation("Reference asset stored at {Path} ({ByteLength} bytes, {MediaType})", fullPath, byteLength, mediaType);

            return new StoredCharacterImageAsset(relativePath, byteLength, sha256, mediaType, width, height);
        }
        catch
        {
            // Do not leave a partial or rejected file (or an empty directory) on disk.
            try { File.Delete(fullPath); } catch { /* best effort */ }
            try
            {
                if (!Directory.EnumerateFileSystemEntries(targetDirectory).Any())
                {
                    Directory.Delete(targetDirectory);
                }
            }
            catch { /* best effort */ }
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.Combine(Path.GetFullPath(_options.SceneImageRoot), relativePath);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.Combine(Path.GetFullPath(_options.SceneImageRoot), relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Reference asset deleted from {Path}", fullPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Detect the media type and, when the header is sufficient, the pixel dimensions of a PNG or
    /// JPEG. Throws for anything else so an invalid/non-image upload is rejected at ingest.
    /// </summary>
    internal static (string MediaType, int? Width, int? Height) ProbeImage(byte[] bytes, int length)
    {
        if (length >= 24 && IsPng(bytes))
        {
            // PNG: signature (8 bytes) + IHDR length/type (8 bytes) + width (4) + height (4).
            var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            return ("image/png", width, height);
        }

        if (length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            var (width, height) = ProbeJpegDimensions(bytes, length);
            return ("image/jpeg", width, height);
        }

        if (length >= 12 && IsWebp(bytes))
        {
            var (width, height) = ProbeWebpDimensions(bytes, length);
            return ("image/webp", width, height);
        }

        throw new InvalidOperationException("Unsupported or non-image reference asset. Only PNG, JPEG, and WebP reference images are accepted.");
    }

    private static bool IsPng(byte[] bytes) =>
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    private static bool IsWebp(byte[] bytes) =>
        bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 && // "RIFF"
        bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50; // "WEBP"

    /// <summary>
    /// Read the canvas width/height of a WebP image. Supports lossy VP8, lossless VP8L, and
    /// extended VP8X chunks. Dimensions are best-effort: an unsupported or truncated header
    /// yields null dimensions rather than failing the ingest.
    /// </summary>
    private static (int? Width, int? Height) ProbeWebpDimensions(byte[] bytes, int length)
    {
        if (length < 16)
        {
            return (null, null);
        }

        var fourCc = (bytes[12], bytes[13], bytes[14], bytes[15]);

        // Lossy VP8: frame tag (20..22), start code (23..25), then a 32-bit little-endian
        // width/height value (26..29): width in bits 0-13, height in bits 16-29.
        if (fourCc == ((byte)'V', (byte)'P', (byte)'8', (byte)' '))
        {
            if (length >= 30)
            {
                var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(26, 4));
                var width = (int)(value & 0x3FFF);
                var height = (int)((value >> 16) & 0x3FFF);
                if (width > 0 && height > 0)
                {
                    return (width, height);
                }
            }
            return (null, null);
        }

        // Lossless VP8L: a 32-bit little-endian value at 20..23 with signature bit 0, then
        // width-1 (bits 1-14) and height-1 (bits 15-28).
        if (fourCc == ((byte)'V', (byte)'P', (byte)'8', (byte)'L'))
        {
            if (length >= 24)
            {
                var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20, 4));
                var width = (int)((value >> 1) & 0x3FFF) + 1;
                var height = (int)((value >> 15) & 0x3FFF) + 1;
                if (width > 0 && height > 0)
                {
                    return (width, height);
                }
            }
            return (null, null);
        }

        // Extended VP8X: canvas width-1 / height-1 as 24-bit little-endian at offsets 24 and 28.
        if (fourCc == ((byte)'V', (byte)'P', (byte)'8', (byte)'X'))
        {
            if (length >= 32)
            {
                var width = ReadUInt24LittleEndian(bytes, 24) + 1;
                var height = ReadUInt24LittleEndian(bytes, 28) + 1;
                if (width > 0 && height > 0)
                {
                    return (width, height);
                }
            }
            return (null, null);
        }

        return (null, null);
    }

    private static int ReadUInt24LittleEndian(byte[] bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);

    private static (int? Width, int? Height) ProbeJpegDimensions(byte[] bytes, int length)
    {
        var i = 2;
        while (i + 9 < length)
        {
            // Find the next marker.
            if (bytes[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = bytes[i + 1];
            // Skip fill bytes and standalone markers.
            if (marker == 0xFF || marker == 0x00 || marker == 0x01)
            {
                i++;
                continue;
            }

            // Start-of-frame markers encode height (2 bytes) then width (2 bytes) after precision.
            if (IsStartOfFrame(marker) && i + 9 < length)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i + 7, 2));
                return (width, height);
            }

            // No length field for standalone markers (0xD0-0xD9 and 0x01).
            if (marker >= 0xD0 && marker <= 0xD9)
            {
                i += 2;
                continue;
            }

            if (i + 3 >= length) break;
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i + 2, 2));
            if (segmentLength < 2) break;
            i += 2 + segmentLength;
        }

        return (null, null);
    }

    private static bool IsStartOfFrame(byte marker) => marker is 0xC0 or 0xC1 or 0xC2 or 0xC3
        or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static string SanitizeSegment(string value)
    {
        var safe = string.Concat(value.Where(c => char.IsLetterOrDigit(c) || c == '-'));
        return string.IsNullOrEmpty(safe) ? "unknown" : safe;
    }
}
