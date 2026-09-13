using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// The ONE edit-session store (B-124 B124-012). It replaces the two parallel repositories whose
/// contracts were identical apart from their type names, so a schema or logic fix now lands once
/// for both the role-play studio and the Asset Manager.
/// </summary>
public interface IMediaEditRepository
{
    Task CreateSessionAsync(MediaEditSession session, CancellationToken cancellationToken = default);
    Task<MediaEditSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<MediaEditSession?> GetLatestSessionAsync(
        MediaEditSubjectKind subjectKind, string subjectId, string sourceImageId, CancellationToken cancellationToken = default);
    Task UpdateSessionStatusAsync(
        string sessionId,
        MediaEditSessionStatus status,
        DateTime updatedUtc,
        DateTime? completedUtc = null,
        CancellationToken cancellationToken = default);
    Task SetDescriptionAsync(
        string sessionId, string description, DateTime updatedUtc, CancellationToken cancellationToken = default);

    Task CreateAttemptAsync(MediaEditCompilationAttempt attempt, CancellationToken cancellationToken = default);
    Task UpdateAttemptAsync(MediaEditCompilationAttempt attempt, CancellationToken cancellationToken = default);
    Task<MediaEditCompilationAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default);
    Task<MediaEditCompilationAttempt?> GetLatestAttemptAsync(
        string editSessionId, CancellationToken cancellationToken = default);

    Task CreateRevisionAsync(MediaEditPromptRevision revision, CancellationToken cancellationToken = default);
    Task<MediaEditPromptRevision?> GetRevisionAsync(string revisionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaEditPromptRevision>> ListRevisionsAsync(
        string attemptId, CancellationToken cancellationToken = default);
    Task<MediaEditPromptRevision> GetExecutableRevisionAsync(
        string editSessionId,
        string attemptId,
        string revisionId,
        string sourceImageSha256,
        string promptSha256,
        CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task DeleteAttemptAsync(string attemptId, CancellationToken cancellationToken = default);
}
