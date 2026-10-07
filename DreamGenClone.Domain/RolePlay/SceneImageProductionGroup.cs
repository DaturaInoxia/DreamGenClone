namespace DreamGenClone.Domain.RolePlay;

public enum SceneImageProductionGroupStatus
{
    Draft = 1,
    InProgress = 2,
    Review = 3,
    Approved = 4,
    Archived = 5
}

public enum SceneImageProductionStage
{
    Composition = 1,
    Identity = 2,
    Finish = 3,

    /// <summary>
    /// A preset edit pass (B-133): relight or re-express an existing attempt from a picked preset whose instruction was
    /// assembled deterministically, with no compiler artifact behind it. Its own stage rather than a flag, because the
    /// compiled stage validates a session, attempt and revision that a preset run does not have.
    /// </summary>
    Preset = 4,

    /// <summary>
    /// A multi-angle camera edit pass: orbit the source subject to a picked azimuth/elevation/distance, assembled
    /// deterministically from the editor LoRA's <c>&lt;sks&gt;</c> grammar, with no compiler artifact behind it. Its
    /// own stage for the same reason a preset run is: the compiled stage validates a session, attempt and revision
    /// that a multi-angle run does not have.
    /// </summary>
    MultiAngle = 5
}

public enum SceneImageAttemptDisposition
{
    Active = 1,
    Shortlisted = 2,
    Rejected = 3,
    Archived = 4
}

public enum SceneImageIdentityPolicy
{
    Required = 1,
    SkippedByUser = 2
}

public enum ApprovedSceneFrameDecisionState
{
    Approved = 1,
    Superseded = 2,
    Revoked = 3
}

public enum SceneImageAttemptRetentionMode
{
    Manual = 1,
    Automatic = 2
}

public sealed class SceneImageAttemptRetentionPolicy
{
    public SceneImageAttemptRetentionMode Mode { get; set; }
    public int? RejectedRetentionDays { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; }
    public long Version { get; set; }
}

public sealed record SceneImageBytePurgeReservation(
    string ImageId,
    string FileRelativePath,
    DateTime ReservedUtc);

public sealed class SceneImageProductionGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string CatalogueId { get; set; } = string.Empty;
    public string BeatId { get; set; } = string.Empty;
    public string BeatProductionPlanId { get; set; } = string.Empty;
    public int BeatProductionPlanVersion { get; set; }
    public string MomentSetId { get; set; } = string.Empty;
    public int MomentSetVersion { get; set; }
    public string MomentId { get; set; } = string.Empty;
    public string MomentEnrichmentId { get; set; } = string.Empty;
    public int MomentEnrichmentRevision { get; set; }
    public string Pov { get; set; } = string.Empty;
    public string? CameraIntentSnapshotJson { get; set; }

    /// <summary>
    /// The per-POV location backdrop the operator bound in the Location stage, as <see cref="SceneImageLocationBackdrop"/>
    /// JSON. Null when this POV is text-only (no backdrop chosen). The group is the durable record the Composition
    /// auto-seed re-reads on every composer open; the composer's bindings are rebuilt per visit.
    /// </summary>
    public string? LocationBackdropJson { get; set; }

    public SceneImageProductionGroupStatus Status { get; set; } = SceneImageProductionGroupStatus.Draft;
    public SceneImageIdentityPolicy IdentityPolicy { get; set; } = SceneImageIdentityPolicy.Required;
    public string? IdentitySkipReason { get; set; }
    public string? CurrentApprovedDecisionId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// The per-POV location backdrop an operator bound to a production group in the Location stage. Carries the image id
/// AND its checksum, production version and operator label, because a bare id is not enough for render revalidation —
/// the same fields <c>ReferenceApplicationSelection</c> carries for a reference binding.
/// </summary>
public sealed record SceneImageLocationBackdrop(
    string AssetId,
    string ImageId,
    string Sha256,
    int? ProductionVersion,
    string Label)
{
    /// <summary>A versionless backdrop can only be bound when nothing is known about the image's lineage.</summary>
    public static SceneImageLocationBackdrop Create(
        string assetId,
        string imageId,
        string sha256,
        int? productionVersion,
        string label)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            throw new InvalidOperationException("A location backdrop requires the container asset id.");
        if (string.IsNullOrWhiteSpace(imageId))
            throw new InvalidOperationException("A location backdrop requires the image id.");
        if (string.IsNullOrWhiteSpace(sha256))
            throw new InvalidOperationException("A location backdrop requires the image checksum.");
        if (string.IsNullOrWhiteSpace(label))
            throw new InvalidOperationException("A location backdrop requires the operator-entered image name.");

        return new SceneImageLocationBackdrop(assetId.Trim(), imageId.Trim(), sha256.Trim(), productionVersion, label.Trim());
    }
}

/// <summary>
/// The moment→location container link: which location container this moment's POVs default to. Keyed on
/// <see cref="MomentId"/> (stable across enrichment revisions — an enrichment id would be lost on every re-enrichment),
/// with one CURRENT row per moment (upsert). Written only by the operator's bind action, never automatically.
/// </summary>
public sealed class SceneMomentLocationLink
{
    public string MomentId { get; set; } = string.Empty;
    public string LocationAssetId { get; set; } = string.Empty;

    /// <summary>The resolved scenario location id, or null when the place is ad-hoc (invented by the RP engine).</summary>
    public string? ScenarioLocationId { get; set; }

    /// <summary>Where the link came from. The only writer is the operator's bind action.</summary>
    public string Origin { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ApprovedSceneFrameDecision
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProductionGroupId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string SceneImageId { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string CatalogueId { get; set; } = string.Empty;
    public string BeatId { get; set; } = string.Empty;
    public string BeatProductionPlanId { get; set; } = string.Empty;
    public int BeatProductionPlanVersion { get; set; }
    public string MomentSetId { get; set; } = string.Empty;
    public int MomentSetVersion { get; set; }
    public string MomentId { get; set; } = string.Empty;
    public string MomentEnrichmentId { get; set; } = string.Empty;
    public int MomentEnrichmentRevision { get; set; }
    public ApprovedSceneFrameDecisionState Decision { get; set; } = ApprovedSceneFrameDecisionState.Approved;
    public string DecidedBy { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime DecisionUtc { get; set; }
}
