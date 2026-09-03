namespace DreamGenClone.Application.RolePlay;

public enum CharacterAssetVersionKind
{
    Identity = 1,
    Body = 2,
    Wardrobe = 3
}

public sealed record CharacterAssetPickerOption(
    CharacterAssetVersionKind VersionKind,
    string VersionId,
    int Version,
    string VersionStatus,
    string CharacterProfileId,
    string SceneAssetId,
    int SceneAssetVersion,
    string SceneAssetSha256,
    string SemanticRole);

public sealed record CharacterAssetVersionSnapshot(
    CharacterAssetVersionKind Kind,
    string Id,
    string CharacterProfileId,
    int Version,
    string Status,
    string? SupersedesId,
    string DescriptorSnapshotJson,
    IReadOnlyList<CharacterAssetPickerOption> Assets);

public interface ICharacterAssetCatalogService
{
    Task<IReadOnlyList<CharacterAssetVersionSnapshot>> LoadVersionsAsync(
        string characterProfileId,
        CancellationToken cancellationToken = default);
}