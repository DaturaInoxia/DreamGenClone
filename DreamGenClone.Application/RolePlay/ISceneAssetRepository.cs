using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// SQLite persistence for the app-wide scene asset library. Follows the self-contained repository
/// pattern used by the other scene-image repositories. Assets are free-floating (not scoped to a
/// character) so the same library can back identity packs, locations, and wardrobe packs.
/// </summary>
public interface ISceneAssetRepository
{
    Task<SceneAsset?> GetAsync(string assetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAsset>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneAsset>> ListByPackAsync(
        string identityPackId, CancellationToken cancellationToken = default);

    /// <summary>Insert a new asset or update mutable fields (status, file metadata, error).</summary>
    Task UpsertAsync(SceneAsset asset, CancellationToken cancellationToken = default);

    Task DeleteAsync(string assetId, CancellationToken cancellationToken = default);

    /// <summary>Count assets that reference a file path (delete guard for shared files).</summary>
    Task<int> CountByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default);
}
