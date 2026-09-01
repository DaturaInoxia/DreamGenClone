using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneMomentEnrichmentParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly string[] SequentialStateMarkers =
    [
        " before ", " after ", " then ", " followed by ", " transitions to ", " moves from ", " and then "
    ];

    public SceneMomentEnrichmentData Parse(
        string rawResponse,
        SceneMomentEnrichmentSourceSnapshot snapshot)
    {
        Require(rawResponse, "Moment enrichment response");
        ArgumentNullException.ThrowIfNull(snapshot);

        Response response;
        try
        {
            response = JsonSerializer.Deserialize<Response>(rawResponse, JsonOptions)
                ?? throw new InvalidOperationException("Moment enrichment response was null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Moment enrichment returned malformed or contract-invalid JSON.", ex);
        }

        if (response.SchemaVersion != SceneMomentEnrichmentContract.CurrentSchemaVersion)
            throw new InvalidOperationException($"Moment enrichment returned unsupported schemaVersion {response.SchemaVersion}.");
        if (!string.Equals(response.CatalogueBeatId, snapshot.BeatId, StringComparison.Ordinal))
            throw new InvalidOperationException("Moment enrichment response does not match the selected Beat.");
        if (!string.Equals(response.MomentId, snapshot.Moment.MomentId, StringComparison.Ordinal))
            throw new InvalidOperationException("Moment enrichment response does not match the selected Moment.");

        Require(response.VisualDescription, "Moment visual description");
        Require(response.Location, "Moment location");
        Require(response.TimeOfDay, "Moment time of day");
        Require(response.Lighting, "Moment lighting");
        Require(response.Environment, "Moment environment");
        Require(response.Mood, "Moment mood");
        ValidateFrozenText(response.VisualDescription, "visual description");

        RequireUnique(response.Characters.Select(item => item.ProfileKey), "Moment enrichment profile keys");
        var expectedProfiles = snapshot.Moment.Participants.ToDictionary(item => item.ProfileKey, StringComparer.Ordinal);
        var unknownProfiles = response.Characters
            .Where(item => !expectedProfiles.ContainsKey(item.ProfileKey))
            .Select(item => item.ProfileKey)
            .ToList();
        if (unknownProfiles.Count > 0)
            throw new InvalidOperationException($"Moment enrichment references unknown or non-cast profile keys: {string.Join(", ", unknownProfiles)}.");
        var missingProfiles = expectedProfiles.Keys
            .Where(key => response.Characters.All(item => !string.Equals(item.ProfileKey, key, StringComparison.Ordinal)))
            .ToList();
        if (missingProfiles.Count > 0)
            throw new InvalidOperationException($"Moment enrichment is missing selected-Moment participants: {string.Join(", ", missingProfiles)}.");

        var castNames = expectedProfiles.Values.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var characters = response.Characters.Select(item =>
        {
            var profile = expectedProfiles[item.ProfileKey];
            if (!string.Equals(item.Name, profile.Name, StringComparison.Ordinal))
                throw new InvalidOperationException($"Moment enrichment profile '{item.ProfileKey}' name does not match authoritative name '{profile.Name}'.");
            if (!string.Equals(item.Involvement, profile.Involvement, StringComparison.Ordinal))
                throw new InvalidOperationException($"Moment enrichment profile '{item.ProfileKey}' involvement does not match the selected Moment.");
            Require(item.PhysicalLocation, $"Character '{item.Name}' physical location");
            Require(item.Position, $"Character '{item.Name}' position");
            Require(item.ActionOrObservation, $"Character '{item.Name}' action or observation");
            Require(item.Sightline, $"Character '{item.Name}' sightline");
            Require(item.Clothing, $"Character '{item.Name}' clothing");
            ValidateFrozenText(item.ActionOrObservation, $"character '{item.Name}' action or observation");
            RequireUnique(item.VisibleCharacterNames, $"Character '{item.Name}' visible character names", allowEmpty: true);
            var unknownNames = item.VisibleCharacterNames.Where(name => !castNames.Contains(name)).ToList();
            if (unknownNames.Count > 0)
                throw new InvalidOperationException($"Character '{item.Name}' references unknown visible character names: {string.Join(", ", unknownNames)}.");
            return new SceneMomentFrozenCharacter(
                profile.ProfileKey,
                profile.CharacterId,
                profile.Name,
                profile.Involvement,
                item.PhysicalLocation,
                item.Position,
                item.ActionOrObservation,
                item.Sightline,
                item.VisibleCharacterNames.ToArray(),
                item.Clothing);
        }).ToArray();

        RequireUnique(response.Objects, "Moment enrichment objects", allowEmpty: true);
        RequireUnique(response.InstantaneousSoundCueKeys, "Moment enrichment sound cue keys", allowEmpty: true);
        var knownCues = snapshot.SoundCues.ToDictionary(item => item.CueKey, StringComparer.Ordinal);
        var unknownCues = response.InstantaneousSoundCueKeys.Where(key => !knownCues.ContainsKey(key)).ToList();
        if (unknownCues.Count > 0)
            throw new InvalidOperationException($"Moment enrichment references unknown sound cue keys: {string.Join(", ", unknownCues)}.");
        var isSoundAnchor = snapshot.Moment.ProductionRoles.Contains(
            SceneMomentProductionRole.SoundEventAnchor.ToString(),
            StringComparer.Ordinal);
        if (!isSoundAnchor && response.InstantaneousSoundCueKeys.Count > 0)
            throw new InvalidOperationException("Moment enrichment cannot attach instantaneous sound cues to a Moment without SoundEventAnchor.");
        if (isSoundAnchor && response.InstantaneousSoundCueKeys.Count == 0)
            throw new InvalidOperationException("A SoundEventAnchor Moment requires at least one instantaneous sound cue.");
        var sounds = response.InstantaneousSoundCueKeys.Select(key =>
        {
            var cue = knownCues[key];
            var eventKey = cue.EventKey ?? cue.StartEventKey ?? cue.EndEventKey;
            if (string.IsNullOrWhiteSpace(eventKey))
                throw new InvalidOperationException($"Sound cue '{key}' has no event anchor for an instantaneous Moment.");
            return new SceneMomentInstantaneousSoundEvent(key, eventKey, cue.Description);
        }).ToArray();

        RequireUnique(response.VideoKeyState.Roles, "Moment enrichment video roles", allowEmpty: true);
        var expectedVideoRoles = snapshot.Moment.ProductionRoles
            .Where(role => role is nameof(SceneMomentProductionRole.VideoStart)
                or nameof(SceneMomentProductionRole.VideoEnd)
                or nameof(SceneMomentProductionRole.VideoInternalKeyframe))
            .ToHashSet(StringComparer.Ordinal);
        if (!expectedVideoRoles.SetEquals(response.VideoKeyState.Roles))
            throw new InvalidOperationException("Moment enrichment video roles must exactly match the selected Moment video roles.");
        if (response.VideoKeyState.StateChangeAllowed)
            throw new InvalidOperationException("Moment enrichment video key state cannot allow state change.");

        var frozen = new SceneMomentFrozenStateContract(
            response.VisualDescription,
            characters,
            response.Location,
            response.TimeOfDay,
            response.Lighting,
            response.Environment,
            response.Mood,
            response.Objects.ToArray(),
            snapshot.Moment.FrozenState);
        var video = new SceneMomentVideoKeyState(response.VideoKeyState.Roles.ToArray(), false);
        return new SceneMomentEnrichmentData(
            JsonSerializer.Serialize(frozen, JsonOptions),
            JsonSerializer.Serialize(sounds, JsonOptions),
            JsonSerializer.Serialize(video, JsonOptions));
    }

    private static void ValidateFrozenText(string value, string field)
    {
        var normalized = $" {value.Trim().ToLowerInvariant()} ";
        if (SequentialStateMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Moment enrichment {field} describes sequential before/after/then action instead of one frozen state.");
    }

    private static void RequireUnique(IEnumerable<string> values, string source, bool allowEmpty = false)
    {
        var list = values.ToList();
        if ((!allowEmpty && list.Count == 0)
            || list.Any(string.IsNullOrWhiteSpace)
            || list.Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new InvalidOperationException($"{source} must be non-empty and unique.");
    }

    private static void Require(string? value, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{source} is required.");
    }

    private sealed class Response
    {
        public required int SchemaVersion { get; init; }
        public required string CatalogueBeatId { get; init; }
        public required string MomentId { get; init; }
        public required string VisualDescription { get; init; }
        public required List<CharacterInput> Characters { get; init; }
        public required string Location { get; init; }
        public required string TimeOfDay { get; init; }
        public required string Lighting { get; init; }
        public required string Environment { get; init; }
        public required string Mood { get; init; }
        public required List<string> Objects { get; init; }
        public required List<string> InstantaneousSoundCueKeys { get; init; }
        public required VideoKeyStateInput VideoKeyState { get; init; }
    }

    private sealed class CharacterInput
    {
        public required string Name { get; init; }
        public required string ProfileKey { get; init; }
        public required string Involvement { get; init; }
        public required string PhysicalLocation { get; init; }
        public required string Position { get; init; }
        public required string ActionOrObservation { get; init; }
        public required string Sightline { get; init; }
        public required List<string> VisibleCharacterNames { get; init; }
        public required string Clothing { get; init; }
    }

    private sealed class VideoKeyStateInput
    {
        public required List<string> Roles { get; init; }
        public required bool StateChangeAllowed { get; init; }
    }
}