using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed record SceneBeatProductionSourceSnapshot(
    int SchemaVersion,
    string CatalogueId,
    int CatalogueVersion,
    SceneBeatProductionBeatSnapshot Beat,
    SceneBeatProductionTurnSnapshot Turn,
    IReadOnlyList<SceneBeatCatalogueEvidenceSnapshot> Evidence,
    IReadOnlyList<SceneBeatCatalogueProfileSnapshot> Profiles);

public sealed record SceneBeatProductionBeatSnapshot(
    string BeatId,
    int Order,
    string Label,
    string Synopsis,
    string PrimaryLocation,
    IReadOnlyList<SceneBeatProductionParticipantSnapshot> Participants,
    IReadOnlyList<string> EvidenceKeys);

public sealed record SceneBeatProductionParticipantSnapshot(string Name, string Involvement, string ProfileKey);

public sealed record SceneBeatProductionTurnSnapshot(
    string SessionId,
    string TurnId,
    int TurnIndex,
    string TurnKind,
    DateTime StartedUtc,
    DateTime CompletedUtc,
    string TurnMembershipSha256);

public sealed class SceneBeatProductionSnapshotBuilder
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SceneBeatProductionSourceSnapshot Build(SceneBeatCatalogue catalogue, SceneBeatCatalogueEntry entry)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(entry);
        if (catalogue.Status != SceneBeatCatalogueStatus.Complete)
            throw new InvalidOperationException($"Beat Production requires completed catalogue '{catalogue.Id}'.");
        if (!string.Equals(entry.CatalogueId, catalogue.Id, StringComparison.Ordinal))
            throw new InvalidOperationException($"Beat '{entry.BeatId}' does not belong to catalogue '{catalogue.Id}'.");
        if (!catalogue.Entries.Any(item => string.Equals(item.BeatId, entry.BeatId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Beat '{entry.BeatId}' is not a persisted member of catalogue '{catalogue.Id}'.");

        var source = Deserialize<SceneBeatCatalogueInputSnapshot>(catalogue.InputSnapshotJson, "catalogue input snapshot");
        if (source.SchemaVersion != SceneBeatCatalogueSnapshotBuilder.CurrentSchemaVersion)
            throw new InvalidOperationException($"Catalogue input snapshot schemaVersion {source.SchemaVersion} is unsupported.");
        if (!string.Equals(source.SessionId, catalogue.SessionId, StringComparison.Ordinal)
            || !string.Equals(source.TurnId, catalogue.TurnId, StringComparison.Ordinal))
            throw new InvalidOperationException("Catalogue input snapshot lineage does not match its persisted catalogue.");

        var evidenceIds = Deserialize<string[]>(entry.EvidenceInteractionIdsJson, "Beat evidence interaction ids");
        RequireUniqueNonEmpty(evidenceIds, "Beat evidence interaction ids");
        var evidenceById = source.Evidence.ToDictionary(item => item.InteractionId, StringComparer.Ordinal);
        var missingEvidence = evidenceIds.Where(id => !evidenceById.ContainsKey(id)).ToList();
        if (missingEvidence.Count > 0)
            throw new InvalidOperationException($"Beat evidence is missing from the immutable Turn snapshot: {string.Join(", ", missingEvidence)}.");
        var evidence = evidenceIds.Select(id => evidenceById[id]).ToList();
        if (!evidence.Any(item => string.Equals(item.Key, "n0", StringComparison.Ordinal)))
            throw new InvalidOperationException("Beat Production source must include Narrative evidence key n0.");

        var participants = Deserialize<ParticipantInput[]>(entry.ParticipantSummaryJson, "Beat participants");
        if (participants.Length == 0)
            throw new InvalidOperationException("Beat Production source requires at least one participant.");
        RequireUniqueNonEmpty(participants.Select(item => item.Name).ToList(), "Beat participant names");
        var profilesByName = source.Profiles.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var missingProfiles = participants.Where(item => !profilesByName.ContainsKey(item.Name)).Select(item => item.Name).ToList();
        if (missingProfiles.Count > 0)
            throw new InvalidOperationException($"Beat participants are missing immutable profiles: {string.Join(", ", missingProfiles)}.");
        var selectedProfiles = participants.Select(item => profilesByName[item.Name]).ToList();

        return new SceneBeatProductionSourceSnapshot(
            CurrentSchemaVersion,
            catalogue.Id,
            catalogue.Version,
            new SceneBeatProductionBeatSnapshot(
                entry.BeatId,
                entry.Order,
                entry.Label,
                entry.BeatSynopsis,
                entry.PrimaryLocation,
                participants.Select(item => new SceneBeatProductionParticipantSnapshot(
                    item.Name,
                    item.Involvement,
                    profilesByName[item.Name].Key)).ToList(),
                evidence.Select(item => item.Key).ToList()),
            new SceneBeatProductionTurnSnapshot(
                source.SessionId,
                source.TurnId,
                source.TurnIndex,
                source.TurnKind,
                source.StartedUtc,
                source.CompletedUtc,
                source.TurnMembershipSha256),
            evidence,
            selectedProfiles);
    }

    public string Serialize(SceneBeatProductionSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
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

    private static void RequireUniqueNonEmpty(IReadOnlyList<string> values, string source)
    {
        if (values.Any(string.IsNullOrWhiteSpace)
            || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new InvalidOperationException($"{source} must be non-empty and unique.");
    }

    private sealed record ParticipantInput(string Name, string Involvement);
}