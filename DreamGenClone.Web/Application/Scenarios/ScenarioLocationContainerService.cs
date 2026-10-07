using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.Scenarios;

/// <inheritdoc cref="IScenarioLocationContainerService" />
public sealed class ScenarioLocationContainerService : IScenarioLocationContainerService
{
    private readonly ISceneAssetService _assets;

    public ScenarioLocationContainerService(ISceneAssetService assets)
    {
        _assets = assets;
    }

    public async Task<ScenarioContainerSyncResult> EnsureContainersAsync(
        Scenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (string.IsNullOrWhiteSpace(scenario.Id))
            throw new InvalidOperationException("A scenario id is required before its location containers can be created.");
        if (scenario.Setting is null)
            throw new InvalidOperationException($"Scenario '{scenario.Id}' has no Setting, so it has no world to link a container to.");

        var world = scenario.Setting.WorldLocation ??= new WorldLocationSetting();
        if (string.IsNullOrWhiteSpace(world.Name))
        {
            throw new InvalidOperationException(
                $"Scenario '{scenario.Name ?? scenario.Id}' has no world container name. Name the world (Setting/World) " +
                "before saving: that container is the root every location container in this scenario hangs under, and " +
                "production resolves it to place a moment's location.");
        }

        var containers = (await _assets.ListAssetsAsync(cancellationToken))
            .Where(asset => asset.Type == SceneAssetType.Location)
            .ToList();

        var worldContainerCreated = false;
        var worldContainer = FindById(containers, world.AssetContainerId);
        if (worldContainer is null)
        {
            worldContainer = await _assets.CreateLocationContainerAsync(
                world.Name,
                WorldDescriptionOrDefault(world),
                scenarioId: null,
                scenarioLocationId: null,
                parentAssetId: null,
                cancellationToken);
            containers.Add(worldContainer);
            worldContainerCreated = true;
        }
        else if (!string.Equals(worldContainer.Name, world.Name.Trim(), StringComparison.Ordinal)
            || worldContainer.ParentAssetId is not null)
        {
            // A world container is a ROOT. Correcting the parent here is what keeps the tree well-formed when a
            // location was previously (mis)created as a world.
            worldContainer = await _assets.UpdateLocationContainerAsync(
                worldContainer.Id, world.Name, description: null, parentAssetId: null, cancellationToken);
        }

        world.AssetContainerId = worldContainer.Id;
        if (string.IsNullOrWhiteSpace(world.RenderingDescription))
        {
            world.RenderingDescription = worldContainer.Prompt;
        }

        var created = 0;
        var reused = 0;
        foreach (var location in scenario.Locations)
        {
            if (string.IsNullOrWhiteSpace(location.Id) || string.IsNullOrWhiteSpace(location.Name))
            {
                continue;
            }

            var mapped = containers.FirstOrDefault(asset =>
                string.Equals(asset.ScenarioId, scenario.Id, StringComparison.Ordinal)
                && string.Equals(asset.ScenarioLocationId, location.Id, StringComparison.Ordinal));

            if (mapped is null)
            {
                var child = await _assets.CreateLocationContainerAsync(
                    location.Name,
                    string.IsNullOrWhiteSpace(location.Description) ? location.Name : location.Description!,
                    scenario.Id,
                    location.Id,
                    worldContainer.Id,
                    cancellationToken);
                containers.Add(child);
                created++;
                continue;
            }

            reused++;
            if (!string.Equals(mapped.Name, location.Name.Trim(), StringComparison.Ordinal)
                || !string.Equals(mapped.ParentAssetId, worldContainer.Id, StringComparison.Ordinal))
            {
                await _assets.UpdateLocationContainerAsync(
                    mapped.Id, location.Name, description: null, parentAssetId: worldContainer.Id, cancellationToken);
            }
        }

        return new ScenarioContainerSyncResult(worldContainer.Id, worldContainerCreated, created, reused);
    }

    public async Task<IReadOnlyDictionary<string?, string>> ListMappedContainerIdsAsync(
        Scenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var containers = (await _assets.ListAssetsAsync(cancellationToken))
            .Where(asset => asset.Type == SceneAssetType.Location)
            .ToList();

        var mapped = new Dictionary<string?, string>(StringComparer.Ordinal);
        foreach (var location in scenario.Locations)
        {
            var match = containers.FirstOrDefault(asset =>
                string.Equals(asset.ScenarioId, scenario.Id, StringComparison.Ordinal)
                && string.Equals(asset.ScenarioLocationId, location.Id, StringComparison.Ordinal));
            if (match is not null)
            {
                mapped[location.Id] = match.Id;
            }
        }

        var world = scenario.Setting?.WorldLocation;
        if (world?.AssetContainerId is { Length: > 0 } worldId && FindById(containers, worldId) is not null)
        {
            mapped[null] = worldId;
        }

        return mapped;
    }

    private static SceneAsset? FindById(IReadOnlyList<SceneAsset> containers, string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : containers.FirstOrDefault(asset => string.Equals(asset.Id, id, StringComparison.Ordinal));

    private static string WorldDescriptionOrDefault(WorldLocationSetting world)
        => string.IsNullOrWhiteSpace(world.RenderingDescription)
            ? world.Name!
            : world.RenderingDescription!;
}
