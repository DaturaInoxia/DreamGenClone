namespace DreamGenClone.Web.Application.RolePlay;

using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>Payload for the shared vision-compilation job.</summary>
public sealed class MediaEditCompilationJobPayload
{
    public string AttemptId { get; set; } = string.Empty;
}

/// <summary>Payload for the shared source-description job.</summary>
public sealed class MediaEditDescriptionJobPayload
{
    public string EditSessionId { get; set; } = string.Empty;
}

/// <summary>Payload for the shared image-editing job.</summary>
public sealed class MediaEditImageEditingJobPayload
{
    public MediaEditSubjectKind SubjectKind { get; set; }
    public string ImageId { get; set; } = string.Empty;

    /// <summary>
    /// Which operation the job executes. Left at <c>Unknown</c> by default so a payload that never
    /// named its operation fails fast instead of being treated as an edit.
    /// </summary>
    public MediaEditOperationKind OperationKind { get; set; }

    /// <summary>Serialized operation parameters; required for crop, absent for edit.</summary>
    public string? OperationJson { get; set; }

    public string? EditorModelId { get; set; }
    public string? ReferenceApplicationsJson { get; set; }

    /// <summary>
    /// Serialized <c>SceneImageLoraSelection</c> list the operator picked for THIS edit (B-143), or null for no
    /// scene LoRA. It travels on the queued payload so the run re-derives the same stack the operator saw when they
    /// queued it - the same reason <see cref="ReferenceApplicationsJson"/> is carried rather than re-resolved.
    /// </summary>
    public string? SceneLorasJson { get; set; }

    /// <summary>The owning scope the run belongs to (a role-play session id when there is one).</summary>
    public string? ScopeId { get; set; }
}
