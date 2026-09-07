namespace DreamGenClone.Domain.RolePlay;

public enum SceneAssetImageEditSessionStatus
{
    Unknown = 0,
    Active = 1,
    Ready = 2,
    ClarificationRequired = 3,
    Invalid = 4,
    Failed = 5,
    Completed = 6
}

/// <summary>
/// Durable edit lifecycle for an Asset Manager image. Asset and image ownership remain distinct
/// from role-play scene/session records.
/// </summary>
public sealed class SceneAssetImageEditSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AssetId { get; set; } = string.Empty;
    public string SourceImageId { get; set; } = string.Empty;
    public string SourceImageSha256 { get; set; } = string.Empty;
    public SceneAssetImageEditSessionStatus Status { get; set; } = SceneAssetImageEditSessionStatus.Active;
    public string? DescriptionText { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
}

public sealed class SceneAssetImageEditCompilationAttempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EditSessionId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string RawIntent { get; set; } = string.Empty;
    public string? ClarificationContextJson { get; set; }
    public string SourceImageSha256 { get; set; } = string.Empty;
    public SceneImageEditCompilationAttemptStatus Status { get; set; }
    public string ResolvedModelSnapshotJson { get; set; } = string.Empty;
    public string CompilerSchemaVersion { get; set; } = string.Empty;
    public string SystemPromptVersion { get; set; } = string.Empty;
    public string? RawModelResponse { get; set; }
    public string? ParsedResultJson { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}

public sealed class SceneAssetImageEditPromptRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompilationAttemptId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public SceneImageEditPromptRevisionKind RevisionKind { get; set; }
    public string PromptSha256 { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}