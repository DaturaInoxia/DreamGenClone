using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// Fully specified, immutable inputs for one pose-conditioned (ControlNet OpenPose) render. The
/// client does not query repositories — the job handler resolves and compiles this from immutable
/// values before calling the client.
/// </summary>
public sealed class PoseConditionedImageRequest
{
    public string PositivePrompt { get; set; } = string.Empty;

    public string NegativePrompt { get; set; } = string.Empty;

    /// <summary>"WxH" (e.g. "1024x1024").</summary>
    public string? Size { get; set; }

    /// <summary>Fixed sampler seed, or null for random.</summary>
    public long? Seed { get; set; }

    /// <summary>Pose skeleton image bytes (OpenPose-rendered skeleton PNG). Uploaded to the ComfyUI
    /// host and applied as OpenPose ControlNet conditioning.</summary>
    public byte[] PoseImageBytes { get; set; } = [];

    /// <summary>ControlNet conditioning strength (0 &lt; strength &lt;= 1). Sent verbatim; the client
    /// fails fast on an out-of-range value.</summary>
    public double Strength { get; set; } = 0.8;

    /// <summary>Correlation / render-attempt id for logging and provenance.</summary>
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Generates a pose-conditioned image by applying an OpenPose ControlNet to the configured local
/// ComfyUI model. Separate from <see cref="IImageGenerationClient"/> (prompt-only) and
/// <see cref="IIdentityConditionedImageClient"/> (identity) — no silent fallback between them.
/// </summary>
public interface IPoseConditionedImageClient
{
    Task<byte[]> GenerateAsync(
        ResolvedPoseImageModel model,
        PoseConditionedImageRequest request,
        CancellationToken cancellationToken = default);
}
