using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// One character's identity reference for a multi-character identity-controlled render: the
/// canonical face bytes, an optional per-character strength override, an optional regional mask
/// (white = the conditioned region), and a label for provenance.
/// </summary>
public sealed class IdentityReferenceInput
{
    /// <summary>Human-readable label (character name) for logging and provenance.</summary>
    public string CharacterLabel { get; set; } = string.Empty;

    /// <summary>Reference image bytes (the canonical face reference from the approved pack).</summary>
    public byte[] ReferenceImageBytes { get; set; } = [];

    /// <summary>Per-character conditioning weight override; null uses the model's configured strength.</summary>
    public double? StrengthOverride { get; set; }

    /// <summary>
    /// Regional mask bytes (white = the character's conditioned region). When null for a
    /// multi-character render, <see cref="Region"/> must be present.
    /// </summary>
    public byte[]? MaskBytes { get; set; }

    /// <summary>Exact normalized ownership region used when no approved mask bytes are supplied.</summary>
    public IdentityReferenceRegion? Region { get; set; }
}

public sealed class IdentityReferenceRegion
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

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

    /// <summary>
    /// Multi-character references. When more than one is present the client conditions each
    /// character via a chained regional workflow (one LoadImage + LoadImageMask + IPAdapter node per
    /// character). When empty, the single <see cref="ReferenceImageBytes"/> path is used.
    /// </summary>
    public List<IdentityReferenceInput> References { get; set; } = [];

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
