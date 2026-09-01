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
    Finish = 3
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
    public SceneImageProductionGroupStatus Status { get; set; } = SceneImageProductionGroupStatus.Draft;
    public SceneImageIdentityPolicy IdentityPolicy { get; set; } = SceneImageIdentityPolicy.Required;
    public string? IdentitySkipReason { get; set; }
    public string? CurrentApprovedDecisionId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
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
