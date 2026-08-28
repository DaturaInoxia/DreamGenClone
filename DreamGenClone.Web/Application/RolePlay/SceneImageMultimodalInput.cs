using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

internal sealed record SceneImageSourceInput(
    string MediaType,
    byte[] Bytes,
    int Width,
    int Height,
    string Sha256);

internal static class SceneImageMultimodalInput
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<SceneImageSourceInput> ReadAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes <= 0 || maximumBytes > int.MaxValue)
            throw new InvalidOperationException("The source image byte limit is invalid for in-memory multimodal transport.");

        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes)
                throw new InvalidOperationException("The source image exceeds the configured byte limit.");
            destination.Write(buffer, 0, read);
        }

        var bytes = destination.ToArray();
        if (bytes.Length == 0)
            throw new InvalidOperationException("The source image is empty.");
        var (mediaType, width, height) = ReadDimensions(bytes);
        return new SceneImageSourceInput(mediaType, bytes, width, height, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public static void Validate(SceneImageSourceInput input, ResolvedMultimodalModel model)
    {
        if (model.MaximumInputImages != 1)
            throw new InvalidOperationException("The compiler model must be configured for exactly one input image.");
        if (!model.AcceptedInputMediaTypes.Contains(input.MediaType))
            throw new InvalidOperationException("The source image media type is not accepted by the compiler model.");
        if (input.Bytes.LongLength > model.MaximumInputImageBytes)
            throw new InvalidOperationException("The source image exceeds the configured byte limit.");
        if (input.Width > model.MaximumInputImageDimension || input.Height > model.MaximumInputImageDimension)
            throw new InvalidOperationException("The source image exceeds the configured dimension limit.");
        if ((long)input.Width * input.Height > model.MaximumInputImagePixels)
            throw new InvalidOperationException("The source image exceeds the configured pixel limit.");
    }

    public static string SerializeResolutionSnapshot(ResolvedMultimodalModel model) => JsonSerializer.Serialize(new
    {
        model.ProviderId,
        model.ModelId,
        model.ProviderBaseUrl,
        model.ChatCompletionsPath,
        model.ReadinessPath,
        model.ReadinessSuccessContractJson,
        model.RequestTimeoutSeconds,
        model.TransitionTimeoutSeconds,
        model.TransitionMarginSeconds,
        model.CredentialReference,
        model.ModelIdentifier,
        model.ProviderName,
        contentPolicy = model.ContentPolicy.ToString(),
        lifecycleStrategy = model.LifecycleStrategy.ToString(),
        model.MaximumInputImages,
        model.MaximumInputImageBytes,
        model.MaximumInputImagePixels,
        model.MaximumInputImageDimension,
        acceptedInputMediaTypes = model.AcceptedInputMediaTypes.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        model.MaximumResponseBytes,
        model.MaximumActiveRequests,
        model.QueueCapacity,
        model.Temperature,
        model.TopP,
        model.MaxTokens,
        model.RuntimeRevision,
        model.ArtifactRevision
    }, JsonOptions);

    private static (string MediaType, int Width, int Height) ReadDimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return ("image/png", ReadPositiveBigEndian(bytes[16..20]), ReadPositiveBigEndian(bytes[20..24]));
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return ReadWebP(bytes);
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            return ReadJpeg(bytes);
        throw new InvalidOperationException("The source image format is not a supported PNG, JPEG, or WebP image.");
    }

    private static (string, int, int) ReadWebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30)
            throw new InvalidOperationException("The WebP source image header is truncated.");
        var chunk = Encoding.ASCII.GetString(bytes[12..16]);
        return chunk switch
        {
            "VP8X" => ("image/webp", 1 + ReadUInt24(bytes[24..27]), 1 + ReadUInt24(bytes[27..30])),
            "VP8 " when bytes.Length >= 30 && bytes[23] == 0x9D && bytes[24] == 0x01 && bytes[25] == 0x2A
                => ("image/webp", BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..28]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..30]) & 0x3FFF),
            "VP8L" when bytes.Length >= 25 && bytes[20] == 0x2F
                => ("image/webp", 1 + (bytes[21] | ((bytes[22] & 0x3F) << 8)), 1 + ((bytes[22] >> 6) | (bytes[23] << 2) | ((bytes[24] & 0x0F) << 10))),
            _ => throw new InvalidOperationException("The WebP source image uses an unsupported or malformed header.")
        };
    }

    private static (string, int, int) ReadJpeg(ReadOnlySpan<byte> bytes)
    {
        var offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF)
                throw new InvalidOperationException("The JPEG source image has a malformed marker sequence.");
            while (offset < bytes.Length && bytes[offset] == 0xFF) offset++;
            if (offset >= bytes.Length) break;
            var marker = bytes[offset++];
            if (marker is 0xD8 or 0xD9 || marker is >= 0xD0 and <= 0xD7) continue;
            if (offset + 2 > bytes.Length) break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..(offset + 2)]);
            if (length < 2 || offset + length > bytes.Length)
                throw new InvalidOperationException("The JPEG source image has a malformed segment length.");
            if (IsJpegStartOfFrame(marker))
            {
                if (length < 7) throw new InvalidOperationException("The JPEG source image frame header is truncated.");
                return ("image/jpeg", ReadPositiveBigEndian16(bytes[(offset + 5)..(offset + 7)]), ReadPositiveBigEndian16(bytes[(offset + 3)..(offset + 5)]));
            }
            offset += length;
        }
        throw new InvalidOperationException("The JPEG source image has no supported frame header.");
    }

    private static bool IsJpegStartOfFrame(byte marker) => marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
    private static int ReadPositiveBigEndian(ReadOnlySpan<byte> bytes) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes));
    private static int ReadPositiveBigEndian16(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt16BigEndian(bytes);
    private static int ReadUInt24(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
}