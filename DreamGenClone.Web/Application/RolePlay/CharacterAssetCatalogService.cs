using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class CharacterAssetCatalogService : ICharacterAssetCatalogService
{
    private readonly ICharacterImageIdentityRepository _identity;
    private readonly ICharacterAppearanceVersionRepository _appearance;
    private readonly ISceneAssetRepository _assets;

    public CharacterAssetCatalogService(
        ICharacterImageIdentityRepository identity,
        ICharacterAppearanceVersionRepository appearance,
        ISceneAssetRepository assets)
    {
        _identity = identity;
        _appearance = appearance;
        _assets = assets;
    }

    public async Task<IReadOnlyList<CharacterAssetVersionSnapshot>> LoadVersionsAsync(
        string characterProfileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterProfileId))
            throw new InvalidOperationException("Character profile id is required for typed asset versions.");

        var results = new List<CharacterAssetVersionSnapshot>();
        foreach (var pack in await _identity.ListPacksAsync(characterProfileId.Trim(), cancellationToken))
        {
            var assets = await _assets.ListByPackAsync(pack.Id, cancellationToken);
            results.Add(new CharacterAssetVersionSnapshot(
                CharacterAssetVersionKind.Identity, pack.Id, pack.CharacterProfileId, pack.Version,
                pack.Status.ToString(), pack.SupersedesId, pack.DescriptorSnapshotJson,
                assets.Select(asset => PickerOption(
                    CharacterAssetVersionKind.Identity, pack.Id, pack.Version, pack.Status.ToString(),
                    pack.CharacterProfileId, asset, "identity-reference")).ToList()));
        }

        foreach (var body in await _appearance.ListBodyProfilesAsync(characterProfileId.Trim(), cancellationToken))
        {
            var bindings = await _appearance.ListBodyAssetBindingsAsync(body.Id, cancellationToken);
            results.Add(new CharacterAssetVersionSnapshot(
                CharacterAssetVersionKind.Body, body.Id, body.CharacterProfileId, body.Version,
                body.Status.ToString(), body.SupersedesId, body.DescriptorSnapshotJson,
                await ResolveBodyAssetsAsync(body, bindings, cancellationToken)));
        }

        foreach (var wardrobe in await _appearance.ListWardrobeLooksAsync(characterProfileId.Trim(), cancellationToken))
        {
            var bindings = await _appearance.ListWardrobeAssetBindingsAsync(wardrobe.Id, cancellationToken);
            results.Add(new CharacterAssetVersionSnapshot(
                CharacterAssetVersionKind.Wardrobe, wardrobe.Id, wardrobe.CharacterProfileId, wardrobe.Version,
                wardrobe.Status.ToString(), wardrobe.SupersedesId, wardrobe.DescriptorSnapshotJson,
                await ResolveWardrobeAssetsAsync(wardrobe, bindings, cancellationToken)));
        }

        return results
            .OrderBy(result => result.Kind)
            .ThenByDescending(result => result.Version)
            .ToList();
    }

    private async Task<IReadOnlyList<CharacterAssetPickerOption>> ResolveBodyAssetsAsync(
        CharacterBodyProfileVersion version,
        IReadOnlyList<CharacterBodyAssetBinding> bindings,
        CancellationToken cancellationToken)
    {
        var results = new List<CharacterAssetPickerOption>(bindings.Count);
        foreach (var binding in bindings.OrderBy(binding => binding.Ordinal))
        {
            var asset = await RequiredAssetAsync(binding.SceneAssetId, cancellationToken);
            results.Add(PickerOption(
                CharacterAssetVersionKind.Body, version.Id, version.Version, version.Status.ToString(),
                version.CharacterProfileId, asset, binding.SemanticRole));
        }
        return results;
    }

    private async Task<IReadOnlyList<CharacterAssetPickerOption>> ResolveWardrobeAssetsAsync(
        CharacterWardrobeLookVersion version,
        IReadOnlyList<CharacterWardrobeAssetBinding> bindings,
        CancellationToken cancellationToken)
    {
        var results = new List<CharacterAssetPickerOption>(bindings.Count);
        foreach (var binding in bindings.OrderBy(binding => binding.Ordinal))
        {
            var asset = await RequiredAssetAsync(binding.SceneAssetId, cancellationToken);
            results.Add(PickerOption(
                CharacterAssetVersionKind.Wardrobe, version.Id, version.Version, version.Status.ToString(),
                version.CharacterProfileId, asset, binding.SemanticRole));
        }
        return results;
    }

    private async Task<SceneAsset> RequiredAssetAsync(string assetId, CancellationToken cancellationToken) =>
        await _assets.GetAsync(assetId, cancellationToken)
        ?? throw new InvalidOperationException($"Typed character version references missing Scene Asset '{assetId}'.");

    private static CharacterAssetPickerOption PickerOption(
        CharacterAssetVersionKind kind,
        string versionId,
        int version,
        string status,
        string characterProfileId,
        SceneAsset asset,
        string semanticRole)
    {
        if (asset.ProductionVersion is null || string.IsNullOrWhiteSpace(asset.Sha256))
            throw new InvalidOperationException(
                $"Scene Asset '{asset.Id}' requires an exact production version and checksum for picker use.");
        return new CharacterAssetPickerOption(
            kind, versionId, version, status, characterProfileId, asset.Id,
            asset.ProductionVersion.Value, asset.Sha256, semanticRole);
    }
}