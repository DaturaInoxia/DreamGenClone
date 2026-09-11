using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>
/// One detected person in the scene image editor bound to an approved identity-pack face.
/// The user assigns a scenario character (whose approved identity pack owns the face) to each
/// vision-detected person; the face reference is an approved
/// <see cref="SceneImageReferenceAssetKind.Face"/> asset of that character's approved pack.
/// </summary>
public sealed class SceneImageEditorIdentitySelection
{
    /// <summary>The vision-compiler target key (image-relative, e.g. "man on image right").</summary>
    public string TargetKey { get; set; } = string.Empty;

    /// <summary>Location-qualified locator used to anchor the person in the identity instruction.</summary>
    public string VisibleLocator { get; set; } = string.Empty;

    /// <summary>Normalized face region from the vision compiler (informational provenance).</summary>
    public SceneImageEditTargetRegion? Region { get; set; }

    /// <summary>The scenario character (identity-pack owner) the user assigned to this person.</summary>
    public string CharacterId { get; set; } = string.Empty;

    /// <summary>Character display name used in the service-authored face-only instruction.</summary>
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>The approved <see cref="SceneImageReferenceAsset"/> face selected from the character's approved pack.</summary>
    public string ReferenceAssetId { get; set; } = string.Empty;
}

/// <summary>
/// Editor-side identity correction request: bind approved identity-pack faces to the detected
/// people of a completed source image and run a separate face-only identity edit that produces an
/// immutable child lineage image. Distinct from the typed-instruction content edit flow.
/// </summary>
public sealed class SceneImageEditorIdentityRequest
{
    public string SessionId { get; set; } = string.Empty;

    public string InteractionId { get; set; } = string.Empty;

    /// <summary>The completed source image whose faces are corrected (any eligible image in the interaction).</summary>
    public string SourceImageId { get; set; } = string.Empty;

    public IReadOnlyList<SceneImageEditorIdentitySelection>? Selections { get; set; }
}
