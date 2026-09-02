namespace DreamGenClone.Domain.RolePlay;

public enum SceneBeatCatalogueStatus
{
    Pending = 0,
    Processing = 1,
    Complete = 2,
    Failed = 3,
    Superseded = 4,
    Cancelled = 5
}

public enum SceneBeatAnalysisAttemptStatus
{
    Queued = 0,
    Processing = 1,
    Complete = 2,
    Failed = 3,
    Superseded = 4,
    Cancelled = 5
}

public sealed class SceneBeatCatalogue
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string TurnId { get; set; } = string.Empty;
    public int Version { get; set; }
    public SceneBeatCatalogueStatus Status { get; set; } = SceneBeatCatalogueStatus.Pending;
    public string? CurrentAttemptId { get; set; }
    public int SchemaVersion { get; set; }
    public string PromptContractVersion { get; set; } = string.Empty;
    public string InputSnapshotJson { get; set; } = string.Empty;
    public string? ModelIdentifier { get; set; }
    public string? ProviderName { get; set; }
    public string ExecutionSettingsJson { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public IReadOnlyList<SceneBeatCatalogueEntry> Entries { get; set; } = [];
}

public sealed class SceneBeatCatalogueEntry
{
    public string CatalogueId { get; set; } = string.Empty;
    public string BeatId { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Label { get; set; } = string.Empty;
    public string BeatSynopsis { get; set; } = string.Empty;
    public string PrimaryLocation { get; set; } = string.Empty;
    public string ParticipantSummaryJson { get; set; } = string.Empty;
    public string EvidenceInteractionIdsJson { get; set; } = string.Empty;
    public string ContentTagsJson { get; set; } = string.Empty;
}

public sealed class SceneBeatAnalysisAttempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OwnerRecordId { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public string JobId { get; set; } = string.Empty;
    public SceneBeatAnalysisAttemptStatus Status { get; set; } = SceneBeatAnalysisAttemptStatus.Queued;
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public string? RawModelResponse { get; set; }
    public string? ReasoningContent { get; set; }
    public string? FinishReason { get; set; }
    public string? ValidationCode { get; set; }
    public string ValidationDetailsJson { get; set; } = string.Empty;
    public long? DurationMs { get; set; }
    public int InputCharacters { get; set; }
    public int? OutputCharacters { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}