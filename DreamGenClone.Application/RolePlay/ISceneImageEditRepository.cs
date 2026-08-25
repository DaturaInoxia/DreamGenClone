using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ISceneImageEditRepository
{
    Task CreateSessionAsync(SceneImageEditSession session, CancellationToken cancellationToken = default);
    Task UpdateSessionStatusAsync(
        string sessionId,
        SceneImageEditSessionStatus status,
        DateTime updatedUtc,
        DateTime? completedUtc = null,
        CancellationToken cancellationToken = default);
    Task<SceneImageEditSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task CreateAttemptAsync(SceneImageEditCompilationAttempt attempt, CancellationToken cancellationToken = default);
    Task UpdateAttemptAsync(SceneImageEditCompilationAttempt attempt, CancellationToken cancellationToken = default);
    Task<SceneImageEditCompilationAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default);
    Task<SceneImageEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default);
    Task CreateRevisionAsync(SceneImageEditPromptRevision revision, CancellationToken cancellationToken = default);
    Task<SceneImageEditPromptRevision?> GetRevisionAsync(string revisionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SceneImageEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default);
    Task<SceneImageEditPromptRevision> GetExecutableRevisionAsync(
        string editSessionId,
        string attemptId,
        string revisionId,
        string sourceImageSha256,
        string promptSha256,
        CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task DeleteAttemptAsync(string attemptId, CancellationToken cancellationToken = default);
    Task DeleteRevisionAsync(string revisionId, CancellationToken cancellationToken = default);
}