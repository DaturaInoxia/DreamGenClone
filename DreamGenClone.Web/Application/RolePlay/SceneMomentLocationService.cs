using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The Location-stage service. Resolution produces a SUGGESTION the operator confirms; the only writer of the group
/// backdrop and the moment→container link is the operator's bind action (never automatic). Missing required values
/// fail with an explicit message rather than silently continuing.
/// </summary>
public sealed class SceneMomentLocationService : ISceneMomentLocationService
{
    private const string OperatorBoundOrigin = "operator-bound";

    private readonly ISceneAssetService _assets;
    private readonly ISceneImageProductionGroupRepository _groups;
    private readonly ISceneMomentLocationLinkRepository _links;

    public SceneMomentLocationService(
        ISceneAssetService assets,
        ISceneImageProductionGroupRepository groups,
        ISceneMomentLocationLinkRepository links)
    {
        _assets = assets;
        _groups = groups;
        _links = links;
    }

    public async Task<SceneMomentLocationSuggestion?> ResolveSuggestionAsync(
        SceneMomentFrozenStateContract frozenState,
        Scenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        ArgumentNullException.ThrowIfNull(scenario);

        var sightline = frozenState.Characters?.FirstOrDefault()?.Sightline;
        var match = SceneMomentLocationResolver.Match(frozenState.Location, scenario.Locations);
        if (!match.Matched || match.Location is null)
        {
            return new SceneMomentLocationSuggestion(
                Matched: false,
                MatchReason: "No scenario location matched — this place is new.",
                Location: null,
                Container: null,
                SpotImage: null,
                SightlineContext: sightline);
        }

        var container = await ResolveCurrentContainerAsync(
            scenario.Id, match.Location.Id, cancellationToken);
        if (container is null)
        {
            return new SceneMomentLocationSuggestion(
                Matched: true,
                MatchReason: $"matched scenario location '{match.Location.Name}' by name, but it has no container yet.",
                Location: match.Location,
                Container: null,
                SpotImage: null,
                SightlineContext: sightline);
        }

        SceneAssetImage? spot = null;
        if (!string.IsNullOrWhiteSpace(match.SpotSuggestion))
        {
            spot = await ResolveSpotImageAsync(container.Id, match.SpotSuggestion, cancellationToken);
        }

        return new SceneMomentLocationSuggestion(
            Matched: true,
            MatchReason: string.IsNullOrWhiteSpace(match.SpotSuggestion)
                ? $"matched scenario location '{match.Location.Name}' by name."
                : $"matched scenario location by name; spot '{match.SpotSuggestion}'",
            Location: match.Location,
            Container: container,
            SpotImage: spot,
            SightlineContext: sightline,
            SpotSuggestion: match.SpotSuggestion);
    }

    public async Task<SceneImageLocationBackdrop> BindBackdropAsync(
        string groupId,
        string imageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            throw new InvalidOperationException("A production group id is required to bind a backdrop.");
        if (string.IsNullOrWhiteSpace(imageId))
            throw new InvalidOperationException("A location image id is required to bind a backdrop.");

        var group = await _groups.GetAsync(groupId, cancellationToken)
            ?? throw new InvalidOperationException($"Production group '{groupId}' was not found.");
        var image = await _assets.GetImageAsync(imageId, cancellationToken)
            ?? throw new InvalidOperationException($"Location image '{imageId}' was not found.");

        if (image.ProductionApprovalStatus != SceneAssetProductionApprovalStatus.Approved)
            throw new InvalidOperationException(
                $"Location image '{imageId}' is not approved for production, so it cannot be bound as a backdrop. " +
                "Approve it in Location Studio first.");
        if (string.IsNullOrWhiteSpace(image.DisplayName))
            throw new InvalidOperationException(
                $"Location image '{imageId}' has no name, so it cannot be bound as a backdrop. Name it first.");
        if (string.IsNullOrWhiteSpace(image.Sha256))
            throw new InvalidOperationException($"Location image '{imageId}' has no checksum, so it cannot be bound.");

        var container = await _assets.GetAssetAsync(image.AssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Location container '{image.AssetId}' for image '{imageId}' was not found.");

        var backdrop = SceneImageLocationBackdrop.Create(
            image.AssetId, image.Id, image.Sha256, image.ProductionVersion, image.DisplayName.Trim());

        await _groups.SetLocationBackdropAsync(
            groupId, Serialize(backdrop), DateTime.UtcNow, cancellationToken);

        await _links.UpsertAsync(new SceneMomentLocationLink
        {
            MomentId = group.MomentId,
            LocationAssetId = backdrop.AssetId,
            ScenarioLocationId = container.ScenarioLocationId,
            Origin = OperatorBoundOrigin,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        }, cancellationToken);

        return backdrop;
    }

    public async Task<SceneImageProductionGroup> ClearBackdropAsync(
        string groupId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            throw new InvalidOperationException("A production group id is required to clear a backdrop.");

        return await _groups.SetLocationBackdropAsync(groupId, null, DateTime.UtcNow, cancellationToken);
    }

    public Task<SceneMomentLocationLink?> GetMomentLinkAsync(
        string momentId,
        CancellationToken cancellationToken = default)
        => _links.GetAsync(momentId, cancellationToken);

    public async Task<SceneMomentLocationChain> ResolveContainerChainAsync(
        Scenario scenario,
        SceneMomentLocationSuggestion? suggestion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var worldContainerId = scenario.Setting?.WorldLocation?.AssetContainerId;
        if (string.IsNullOrWhiteSpace(worldContainerId))
        {
            throw new InvalidOperationException(
                $"Scenario '{scenario.Name ?? scenario.Id}' has no world container mapped, so a location created from " +
                "this moment would have no place in the hierarchy. Open the scenario's Setting/World section, name the " +
                "world container and save — the scenario then creates it and every location container under it.");
        }

        var worldContainer = await _assets.GetAssetAsync(worldContainerId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Scenario '{scenario.Name ?? scenario.Id}' maps world container '{worldContainerId}', which no longer " +
                "exists. Re-save the scenario to recreate it.");

        var locationContainer = suggestion?.Container;
        if (locationContainer is null
            && suggestion?.Location is { } matched
            && !string.IsNullOrWhiteSpace(matched.Id))
        {
            locationContainer = await ResolveCurrentContainerAsync(scenario.Id, matched.Id, cancellationToken);
        }

        return new SceneMomentLocationChain(worldContainer, locationContainer, suggestion?.SpotSuggestion);
    }

    private async Task<SceneAsset?> ResolveCurrentContainerAsync(
        string scenarioId,
        string scenarioLocationId,
        CancellationToken cancellationToken)
    {
        var candidates = (await _assets.ListAssetsAsync(cancellationToken))
            .Where(asset => asset.Type == SceneAssetType.Location
                && string.Equals(asset.ScenarioId, scenarioId, StringComparison.Ordinal)
                && string.Equals(asset.ScenarioLocationId, scenarioLocationId, StringComparison.Ordinal))
            .ToList();

        // The current container is the one no other candidate supersedes.
        var supersededIds = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SupersedesAssetId))
            .Select(candidate => candidate.SupersedesAssetId!)
            .ToHashSet(StringComparer.Ordinal);
        return candidates.FirstOrDefault(candidate => !supersededIds.Contains(candidate.Id))
            ?? candidates.OrderByDescending(candidate => candidate.ProductionVersion).FirstOrDefault();
    }

    private async Task<SceneAssetImage?> ResolveSpotImageAsync(
        string containerId,
        string spotSuggestion,
        CancellationToken cancellationToken)
    {
        var tokens = spotSuggestion
            .Split([' ', '-', '—', '–'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 1)
            .ToList();
        if (tokens.Count == 0)
        {
            return null;
        }

        var images = (await _assets.ListImagesAsync(containerId, cancellationToken))
            .Where(image => image.ProductionApprovalStatus == SceneAssetProductionApprovalStatus.Approved)
            .ToList();

        foreach (var token in tokens)
        {
            var match = images.FirstOrDefault(image =>
                !string.IsNullOrWhiteSpace(image.DisplayName)
                && image.DisplayName.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string Serialize(SceneImageLocationBackdrop backdrop)
        => JsonSerializer.Serialize(backdrop, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>The Location-stage suggestion. Every field is a suggestion the operator confirms; nothing here binds.</summary>
public sealed record SceneMomentLocationSuggestion(
    bool Matched,
    string MatchReason,
    DreamGenClone.Web.Domain.Scenarios.Location? Location,
    SceneAsset? Container,
    SceneAssetImage? SpotImage,
    string? SightlineContext,
    string? SpotSuggestion = null);
