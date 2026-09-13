using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// What the editing step needs beyond the image row itself. The caller owns creating that row (it is
/// subject-specific); this owns queueing it, so there is exactly one edit job type and one payload.
/// </summary>
public sealed record MediaEditRunRequest(
    MediaEditSubjectKind SubjectKind,
    string ImageId,
    string EditorModelId,
    int MaxAttempts,
    string? ReferenceApplicationsJson = null,
    string? ScopeId = null);

/// <summary>
/// Queues a non-model <b>operation</b> run (a crop) for a subject image row the caller has already
/// persisted. Same job type, same lane and the same retry budget as <see cref="MediaEditRunRequest"/>,
/// but there is no editor model to resolve and no endpoint to warm: the operation is deterministic and
/// local, so it is admitted immediately and can never be staged behind model availability.
/// </summary>
public sealed record MediaEditOperationRunRequest(
    MediaEditSubjectKind SubjectKind,
    string ImageId,
    MediaEditOperation Operation,
    int MaxAttempts,
    string? ScopeId = null);

/// <summary>
/// The ONE prompt-compilation lifecycle. Replaces
/// <c>SceneImageEditCompilationService</c> and <c>SceneAssetImageEditCompilationService</c>, whose
/// bodies were the same apart from their store types.
/// </summary>
public interface IMediaEditCompilationService
{
    Task<MediaEditSession> CreateSessionAsync(
        CreateMediaEditSessionRequest request, CancellationToken cancellationToken = default);

    Task<MediaEditCompilationAttempt> EnqueueCompilationAsync(
        EnqueueMediaEditCompilationRequest request, CancellationToken cancellationToken = default);

    Task EnqueueDescriptionAsync(
        string editSessionId, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues the editing step for a subject image row that the caller has already persisted. One job
    /// type (<c>media-edit-image-editing</c>) serves every subject kind. The <b>chosen</b> editor model
    /// is carried on the job, and this decides — from that model — whether the run starts immediately
    /// (a local ComfyUI endpoint is always running) or stages until its serverless endpoint is warm.
    /// </summary>
    Task EnqueueRunAsync(MediaEditRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an operation run (a crop) through the same job type, lane and failure marking as an edit.
    /// </summary>
    Task EnqueueOperationRunAsync(
        MediaEditOperationRunRequest request, CancellationToken cancellationToken = default);

    Task<MediaEditPromptRevision> AppendPromptRevisionAsync(
        AppendMediaEditPromptRevisionRequest request, CancellationToken cancellationToken = default);

    Task<MediaEditSession?> GetSessionAsync(string editSessionId, CancellationToken cancellationToken = default);
    Task<MediaEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default);
}
