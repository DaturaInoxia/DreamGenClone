namespace DreamGenClone.Domain.RolePlay;

public enum SceneMomentProductionRole
{
    StillCandidate = 1,
    VideoStart = 2,
    VideoEnd = 3,
    VideoInternalKeyframe = 4,
    SoundEventAnchor = 5
}

public sealed class SceneMomentSet
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CatalogueId { get; set; } = string.Empty;
    public string BeatId { get; set; } = string.Empty;
    public string BeatProductionPlanId { get; set; } = string.Empty;
    public int BeatProductionPlanVersion { get; set; }
    public int Version { get; set; }
    public SceneBeatCatalogueStatus Status { get; set; } = SceneBeatCatalogueStatus.Pending;
    public string? CurrentAttemptId { get; set; }
    public string? RecommendedMomentId { get; set; }
    public int SchemaVersion { get; set; }
    public string PromptContractVersion { get; set; } = string.Empty;
    public string BeatSnapshotJson { get; set; } = string.Empty;
    public string TurnEvidenceSnapshotJson { get; set; } = string.Empty;
    public string? ModelIdentifier { get; set; }
    public string? ProviderName { get; set; }
    public string ExecutionSettingsJson { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public IReadOnlyList<SceneMoment> Moments { get; set; } = [];
}

public sealed class SceneMoment
{
    public string MomentSetId { get; set; } = string.Empty;
    public string MomentId { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Label { get; set; } = string.Empty;
    public string TemporalAnchor { get; set; } = string.Empty;
    public string FrozenState { get; set; } = string.Empty;
    public string VisibleAction { get; set; } = string.Empty;
    public string ParticipantSummaryJson { get; set; } = string.Empty;
    public string CompositionRationale { get; set; } = string.Empty;
    public string ProductionRolesJson { get; set; } = string.Empty;
    public string EvidenceInteractionIdsJson { get; set; } = string.Empty;
}

public sealed record SceneMomentSetData(
    string RecommendedMomentId,
    IReadOnlyList<SceneMoment> Moments);