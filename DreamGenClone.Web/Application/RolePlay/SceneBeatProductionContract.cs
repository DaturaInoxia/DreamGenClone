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

public sealed record SceneBeatProductionPassMessages(
    string PassId,
    string SystemPrompt,
    string UserPrompt,
    string ResponseSchemaName,
    JsonElement ResponseSchema);

public sealed class SceneBeatProductionContract
{
    public const string ContractVersion = "scene-beat-production-v3";
    public const string ResponseSchemaName = "scene_beat_production";

    public SceneBeatProductionContractMessages BuildMessages(SceneBeatProductionSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != SceneBeatProductionSnapshotBuilder.CurrentSchemaVersion)
            throw new InvalidOperationException($"Beat Production source schemaVersion {snapshot.SchemaVersion} is unsupported.");

        return new SceneBeatProductionContractMessages(
            ContractVersion,
            SystemPrompt,
            BuildBaseUserContext(snapshot),
            ResponseSchemaName,
            CreateResponseSchema(snapshot.Profiles.Select(profile => profile.Key)));
    }

    private static string BuildBaseUserContext(SceneBeatProductionSourceSnapshot snapshot)
    {
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

        return user.ToString().TrimEnd();
    }

    public SceneBeatProductionPassMessages BuildStructurePass(SceneBeatProductionSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new SceneBeatProductionPassMessages(
            "structure",
            StructureSystemPrompt,
            BuildBaseUserContext(snapshot),
            "scene_beat_production_structure",
            JsonSerializer.SerializeToElement(Object(
                ("schemaVersion", VersionConst()),
                ("catalogueBeatId", String()),
                ("events", Array(Event(), 1)),
                ("timeline", Timeline()),
                ("actionArc", Array(ActionStep(snapshot.Profiles.Select(profile => profile.Key)))))));
    }

    public SceneBeatProductionPassMessages BuildSpokenPass(
        SceneBeatProductionSourceSnapshot snapshot,
        string establishedEventsJson)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new SceneBeatProductionPassMessages(
            "spoken",
            SpokenSystemPrompt,
            BuildBaseUserContext(snapshot) + "\n\nESTABLISHED EVENTS (chronological anchors):\n" + establishedEventsJson,
            "scene_beat_production_spoken",
            JsonSerializer.SerializeToElement(Object(
                ("schemaVersion", VersionConst()),
                ("catalogueBeatId", String()),
                ("narration", Array(DialogueCue())),
                ("dialogue", Array(DialogueCue())))));
    }

    public SceneBeatProductionPassMessages BuildSoundscapePass(
        SceneBeatProductionSourceSnapshot snapshot,
        string establishedEventsJson)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new SceneBeatProductionPassMessages(
            "soundscape",
            SoundscapeSystemPrompt,
            BuildBaseUserContext(snapshot) + "\n\nESTABLISHED EVENTS (chronological anchors):\n" + establishedEventsJson,
            "scene_beat_production_soundscape",
            JsonSerializer.SerializeToElement(Object(
                ("schemaVersion", VersionConst()),
                ("catalogueBeatId", String()),
                ("ambience", Ambience()),
                ("soundEvents", Array(SoundCue())),
                ("music", Array(MusicSection())))));
    }

    public SceneBeatProductionPassMessages BuildAssemblyPass(
        SceneBeatProductionSourceSnapshot snapshot,
        string establishedJson)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new SceneBeatProductionPassMessages(
            "assembly",
            AssemblySystemPrompt,
            BuildBaseUserContext(snapshot) + "\n\nESTABLISHED PRODUCTION DATA (events, cues, sound, music):\n" + establishedJson,
            "scene_beat_production_assembly",
            JsonSerializer.SerializeToElement(Object(
                ("schemaVersion", VersionConst()),
                ("catalogueBeatId", String()),
                ("startContinuity", Continuity()),
                ("endContinuity", Continuity()),
                ("typedReferences", Array(TypedReference())),
                ("videoCoverage", Array(VideoCoverage())))));
    }

    private static JsonObject VersionConst()
        => new() { ["const"] = SceneBeatProductionSnapshotBuilder.CurrentSchemaVersion };

    public static JsonElement CreateResponseSchema(IEnumerable<string> profileKeys)
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
            ("actionArc", Array(ActionStep(profileKeys))),
            ("startContinuity", Continuity()),
            ("endContinuity", Continuity()),
            ("typedReferences", Array(TypedReference())),
            ("videoCoverage", Array(VideoCoverage()))));

    private const string SystemPrompt = """
        You are a multimodal narrative production analyst. Expand exactly one selected narrative Beat into canonical, provider-neutral temporal production data for downstream speech, sound, music, image-key-state, and video planning.

        Use only the selected Beat and supplied immutable evidence. Keep chronology source-supported. Use only supplied evidence keys and profile keys. Never invent UUIDs, speakers, addressees, quotations, source text, character facts, or Moment IDs. In actionArc, subjectKey and targetKey are participant profile keys (e.g. p0 or p1) only; doors, mirrors, towels, faucets, and other objects belong in targetObject and must never be used as profile keys. In startContinuity and endContinuity, characterStates and wardrobeStates keys are the participant profile keys (e.g. p0) - never suffix them (e.g. p0-wardrobe is invalid); objectStates keys are free-form object identifiers (e.g. porch-light); typedReferences.subjectKey is a participant profile key.

        Dialogue and narration must preserve exact source text drawn from one supplied evidence item. For every cue, set sourceKey to that evidence key and set exactSourceText to a character-for-character contiguous substring of that evidence content, including any newlines and internal whitespace; never trim, reflow, re-case, paraphrase, or stitch together non-contiguous fragments. The application locates each cue by searching its evidence for exactSourceText, so it must appear verbatim. Keep immutable display text separate from normalized spoken text and record normalization method/version. If attribution is ambiguous, set reviewStatus to ReviewRequired, leave speakerKey null, and explain reviewReason. Narration cues have no speaker: leave speakerKey null and set reviewStatus to Validated.

        Express time as Beat-relative seconds and/or event anchors. Every window requires a duration intent and explicit precision/overlap policy. Include explicit ambience, including authored silence when appropriate. Set ambience.location exactly equal to the selected Beat's location value shown in the prompt (the `location=` line); copy it verbatim with no added detail, paraphrasing, or spot descriptions. Keep music instrumental unless lyrics are explicitly authored in the evidence.

        Video coverage is semantic intent, not provider syntax. requiredMomentRoles must be the literal canonical key-state roles 'start' and 'end' - exactly ['start'] for MomentHold and ['start','end'] for MomentAction, MomentTransition, BeatExcerpt, and WholeBeat - never descriptive or free-form role names, and must not invent Moment IDs. Declare audio ownership per referenced cue. Typed references describe required roles and lineage placeholders; sourceRecordId and assetId must be null during analysis (they are filled downstream) - never set them to profile keys or any identifier, and they do not select assets that were not supplied.

        Return only JSON matching the supplied schema. Do not use markdown fences, explanatory text, provider tags, prompts, model names, frame numbers, sampling settings, or inferred missing fields.
        """;

    private const string StructureSystemPrompt = """
        You are a multimodal narrative production analyst. Expand exactly one selected narrative Beat into its ordered events, overall timeline, and physical action arc.

        Use only the selected Beat and supplied immutable evidence. Use only supplied evidence keys and participant profile keys. Never invent UUIDs, quotations, character facts, or Moment IDs. Assign each event a stable eventKey (e.g. e1, e2) in chronological order. In actionArc, subjectKey and targetKey are participant profile keys only (e.g. p0 or p1); doors, mirrors, towels, faucets, and other objects belong in targetObject and must never be used as profile keys; every action step references an established eventKey. Express time as Beat-relative seconds and/or event anchors; every window requires a duration intent and explicit precision/overlap policy.

        Return only JSON matching the supplied schema. Do not use markdown fences, explanatory text, or inferred missing fields.
        """;

    private const string SpokenSystemPrompt = """
        You are a multimodal narrative production analyst. Produce the spoken track (narration and dialogue cues) for one selected Beat, anchored to the already-established events supplied in the prompt.

        Dialogue and narration must preserve exact source text drawn from one supplied evidence item. For every cue, set sourceKey to that evidence key and set exactSourceText to a character-for-character contiguous substring of that evidence content, including any newlines and internal whitespace; never trim, reflow, re-case, paraphrase, or stitch together non-contiguous fragments. The application locates each cue by searching its evidence for exactSourceText, so it must appear verbatim. Keep immutable display text separate from normalized spoken text and record normalization method/version. If attribution is ambiguous, set reviewStatus to ReviewRequired, leave speakerKey null, and explain reviewReason. Narration cues have no speaker: leave speakerKey null and set reviewStatus to Validated. Each cue's eventKey must be one of the established eventKeys.

        Return only JSON matching the supplied schema. Do not use markdown fences, explanatory text, or inferred missing fields.
        """;

    private const string SoundscapeSystemPrompt = """
        You are a multimodal narrative production analyst. Design the soundscape (ambience, discrete sound events, and instrumental music) for one selected Beat, anchored to the already-established events supplied in the prompt.

        Include explicit ambience, including authored silence when appropriate. Set ambience.location exactly equal to the selected Beat's location value shown in the prompt (the `location=` line); copy it verbatim with no added detail, paraphrasing, or spot descriptions. Keep music instrumental unless lyrics are explicitly authored in the evidence. Every sound and music window requires a duration intent and explicit precision/overlap policy, and any eventKey must be one of the established eventKeys. Use only supplied evidence and profile keys; never invent facts.

        Return only JSON matching the supplied schema. Do not use markdown fences, explanatory text, or inferred missing fields.
        """;

    private const string AssemblySystemPrompt = """
        You are a multimodal narrative production analyst. Produce the start/end continuity key-states, typed references, and video coverage for one selected Beat, anchored to the already-established events, dialogue/narration cues, sound cues, and music sections supplied in the prompt.

        In startContinuity and endContinuity, characterStates and wardrobeStates keys are the participant profile keys (e.g. p0) - never suffix them (e.g. p0-wardrobe is invalid); objectStates keys are free-form object identifiers (e.g. porch-light); typedReferences.subjectKey is a participant profile key. Typed references describe required roles and lineage placeholders; sourceRecordId and assetId must be null during analysis (they are filled downstream) - never set them to profile keys or any identifier. Video coverage is semantic intent, not provider syntax. requiredMomentRoles must be the literal canonical key-state roles 'start' and 'end' - exactly ['start'] for MomentHold and ['start','end'] for MomentAction, MomentTransition, BeatExcerpt, and WholeBeat - never descriptive or free-form role names, and must not invent Moment IDs. For each video coverage item, first form the complete union of dialogueCueKeys, soundCueKeys, and musicSectionKeys. Emit exactly one audioOwnership entry for every key in that union, with the same key spelling; emit no duplicate entries and do not omit keys. A coverage item with no referenced audio keys must have an empty audioOwnership array. Reference only established eventKeys, dialogue/sound cue keys, and music section keys.

        Return only JSON matching the supplied schema. Do not use markdown fences, explanatory text, or inferred missing fields.
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

    private static JsonObject ActionStep(IEnumerable<string> profileKeys)
    {
        var keys = profileKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
            throw new InvalidOperationException("Beat Production actionArc schema requires at least one participant profile key.");

        return Object(
            ("order", PositiveInteger()),
            ("eventKey", String()),
            ("subjectKey", NullableEnum(keys)),
            ("action", String()),
            ("targetKey", NullableEnum(keys)),
            ("targetObject", NullableString()),
            ("resultingState", String()));
    }

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
    private static JsonObject NullableEnum(params string[] values) => new()
    {
        ["type"] = new JsonArray("string", "null"),
        ["enum"] = new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value)).Append(null).ToArray())
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