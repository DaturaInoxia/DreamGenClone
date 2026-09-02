using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneMomentDiscoveryParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] SequentialStateMarkers =
    [
        " before ", " after ", " then ", " followed by ", " transitions to ", " moves from ", " and then "
    ];

    public SceneMomentSetData Parse(
        string momentSetId,
        string rawResponse,
        SceneMomentDiscoverySourceSnapshot snapshot)
    {
        Require(momentSetId, "Moment Set id");
        Require(rawResponse, "Moment discovery response");
        ArgumentNullException.ThrowIfNull(snapshot);

        Response response;
        try
        {
            response = JsonSerializer.Deserialize<Response>(rawResponse, JsonOptions)
                ?? throw new InvalidOperationException("Moment discovery response was null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Moment discovery returned malformed or contract-invalid JSON.", ex);
        }

        if (response.SchemaVersion != SceneMomentDiscoveryContract.CurrentSchemaVersion)
            throw new InvalidOperationException($"Moment discovery returned unsupported schemaVersion {response.SchemaVersion}.");
        if (!string.Equals(response.CatalogueBeatId, snapshot.BeatId, StringComparison.Ordinal))
            throw new InvalidOperationException("Moment discovery response does not match the selected Beat.");
        if (response.Moments.Count is < 2 or > 4)
            throw new InvalidOperationException("Moment discovery requires 2 to 4 Moments.");
        if (!response.Moments.Select(moment => moment.Order).SequenceEqual(Enumerable.Range(1, response.Moments.Count)))
            throw new InvalidOperationException("Moment discovery order must be contiguous, positive, and temporal.");
        RequireUnique(response.Moments.Select(moment => moment.MomentId), "Moment ids");
        if (response.Moments.Count(moment => string.Equals(moment.MomentId, response.RecommendedMomentId, StringComparison.Ordinal)) != 1)
            throw new InvalidOperationException("Recommended Moment id must identify exactly one returned Moment.");

        var knownProfiles = snapshot.Profiles.ToDictionary(profile => profile.Key, StringComparer.Ordinal);
        var knownEvidence = snapshot.Evidence.ToDictionary(evidence => evidence.Key, StringComparer.Ordinal);
        var moments = response.Moments.Select(item => ParseMoment(
            momentSetId, item, knownProfiles, knownEvidence)).ToList();
        var recommended = response.Moments.Single(moment => moment.MomentId == response.RecommendedMomentId);
        if (!recommended.ProductionRoles.Contains(SceneMomentProductionRole.StillCandidate))
            throw new InvalidOperationException("Recommended Moment must have the StillCandidate production role.");

        var requiredRoles = ResolveRequiredRoles(snapshot.VideoCoverage);
        var providedRoles = response.Moments.SelectMany(moment => moment.ProductionRoles).ToHashSet();
        var missingRoles = requiredRoles.Where(role => !providedRoles.Contains(role)).ToList();
        if (missingRoles.Count > 0)
            throw new InvalidOperationException($"Moment discovery is missing required production roles: {string.Join(", ", missingRoles)}.");

        return new SceneMomentSetData(response.RecommendedMomentId, moments);
    }

    private static SceneMoment ParseMoment(
        string momentSetId,
        MomentInput item,
        IReadOnlyDictionary<string, SceneMomentDiscoveryProfileSnapshot> knownProfiles,
        IReadOnlyDictionary<string, SceneMomentDiscoveryEvidenceSnapshot> knownEvidence)
    {
        Require(item.MomentId, "Moment id");
        Require(item.Label, $"Moment '{item.MomentId}' label");
        Require(item.TemporalAnchor, $"Moment '{item.MomentId}' temporal anchor");
        Require(item.FrozenState, $"Moment '{item.MomentId}' frozen state");
        Require(item.VisibleAction, $"Moment '{item.MomentId}' visible action");
        Require(item.CompositionRationale, $"Moment '{item.MomentId}' composition rationale");
        ValidateFrozenText(item.TemporalAnchor, item.MomentId, "temporal anchor");
        ValidateFrozenText(item.FrozenState, item.MomentId, "frozen state");
        ValidateFrozenText(item.VisibleAction, item.MomentId, "visible action");
        if (item.Participants.Count == 0)
            throw new InvalidOperationException($"Moment '{item.MomentId}' requires at least one participant.");
        RequireUnique(item.Participants.Select(participant => participant.ProfileKey), $"Moment '{item.MomentId}' participant keys");
        var unknownProfiles = item.Participants.Where(participant => !knownProfiles.ContainsKey(participant.ProfileKey)).Select(participant => participant.ProfileKey).ToList();
        if (unknownProfiles.Count > 0)
            throw new InvalidOperationException($"Moment '{item.MomentId}' references unknown profile keys: {string.Join(", ", unknownProfiles)}.");
        if (!item.Participants.Any(participant => string.Equals(participant.Involvement, "active", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Moment '{item.MomentId}' requires at least one active participant.");
        if (item.ProductionRoles.Count == 0 || item.ProductionRoles.Distinct().Count() != item.ProductionRoles.Count)
            throw new InvalidOperationException($"Moment '{item.MomentId}' production roles must be non-empty and unique.");
        RequireUnique(item.EvidenceKeys, $"Moment '{item.MomentId}' evidence keys");
        var unknownEvidence = item.EvidenceKeys.Where(key => !knownEvidence.ContainsKey(key)).ToList();
        if (unknownEvidence.Count > 0)
            throw new InvalidOperationException($"Moment '{item.MomentId}' references unknown evidence keys: {string.Join(", ", unknownEvidence)}.");

        return new SceneMoment
        {
            MomentSetId = momentSetId,
            MomentId = item.MomentId,
            Order = item.Order,
            Label = item.Label,
            TemporalAnchor = item.TemporalAnchor,
            FrozenState = item.FrozenState,
            VisibleAction = item.VisibleAction,
            ParticipantSummaryJson = JsonSerializer.Serialize(item.Participants, JsonOptions),
            CompositionRationale = item.CompositionRationale,
            ProductionRolesJson = JsonSerializer.Serialize(item.ProductionRoles, JsonOptions),
            EvidenceInteractionIdsJson = JsonSerializer.Serialize(
                item.EvidenceKeys.Select(key => knownEvidence[key].InteractionId).ToList(), JsonOptions)
        };
    }

    private static HashSet<SceneMomentProductionRole> ResolveRequiredRoles(
        IReadOnlyList<SceneVideoCoverageSnapshot> coverage)
    {
        var roles = new HashSet<SceneMomentProductionRole>();
        foreach (var role in coverage.SelectMany(item => item.RequiredMomentRoles))
        {
            roles.Add(role.ToLowerInvariant() switch
            {
                "start" => SceneMomentProductionRole.VideoStart,
                "end" => SceneMomentProductionRole.VideoEnd,
                "internal" or "internalkeyframe" => SceneMomentProductionRole.VideoInternalKeyframe,
                _ => throw new InvalidOperationException($"Beat Production requested unsupported Moment role '{role}'.")
            });
        }
        return roles;
    }

    private static void ValidateFrozenText(string value, string momentId, string field)
    {
        var normalized = $" {value.Trim().ToLowerInvariant()} ";
        if (SequentialStateMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Moment '{momentId}' {field} describes sequential before/after action instead of one frozen state.");
    }

    private static void RequireUnique(IEnumerable<string> values, string context)
    {
        var list = values.ToList();
        if (list.Count == 0 || list.Any(string.IsNullOrWhiteSpace) || list.Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new InvalidOperationException($"{context} must be non-empty and unique.");
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{name} is required.");
    }

    private sealed class Response
    {
        public required int SchemaVersion { get; init; }
        public required string CatalogueBeatId { get; init; }
        public required string RecommendedMomentId { get; init; }
        public required List<MomentInput> Moments { get; init; }
    }

    private sealed class MomentInput
    {
        public required string MomentId { get; init; }
        public required int Order { get; init; }
        public required string Label { get; init; }
        public required string TemporalAnchor { get; init; }
        public required string FrozenState { get; init; }
        public required string VisibleAction { get; init; }
        public required List<ParticipantInput> Participants { get; init; }
        public required string CompositionRationale { get; init; }
        public required List<SceneMomentProductionRole> ProductionRoles { get; init; }
        public required List<string> EvidenceKeys { get; init; }
    }

    private sealed class ParticipantInput
    {
        public required string ProfileKey { get; init; }
        public required string Involvement { get; init; }
    }
}