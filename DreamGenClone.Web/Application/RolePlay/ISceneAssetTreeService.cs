namespace DreamGenClone.Web.Application.RolePlay;

public interface ISceneAssetTreeService
{
    /// <summary>
    /// Builds the grouped Asset Manager tree: character identity packs (face/full-body/wardrobe view
    /// sets), location profiles, and the remaining asset library under their owning roots. No flat
    /// mode.
    /// </summary>
    Task<IReadOnlyList<AssetTreeRoot>> BuildTreeAsync(CancellationToken cancellationToken = default);
}
