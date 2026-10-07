using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.Scenarios;

/// <summary>
/// Owns the mapping between a scenario and the Asset Manager containers that represent it: the world/Setting's root
/// container and one container per <see cref="Scenario.Locations"/> entry, parented under it.
///
/// <para>
/// The scenario is the AUTHORITY for this tree. It creates the world container and every scenario-location container;
/// production only ever creates the spot container for a place the moment invented, underneath the container this
/// service established. That is what makes the provenance flow the same way every time instead of drifting into
/// ad-hoc containers that nothing can resolve.
/// </para>
///
/// <para>
/// Runs on scenario save. A blank world name is refused rather than defaulted: a container with an invented name
/// would be a container nobody asked for, and the mapping would be wrong from the first moment.
/// </para>
/// </summary>
public interface IScenarioLocationContainerService
{
    /// <summary>
    /// Ensures the world container and one container per scenario location exist, writing
    /// <see cref="WorldLocationSetting.AssetContainerId"/> and each container's scenario-location mapping back onto
    /// the scenario. Idempotent: an existing container is reused and only corrected when its name or parent drifted.
    /// </summary>
    Task<ScenarioContainerSyncResult> EnsureContainersAsync(
        Scenario scenario,
        CancellationToken cancellationToken = default);

    /// <summary>The containers currently mapped to a scenario, keyed by scenario location id (null key = the world).</summary>
    Task<IReadOnlyDictionary<string?, string>> ListMappedContainerIdsAsync(
        Scenario scenario,
        CancellationToken cancellationToken = default);
}

/// <summary>What one <see cref="IScenarioLocationContainerService.EnsureContainersAsync"/> pass created or reused.</summary>
public sealed record ScenarioContainerSyncResult(
    string WorldContainerId,
    bool WorldContainerCreated,
    int LocationContainersCreated,
    int LocationContainersReused)
{
    public string Summary => WorldContainerCreated
        ? $"Created the world container and {LocationContainersCreated} location container(s)."
        : $"World container already mapped; created {LocationContainersCreated}, reused {LocationContainersReused}.";
}
