using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// Fully specified, immutable inputs for one identity-conditioned render. The client does not query
/// repositories or choose a pack — the job handler compiles this from resolved immutable values.
/// </summary>
public sealed class IdentityControlledImageRequest
{
    public string PositivePrompt { get; set; } = string.Empty;

    public string NegativePrompt { get; set; } = string.Empty;

    /// <summary>"WxH" (e.g. "1024x1024").</summary>
    public string? Size { get; set; }

    /// <summary>Fixed sampler seed, or null for random.</summary>
    public long? Seed { get; set; }

    /// <summary>Reference image bytes (the canonical face reference from the approved pack).</summary>
    public byte[] ReferenceImageBytes { get; set; } = [];

    /// <summary>Correlation / render-attempt id for logging and provenance.</summary>
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Generates a single-actor identity-conditioned image using the configured mechanism. Separate from
/// <see cref="IImageGenerationClient"/> (prompt-only) and <see cref="IImageEditingClient"/>
/// (source-image editing) — no silent fallback between them.
/// </summary>
public interface IIdentityConditionedImageClient
{
    Task<byte[]> GenerateAsync(
        ResolvedIdentityImageModel model,
        IdentityControlledImageRequest request,
        CancellationToken cancellationToken = default);
}
