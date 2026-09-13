using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Projects the identity store, the asset library, and location-profile references into one grouped
/// tree for the Asset Manager. Grouping is root-agnostic and data-driven: every row supplies the
/// group key, owner metadata and a view key; nothing here is character-shaped.
/// </summary>
public sealed class SceneAssetTreeService : ISceneAssetTreeService
{
    private readonly IScenarioService _scenarios;
    private readonly ICharacterImageIdentityService _identity;
    private readonly ISceneAssetService _library;
    private readonly IReferenceBootstrapRepository _locations;

    public SceneAssetTreeService(
        IScenarioService scenarios,
        ICharacterImageIdentityService identity,
        ISceneAssetService library,
        IReferenceBootstrapRepository locations)
    {
        _scenarios = scenarios;
        _identity = identity;
        _library = library;
        _locations = locations;
    }

    public async Task<IReadOnlyList<AssetTreeRoot>> BuildTreeAsync(CancellationToken cancellationToken = default)
    {
        var roots = new List<AssetTreeRoot>();
        var libraryAssets = (await _library.ListAssetsAsync(cancellationToken)).ToList();
        var libraryById = libraryAssets.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var consumedLibraryIds = new HashSet<string>(StringComparer.Ordinal);

        var charactersByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var scenario in await _scenarios.GetAllScenariosAsync())
        {
            foreach (var character in scenario.Characters)
            {
                if (string.IsNullOrWhiteSpace(character.Id))
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(character.Name) ? "Unnamed" : character.Name;
                if (!charactersByName.TryGetValue(name, out var ids))
                {
                    ids = [];
                    charactersByName[name] = ids;
                }

                if (!ids.Contains(character.Id, StringComparer.Ordinal))
                {
                    ids.Add(character.Id);
                }
            }
        }

        foreach (var (characterName, characterIds) in charactersByName)
        {
            var hasContent = false;
            foreach (var characterId in characterIds)
            {
                var packs = (await _identity.ListPacksAsync(characterId, cancellationToken)).ToList();
                hasContent |= packs.Count > 0;

                foreach (var lib in libraryAssets
                    .Where(a => string.Equals(a.CharacterProfileId, characterId, StringComparison.Ordinal)))
                {
                    consumedLibraryIds.Add(lib.Id);
                    hasContent = true;
                }
            }

            if (!hasContent)
            {
                continue;
            }

            roots.Add(new AssetTreeRoot
            {
                Owner = new AssetTreeOwner
                {
                    RootKind = "Character",
                    OwnerId = characterIds[0],
                    OwnerName = characterName,
                    Href = $"/characters/{Uri.EscapeDataString(characterIds[0])}"
                },
                Groups = []
            });
        }

        foreach (var asset in libraryAssets
            .Where(a => a.Type == SceneAssetType.Character && !consumedLibraryIds.Contains(a.Id))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            consumedLibraryIds.Add(asset.Id);

            // Mapped to a scenario character → represented by that scenario character's root above.
            if (!string.IsNullOrWhiteSpace(asset.CharacterProfileId))
            {
                continue;
            }

            roots.Add(new AssetTreeRoot
            {
                Owner = new AssetTreeOwner
                {
                    RootKind = "Character",
                    OwnerId = asset.Id,
                    OwnerName = asset.Name,
                    Href = $"/characters/{Uri.EscapeDataString(asset.Id)}"
                },
                Groups = []
            });
        }

        var locationProfiles = (await _locations.ListLocationProfilesAsync(cancellationToken)).ToList();
        foreach (var profile in locationProfiles)
        {
            var references = (await _locations.ListLocationReferencesAsync(profile.Id, cancellationToken)).ToList();
            var items = new List<AssetTreeItem>();
            foreach (var reference in references.OrderBy(r => r.OrderedIndex))
            {
                if (!libraryById.TryGetValue(reference.AssetId, out var asset))
                {
                    continue;
                }

                consumedLibraryIds.Add(asset.Id);
                items.Add(ToLibraryItem(asset, "Reference"));
            }

            roots.Add(new AssetTreeRoot
            {
                Owner = new AssetTreeOwner
                {
                    RootKind = "Location",
                    OwnerId = profile.Id,
                    OwnerName = profile.Name,
                    Badge = profile.Status.ToString(),
                    Href = $"/locations/{Uri.EscapeDataString(profile.Id)}"
                },
                Groups =
                [
                    new AssetTreeGroup
                    {
                        Key = $"location:{profile.Id}",
                        Label = "Location references",
                        Items = items
                    }
                ]
            });
        }

        foreach (var asset in libraryAssets
            .Where(a => a.Type == SceneAssetType.Location && !consumedLibraryIds.Contains(a.Id))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            consumedLibraryIds.Add(asset.Id);
            roots.Add(new AssetTreeRoot
            {
                Owner = new AssetTreeOwner
                {
                    RootKind = "Location",
                    OwnerId = asset.Id,
                    OwnerName = asset.Name,
                    Href = $"/locations/{Uri.EscapeDataString(asset.Id)}"
                },
                Groups = []
            });
        }

        var unassigned = libraryAssets.Where(a => !consumedLibraryIds.Contains(a.Id)).ToList();
        if (unassigned.Count > 0)
        {
            roots.Add(new AssetTreeRoot
            {
                Owner = new AssetTreeOwner
                {
                    RootKind = "Cleanup",
                    OwnerId = "cleanup",
                    OwnerName = "Cleanup"
                },
                Groups = BuildLibraryTypeGroups(unassigned)
            });
        }

        return roots;
    }

    private static AssetTreeItem ToLibraryItem(SceneAsset asset, string? kindLabel = null) => new()
    {
        SourceStore = "Library",
        AssetId = asset.Id,
        Name = asset.Name,
        KindLabel = kindLabel ?? asset.Type?.ToString() ?? asset.Kind.ToString(),
        KindFilter = asset.Type?.ToString() ?? asset.Kind.ToString(),
        ApprovalFilter = asset.ProductionApprovalStatus?.ToString() ?? "Unreviewed",
        ViewKey = LibraryViewKey(asset),
        FileRelativePath = asset.FileRelativePath,
        StatusLabel = asset.ProductionApprovalStatus is not null
            ? $"{asset.Status} · {asset.ProductionApprovalStatus}"
            : asset.Status.ToString(),
        CreatedUtc = asset.CreatedUtc,
        CharacterProfileId = asset.CharacterProfileId,
        IdentityPackId = asset.IdentityPackId,
        CandidateBatchId = asset.CandidateBatchId,
        Href = $"/asset-studio/{asset.Id}"
    };

    private static string? LibraryViewKey(SceneAsset asset)
    {
        if (asset.FaceView is not null) return asset.FaceView.ToString();
        if (asset.BodyView is not null) return asset.BodyState is null ? asset.BodyView.ToString() : $"{asset.BodyState} {asset.BodyView}";
        if (!string.IsNullOrWhiteSpace(asset.ViewDescriptorJson))
        {
            try
            {
                return ReferenceViewDescriptor.FromJson(asset.ViewDescriptorJson).Label;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return null;
    }

    private static IReadOnlyList<AssetTreeGroup> BuildLibraryTypeGroups(IReadOnlyList<SceneAsset> assets)
        => assets
            .GroupBy(a => a.Type?.ToString() ?? "Unclassified")
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AssetTreeGroup
            {
                Key = $"library-type:{g.Key}",
                Label = g.Key,
                Items = g.Select(a => ToLibraryItem(a)).ToList()
            })
            .ToList();
}
