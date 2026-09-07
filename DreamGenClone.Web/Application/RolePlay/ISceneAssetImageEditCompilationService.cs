using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public interface ISceneAssetImageEditCompilationService
{
    Task<SceneAssetImageEditSession> CreateSessionAsync(CreateSceneAssetImageEditSessionRequest request, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditCompilationAttempt> EnqueueCompilationAsync(EnqueueSceneAssetImageEditCompilationRequest request, CancellationToken cancellationToken = default);
    Task EnqueueDescriptionAsync(string editSessionId, bool force = false, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditPromptRevision> AppendPromptRevisionAsync(AppendSceneAssetImageEditPromptRevisionRequest request, CancellationToken cancellationToken = default);
    Task<SceneAssetImage> EnqueueEditAsync(EnqueueSceneAssetImageEditRequest request, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditSession?> GetSessionAsync(string editSessionId, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SceneAssetImageEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default);
}