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

    /// <summary>
    /// Every attempt of a production group — including attempts made under an EARLIER enrichment of the same moment
    /// and point of view.
    ///
    /// <para>
    /// A group is the composition of one moment at one POV; re-enriching the moment restates its facts without making a
    /// different composition, so takes made before the re-enrichment are still takes of this group and must keep
    /// appearing in its deck with the same controls as any other take. The group is passed rather than its id so the
    /// moment lineage can never be forgotten by a caller: this query is the SQL form of
    /// <c>SceneImageGroupMembership.Contains</c>, and the two must agree. A different moment set, moment, or POV is
    /// never included.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SceneImageRecord>> ListImagesByProductionGroupAsync(
        SceneImageProductionGroup group, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SceneImageRecord>> ListImagesBySessionAsync(
        string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an attempt's disposition, accepting the group it belongs to under the same membership rule as
    /// <see cref="ListImagesByProductionGroupAsync"/> — so a take carried over from an earlier enrichment keeps the
    /// same disposition controls as any other take.
    /// </summary>
    Task<bool> TrySetDispositionAsync(
        SceneImageProductionGroup group,
        string imageId,
        SceneImageAttemptDisposition expectedDisposition,
        SceneImageAttemptDisposition nextDisposition,
        DateTime updatedUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the operator's name for a rendered image and returns the row as it now stands. A targeted update, so a name
    /// written while a render is in flight cannot revert the render's own writes.
    /// </summary>
    Task<SceneImageRecord> SetDisplayNameAsync(
        string imageId,
        string? displayName,
        DateTime updatedUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a rendered image's tags and returns the row as it now stands. The caller supplies the tag WORDS — the
    /// stored shape and the "is this a tag at all" rule are this layer's, shared with the asset store.
    /// </summary>
    Task<SceneImageRecord> SetTagsAsync(
        string imageId,
        IReadOnlyList<string> tags,
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
