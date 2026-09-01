using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed record SceneMomentDiscoveryContractMessages(
    string ContractVersion,
    string SystemPrompt,
    string UserPrompt,
    string ResponseSchemaName,
    JsonElement ResponseSchema);

public sealed record SceneMomentDiscoveryEvidenceSnapshot(
    string Key,
    int SourceOrder,
    string InteractionId,
    string ActorName,
    string InteractionType,
    string Content,
    string SourceSha256);

public sealed record SceneMomentDiscoveryProfileSnapshot(
    string Key,
    string? CharacterId,
    string Name,
    string Involvement);

public sealed record SceneMomentDiscoverySourceSnapshot(
    int SchemaVersion,
    string CatalogueId,
    int CatalogueVersion,
    string BeatId,
    string BeatProductionPlanId,
    int BeatProductionPlanVersion,
    string BeatLabel,
    string BeatSynopsis,
    string PrimaryLocation,
    string NarrativeArcJson,
    string TimelineJson,
    string ActionArcJson,
    string StartContinuityJson,
    string EndContinuityJson,
    IReadOnlyList<SceneVideoCoverageSnapshot> VideoCoverage,
    IReadOnlyList<SceneMomentDiscoveryEvidenceSnapshot> Evidence,
    IReadOnlyList<SceneMomentDiscoveryProfileSnapshot> Profiles);

public sealed record SceneVideoCoverageSnapshot(
    string CoverageKey,
    string CoverageKind,
    IReadOnlyList<string> SourceEventKeys,
    IReadOnlyList<string> RequiredMomentRoles,
    IReadOnlyList<string> PermittedActionPhases);

public sealed record SceneMomentDiscoveryBeatSnapshot(
    int SchemaVersion,
    string CatalogueId,
    int CatalogueVersion,
    string BeatId,
    string BeatProductionPlanId,
    int BeatProductionPlanVersion,
    string BeatLabel,
    string BeatSynopsis,
    string PrimaryLocation,
    string NarrativeArcJson,
    string TimelineJson,
    string ActionArcJson,
    string StartContinuityJson,
    string EndContinuityJson,
    IReadOnlyList<SceneVideoCoverageSnapshot> VideoCoverage,
    IReadOnlyList<SceneMomentDiscoveryProfileSnapshot> Profiles);

public sealed class SceneMomentDiscoveryContract
{
    public const int CurrentSchemaVersion = 1;
    public const string ContractVersion = "scene-moment-discovery-v1";
    public const string ResponseSchemaName = "scene_moment_discovery";

    public SceneMomentDiscoveryContractMessages BuildMessages(SceneMomentDiscoverySourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidOperationException($"Moment discovery source schemaVersion {snapshot.SchemaVersion} is unsupported.");

        var user = new StringBuilder();
        user.AppendLine("SELECTED BEAT:");
        user.AppendLine($"id={snapshot.BeatId} | label={snapshot.BeatLabel} | location={snapshot.PrimaryLocation}");
        user.AppendLine(snapshot.BeatSynopsis);
        user.AppendLine("ORDERED EVENTS:");
        user.AppendLine(snapshot.NarrativeArcJson);
        user.AppendLine("TIMELINE:");
        user.AppendLine(snapshot.TimelineJson);
        user.AppendLine("ACTION ARC:");
        user.AppendLine(snapshot.ActionArcJson);
        user.AppendLine("CONTINUITY BOUNDARIES:");
        user.AppendLine(snapshot.StartContinuityJson);
        user.AppendLine(snapshot.EndContinuityJson);
        user.AppendLine("REQUESTED VIDEO KEY STATES:");
        foreach (var coverage in snapshot.VideoCoverage)
        {
            user.AppendLine(
                $"[{coverage.CoverageKey}] kind={coverage.CoverageKind} | events={string.Join(',', coverage.SourceEventKeys)} | roles={string.Join(',', coverage.RequiredMomentRoles)} | phases={string.Join(',', coverage.PermittedActionPhases)}");
        }
        user.AppendLine("KNOWN PARTICIPANTS:");
        foreach (var profile in snapshot.Profiles)
            user.AppendLine($"[{profile.Key}] {profile.Name} | involvement={profile.Involvement}");
        user.AppendLine("AUTHORITATIVE EVIDENCE:");
        foreach (var evidence in snapshot.Evidence.OrderBy(item => item.SourceOrder))
        {
            user.AppendLine($"[{evidence.Key}] {evidence.ActorName} ({evidence.InteractionType}):");
            user.AppendLine(evidence.Content);
        }

        return new SceneMomentDiscoveryContractMessages(
            ContractVersion,
            SystemPrompt,
            user.ToString().TrimEnd(),
            ResponseSchemaName,
            CreateResponseSchema());
    }

    public static JsonElement CreateResponseSchema()
        => JsonSerializer.SerializeToElement(Object(
            ("schemaVersion", new JsonObject { ["const"] = CurrentSchemaVersion }),
            ("catalogueBeatId", String()),
            ("recommendedMomentId", String("^m[1-9][0-9]*$")),
            ("moments", Array(Moment(), 2, 4))));

    private static JsonObject Moment() => Object(
        ("momentId", String("^m[1-9][0-9]*$")),
        ("order", PositiveInteger()),
        ("label", String()),
        ("temporalAnchor", String()),
        ("frozenState", String()),
        ("visibleAction", String()),
        ("participants", Array(Object(
            ("profileKey", String()),
            ("involvement", Enum("active", "observer"))), 1)),
        ("compositionRationale", String()),
        ("productionRoles", UniqueEnumArray(
            "StillCandidate", "VideoStart", "VideoEnd", "VideoInternalKeyframe", "SoundEventAnchor")),
        ("evidenceKeys", UniqueStringArray(1)));

    private const string SystemPrompt = """
        You are a narrative key-state planner. From exactly one selected Beat Production Plan, identify 2 to 4 compact Moments that together satisfy useful still-image choices, every requested video key-state role, and sound key-state coverage. Assign the SoundEventAnchor production role to at least one Moment where a distinct instantaneous sound event occurs (for example a door creak, a glass setting on the rail, or water running), and do not assign SoundEventAnchor to a Moment with no distinct instantaneous sound.

        Each Moment is exactly one frozen instant, not a time range, shot sequence, montage, mini-scene, or before-and-after action. temporalAnchor locates one instant in the supplied timeline using only a supplied event key and a second offset, for example "e5, ~45s into beat"; never describe a transition. frozenState describes only the state visible at that instant. visibleAction names an action arrested at that instant. Never use the words "before", "after", "then", "followed by", "transitions to", "moves from", or "and then" in temporalAnchor, frozenState, or visibleAction - those fields describe one frozen state only.

        Use only supplied profile keys, evidence keys, event chronology, action phases, continuity boundaries, and requested production roles. Never invent UUIDs, people, events, evidence, source facts, or media assets. Return 2 to 4 Moments in temporal order and recommend exactly one returned StillCandidate.

        Return only JSON matching the supplied schema. Do not use markdown fences, explanatory text, provider tags, prompts, model names, camera-provider syntax, generation settings, inferred missing fields, or alternate roots.
        """;

    private static JsonObject Object(params (string Name, JsonNode Schema)[] properties)
    {
        var propertyObject = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema) in properties)
        {
            propertyObject[name] = schema;
            required.Add(name);
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = required,
            ["properties"] = propertyObject
        };
    }

    private static JsonObject String(string? pattern = null)
    {
        var schema = new JsonObject { ["type"] = "string", ["minLength"] = 1 };
        if (pattern is not null) schema["pattern"] = pattern;
        return schema;
    }

    private static JsonObject PositiveInteger() => new() { ["type"] = "integer", ["minimum"] = 1 };

    private static JsonObject Enum(params string[] values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
    };

    private static JsonObject Array(JsonNode items, int? minimum = null, int? maximum = null)
    {
        var schema = new JsonObject { ["type"] = "array", ["items"] = items };
        if (minimum.HasValue) schema["minItems"] = minimum.Value;
        if (maximum.HasValue) schema["maxItems"] = maximum.Value;
        return schema;
    }

    private static JsonObject UniqueStringArray(int minimum)
    {
        var schema = Array(String(), minimum);
        schema["uniqueItems"] = true;
        return schema;
    }

    private static JsonObject UniqueEnumArray(params string[] values)
    {
        var schema = Array(Enum(values), 1);
        schema["uniqueItems"] = true;
        return schema;
    }
}