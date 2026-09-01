using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed record SceneBeatProductionContractMessages(
    string ContractVersion,
    string SystemPrompt,
    string UserPrompt,
    string ResponseSchemaName,
    JsonElement ResponseSchema);

public sealed class SceneBeatProductionContract
{
    public const string ContractVersion = "scene-beat-production-v1";
    public const string ResponseSchemaName = "scene_beat_production";

    public SceneBeatProductionContractMessages BuildMessages(SceneBeatProductionSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != SceneBeatProductionSnapshotBuilder.CurrentSchemaVersion)
            throw new InvalidOperationException($"Beat Production source schemaVersion {snapshot.SchemaVersion} is unsupported.");

        var user = new StringBuilder();
        user.AppendLine("SELECTED BEAT:");
        user.AppendLine($"id={snapshot.Beat.BeatId} | order={snapshot.Beat.Order} | label={snapshot.Beat.Label}");
        user.AppendLine($"location={snapshot.Beat.PrimaryLocation}");
        user.AppendLine(snapshot.Beat.Synopsis);
        user.AppendLine("PARTICIPANTS:");
        foreach (var participant in snapshot.Beat.Participants)
        {
            var profile = snapshot.Profiles.Single(item => item.Key == participant.ProfileKey);
            user.AppendLine($"[{profile.Key}] {profile.Name} | involvement={participant.Involvement} | role={profile.Role} | gender={profile.Gender}");
            user.AppendLine($"description={profile.Description} | appearance={profile.Appearance} | clothing={profile.Clothing}");
        }
        user.AppendLine("AUTHORITATIVE EVIDENCE:");
        foreach (var evidence in snapshot.Evidence.OrderBy(item => item.SourceOrder))
        {
            user.AppendLine($"[{evidence.Key}] {evidence.ActorName} ({evidence.InteractionType}):");
            user.AppendLine(evidence.Content);
        }

        return new SceneBeatProductionContractMessages(
            ContractVersion,
            SystemPrompt,
            user.ToString().TrimEnd(),
            ResponseSchemaName,
            CreateResponseSchema());
    }

    public static JsonElement CreateResponseSchema()
        => JsonSerializer.SerializeToElement(Object(
            ("schemaVersion", new JsonObject { ["const"] = SceneBeatProductionSnapshotBuilder.CurrentSchemaVersion }),
            ("catalogueBeatId", String()),
            ("events", Array(Event(), 1)),
            ("timeline", Timeline()),
            ("narration", Array(DialogueCue())),
            ("dialogue", Array(DialogueCue())),
            ("ambience", Ambience()),
            ("soundEvents", Array(SoundCue())),
            ("music", Array(MusicSection())),
            ("actionArc", Array(ActionStep())),
            ("startContinuity", Continuity()),
            ("endContinuity", Continuity()),
            ("typedReferences", Array(TypedReference())),
            ("videoCoverage", Array(VideoCoverage()))));

    private const string SystemPrompt = """
        You are a multimodal narrative production analyst. Expand exactly one selected narrative Beat into canonical, provider-neutral temporal production data for downstream speech, sound, music, image-key-state, and video planning.

        Use only the selected Beat and supplied immutable evidence. Keep chronology source-supported. Use only supplied evidence keys and profile keys. Never invent UUIDs, speakers, addressees, quotations, source offsets, character facts, or Moment IDs.

        Dialogue and narration must preserve exact source text and zero-based start/end offsets in one supplied evidence item. Keep immutable display text separate from normalized spoken text and record normalization method/version. If attribution is ambiguous, set reviewStatus to ReviewRequired, leave speakerKey null, and explain reviewReason.

        Express time as Beat-relative seconds and/or event anchors. Every window requires a duration intent and explicit precision/overlap policy. Include explicit ambience, including authored silence when appropriate. Keep music instrumental unless lyrics are explicitly authored in the evidence.

        Video coverage is semantic intent, not provider syntax. It may request start/end/internal key-state roles but must not invent Moment IDs. Declare audio ownership per referenced cue. Typed references describe required roles and lineage placeholders; they do not select assets that were not supplied.

        Return only JSON matching the supplied schema. Do not use markdown fences, explanatory text, provider tags, prompts, model names, frame numbers, sampling settings, or inferred missing fields.
        """;

    private static JsonObject Event() => Object(
        ("eventKey", String()), ("order", PositiveInteger()), ("description", String()),
        ("evidenceKeys", UniqueStringArray(1)), ("window", Window()));

    private static JsonObject Timeline() => Object(
        ("durationIntent", String()), ("beatWindow", Window()));

    private static JsonObject DialogueCue() => Object(
        ("cueKey", String()),
        ("order", PositiveInteger()),
        ("kind", Enum("Dialogue", "Narration", "Thought")),
        ("eventKey", String()),
        ("exactSourceText", String()),
        ("displayText", String()),
        ("normalizedSpokenText", String()),
        ("normalizationMethod", String()),
        ("normalizationVersion", String()),
        ("sourceKey", String()),
        ("startOffset", NonNegativeInteger()),
        ("endOffset", PositiveInteger()),
        ("speakerKey", NullableString()),
        ("addresseeKeys", UniqueStringArray()),
        ("performance", Performance()),
        ("window", Window()),
        ("lipSyncRelevant", Boolean()),
        ("reviewStatus", Enum("Validated", "ReviewRequired")),
        ("reviewReason", NullableString()));

    private static JsonObject Performance() => Object(
        ("speakerKey", NullableString()),
        ("languageCode", String()),
        ("locale", NullableString()),
        ("emotion", String()),
        ("intensity", String()),
        ("pace", String()),
        ("accentIntent", NullableString()),
        ("pauseCues", StringArray()),
        ("overlapOrInterruption", NullableString()),
        ("pronunciationLexemes", Array(Object(
            ("sourceText", String()), ("pronunciation", String()), ("alphabet", NullableString())))),
        ("nonVerbalVocalEvents", StringArray()));

    private static JsonObject Ambience() => Object(
        ("location", String()),
        ("timeContext", String()),
        ("soundSources", StringArray()),
        ("intensityEnvelope", String()),
        ("spatialIntent", String()),
        ("authoredSilence", Boolean()),
        ("continuityIntent", String()),
        ("window", Window()));

    private static JsonObject SoundCue() => Object(
        ("cueKey", String()),
        ("order", PositiveInteger()),
        ("kind", Enum("Ambience", "SoundEffect")),
        ("eventKey", NullableString()),
        ("locationSource", NullableString()),
        ("subjectKey", NullableString()),
        ("objectReference", NullableString()),
        ("description", String()),
        ("intensityEnvelope", String()),
        ("diegetic", Boolean()),
        ("spatialIntent", String()),
        ("window", Window()),
        ("loop", Boolean()),
        ("stemIntent", NullableString()),
        ("continuityGroup", String()),
        ("reviewStatus", Enum("Validated", "ReviewRequired")),
        ("reviewReason", NullableString()));

    private static JsonObject MusicSection() => Object(
        ("sectionKey", String()),
        ("order", PositiveInteger()),
        ("mood", String()),
        ("instrumentation", StringArray()),
        ("tempoBpm", NullableNonNegativeNumber()),
        ("musicalKey", NullableString()),
        ("transitionIntent", String()),
        ("instrumental", Boolean()),
        ("continuityIntent", String()),
        ("window", Window()));

    private static JsonObject ActionStep() => Object(
        ("order", PositiveInteger()),
        ("eventKey", String()),
        ("subjectKey", String()),
        ("action", String()),
        ("targetKey", NullableString()),
        ("targetObject", NullableString()),
        ("resultingState", String()));

    private static JsonObject Continuity() => Object(
        ("location", String()),
        ("characterStates", KeyValueArray()),
        ("wardrobeStates", KeyValueArray()),
        ("objectStates", KeyValueArray()),
        ("lighting", String()),
        ("stateSummary", String()));

    private static JsonObject KeyValueArray() => Array(Object(("key", String()), ("value", String())));

    private static JsonObject TypedReference() => Object(
        ("referenceKey", String()),
        ("role", Enum(
            "CharacterIdentity", "VoiceIdentity", "WardrobeContinuity", "LocationContinuity",
            "PropContinuity", "Pose", "Style", "VideoFirstFrame", "VideoLastFrame",
            "VideoInternalKeyframe", "SourceVideo", "SourceSpeech", "MusicConditioning", "LipSyncVisualSource")),
        ("mediaKind", String()),
        ("sourceRecordId", NullableString()),
        ("assetId", NullableString()),
        ("subjectKey", NullableString()),
        ("window", Nullable(Window())),
        ("required", Boolean()));

    private static JsonObject VideoCoverage() => Object(
        ("coverageKey", String()),
        ("kind", Enum("MomentHold", "MomentAction", "MomentTransition", "BeatExcerpt", "WholeBeat")),
        ("window", Window()),
        ("sourceEventKeys", UniqueStringArray(1)),
        ("requiredMomentRoles", UniqueStringArray()),
        ("permittedActionPhases", UniqueStringArray()),
        ("cameraIntent", String()),
        ("lensIntent", String()),
        ("motionIntent", String()),
        ("pacingIntent", String()),
        ("referenceKeys", UniqueStringArray()),
        ("dialogueCueKeys", UniqueStringArray()),
        ("soundCueKeys", UniqueStringArray()),
        ("musicSectionKeys", UniqueStringArray()),
        ("audioOwnership", Array(Object(("cueKey", String()), ("ownershipIntent", String())))),
        ("lipSyncRequired", Boolean()),
        ("performanceIntent", String()),
        ("durationFitPolicy", String()),
        ("reviewStatus", Enum("Validated", "ReviewRequired")),
        ("reviewReason", NullableString()));

    private static JsonObject Window() => Object(
        ("startSeconds", NullableNonNegativeNumber()),
        ("endSeconds", NullableNonNegativeNumber()),
        ("startEventKey", NullableString()),
        ("endEventKey", NullableString()),
        ("durationIntent", String()),
        ("precision", Enum("Exact", "Estimated", "Relative")),
        ("overlapPolicy", Enum("Disallow", "Allow", "Duck", "Interrupt")),
        ("continuityLeadIn", Boolean()),
        ("continuityTail", Boolean()));

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
    private static JsonObject NullableString() => new() { ["type"] = new JsonArray("string", "null") };
    private static JsonObject Boolean() => new() { ["type"] = "boolean" };
    private static JsonObject PositiveInteger() => new() { ["type"] = "integer", ["minimum"] = 1 };
    private static JsonObject NonNegativeInteger() => new() { ["type"] = "integer", ["minimum"] = 0 };
    private static JsonObject NullableNonNegativeNumber() => new()
    {
        ["type"] = new JsonArray("number", "null"),
        ["minimum"] = 0
    };
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
    private static JsonObject StringArray() => Array(String());
    private static JsonObject UniqueStringArray(int? minimum = null)
    {
        var schema = Array(String(), minimum);
        schema["uniqueItems"] = true;
        return schema;
    }
    private static JsonObject Nullable(JsonObject schema)
    {
        schema["type"] = new JsonArray("object", "null");
        return schema;
    }
}