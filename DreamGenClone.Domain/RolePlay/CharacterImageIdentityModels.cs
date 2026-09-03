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
/// The view/angle of a face reference asset. Used by the multi-angle compiler to pick the
/// reference nearest the target head angle (e.g. a profile ref when the pose turns the head to
/// the side). Only meaningful for <see cref="SceneImageReferenceAssetKind.Face"/> assets.
/// </summary>
public enum SceneImageReferenceFaceView
{
    Front = 1,
    ThreeQuarterLeft = 2,
    ThreeQuarterRight = 3,
    ProfileLeft = 4,
    ProfileRight = 5
}

/// <summary>
/// Non-blocking quality assessment for a reference asset, set by the curator. Informational only —
/// it never gates approval or rendering, so a low-quality face can still be used while flagged.
/// </summary>
public enum SceneImageReferenceQuality
{
    NotRated = 1,
    Good = 2,
    Ok = 3,
    NotGood = 4
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

public enum SceneIdentityConstraintScore
{
    NotScored = 1,
    Pass = 2,
    Fail = 3
}

public sealed class SceneIdentityEvaluationCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EvaluationRunId { get; set; } = string.Empty;
    public string CapabilityCellId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string CharacterPairJson { get; set; } = "[]";
    public string PoseKey { get; set; } = string.Empty;
    public string ViewKey { get; set; } = string.Empty;
    public long Seed { get; set; }
    public string PromptHash { get; set; } = string.Empty;
    public string ControlHash { get; set; } = string.Empty;
    public string ExpectedConstraintsJson { get; set; } = "{}";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SceneIdentityEvaluationResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EvaluationCaseId { get; set; } = string.Empty;
    public string AttemptId { get; set; } = string.Empty;
    public string OutputSha256 { get; set; } = string.Empty;
    public string ConstraintScoresJson { get; set; } = "{}";
    public string Notes { get; set; } = string.Empty;
    public string Reviewer { get; set; } = string.Empty;
    public DateTime ReviewedUtc { get; set; }
}

public sealed class CharacterIdentityDecision
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string IdentityPackId { get; set; } = string.Empty;
    public string EvaluationRunId { get; set; } = string.Empty;
    public SceneImageIdentityDecisionValue Decision { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public string Rationale { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
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

    /// <summary>
    /// The view/angle of this face reference (null for non-face assets). Required for approved
    /// <see cref="SceneImageReferenceAssetKind.Face"/> assets so the render compiler can match the
    /// reference to the target head angle (multi-angle conditioning).
    /// </summary>
    public SceneImageReferenceFaceView? FaceView { get; set; }

    /// <summary>Non-blocking quality rating set by the curator (informational, never a gate).</summary>
    public SceneImageReferenceQuality QualityRating { get; set; } = SceneImageReferenceQuality.NotRated;

    /// <summary>Human-readable reasons for the automatic quality rating (informational only).</summary>
    public string QualityNotes { get; set; } = string.Empty;

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
