using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneMomentEnrichmentSnapshotBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public SceneMomentEnrichmentSourceSnapshot Build(
        SceneMoment selectedMoment,
        SceneMomentSet momentSet,
        SceneBeatProductionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(selectedMoment);
        ArgumentNullException.ThrowIfNull(momentSet);
        ArgumentNullException.ThrowIfNull(plan);
        ValidateLineage(selectedMoment, momentSet, plan);

        var discovery = DeserializeDiscoverySnapshot(
            momentSet.BeatSnapshotJson,
            momentSet.TurnEvidenceSnapshotJson);
        if (discovery.SchemaVersion != SceneMomentDiscoveryContract.CurrentSchemaVersion
            || !string.Equals(discovery.CatalogueId, momentSet.CatalogueId, StringComparison.Ordinal)
            || !string.Equals(discovery.BeatId, momentSet.BeatId, StringComparison.Ordinal)
            || !string.Equals(discovery.BeatProductionPlanId, plan.Id, StringComparison.Ordinal)
            || discovery.BeatProductionPlanVersion != plan.Version)
            throw new InvalidOperationException("Moment enrichment Beat snapshot lineage does not match its persisted parents.");

        var planSource = Deserialize<SceneBeatProductionSourceSnapshot>(
            plan.SourceSnapshotJson,
            "Beat Production source snapshot");
        if (planSource.SchemaVersion != SceneBeatProductionSnapshotBuilder.CurrentSchemaVersion
            || !string.Equals(planSource.CatalogueId, plan.CatalogueId, StringComparison.Ordinal)
            || planSource.CatalogueVersion != plan.CatalogueVersion
            || !string.Equals(planSource.Beat.BeatId, plan.BeatId, StringComparison.Ordinal))
            throw new InvalidOperationException("Moment enrichment Beat Production source lineage does not match its persisted plan.");

        var participantInputs = Deserialize<ParticipantInput[]>(
            selectedMoment.ParticipantSummaryJson,
            $"Moment '{selectedMoment.MomentId}' participants");
        if (participantInputs.Length == 0)
            throw new InvalidOperationException($"Moment '{selectedMoment.MomentId}' requires at least one participant.");
        RequireUnique(participantInputs.Select(item => item.ProfileKey), $"Moment '{selectedMoment.MomentId}' participant keys");

        var profilesByKey = planSource.Profiles.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var missingProfiles = participantInputs
            .Where(item => !profilesByKey.ContainsKey(item.ProfileKey))
            .Select(item => item.ProfileKey)
            .ToList();
        if (missingProfiles.Count > 0)
            throw new InvalidOperationException($"Moment enrichment is missing authoritative profiles: {string.Join(", ", missingProfiles)}.");
        var participants = participantInputs.Select(item =>
        {
            var profile = profilesByKey[item.ProfileKey];
            if (string.IsNullOrWhiteSpace(profile.CharacterId))
                throw new InvalidOperationException($"Moment enrichment profile '{profile.Key}' has no authoritative Character id.");
            return new SceneMomentEnrichmentParticipantSnapshot(
                profile.Key,
                profile.CharacterId,
                profile.Name,
                item.Involvement,
                profile.Role,
                profile.Gender,
                profile.Description,
                profile.Appearance,
                profile.Clothing);
        }).ToArray();

        var roles = Deserialize<SceneMomentProductionRole[]>(
            selectedMoment.ProductionRolesJson,
            $"Moment '{selectedMoment.MomentId}' production roles");
        RequireUnique(roles.Select(item => item.ToString()), $"Moment '{selectedMoment.MomentId}' production roles");

        var citedEvidenceIds = Deserialize<string[]>(
            selectedMoment.EvidenceInteractionIdsJson,
            $"Moment '{selectedMoment.MomentId}' evidence interaction ids");
        RequireUnique(citedEvidenceIds, $"Moment '{selectedMoment.MomentId}' evidence interaction ids");
        var evidenceById = discovery.Evidence.ToDictionary(item => item.InteractionId, StringComparer.Ordinal);
        var missingEvidence = citedEvidenceIds.Where(id => !evidenceById.ContainsKey(id)).ToList();
        if (missingEvidence.Count > 0)
            throw new InvalidOperationException($"Moment evidence is missing from the immutable Turn snapshot: {string.Join(", ", missingEvidence)}.");
        var narrative = discovery.Evidence.Where(item => string.Equals(item.Key, "n0", StringComparison.Ordinal)).ToList();
        if (narrative.Count != 1)
            throw new InvalidOperationException("Moment enrichment requires exactly one authoritative Narrative evidence key n0.");
        var selectedEvidence = discovery.Evidence
            .Where(item => citedEvidenceIds.Contains(item.InteractionId, StringComparer.Ordinal)
                || string.Equals(item.Key, "n0", StringComparison.Ordinal))
            .OrderBy(item => item.SourceOrder)
            .Select(item => item with { })
            .ToArray();

        var moment = new SceneMomentEnrichmentSelectedMomentSnapshot(
            momentSet.Id,
            momentSet.Version,
            selectedMoment.MomentId,
            selectedMoment.Order,
            selectedMoment.Label,
            selectedMoment.TemporalAnchor,
            selectedMoment.FrozenState,
            selectedMoment.VisibleAction,
            selectedMoment.CompositionRationale,
            participants,
            roles.Select(item => item.ToString()).ToArray(),
            citedEvidenceIds.ToArray());

        return new SceneMomentEnrichmentSourceSnapshot(
            SceneMomentEnrichmentContract.CurrentSchemaVersion,
            plan.CatalogueId,
            plan.CatalogueVersion,
            plan.BeatId,
            plan.Id,
            plan.Version,
            planSource.Beat.Label,
            planSource.Beat.Synopsis,
            planSource.Beat.PrimaryLocation,
            RequireJson(plan.NarrativeArcJson, "Beat Production narrative arc"),
            RequireJson(plan.TimelineJson, "Beat Production timeline"),
            RequireJson(plan.ActionArcJson, "Beat Production action arc"),
            RequireJson(plan.StartContinuityJson, "Beat Production start continuity"),
            RequireJson(plan.EndContinuityJson, "Beat Production end continuity"),
            moment,
            ParseSoundCues(plan.SoundEventCuesJson),
            selectedEvidence);
    }

    public string SerializeMomentSnapshot(SceneMomentEnrichmentSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(new SceneMomentEnrichmentMomentSnapshot(
            snapshot.SchemaVersion,
            snapshot.CatalogueId,
            snapshot.CatalogueVersion,
            snapshot.BeatId,
            snapshot.BeatProductionPlanId,
            snapshot.BeatProductionPlanVersion,
            snapshot.BeatLabel,
            snapshot.BeatSynopsis,
            snapshot.PrimaryLocation,
            snapshot.NarrativeArcJson,
            snapshot.TimelineJson,
            snapshot.ActionArcJson,
            snapshot.StartContinuityJson,
            snapshot.EndContinuityJson,
            snapshot.Moment,
            snapshot.SoundCues), JsonOptions);
    }

    public string SerializeEvidenceSnapshot(SceneMomentEnrichmentSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot.Evidence, JsonOptions);
    }

    public SceneMomentEnrichmentSourceSnapshot Deserialize(
        string momentSnapshotJson,
        string evidenceSnapshotJson)
    {
        var moment = Deserialize<SceneMomentEnrichmentMomentSnapshot>(
            momentSnapshotJson,
            "Moment enrichment Moment snapshot");
        var evidence = Deserialize<IReadOnlyList<SceneMomentDiscoveryEvidenceSnapshot>>(
            evidenceSnapshotJson,
            "Moment enrichment evidence snapshot");
        return new SceneMomentEnrichmentSourceSnapshot(
            moment.SchemaVersion,
            moment.CatalogueId,
            moment.CatalogueVersion,
            moment.BeatId,
            moment.BeatProductionPlanId,
            moment.BeatProductionPlanVersion,
            moment.BeatLabel,
            moment.BeatSynopsis,
            moment.PrimaryLocation,
            moment.NarrativeArcJson,
            moment.TimelineJson,
            moment.ActionArcJson,
            moment.StartContinuityJson,
            moment.EndContinuityJson,
            moment.Moment,
            moment.SoundCues,
            evidence);
    }

    private static void ValidateLineage(
        SceneMoment selectedMoment,
        SceneMomentSet momentSet,
        SceneBeatProductionPlan plan)
    {
        if (plan.Status != SceneBeatCatalogueStatus.Complete || plan.Version <= 0)
            throw new InvalidOperationException($"Moment enrichment requires completed Beat Production Plan '{plan.Id}' with a positive version.");
        if (momentSet.Status != SceneBeatCatalogueStatus.Complete || momentSet.Version <= 0)
            throw new InvalidOperationException($"Moment enrichment requires completed Moment Set '{momentSet.Id}' with a positive version.");
        if (!string.Equals(momentSet.CatalogueId, plan.CatalogueId, StringComparison.Ordinal)
            || !string.Equals(momentSet.BeatId, plan.BeatId, StringComparison.Ordinal)
            || !string.Equals(momentSet.BeatProductionPlanId, plan.Id, StringComparison.Ordinal)
            || momentSet.BeatProductionPlanVersion != plan.Version)
            throw new InvalidOperationException("Moment enrichment parent lineage does not match.");
        if (!string.Equals(selectedMoment.MomentSetId, momentSet.Id, StringComparison.Ordinal)
            || momentSet.Moments.Count(item => ReferenceEquals(item, selectedMoment)
                || string.Equals(item.MomentId, selectedMoment.MomentId, StringComparison.Ordinal)) != 1)
            throw new InvalidOperationException($"Selected Moment '{selectedMoment.MomentId}' is not a unique member of Moment Set '{momentSet.Id}'.");
    }

    private static IReadOnlyList<SceneMomentEnrichmentSoundCueSnapshot> ParseSoundCues(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Beat Production sound event cues are required.");
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Beat Production sound event cues must be an array.");
            var cues = document.RootElement.EnumerateArray().Select(item =>
            {
                var window = item.GetProperty("window");
                return new SceneMomentEnrichmentSoundCueSnapshot(
                    RequiredString(item, "cueKey"),
                    OptionalString(item, "eventKey"),
                    RequiredString(item, "description"),
                    OptionalDecimal(window, "startSeconds"),
                    OptionalDecimal(window, "endSeconds"),
                    OptionalString(window, "startEventKey"),
                    OptionalString(window, "endEventKey"));
            }).ToArray();
            RequireUnique(cues.Select(item => item.CueKey), "Beat Production sound cue keys", allowEmpty: true);
            return cues;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Beat Production sound event cues are invalid JSON.", ex);
        }
    }

    private static T Deserialize<T>(string json, string source)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"{source} is required.");
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException($"{source} was null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{source} is invalid JSON.", ex);
        }
    }

    private static SceneMomentDiscoverySourceSnapshot DeserializeDiscoverySnapshot(
        string beatSnapshotJson,
        string evidenceSnapshotJson)
    {
        var beat = Deserialize<SceneMomentDiscoveryBeatSnapshot>(beatSnapshotJson, "Moment Set Beat snapshot");
        var evidence = Deserialize<IReadOnlyList<SceneMomentDiscoveryEvidenceSnapshot>>(evidenceSnapshotJson, "Moment Set evidence snapshot");
        return new SceneMomentDiscoverySourceSnapshot(
            beat.SchemaVersion, beat.CatalogueId, beat.CatalogueVersion, beat.BeatId,
            beat.BeatProductionPlanId, beat.BeatProductionPlanVersion, beat.BeatLabel,
            beat.BeatSynopsis, beat.PrimaryLocation, beat.NarrativeArcJson, beat.TimelineJson,
            beat.ActionArcJson, beat.StartContinuityJson, beat.EndContinuityJson,
            beat.VideoCoverage, evidence, beat.Profiles);
    }

    private static string RequireJson(string json, string source)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"{source} is required.");
        try
        {
            using var _ = JsonDocument.Parse(json);
            return json;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{source} is invalid JSON.", ex);
        }
    }

    private static string RequiredString(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Beat Production sound cue {name} is required.")
            : value;
    }

    private static string? OptionalString(JsonElement element, string name)
        => element.GetProperty(name).ValueKind == JsonValueKind.Null
            ? null
            : element.GetProperty(name).GetString();

    private static decimal? OptionalDecimal(JsonElement element, string name)
        => element.GetProperty(name).ValueKind == JsonValueKind.Null
            ? null
            : element.GetProperty(name).GetDecimal();

    private static void RequireUnique(IEnumerable<string> values, string source, bool allowEmpty = false)
    {
        var list = values.ToList();
        if ((!allowEmpty && list.Count == 0)
            || list.Any(string.IsNullOrWhiteSpace)
            || list.Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new InvalidOperationException($"{source} must be non-empty and unique.");
    }

    private sealed record ParticipantInput(string ProfileKey, string Involvement);
}