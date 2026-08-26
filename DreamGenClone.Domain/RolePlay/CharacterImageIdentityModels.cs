namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Lifecycle of one character image identity pack version. Draft packs are mutable; approval
/// freezes a version; superseding retires it in favor of a new draft.
/// </summary>
public enum CharacterImageIdentityPackStatus
{
    Draft = 1,
    Approved = 2,
    Superseded = 3
}

/// <summary>Semantic kind of a reference asset inside an identity pack.</summary>
public enum SceneImageReferenceAssetKind
{
    Face = 1,
    FullBody = 2,
    Wardrobe = 3
}

/// <summary>
/// Consent state for a reference asset's source material. <see cref="Unknown"/> assets can never
/// be approved (FR2-004).
/// </summary>
public enum SceneImageReferenceConsentState
{
    Unknown = 1,
    Confirmed = 2,
    NotApplicable = 3
}

/// <summary>
/// Identity conditioning mechanisms supported by the controlled render path. Only mechanisms
/// selected and approved by the host proof report may be resolved at render time.
/// </summary>
public enum SceneImageIdentityMechanism
{
    Unknown = 0,
    IpAdapter = 1,
    PuLid = 2
}

/// <summary>Outcome of the identity-matrix LoRA decision (FR2-016).</summary>
public enum SceneImageIdentityDecisionValue
{
    NotRequired = 1,
    Required = 2,
    Deferred = 3
}

/// <summary>
/// A versioned, auditable collection of reference assets that together define how one recurring
/// character should look. Tied to a single scenario character via <see cref="CharacterProfileId"/>.
/// </summary>
public sealed class CharacterImageIdentityPack
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The scenario character this pack describes (owner scope).</summary>
    public string CharacterProfileId { get; set; } = string.Empty;

    /// <summary>Positive, unique per character.</summary>
    public int Version { get; set; } = 1;

    public CharacterImageIdentityPackStatus Status { get; set; } = CharacterImageIdentityPackStatus.Draft;

    /// <summary>Stable visual descriptor snapshot recorded at approval time.</summary>
    public string DescriptorSnapshotJson { get; set; } = "{}";

    /// <summary>Required for approval: an approved <see cref="SceneImageReferenceAssetKind.Face"/> asset.</summary>
    public string? CanonicalFaceAssetId { get; set; }

    /// <summary>Previous pack version this pack supersedes.</summary>
    public string? SupersedesId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedUtc { get; set; }
}

/// <summary>
/// An immutable reference image inside an identity pack. The referenced file is immutable;
/// replacing it creates a new asset row. Bytes live under the configured scene-image root.
/// </summary>
public sealed class SceneImageReferenceAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string IdentityPackId { get; set; } = string.Empty;

    public SceneImageReferenceAssetKind AssetKind { get; set; } = SceneImageReferenceAssetKind.Face;

    public string FileRelativePath { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public int? Width { get; set; }
    public int? Height { get; set; }
    public long ByteLength { get; set; }

    /// <summary>Uppercase 64-character SHA-256 of the stored bytes.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Human-readable provenance for the source material.</summary>
    public string SourceLabel { get; set; } = string.Empty;

    public SceneImageReferenceConsentState ConsentState { get; set; } = SceneImageReferenceConsentState.Unknown;

    public bool IsApproved { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
