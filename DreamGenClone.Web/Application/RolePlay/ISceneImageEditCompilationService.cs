using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public interface ISceneImageEditCompilationService
{
    Task<SceneImageEditSession> CreateSessionAsync(
        CreateSceneImageEditSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneImageEditCompilationAttempt> EnqueueCompilationAsync(
        EnqueueSceneImageEditCompilationRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneImageEditPromptRevision> AppendPromptRevisionAsync(
        AppendSceneImageEditPromptRevisionRequest request,
        CancellationToken cancellationToken = default);

    Task EnqueueDescriptionAsync(string editSessionId, bool force = false, CancellationToken cancellationToken = default);

    Task<SceneImageEditSession?> GetSessionAsync(string editSessionId, CancellationToken cancellationToken = default);
    Task<SceneImageEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SceneImageEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default);
}