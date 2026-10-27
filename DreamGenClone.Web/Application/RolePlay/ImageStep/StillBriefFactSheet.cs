using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Renders a compiled Still brief's selected facts as LABELLED PROSE for the compile LLM (B-167 Part B, P0).
/// </summary>
/// <remarks>
/// <para>
/// The compiler used to be handed the raw semantic snapshot — a JSON object whose nested keys and repeated prose made
/// it treat the brief as something to transcribe, which is how a caption came back reciting "Becky: a; b; c". A fact
/// sheet states each fact once, under a label that says what kind of fact it is, and says explicitly that operator
/// edits are INTENT to translate rather than text to copy.
/// </para>
/// <para>
/// It renders whatever the snapshot actually contains: the caller has already applied overrides, reference removals,
/// POV suppression and the frame, so this never re-derives or re-adds an element. A fact the brief does not carry is
/// simply absent from the sheet.
/// </para>
/// </remarks>
public static class StillBriefFactSheet
{
    /// <summary>The heading both prompt builders emit, so the section is identifiable in a stored prompt.</summary>
    public const string Heading = "CANONICAL STILL BRIEF — SELECTED FACTS";

    /// <summary>The suffix that marks a value the operator wrote and the compiler must reinterpret.</summary>
    public const string IntentMarker = " [operator intent — translate it into picture facts, do not copy the wording]";

    /// <summary>A profile key that reached a rendered value: it identifies a body inside the brief, never a word.</summary>
    private static readonly Regex ProfileKeyToken = new(
        @"(?<![A-Za-z0-9])p\d{1,2}(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Render(
        string semanticInputSnapshotJson,
        ScenePromptOverrides? overrides,
        string? viewpointProfileKey = null,
        IReadOnlyDictionary<string, string>? namesByKey = null)
    {
        ArgumentNullException.ThrowIfNull(semanticInputSnapshotJson);

        if (JsonNode.Parse(semanticInputSnapshotJson) is not JsonObject root)
        {
            throw new InvalidOperationException(
                "The compiled Still brief semantic snapshot is not a JSON object, so no fact sheet can be rendered.");
        }

        var edited = EditedKeys(overrides);
        var frozen = root["frozenState"] as JsonObject;
        var moment = root["moment"] as JsonObject;
        var names = new Dictionary<string, string>(
            ParticipantNames.FromFrozenState(frozen),
            StringComparer.OrdinalIgnoreCase);
        if (namesByKey is not null)
        {
            foreach (var (key, name) in namesByKey)
            {
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(name)) names[key.Trim()] = name.Trim();
            }
        }

        // A participant fact sheet is rendered AFTER the viewpoint character was dropped from the frozen cast, so a key
        // whose person is no longer in the record is the camera holder: the word for it is "the camera", never the key
        // (reported live 2026-10-27, prompt record 8655924e: "facing: toward p0" was copied into the caption).
        string Sanitize(string value)
            => ProfileKeyToken.Replace(value, match =>
            {
                var key = match.Value;
                if (!string.IsNullOrWhiteSpace(viewpointProfileKey)
                    && string.Equals(key, viewpointProfileKey.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return SceneImagePovProjection.UnseenViewpoint;
                }

                return names.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : "the other person";
            });

        var sheet = new StringBuilder();

        sheet.AppendLine(Heading);
        sheet.AppendLine("Each line below is ONE fact. Translate them into picture facts; state each once and in your own words.");
        // The facts are presented one per line for READING, and a small model mirrors that shape back as a per-person
        // line list. Saying so here is what keeps the answer a single paragraph without needing a corrective pass.
        sheet.AppendLine("This list is the INPUT shape, not the answer shape: return your caption as ONE continuous paragraph of prose, with no line breaks, no bullet points and no per-person lines.");
        sheet.AppendLine();

        Line(sheet, "PLACE", frozen, "location", edited, "frozenState.location", Sanitize);
        Line(sheet, "TIME OF DAY", frozen, "timeOfDay", edited, "frozenState.timeOfDay", Sanitize);
        Line(sheet, "LIGHTING", frozen, "lighting", edited, "frozenState.lighting", Sanitize);
        Line(sheet, "ENVIRONMENT", frozen, "environment", edited, "frozenState.environment", Sanitize);
        Line(sheet, "MOOD", frozen, "mood", edited, "frozenState.mood", Sanitize);
        Line(sheet, "OBJECTS", frozen, "objects", edited, "frozenState.objects", Sanitize);

        if (moment is not null)
        {
            Line(sheet, "TEMPORAL ANCHOR", moment, "temporalAnchor", edited, "moment.temporalAnchor", Sanitize);
            // The moment's COMPOSITION RATIONALE is deliberately NOT rendered. It is the discovery step's note on why
            // this instant was picked, it restates the event in prose, and — unlike every other line here — it has no
            // operator-editable row, so a fact the operator deleted from the event came back through it and reached the
            // image ("Bites down on her own forearm" after the operator removed the bite; reported live 2026-10-25,
            // SceneImage 50e778e5). B-167 rule: the event line is the single owner of the moment's prose.
        }

        if (frozen?["event"] is JsonObject eventNode)
        {
            var line = Scalar(eventNode, "line");
            var anchor = Scalar(eventNode, "anchor");
            if (line.Length > 0)
            {
                var suffix = anchor.Length > 0 ? $" (at {anchor})" : string.Empty;
                var marker = edited.Contains("frozenState.event.line") ? IntentMarker : string.Empty;
                sheet.AppendLine($"EVENT: {Sanitize(line)}{suffix}{marker}");
            }
        }

        sheet.AppendLine($"CONTACTS: {RenderContacts(frozen?["contacts"] as JsonArray, edited, names)}");
        sheet.AppendLine();

        RenderCast(sheet, frozen?["characters"] as JsonArray, edited, overrides, Sanitize);
        RenderFrame(sheet, root["blocking"] as JsonObject, names, Sanitize);
        RenderReferences(sheet, root["typedReferences"] as JsonArray, names);

        return sheet.ToString().TrimEnd();
    }

    private static void RenderCast(
        StringBuilder sheet,
        JsonArray? characters,
        IReadOnlySet<string> edited,
        ScenePromptOverrides? overrides,
        Func<string, string> sanitize)
    {
        if (characters is null || characters.Count == 0)
        {
            sheet.AppendLine("CAST: none recorded.");
            return;
        }

        sheet.AppendLine("CAST (world facts — position, facing and contact are POV-independent):");
        foreach (var node in characters)
        {
            if (node is not JsonObject character) continue;
            var profileKey = Scalar(character, "profileKey");
            var name = Scalar(character, "name");
            var involvement = Scalar(character, "involvement");
            // The NAME is what the caption may say. The profile key is an internal identifier (and "(p0)" is weight
            // syntax in a diffusion prompt), so it labels nothing here — operator override keys are built from
            // `characterKey` below, not from this line.
            var label = string.IsNullOrWhiteSpace(name) ? profileKey : name;
            var key = string.IsNullOrWhiteSpace(profileKey) ? name : profileKey;
            sheet.AppendLine($"- {label} [{involvement}]");

            var at = character["at"] as JsonObject;
            if (at is not null)
            {
                var anchor = Scalar(at, "anchor");
                var posture = Scalar(at, "posture");
                var body = string.Join(" ", new[] { posture, anchor.Length > 0 ? $"at {anchor}" : string.Empty }
                    .Where(part => part.Length > 0));
                AppendCastLine(sheet, "body", body, edited, overrides, key, sanitize, "at.anchor", "at.posture");
            }

            var facing = character["facing"] as JsonObject;
            if (facing is not null)
            {
                AppendCastLine(sheet, "facing", Scalar(facing, "toward"), edited, overrides, key, sanitize, "facing.toward");
            }

            AppendCastLine(sheet, "stance", Scalar(character, "position"), edited, overrides, key, sanitize, "position");
            AppendCastLine(sheet, "action", Scalar(character, "actionOrObservation"), edited, overrides, key, sanitize, "actionOrObservation");
            AppendCastLine(sheet, "sightline", Scalar(character, "sightline"), edited, overrides, key, sanitize, "sightline");
            AppendCastLine(sheet, "wardrobe", Scalar(character, "clothing"), edited, overrides, key, sanitize, "clothing");
            AppendCastLine(sheet, "place", Scalar(character, "physicalLocation"), edited, overrides, key, sanitize, "physicalLocation");
            AppendCastLine(sheet, "visible to them", RenderStringArray(character["visibleCharacterNames"] as JsonArray), edited, overrides, key, sanitize);
        }
    }

    private static void AppendCastLine(
        StringBuilder sheet,
        string label,
        string value,
        IReadOnlySet<string> edited,
        ScenePromptOverrides? overrides,
        string characterKey,
        Func<string, string> sanitize,
        params string[] leaves)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var key = $"{StillBriefElements.CharacterPrefix}{characterKey}";
        var marker = leaves.Any(leaf => edited.Contains($"{key}.{leaf}")) ? IntentMarker : string.Empty;
        sheet.AppendLine($"  {label}: {sanitize(value)}{marker}");
    }

    private static string RenderContacts(
        JsonArray? contacts,
        IReadOnlySet<string> edited,
        IReadOnlyDictionary<string, string> names)
    {
        if (contacts is null)
        {
            // Null means "not recorded" (a v3 moment). Saying "none" would claim knowledge the record does not have.
            return "not recorded — state no body-to-body contact and do not invent one.";
        }

        if (contacts.Count == 0)
        {
            return "none — two bodies do not touch in this instant.";
        }

        // Contacts name both bodies by profile key so the record is unambiguous. A caption may never carry that key:
        // the model cannot resolve it, and "(p0)" is weight syntax in the diffusion prompt. Resolve to names here.
        var marker = edited.Contains("frozenState.contacts") ? IntentMarker : string.Empty;
        var described = contacts
            .OfType<JsonObject>()
            .Select(contact =>
            {
                var from = Scalar(contact, "from");
                var part = Scalar(contact, "part");
                var to = Scalar(contact, "to");
                var targetPart = Scalar(contact, "targetPart");
                var manner = Scalar(contact, "manner");
                // A self-contact is one body touching itself; naming the same person twice reads as two people.
                var target = string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
                    ? $"own {targetPart}"
                    : $"{ParticipantNames.Of(names, to)}'s {targetPart}";
                return $"{ParticipantNames.Of(names, from)}'s {part} on {target} ({manner})";
            });

        return string.Join("; ", described) + marker;
    }

    private static void RenderFrame(
        StringBuilder sheet,
        JsonObject? blocking,
        IReadOnlyDictionary<string, string> names,
        Func<string, string> sanitize)
    {
        if (blocking is null) return;

        sheet.AppendLine();
        sheet.AppendLine("FRAME (recorded — this is the composition the render is framed with):");
        if (blocking["camera"] is JsonObject camera)
        {
            var parts = new[] { "angle", "shotSize", "side" }
                .Select(property => Scalar(camera, property))
                .Where(value => value.Length > 0);
            var cameraLine = string.Join(", ", parts);
            if (cameraLine.Length > 0) sheet.AppendLine($"- camera: {cameraLine}");
        }

        if (blocking["subjects"] is JsonArray subjects)
        {
            foreach (var node in subjects.OfType<JsonObject>())
            {
                var subjectKey = Scalar(node, "subjectKey");
                var roleText = sanitize(Scalar(node, "roleText"));
                if (subjectKey.Length == 0 && roleText.Length == 0) continue;
                // The placement reads with the person's NAME; the subject key is an internal identifier and a
                // diffusion prompt cannot resolve it.
                var label = ParticipantNames.Of(names, subjectKey, string.Empty);
                sheet.AppendLine(label.Length > 0 ? $"- {label}: {roleText}" : $"- {roleText}");
            }
        }
    }

    private static void RenderReferences(
        StringBuilder sheet,
        JsonArray? references,
        IReadOnlyDictionary<string, string> names)
    {
        if (references is null || references.Count == 0) return;

        sheet.AppendLine();
        sheet.AppendLine("REFERENCES (an image supplies this element — do not describe it in words):");
        foreach (var node in references.OfType<JsonObject>())
        {
            var subject = Scalar(node, "subjectKey");
            var role = Scalar(node, "role");
            var kind = Scalar(node, "kind");
            var label = ReferenceLabel(role, kind);
            // A key is an internal identifier, never a word for the caption: resolve the subject to a person phrase.
            var described = subject.Length == 0
                ? $"the {label}"
                : $"the {label} of {ParticipantNames.Of(names, subject)}";
            sheet.AppendLine($"- {described}");
        }
    }

    /// <summary>The element a typed reference supplies, in the operator's vocabulary rather than the record's enum name.</summary>
    private static string ReferenceLabel(string role, string kind)
        => (role.Length > 0 ? role : kind) switch
        {
            "CharacterIdentity" => "face",
            "WardrobeContinuity" => "wardrobe",
            "VoiceIdentity" => "voice",
            "LocationContinuity" => "place",
            "PropContinuity" => "prop",
            "Pose" => "pose",
            "Style" => "style",
            var other when other.Length > 0 => other.ToLowerInvariant(),
            _ => "reference"
        };

    private static void Line(
        StringBuilder sheet,
        string label,
        JsonObject? source,
        string property,
        IReadOnlySet<string> edited,
        string elementKey,
        Func<string, string> sanitize)
    {
        if (source is null) return;
        var value = source[property] is JsonArray
            ? RenderStringArray(source[property] as JsonArray)
            : Scalar(source, property);
        if (string.IsNullOrWhiteSpace(value)) return;
        var marker = edited.Contains(elementKey) ? IntentMarker : string.Empty;
        sheet.AppendLine($"{label}: {sanitize(value)}{marker}");
    }

    private static string RenderStringArray(JsonArray? array)
        => array is null
            ? string.Empty
            : string.Join("; ", array.Select(item => Scalar(item)).Where(value => value.Length > 0));

    private static string Scalar(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => (text ?? string.Empty).Trim(),
        _ => string.Empty
    };

    private static string Scalar(JsonObject? obj, string property)
    {
        if (obj is null) return string.Empty;
        foreach (var pair in obj)
        {
            if (string.Equals(pair.Key, property, StringComparison.OrdinalIgnoreCase)) return Scalar(pair.Value);
        }
        return string.Empty;
    }

    /// <summary>The element keys the operator overrode, so a value they wrote reads as intent rather than as a fact.</summary>
    private static HashSet<string> EditedKeys(ScenePromptOverrides? overrides)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (overrides is null) return set;
        foreach (var field in overrides.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.ElementKey) || field.Removed) continue;
            set.Add(field.ElementKey.Trim());
        }
        return set;
    }
}
