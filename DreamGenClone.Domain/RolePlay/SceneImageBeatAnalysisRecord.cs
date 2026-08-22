namespace DreamGenClone.Domain.RolePlay;

public enum SceneImageBeatAnalysisStatus
{
    Pending = 0,
    Complete = 1,
    Failed = 2
}

/// <summary>Persisted model analysis of the image-worthy moments within one role-play turn.</summary>
public sealed class SceneImageBeatAnalysisRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string TurnId { get; set; } = string.Empty;
    public string AnchorInteractionId { get; set; } = string.Empty;
    public SceneImageBeatAnalysisStatus Status { get; set; } = SceneImageBeatAnalysisStatus.Pending;
    public string BeatsJson { get; set; } = "[]";
    public string InputSnapshotJson { get; set; } = "{}";
    public string? RawModelResponse { get; set; }
    public string? ReasoningContent { get; set; }
    public string? ModelIdentifier { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}