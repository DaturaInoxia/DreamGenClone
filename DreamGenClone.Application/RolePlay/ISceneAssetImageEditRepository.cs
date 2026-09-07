using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ISceneAssetImageEditRepository
{
    Task CreateSessionAsync(SceneAssetImageEditSession session, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task UpdateSessionStatusAsync(
        string sessionId,
        SceneAssetImageEditSessionStatus status,
        DateTime updatedUtc,
        DateTime? completedUtc = null,
        CancellationToken cancellationToken = default);
    Task SetDescriptionAsync(
        string sessionId,
        string description,
        DateTime updatedUtc,
        CancellationToken cancellationToken = default);
    Task CreateAttemptAsync(SceneAssetImageEditCompilationAttempt attempt, CancellationToken cancellationToken = default);
    Task UpdateAttemptAsync(SceneAssetImageEditCompilationAttempt attempt, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditCompilationAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default);
    Task CreateRevisionAsync(SceneAssetImageEditPromptRevision revision, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditPromptRevision?> GetRevisionAsync(string revisionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SceneAssetImageEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default);
    Task<SceneAssetImageEditPromptRevision> GetExecutableRevisionAsync(
        string editSessionId,
        string attemptId,
        string revisionId,
        string sourceImageSha256,
        string promptSha256,
        CancellationToken cancellationToken = default);
}