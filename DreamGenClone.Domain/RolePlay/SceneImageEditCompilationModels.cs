namespace DreamGenClone.Domain.RolePlay;

public enum SceneImageEditSessionStatus
{
    Unknown = 0,
    Active = 1,
    Ready = 2,
    ClarificationRequired = 3,
    Invalid = 4,
    Failed = 5,
    Completed = 6
}

public enum SceneImageEditCompilationAttemptStatus
{
    Unknown = 0,
    Pending = 1,
    Compiling = 2,
    Ready = 3,
    ClarificationRequired = 4,
    Invalid = 5,
    Failed = 6
}

public enum SceneImageEditCompilationResultStatus
{
    Unknown = 0,
    Ready = 1,
    ClarificationRequired = 2,
    Invalid = 3
}

public enum SceneImageEditPromptRevisionKind
{
    Unknown = 0,
    CompilerOutput = 1,
    UserEdited = 2
}

public sealed class SceneImageEditSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SourceImageId { get; set; } = string.Empty;
    public string SourceImageSha256 { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public SceneImageEditSessionStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
    public string? DescriptionText { get; set; }
}

public sealed class SceneImageEditCompilationAttempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
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

public sealed class SceneImageEditCompilationResult
{
    public string SchemaVersion { get; set; } = string.Empty;
    public SceneImageEditCompilationResultStatus Status { get; set; }
    public string SourceSummary { get; set; } = string.Empty;
    public List<SceneImageEditTarget> Targets { get; set; } = [];
    public List<string> RequestedChanges { get; set; } = [];
    public List<string> Preserve { get; set; } = [];
    public string? ClarificationQuestion { get; set; }
    public string? InvalidReason { get; set; }
    public string? CompiledPrompt { get; set; }
}

public sealed class SceneImageEditTarget
{
    public string Key { get; set; } = string.Empty;
    public string VisibleLocator { get; set; } = string.Empty;
    public SceneImageEditTargetRegion? Region { get; set; }
}

public sealed class SceneImageEditTargetRegion
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class SceneImageEditPromptRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompilationAttemptId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public SceneImageEditPromptRevisionKind RevisionKind { get; set; }
    public string PromptSha256 { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}