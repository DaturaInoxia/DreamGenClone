using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The Location-stage service: resolves the moment's location into a suggestion (never a lock), creates a location
/// container from the moment's place-only seed, and binds/clears the per-POV backdrop plus that POV's moment→container
/// link. Omniscient and each character POV carry their own binding.
/// </summary>
public interface ISceneMomentLocationService
{
    Task<SceneMomentLocationSuggestion?> ResolveSuggestionAsync(
        SceneMomentFrozenStateContract frozenState,
        Scenario scenario,
        CancellationToken cancellationToken = default);

    Task<SceneImageLocationBackdrop> BindBackdropAsync(
        string groupId,
        string imageId,
        CancellationToken cancellationToken = default);

    Task<SceneImageProductionGroup> ClearBackdropAsync(
        string groupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-seeds a group's backdrop from the durable (moment, POV) binding when the group has none. A re-enrichment
    /// creates a new production group, and this is the ONE place the operator's recorded binding is carried across it:
    /// the group is returned unchanged when it already carries a backdrop for this enrichment, or when this POV has
    /// recorded none. A recorded binding that no longer names an approvable, named image fails loudly rather than being
    /// silently dropped.
    /// </summary>
    Task<SceneImageProductionGroup> RestoreBackdropAsync(
        string groupId,
        CancellationToken cancellationToken = default);

    /// <summary>The location binding recorded for one POV of a moment, or null when that POV has bound nothing.</summary>
    Task<SceneMomentLocationLink?> GetMomentLinkAsync(
        string momentId,
        string pov,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the container chain a moment's place belongs to: the scenario's world container (from
    /// <c>Setting.WorldLocation</c>) and, when the moment's location matched a scenario location, that location's
    /// container. Fails loudly when the scenario has no world container, because the chain is what places the new
    /// container — inventing a root instead would leave the container unreachable from the scenario.
    /// </summary>
    Task<SceneMomentLocationChain> ResolveContainerChainAsync(
        DreamGenClone.Web.Domain.Scenarios.Scenario scenario,
        SceneMomentLocationSuggestion? suggestion,
        CancellationToken cancellationToken = default);
}

/// <summary>The containers a moment's place sits inside, and the spot text that names the new child.</summary>
public sealed record SceneMomentLocationChain(
    SceneAsset WorldContainer,
    SceneAsset? LocationContainer,
    string? SpotSuggestion);
