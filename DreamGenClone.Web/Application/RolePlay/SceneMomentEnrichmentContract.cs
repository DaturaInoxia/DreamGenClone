using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed record SceneMomentEnrichmentContractMessages(
    string ContractVersion,
    string SystemPrompt,
    string UserPrompt,
    string ResponseSchemaName,
    JsonElement ResponseSchema);

public sealed record SceneMomentEnrichmentParticipantSnapshot(
    string ProfileKey,
    string CharacterId,
    string Name,
    string Involvement,
    string Role,
    string Gender,
    string Description,
    string Appearance,
    string Clothing);

public sealed record SceneMomentEnrichmentSoundCueSnapshot(
    string CueKey,
    string? EventKey,
    string Description,
    decimal? StartSeconds,
    decimal? EndSeconds,
    string? StartEventKey,
    string? EndEventKey);

public sealed record SceneMomentEnrichmentSelectedMomentSnapshot(
    string MomentSetId,
    int MomentSetVersion,
    string MomentId,
    int Order,
    string Label,
    string TemporalAnchor,
    string FrozenState,
    string VisibleAction,
    string CompositionRationale,
    IReadOnlyList<SceneMomentEnrichmentParticipantSnapshot> Participants,
    IReadOnlyList<string> ProductionRoles,
    IReadOnlyList<string> EvidenceInteractionIds);

public sealed record SceneMomentEnrichmentSourceSnapshot(
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
    SceneMomentEnrichmentSelectedMomentSnapshot Moment,
    IReadOnlyList<SceneMomentEnrichmentSoundCueSnapshot> SoundCues,
    IReadOnlyList<SceneMomentDiscoveryEvidenceSnapshot> Evidence);

public sealed record SceneMomentEnrichmentMomentSnapshot(
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
    SceneMomentEnrichmentSelectedMomentSnapshot Moment,
    IReadOnlyList<SceneMomentEnrichmentSoundCueSnapshot> SoundCues);

public sealed class SceneMomentEnrichmentContract
{
    public const int CurrentSchemaVersion = 1;
    public const string ContractVersion = "scene-moment-enrichment-v1";
    public const string ResponseSchemaName = "scene_moment_enrichment";

    public SceneMomentEnrichmentContractMessages BuildMessages(SceneMomentEnrichmentSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidOperationException($"Moment enrichment source schemaVersion {snapshot.SchemaVersion} is unsupported.");

        var user = new StringBuilder();
        user.AppendLine("SELECTED BEAT:");
        user.AppendLine($"id={snapshot.BeatId} | label={snapshot.BeatLabel} | location={snapshot.PrimaryLocation}");
        user.AppendLine(snapshot.BeatSynopsis);
        user.AppendLine("SELECTED MOMENT:");
        user.AppendLine($"id={snapshot.Moment.MomentId} | order={snapshot.Moment.Order} | label={snapshot.Moment.Label}");
        user.AppendLine($"temporalAnchor={snapshot.Moment.TemporalAnchor}");
        user.AppendLine($"frozenState={snapshot.Moment.FrozenState}");
        user.AppendLine($"visibleAction={snapshot.Moment.VisibleAction}");
        user.AppendLine($"productionRoles={string.Join(',', snapshot.Moment.ProductionRoles)}");
        user.AppendLine("SELECTED CAST:");
        foreach (var profile in snapshot.Moment.Participants)
        {
            user.AppendLine($"[{profile.ProfileKey}] {profile.Name} | involvement={profile.Involvement} | role={profile.Role} | gender={profile.Gender}");
            user.AppendLine($"description={profile.Description} | appearance={profile.Appearance} | clothing={profile.Clothing}");
        }
        user.AppendLine("BEAT CONTINUITY:");
        user.AppendLine(snapshot.StartContinuityJson);
        user.AppendLine(snapshot.EndContinuityJson);
        user.AppendLine("BEAT ACTION AND TIMELINE:");
        user.AppendLine(snapshot.NarrativeArcJson);
        user.AppendLine(snapshot.TimelineJson);
        user.AppendLine(snapshot.ActionArcJson);
        user.AppendLine("KNOWN SOUND CUES:");
        foreach (var cue in snapshot.SoundCues)
            user.AppendLine($"[{cue.CueKey}] event={cue.EventKey} | {cue.Description}");
        user.AppendLine("AUTHORITATIVE EVIDENCE:");
        foreach (var evidence in snapshot.Evidence.OrderBy(item => item.SourceOrder))
        {
            user.AppendLine($"[{evidence.Key}] {evidence.ActorName} ({evidence.InteractionType}):");
            user.AppendLine(evidence.Content);
        }

        return new SceneMomentEnrichmentContractMessages(
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
            ("momentId", String()),
            ("visualDescription", String()),
            ("characters", Array(Character(), 1)),
            ("location", String()),
            ("timeOfDay", String()),
            ("lighting", String()),
            ("environment", String()),
            ("mood", String()),
            ("objects", UniqueStringArray()),
            ("instantaneousSoundCueKeys", UniqueStringArray()),
            ("videoKeyState", Object(
                ("roles", UniqueEnumArray("VideoStart", "VideoEnd", "VideoInternalKeyframe")),
                ("stateChangeAllowed", Boolean())))));

    private static JsonObject Character() => Object(
        ("name", String()),
        ("profileKey", String()),
        ("involvement", Enum("active", "observer")),
        ("physicalLocation", String()),
        ("position", String()),
        ("actionOrObservation", String()),
        ("sightline", String()),
        ("visibleCharacterNames", UniqueStringArray()),
        ("clothing", String()));

    private const string SystemPrompt = """
        You are a frozen-state production planner. Enrich exactly one selected Moment into one canonical, provider-neutral visual state, its instantaneous sound anchors, and its selected video key-state roles.

        Describe exactly one instant. Do not write a sequence, transition, before-and-after state, montage, shot list, or action progression. Include exactly the selected Moment cast, using each supplied profile key and exact profile name once. Use visibleCharacterNames only for supplied cast members visible to that character.

        Use only supplied evidence, profile facts, Beat continuity, sound cue keys, and selected production roles. instantaneousSoundCueKeys must be empty unless the selected Moment's productionRoles includes SoundEventAnchor; when it does include SoundEventAnchor, list at least one instantaneous cue, and never list cues otherwise. videoKeyState.roles must equal the selected Moment's Video roles exactly, and stateChangeAllowed must be false.

        Never invent UUIDs, people, profile keys, cue keys, events, clothing, continuity, source facts, or media assets. Return only JSON matching the supplied schema. Do not use markdown fences, explanatory text, provider tags, prompts, model names, camera-provider syntax, generation settings, inferred missing fields, or alternate roots.
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

    private static JsonObject String() => new() { ["type"] = "string", ["minLength"] = 1 };

    private static JsonObject Boolean() => new() { ["type"] = "boolean" };

    private static JsonObject Enum(params string[] values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
    };

    private static JsonObject Array(JsonNode items, int? minimum = null)
    {
        var schema = new JsonObject { ["type"] = "array", ["items"] = items };
        if (minimum.HasValue) schema["minItems"] = minimum.Value;
        return schema;
    }

    private static JsonObject UniqueStringArray()
    {
        var schema = Array(String());
        schema["uniqueItems"] = true;
        return schema;
    }

    private static JsonObject UniqueEnumArray(params string[] values)
    {
        var schema = Array(Enum(values));
        schema["uniqueItems"] = true;
        return schema;
    }
}