using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The Location-stage service: resolves the moment's location into a suggestion (never a lock), creates a location
/// container from the moment's place-only seed, and binds/clears the per-POV backdrop plus the moment→container link.
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

    Task<SceneMomentLocationLink?> GetMomentLinkAsync(
        string momentId,
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
