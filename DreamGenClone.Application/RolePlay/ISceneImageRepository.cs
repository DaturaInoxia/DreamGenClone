using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// SQLite persistence for the scene-image pipeline: editable prompt records and rendered image
/// records. Mirrors the repository pattern used by other RP persistence interfaces.
/// </summary>
public interface ISceneImageRepository
{
    // ---- Beat analysis records ----
    Task UpsertBeatAnalysisAsync(SceneImageBeatAnalysisRecord analysis, CancellationToken cancellationToken = default);
    Task<SceneImageBeatAnalysisRecord?> GetBeatAnalysisByTurnAsync(
        string sessionId, string turnId, CancellationToken cancellationToken = default);

    // ---- Prompt records ----
    Task UpsertPromptAsync(SceneImagePromptRecord prompt, CancellationToken cancellationToken = default);
    Task<SceneImagePromptRecord?> GetPromptAsync(string promptId, CancellationToken cancellationToken = default);
    Task<SceneImagePromptRecord?> GetLatestPromptAsync(
        string sessionId, string interactionId, CancellationToken cancellationToken = default);
    Task<SceneImagePromptRecord?> GetLatestCompletedPromptAsync(
        string sessionId,
        string interactionId,
        string beatAnalysisId,
        string beatId,
        string pov,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Most recent completed production prompt for a group/brief. Pass <paramref name="promptStyle"/>
    /// to restrict to one prompt style (natural language vs Pony tags); legacy Unknown rows are
    /// treated as natural language. When null, the latest completed prompt of any style is returned.
    /// </summary>
    Task<SceneImagePromptRecord?> GetLatestCompletedProductionPromptAsync(
        string sessionId,
        string interactionId,
        string productionGroupId,
        string compiledMediaBriefId,
        SceneImagePromptStyle? promptStyle = null,
        CancellationToken cancellationToken = default);

    /// <summary>Persist the user-edited prompt text to a prompt record's OutputPrompt.</summary>
    Task UpdatePromptOutputAsync(
        string promptId, string outputPrompt, CancellationToken cancellationToken = default);

    // ---- Image records ----
    Task InsertImageAsync(SceneImageRecord image, CancellationToken cancellationToken = default);
    Task<SceneImageRecord?> GetImageAsync(string imageId, CancellationToken cancellationToken = default);
    Task<bool> TryCancelImageAsync(
        string imageId,
        string sessionId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the queued row for the run that is about to call the model: 'Pending' becomes 'Generating'
    /// and the start timestamp is recorded.
    ///
    /// <see cref="TryCompleteImageAsync"/> only completes a claimed row, so every render and every edit
    /// must claim first. Only a 'Pending' row is claimed — a row that already finished, was cancelled, or
    /// was claimed by an earlier delivery of the same job matches nothing and returns false.
    /// </summary>
    Task<bool> TryClaimImageAsync(
        string imageId,
        DateTime startedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryCompleteImageAsync(SceneImageRecord image, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a row produced by a deterministic operation (crop, enhance).
    ///
    /// <see cref="TryCompleteImageAsync"/> only completes a row a worker has claimed (Status
    /// 'Generating'), because a render or an edit is claimed before it runs. An operation has no claim
    /// step — its row is queued 'Pending' and is finished by the same job that picked it up — so it
    /// completes from either state, exactly as <see cref="TryFailImageAsync"/> already accepts both.
    /// A row that has already reached a terminal status is never overwritten.
    /// </summary>
    Task<bool> TryCompleteOperationImageAsync(SceneImageRecord image, CancellationToken cancellationToken = default);
    Task<bool> TryFailImageAsync(SceneImageRecord image, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SceneImageRecord>> ListImagesByInteractionAsync(
        string sessionId, string interactionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SceneImageRecord>> ListImagesByProductionGroupAsync(
        string productionGroupId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SceneImageRecord>> ListImagesBySessionAsync(
        string sessionId, CancellationToken cancellationToken = default);
    Task<bool> TrySetDispositionAsync(
        string imageId,
        string productionGroupId,
        SceneImageAttemptDisposition expectedDisposition,
        SceneImageAttemptDisposition nextDisposition,
        DateTime updatedUtc,
        CancellationToken cancellationToken = default);
    Task<SceneImageBytePurgeReservation> ReserveRejectedBytesPurgeAsync(
        string imageId,
        DateTime reservedUtc,
        CancellationToken cancellationToken = default);
    Task CompleteRejectedBytesPurgeAsync(
        SceneImageBytePurgeReservation reservation,
        CancellationToken cancellationToken = default);
    Task ReleaseRejectedBytesPurgeAsync(
        SceneImageBytePurgeReservation reservation,
        CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> CountImagesByInteractionAsync(
        string sessionId, CancellationToken cancellationToken = default);
    Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default);
}
