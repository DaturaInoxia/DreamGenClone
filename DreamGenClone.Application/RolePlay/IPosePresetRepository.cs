using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IPosePresetRepository
{
    Task<IReadOnlyList<PosePreset>> ListAsync(CancellationToken cancellationToken = default);

    Task<PosePreset?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The one search path for the pose library. <paramref name="keyword"/> matches the preset name, its
    /// keywords or its category; <paramref name="category"/> and <paramref name="libraryId"/> are exact
    /// filters. A blank argument means "do not filter on this", never "use a default".
    /// </summary>
    Task<IReadOnlyList<PosePreset>> SearchAsync(
        string? keyword,
        string? category = null,
        string? libraryId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PoseLibrary>> ListLibrariesAsync(CancellationToken cancellationToken = default);

    Task<PoseLibrary?> GetLibraryAsync(string id, CancellationToken cancellationToken = default);

    Task UpsertLibraryAsync(PoseLibrary library, CancellationToken cancellationToken = default);

    Task UpsertAsync(PosePreset preset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes ONLY the derived metadata columns of an existing preset: stance, direction, camera, rating, the composed
    /// prompt and the review flag. Separate from <see cref="UpsertAsync"/> because the two callers want different
    /// things: an import creates the row (keypoints, skeleton, provenance) and the metadata backfill fills in metadata
    /// for rows that already exist, including rows whose pose predates metadata. Nothing else on the row is touched, so
    /// a backfill can never rewrite a pose.
    /// </summary>
    Task UpdateMetadataAsync(PosePreset preset, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
}
