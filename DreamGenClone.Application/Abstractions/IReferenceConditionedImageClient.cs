using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// One reference image for a reference-conditioned GENERATION: the bytes, the filename it is uploaded
/// under inside ComfyUI, and the semantic role for provenance.
/// </summary>
public sealed class ReferenceConditionedImageInput
{
    /// <summary>Why this image is in the request, e.g. "identity face for Becky", "location continuity".</summary>
    public string SemanticRole { get; set; } = string.Empty;

    /// <summary>Upload filename (contains no path).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Encoded image bytes (PNG).</summary>
    public byte[] Content { get; set; } = [];
}

/// <summary>
/// Fully specified, immutable inputs for one reference-conditioned generation. The client neither
/// queries repositories nor chooses references — the job handler compiles this from resolved,
/// immutable values.
/// </summary>
public sealed class ReferenceConditionedImageRequest
{
    public string PositivePrompt { get; set; } = string.Empty;

    public string NegativePrompt { get; set; } = string.Empty;

    /// <summary>"WxH" output canvas (e.g. "1216x832"), or null for the family default.</summary>
    public string? Size { get; set; }

    /// <summary>Fixed sampler seed, or null for random.</summary>
    public long? Seed { get; set; }

    public SceneImageGenerationOptions? Options { get; set; }

    /// <summary>
    /// Ordered references. The order is REQUEST DATA, not decoration: for Qwen-Image-2.1 the first
    /// reference anchors the composition (measured 2026-09-23 — swapping the location and a face
    /// reference swapped which subject landed on the left).
    /// </summary>
    public List<ReferenceConditionedImageInput> References { get; set; } = [];

    /// <summary>Correlation / render-attempt id for logging and provenance.</summary>
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Generates an image WITH reference images in a single call — the Qwen-Image-2.1 native
/// multi-reference path, where character faces and a location image condition the render instead of
/// being applied afterwards by an editing pass.
/// <para>
/// Deliberately separate from <see cref="IImageGenerationClient"/> (prompt-only),
/// <see cref="IIdentityConditionedImageClient"/> (a configured identity mechanism such as IP-Adapter or
/// PuLID) and <see cref="IImageEditingClient"/> (source-image editing). There is no silent fallback
/// between them: a family that does not implement reference-conditioned generation fails fast.
/// </para>
/// </summary>
public interface IReferenceConditionedImageClient
{
    Task<byte[]> GenerateWithReferencesAsync(
        ResolvedImageModel model,
        ReferenceConditionedImageRequest request,
        CancellationToken cancellationToken = default);
}
