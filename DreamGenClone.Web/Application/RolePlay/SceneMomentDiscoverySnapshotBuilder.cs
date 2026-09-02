using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneMomentDiscoverySnapshotBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SceneMomentDiscoverySourceSnapshot Build(SceneBeatProductionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Status != SceneBeatCatalogueStatus.Complete)
            throw new InvalidOperationException($"Moment discovery requires completed Beat Production Plan '{plan.Id}'.");
        if (plan.Version <= 0)
            throw new InvalidOperationException("Moment discovery requires a persisted positive Beat Production Plan version.");

        var source = Deserialize<SceneBeatProductionSourceSnapshot>(
            plan.SourceSnapshotJson,
            "Beat Production source snapshot");
        if (source.SchemaVersion != SceneBeatProductionSnapshotBuilder.CurrentSchemaVersion)
            throw new InvalidOperationException($"Beat Production source schemaVersion {source.SchemaVersion} is unsupported.");
        if (!string.Equals(source.CatalogueId, plan.CatalogueId, StringComparison.Ordinal)
            || source.CatalogueVersion != plan.CatalogueVersion
            || !string.Equals(source.Beat.BeatId, plan.BeatId, StringComparison.Ordinal))
            throw new InvalidOperationException("Beat Production source snapshot lineage does not match its persisted plan.");
        if (source.Evidence.Count == 0 || source.Profiles.Count == 0)
            throw new InvalidOperationException("Moment discovery requires immutable Beat evidence and profiles.");

        var involvementByProfile = source.Beat.Participants.ToDictionary(
            participant => participant.ProfileKey,
            participant => participant.Involvement,
            StringComparer.Ordinal);
        var missingProfiles = source.Profiles
            .Where(profile => !involvementByProfile.ContainsKey(profile.Key))
            .Select(profile => profile.Key)
            .ToList();
        if (missingProfiles.Count > 0)
            throw new InvalidOperationException($"Moment discovery profiles are missing Beat involvement: {string.Join(", ", missingProfiles)}.");

        return new SceneMomentDiscoverySourceSnapshot(
            SceneMomentDiscoveryContract.CurrentSchemaVersion,
            plan.CatalogueId,
            plan.CatalogueVersion,
            plan.BeatId,
            plan.Id,
            plan.Version,
            source.Beat.Label,
            source.Beat.Synopsis,
            source.Beat.PrimaryLocation,
            RequireJson(plan.NarrativeArcJson, "Beat Production narrative arc"),
            RequireJson(plan.TimelineJson, "Beat Production timeline"),
            RequireJson(plan.ActionArcJson, "Beat Production action arc"),
            RequireJson(plan.StartContinuityJson, "Beat Production start continuity"),
            RequireJson(plan.EndContinuityJson, "Beat Production end continuity"),
            plan.VideoCoveragePlans.Select(coverage => new SceneVideoCoverageSnapshot(
                coverage.CoverageKey,
                coverage.CoverageKind.ToString(),
                coverage.SourceEventKeys,
                coverage.RequiredMomentRoles,
                coverage.PermittedActionPhases)).ToList(),
            source.Evidence.Select(evidence => new SceneMomentDiscoveryEvidenceSnapshot(
                evidence.Key,
                evidence.SourceOrder,
                evidence.InteractionId,
                evidence.ActorName,
                evidence.InteractionType,
                evidence.Content,
                evidence.SourceSha256)).ToList(),
            source.Profiles.Select(profile => new SceneMomentDiscoveryProfileSnapshot(
                profile.Key,
                profile.CharacterId,
                profile.Name,
                involvementByProfile[profile.Key])).ToList());
    }

    public string SerializeBeatSnapshot(SceneMomentDiscoverySourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(new SceneMomentDiscoveryBeatSnapshot(
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
            snapshot.VideoCoverage,
            snapshot.Profiles), JsonOptions);
    }

    public string SerializeEvidenceSnapshot(SceneMomentDiscoverySourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot.Evidence, JsonOptions);
    }

    public SceneMomentDiscoverySourceSnapshot Deserialize(
        string beatSnapshotJson,
        string evidenceSnapshotJson)
    {
        var beat = Deserialize<SceneMomentDiscoveryBeatSnapshot>(beatSnapshotJson, "Moment discovery Beat snapshot");
        var evidence = Deserialize<IReadOnlyList<SceneMomentDiscoveryEvidenceSnapshot>>(
            evidenceSnapshotJson,
            "Moment discovery evidence snapshot");
        return new SceneMomentDiscoverySourceSnapshot(
            beat.SchemaVersion,
            beat.CatalogueId,
            beat.CatalogueVersion,
            beat.BeatId,
            beat.BeatProductionPlanId,
            beat.BeatProductionPlanVersion,
            beat.BeatLabel,
            beat.BeatSynopsis,
            beat.PrimaryLocation,
            beat.NarrativeArcJson,
            beat.TimelineJson,
            beat.ActionArcJson,
            beat.StartContinuityJson,
            beat.EndContinuityJson,
            beat.VideoCoverage,
            evidence,
            beat.Profiles);
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
}