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
    Task<SceneImagePromptRecord?> GetLatestCompletedProductionPromptAsync(
        string sessionId,
        string interactionId,
        string productionGroupId,
        string compiledMediaBriefId,
        CancellationToken cancellationToken = default);

    /// <summary>Persist the user-edited prompt text to a prompt record's OutputPrompt.</summary>
    Task UpdatePromptOutputAsync(
        string promptId, string outputPrompt, CancellationToken cancellationToken = default);

    // ---- Image records ----
    Task InsertImageAsync(SceneImageRecord image, CancellationToken cancellationToken = default);
    Task<SceneImageRecord?> GetImageAsync(string imageId, CancellationToken cancellationToken = default);
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
