namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// Immutable value object describing how to call the pose-conditioned (ControlNet OpenPose) image
/// path, resolved from the Model Manager at call time. Mirrors <see cref="ResolvedIdentityImageModel"/>
/// for the pose/ControlNet render path. Every field is a configured value; the resolver never
/// substitutes a default.
/// </summary>
public sealed record ResolvedPoseImageModel(
    string ProviderBaseUrl,
    int ProviderTimeoutSeconds,
    string ModelIdentifier,
    ImageContentPolicy ContentPolicy,
    string ProviderName,
    /// <summary>ComfyUI ControlNet weight path as ComfyUI lists it (e.g. Windows
    /// "thibaud-openpose-xl2\OpenPoseXL2.safetensors"). Configured per model — never hardcoded.</summary>
    string ControlNetAdapterRef,
    /// <summary>Configured default conditioning strength for this model's ControlNet (0 &lt; strength &lt;= 1).</summary>
    double DefaultStrength,
    string? ApiKeyEncrypted = null,
    ImageProtocol ImageProtocol = ImageProtocol.ComfyUi);
