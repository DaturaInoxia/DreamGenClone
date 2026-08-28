using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// Immutable value object describing how to call the identity-conditioned image path, resolved from
/// the Model Manager at call time. Mirrors <see cref="ResolvedImageModel"/> but for the identity
/// render path. Every field is a configured value; the resolver never substitutes a default.
/// </summary>
public sealed record ResolvedIdentityImageModel(
    string ProviderBaseUrl,
    int ProviderTimeoutSeconds,
    string ModelIdentifier,
    ImageContentPolicy ContentPolicy,
    string ProviderName,
    SceneImageIdentityMechanism Mechanism,
    string AdapterRef,
    string? ClipVisionRef,
    double IdentityStrength,
    string? ApiKeyEncrypted = null,
    ImageProtocol ImageProtocol = ImageProtocol.ComfyUi);
