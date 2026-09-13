namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Which store an editable image belongs to. The edit pipeline itself is subject-agnostic: this
/// value only says how to reach the image the session edits.
/// </summary>
public enum MediaEditSubjectKind
{
    Unknown = 0,

    /// <summary>A role-play scene image; the subject id is the interaction id.</summary>
    SceneImage = 1,

    /// <summary>An Asset Manager image; the subject id is the asset id.</summary>
    AssetImage = 2
}

/// <summary>Lifecycle of one media edit session, independent of where the image lives.</summary>
public enum MediaEditSessionStatus
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
/// The single edit session record for every editable image. This replaced the two parallel
/// session models (<c>SceneImageEditSession</c> / <c>SceneAssetImageEditSession</c>) whose columns
/// were identical and whose fixes had to be applied twice.
/// </summary>
public sealed class MediaEditSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public MediaEditSubjectKind SubjectKind { get; set; }

    /// <summary>Interaction id for scene images, asset id for asset images.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>
    /// The subject's parent scope when one exists (the role-play session for a scene image);
    /// null when the subject has no parent. Kept so the migration out of the legacy stores is lossless.
    /// </summary>
    public string? SubjectScopeId { get; set; }

    public string SourceImageId { get; set; } = string.Empty;
    public string SourceImageSha256 { get; set; } = string.Empty;
    public MediaEditSessionStatus Status { get; set; } = MediaEditSessionStatus.Active;
    public string? DescriptionText { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
}

/// <summary>One vision-compiler attempt against a media edit session.</summary>
public sealed class MediaEditCompilationAttempt
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

/// <summary>One stored compiled/edited prompt for an attempt.</summary>
public sealed class MediaEditPromptRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompilationAttemptId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public SceneImageEditPromptRevisionKind RevisionKind { get; set; }
    public string PromptSha256 { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
