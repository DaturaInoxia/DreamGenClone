using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The Location-stage service. Resolution produces a SUGGESTION the operator confirms; the only writer of the group
/// backdrop and the per-POV moment→container link is the operator's bind action (never automatic). Missing required
/// values fail with an explicit message rather than silently continuing.
///
/// <para>
/// One binding per POV: Omniscient and each character POV keep their own location image, and the (moment, POV) link is
/// the durable half that carries it across a re-enrichment.
/// </para>
/// </summary>
public sealed class SceneMomentLocationService : ISceneMomentLocationService
{
    private const string OperatorBoundOrigin = "operator-bound";

    private readonly ISceneAssetService _assets;
    private readonly ISceneImageProductionGroupRepository _groups;
    private readonly ISceneMomentLocationLinkRepository _links;
    private readonly ILocationImageAnalysisQueue _analysisQueue;

    public SceneMomentLocationService(
        ISceneAssetService assets,
        ISceneImageProductionGroupRepository groups,
        ISceneMomentLocationLinkRepository links,
        ILocationImageAnalysisQueue analysisQueue)
    {
        _assets = assets;
        _groups = groups;
        _links = links;
        _analysisQueue = analysisQueue;
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

        var group = await _groups.GetAsync(groupId, cancellationToken)
            ?? throw new InvalidOperationException($"Production group '{groupId}' was not found.");

        var (container, image, backdrop) = await RequireBindableAsync(imageId, cancellationToken);

        await _groups.SetLocationBackdropAsync(
            groupId, Serialize(backdrop), DateTime.UtcNow, cancellationToken);

        // The durable half of the same binding, keyed on the POV that bound it: the group is keyed on the Moment
        // enrichment, so this is what carries the binding across a re-enrichment.
        await _links.UpsertAsync(new SceneMomentLocationLink
        {
            MomentId = group.MomentId,
            Pov = group.Pov,
            LocationAssetId = backdrop.AssetId,
            ScenarioLocationId = container.ScenarioLocationId,
            Origin = OperatorBoundOrigin,
            BackdropJson = Serialize(backdrop),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        }, cancellationToken);

        // Binding a location image is when its contents become relevant to the prompt, so this is where it is read
        // (B-160). Queued, not awaited: the reading is a vision call, and a bind that blocked on it would make the
        // operator's own action wait on a model. The handler is idempotent per checksum, so a location bound repeatedly
        // is read once, and a re-bound image with a new checksum is read again.
        await _analysisQueue.RequestAsync(image.Id, cancellationToken: cancellationToken);

        return backdrop;
    }

    public async Task<SceneImageProductionGroup> ClearBackdropAsync(
        string groupId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            throw new InvalidOperationException("A production group id is required to clear a backdrop.");

        var group = await _groups.GetAsync(groupId, cancellationToken)
            ?? throw new InvalidOperationException($"Production group '{groupId}' was not found.");

        var now = DateTime.UtcNow;
        var cleared = await _groups.SetLocationBackdropAsync(groupId, null, now, cancellationToken);

        // The durable row must stop carrying the image too, or the next enrichment would restore the binding the
        // operator just cleared. The container it names is unchanged: clearing the image is not un-resolving the place.
        var link = await _links.GetAsync(group.MomentId, group.Pov, cancellationToken);
        if (link is not null && !string.IsNullOrWhiteSpace(link.BackdropJson))
        {
            link.BackdropJson = null;
            link.UpdatedUtc = now;
            await _links.UpsertAsync(link, cancellationToken);
        }

        return cleared;
    }

    public async Task<SceneImageProductionGroup> RestoreBackdropAsync(
        string groupId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            throw new InvalidOperationException("A production group id is required to restore a backdrop.");

        var group = await _groups.GetAsync(groupId, cancellationToken)
            ?? throw new InvalidOperationException($"Production group '{groupId}' was not found.");

        // This enrichment's group already carries the binding; the durable row is read only when it does not.
        if (!string.IsNullOrWhiteSpace(group.LocationBackdropJson))
            return group;

        var link = await _links.GetAsync(group.MomentId, group.Pov, cancellationToken);
        if (link is null || string.IsNullOrWhiteSpace(link.BackdropJson))
            return group;

        var recorded = DeserializeRecordedBackdrop(link, group);
        if (!string.Equals(recorded.AssetId, link.LocationAssetId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The location binding recorded for {group.Pov} POV of moment '{group.MomentId}' names container " +
                $"'{recorded.AssetId}' while the link names '{link.LocationAssetId}'. The two halves disagree, so the " +
                "binding cannot be restored; bind the location again for this POV.");

        // Re-validated through the SAME gate a fresh bind uses: a binding recorded before its image was revoked or
        // renamed does not come back to life as an unapprovable backdrop.
        var (_, image, backdrop) = await RequireBindableAsync(recorded.ImageId, cancellationToken);

        var restored = await _groups.SetLocationBackdropAsync(
            group.Id, Serialize(backdrop), DateTime.UtcNow, cancellationToken);
        await _analysisQueue.RequestAsync(image.Id, cancellationToken: cancellationToken);
        return restored;
    }

    /// <summary>The location binding recorded for one POV of a moment, or null when that POV has bound nothing.</summary>
    public Task<SceneMomentLocationLink?> GetMomentLinkAsync(
        string momentId,
        string pov,
        CancellationToken cancellationToken = default)
        => _links.GetAsync(momentId, pov, cancellationToken);

    /// <summary>
    /// The single gate every location binding passes through — the operator's bind and the restore of a recorded one —
    /// so the rules that make an image bindable exist in exactly one place.
    /// </summary>
    private async Task<(SceneAsset Container, SceneAssetImage Image, SceneImageLocationBackdrop Backdrop)> RequireBindableAsync(
        string imageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageId))
            throw new InvalidOperationException("A location image id is required to bind a backdrop.");

        var image = await _assets.GetImageAsync(imageId, cancellationToken)
            ?? throw new InvalidOperationException($"Location image '{imageId}' was not found.");

        // The bindable rule is the domain's, so the picker that OFFERS images and this gate that REFUSES one cannot
        // disagree about which images are eligible.
        if (SceneImageLocationBindGate.BlockerFor(image) is { } blocker)
            throw new InvalidOperationException(blocker.Message);

        var container = await _assets.GetAssetAsync(image.AssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Location container '{image.AssetId}' for image '{imageId}' was not found.");

        // The gate above is what guarantees the name is non-blank, so the label is read here rather than re-checked.
        var backdrop = SceneImageLocationBackdrop.Create(
            image.AssetId, image.Id, image.Sha256, image.ProductionVersion, image.DisplayName!.Trim());

        return (container, image, backdrop);
    }

    private static SceneImageLocationBackdrop DeserializeRecordedBackdrop(
        SceneMomentLocationLink link,
        SceneImageProductionGroup group)
    {
        SceneImageLocationBackdrop? recorded;
        try
        {
            recorded = JsonSerializer.Deserialize<SceneImageLocationBackdrop>(link.BackdropJson!, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The location binding recorded for {group.Pov} POV of moment '{group.MomentId}' is unreadable: " +
                exception.Message, exception);
        }

        return recorded
            ?? throw new InvalidOperationException(
                $"The location binding recorded for {group.Pov} POV of moment '{group.MomentId}' is empty.");
    }

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
