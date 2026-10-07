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
    private readonly ICharacterIdentityOwnerResolver _owners;

    /// <summary>One identity owner's row in the tree: the owner key plus the instance ids that resolve to it.</summary>
    private sealed class OwnerGroup(string ownerId, string ownerName)
    {
        public string OwnerId { get; } = ownerId;

        public string OwnerName { get; } = ownerName;

        public List<string> InstanceIds { get; } = [];
    }

    public SceneAssetTreeService(
        IScenarioService scenarios,
        ICharacterImageIdentityService identity,
        ISceneAssetService library,
        ICharacterIdentityOwnerResolver owners)
    {
        _scenarios = scenarios;
        _identity = identity;
        _library = library;
        _owners = owners;
    }

    public async Task<IReadOnlyList<AssetTreeRoot>> BuildTreeAsync(CancellationToken cancellationToken = default)
    {
        var roots = new List<AssetTreeRoot>();
        var libraryAssets = (await _library.ListAssetsAsync(cancellationToken)).ToList();
        var consumedLibraryIds = new HashSet<string>(StringComparer.Ordinal);

        // B-127: group by identity OWNER (the character template), never by display name. The Asset Manager used to
        // merge characters by name and link to the first id it happened to see, which could open the scenario
        // instance that holds no packs at all. A character whose identity cannot be resolved keeps its own root so
        // nothing disappears from the list.
        var groupsByOwner = new Dictionary<string, OwnerGroup>(StringComparer.Ordinal);
        foreach (var scenario in await _scenarios.GetAllScenariosAsync())
        {
            foreach (var character in scenario.Characters)
            {
                if (string.IsNullOrWhiteSpace(character.Id))
                {
                    continue;
                }

                var instanceName = string.IsNullOrWhiteSpace(character.Name) ? "Unnamed" : character.Name;
                string ownerId;
                string ownerName;
                try
                {
                    var owner = await _owners.ResolveAsync(character.Id, cancellationToken);
                    ownerId = owner.TemplateId;
                    ownerName = owner.TemplateName;
                }
                catch (InvalidOperationException)
                {
                    // No character template (yet): its own root, named as itself and marked as unlinked.
                    ownerId = character.Id;
                    ownerName = $"{instanceName} (unlinked)";
                }

                if (!groupsByOwner.TryGetValue(ownerId, out var group))
                {
                    group = new OwnerGroup(ownerId, ownerName);
                    groupsByOwner[ownerId] = group;
                }

                group.InstanceIds.Add(character.Id);
            }
        }

        foreach (var group in groupsByOwner.Values)
        {
            var hasContent = (await _identity.ListPacksAsync(group.OwnerId, cancellationToken)).Any();
            foreach (var instanceId in group.InstanceIds)
            {
                foreach (var lib in libraryAssets
                    .Where(a => string.Equals(a.CharacterProfileId, instanceId, StringComparison.Ordinal)))
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
                    OwnerId = group.OwnerId,
                    OwnerName = group.OwnerName,
                    Href = $"/characters/{Uri.EscapeDataString(group.OwnerId)}"
                },
                Groups = []
            });
        }

        foreach (var asset in libraryAssets
            .Where(a => a.Type == SceneAssetType.Character && !consumedLibraryIds.Contains(a.Id))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            consumedLibraryIds.Add(asset.Id);

            // Mapped to a scenario character → represented by that character's owner root above.
            if (!string.IsNullOrWhiteSpace(asset.CharacterProfileId))
            {
                continue;
            }

            // B-127: a character asset is an instance, so it is labelled by the owner it resolves to; one that
            // resolves to nothing is marked as such rather than posing as an identity root of its own.
            var ownerLabel = asset.Name;
            try
            {
                var owner = await _owners.ResolveAsync(asset.Id, cancellationToken);
                ownerLabel = owner.TemplateName;
            }
            catch (InvalidOperationException)
            {
                ownerLabel = $"{asset.Name} (unlinked)";
            }

            roots.Add(new AssetTreeRoot
            {
                Owner = new AssetTreeOwner
                {
                    RootKind = "Character",
                    OwnerId = asset.Id,
                    OwnerName = ownerLabel,
                    Href = $"/characters/{Uri.EscapeDataString(asset.Id)}"
                },
                Groups = []
            });
        }

        // B-148 D19: location roots are the location CONTAINERS, rendered with the hierarchy (world container →
        // scenario-location containers → spot containers). The retired bootstrap location profiles linked an id
        // `/locations/{id}` could not resolve, so the roots must come from the containers themselves.
        var locationContainers = libraryAssets
            .Where(a => a.Type == SceneAssetType.Location && !consumedLibraryIds.Contains(a.Id))
            .ToList();
        var locationById = locationContainers.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var byParent = locationContainers
            .Where(a => !string.IsNullOrWhiteSpace(a.ParentAssetId))
            .ToLookup(a => a.ParentAssetId!, StringComparer.Ordinal);

        // A root is a location whose parent is missing or is not itself a location container (the world container).
        foreach (var rootAsset in locationContainers
            .Where(a => string.IsNullOrWhiteSpace(a.ParentAssetId) || !locationById.ContainsKey(a.ParentAssetId))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            consumedLibraryIds.Add(rootAsset.Id);
            // Every descendant is consumed too, or a nested container would also appear under Cleanup.
            foreach (var descendant in Descendants(rootAsset.Id, byParent))
            {
                consumedLibraryIds.Add(descendant.Id);
            }

            var directChildren = Children(rootAsset.Id, byParent);
            roots.Add(new AssetTreeRoot
            {
                Owner = new AssetTreeOwner
                {
                    RootKind = "Location",
                    OwnerId = rootAsset.Id,
                    OwnerName = rootAsset.Name,
                    Href = $"/locations/{Uri.EscapeDataString(rootAsset.Id)}"
                },
                Groups = directChildren.Count == 0
                    ? []
                    :
                    [
                        new AssetTreeGroup
                        {
                            Key = $"location:{rootAsset.Id}",
                            Label = "Locations",
                            Items = directChildren.Select(ToLocationContainerItem).ToList(),
                            Children = directChildren
                                .Select(child => BuildLocationGroup(child, byParent))
                                .Where(group => group.Items.Count > 0)
                                .ToList()
                        }
                    ]
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

    /// <summary>
    /// One nested group per child container: the container's own name, its direct children as items, and one group
    /// per child that has children of its own. Recursion is what makes the tree show world → location → spot rather
    /// than silently dropping everything below the first level.
    /// </summary>
    private static AssetTreeGroup BuildLocationGroup(
        SceneAsset container,
        ILookup<string, SceneAsset> byParent)
    {
        var children = Children(container.Id, byParent);
        return new AssetTreeGroup
        {
            Key = $"location:{container.Id}",
            Label = container.Name,
            Items = children.Select(ToLocationContainerItem).ToList(),
            Children = children
                .Select(child => BuildLocationGroup(child, byParent))
                .Where(group => group.Items.Count > 0)
                .ToList()
        };
    }

    private static List<SceneAsset> Children(string assetId, ILookup<string, SceneAsset> byParent)
        => byParent[assetId].OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private static IEnumerable<SceneAsset> Descendants(string assetId, ILookup<string, SceneAsset> byParent)
    {
        foreach (var child in byParent[assetId])
        {
            yield return child;
            foreach (var descendant in Descendants(child.Id, byParent))
            {
                yield return descendant;
            }
        }
    }

    private static AssetTreeItem ToLocationContainerItem(SceneAsset asset) => new()    {
        SourceStore = "Location",
        AssetId = asset.Id,
        Name = asset.Name,
        KindLabel = asset.Type?.ToString() ?? asset.Kind.ToString(),
        KindFilter = asset.Type?.ToString() ?? asset.Kind.ToString(),
        ApprovalFilter = asset.ProductionApprovalStatus?.ToString() ?? "Unreviewed",
        StatusLabel = asset.ProductionApprovalStatus is not null
            ? $"{asset.Status} · {asset.ProductionApprovalStatus}"
            : asset.Status.ToString(),
        CreatedUtc = asset.CreatedUtc,
        Href = $"/locations/{Uri.EscapeDataString(asset.Id)}"
    };

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
