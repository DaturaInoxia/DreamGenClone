using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// SQLite persistence for composed clips (B-152). Mirrors <see cref="ISceneImageRepository"/>'s claim/complete
/// discipline: a render claims a Pending row, and only a claimed row can be completed, so a duplicate delivery of
/// the same durable job cannot overwrite a finished clip.
/// </summary>
public interface ISceneVideoRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task InsertAsync(SceneVideoRecord record, CancellationToken cancellationToken = default);

    Task<SceneVideoRecord?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Claims a Pending row for the run that is about to call the model.</summary>
    Task<bool> TryClaimAsync(string id, DateTime startedUtc, CancellationToken cancellationToken = default);

    Task<bool> TryCompleteAsync(SceneVideoRecord record, CancellationToken cancellationToken = default);

    Task<bool> TryFailAsync(SceneVideoRecord record, CancellationToken cancellationToken = default);

    Task<bool> TryCancelAsync(string id, DateTime cancelledUtc, CancellationToken cancellationToken = default);

    /// <summary>Most recent compositions first, for the Queue tab and the standalone landing list.</summary>
    Task<IReadOnlyList<SceneVideoRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneVideoRecord>> ListBySessionAsync(
        string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The clips that continue this one, newest first (B-156). A source may have several, so this is a list and
    /// never a single row; an empty list means the clip is a chain's leaf.
    /// </summary>
    Task<IReadOnlyList<SceneVideoRecord>> ListContinuationsAsync(
        string sourceVideoId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
