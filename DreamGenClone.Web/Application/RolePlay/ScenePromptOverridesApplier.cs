using System.Text.Json.Nodes;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>The effective prompt payload after user overrides/removals are applied.</summary>
public sealed record ScenePromptPayload(
    CompiledMediaBrief Brief,
    IReadOnlyDictionary<string, string> AppearanceOverrides,
    IReadOnlySet<string> RemovedCharacters,
    IReadOnlyDictionary<string, string>? AppearanceElementOverrides = null);

/// <summary>
/// The transformed semantic snapshot plus the appearance/removal side-channels.
/// </summary>
/// <param name="AppearanceElementOverrides">
/// Per-ELEMENT appearance state, keyed <c>&lt;identity&gt;|face</c> or <c>&lt;identity&gt;|build</c>, where an empty
/// value means "a reference image supplies this element, do not describe it" and a non-empty value is the text that
/// replaces it. B-166 D10: a face reference supplies the FACE and says nothing about the BUILD, so the two have to be
/// removable independently. Null means no element-level override was requested at all.
/// </param>
public sealed record ScenePromptEffectiveSnapshot(
    string SnapshotJson,
    IReadOnlyDictionary<string, string> AppearanceOverrides,
    IReadOnlySet<string> RemovedCharacters,
    IReadOnlyDictionary<string, string>? AppearanceElementOverrides = null);

/// <summary>
/// Applies <see cref="ScenePromptOverrides"/> to the compiled Still brief's semantic snapshot as HARD
/// SUBSTITUTIONS, so the prompt compiler receives exactly one value per element (never the original
/// plus an override, and never a duplicate copy of it elsewhere in the request). Element scopes mirror
/// the real snapshot shape — `scene.*`, `moment.*`, `frozenState.*`, `continuity.<block>.*` and
/// `character:{key}.*` — so every content carrier the compiler reads is addressable. Removing a
/// character also strips them from the participant summary, the continuity state, the
/// visible-character name lists and the typed references. The persisted brief is never mutated — a
/// shallow copy with the transformed snapshot is returned.
/// </summary>
public static class ScenePromptOverridesApplier
{
    private static readonly IReadOnlyDictionary<string, string> NoAppearanceOverrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static ScenePromptPayload Apply(CompiledMediaBrief brief, ScenePromptOverrides? overrides)
    {
        ArgumentNullException.ThrowIfNull(brief);
        var effective = ApplySnapshot(brief.SemanticInputSnapshotJson, overrides);
        return new ScenePromptPayload(
            brief with { SemanticInputSnapshotJson = effective.SnapshotJson },
            effective.AppearanceOverrides,
            effective.RemovedCharacters,
            effective.AppearanceElementOverrides);
    }

    /// <summary>
    /// The operator's VALUE edits to every fact the POV projection READS: the per-character rows it builds the in-frame
    /// cast from, plus the scene-level <c>contacts</c> it derives the camera holder's visible body from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is <see cref="ApplyOperatorCharacterEdits"/> minus its blind spot. That method filtered on the key PREFIX, so
    /// every scene-level fact was silently dropped — including <c>frozenState.contacts</c>, which the projection reads.
    /// Measured live 2026-10-25 (SceneImage 665618db): the operator replaced the contacts with "hands clenching her
    /// thighs", the compiler's fact sheet honoured it, and the camera-holder note still read the enrichment's original
    /// "penis (buried to the root)". One fact, two readers, two answers — and the stale one reached the image.
    /// </para>
    /// <para>
    /// Deliberately still narrow: a REMOVAL is the projection's own decision, and the scene anchors (<c>location</c>,
    /// <c>objects</c>) are what the frame's geometry is derived from, so an operator edit must not move the camera.
    /// Only facts the projection reads as CONTENT are applied here, and that list is explicit rather than a wildcard.
    /// </para>
    /// </remarks>
    public static string ApplyOperatorProjectionEdits(string semanticInputSnapshotJson, ScenePromptOverrides? overrides)
    {
        ArgumentNullException.ThrowIfNull(semanticInputSnapshotJson);
        if (overrides is null || overrides.IsEmpty) return semanticInputSnapshotJson;

        var projectedEdits = overrides.Fields
            .Where(field => !field.Removed && IsProjectionContentKey(field.ElementKey))
            .ToList();
        if (projectedEdits.Count == 0) return semanticInputSnapshotJson;

        return ApplySnapshot(
            semanticInputSnapshotJson,
            new ScenePromptOverrides { Fields = projectedEdits }).SnapshotJson;
    }

    /// <summary>
    /// The scene-level facts the POV projection consumes as content. One list, so a future scene-level reader cannot be
    /// added without being added here.
    /// </summary>
    private static bool IsProjectionContentKey(string? elementKey)
    {
        if (string.IsNullOrWhiteSpace(elementKey)) return false;
        var key = elementKey.Trim();
        return key.StartsWith("character:", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "frozenState.contacts", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The OPERATOR-authored character facts alone, applied to a snapshot.
    /// </summary>
    /// <remarks>
    /// A surface that reads a character's own fields (the POV projection composes its in-frame facts from
    /// <c>frozenState.characters</c>) must see the text the operator typed, not the enrichment's original. Applying the
    /// WHOLE override set there would also apply the suppression half — the room is removed because a location image
    /// supplies it — and the frame would be derived from a scene with no place in it. So only non-removed
    /// <c>character:{key}.*</c> values are applied here; removals stay the projection's own decision.
    /// </remarks>
    public static string ApplyOperatorCharacterEdits(string semanticInputSnapshotJson, ScenePromptOverrides? overrides)
    {
        ArgumentNullException.ThrowIfNull(semanticInputSnapshotJson);
        if (overrides is null || overrides.IsEmpty) return semanticInputSnapshotJson;

        var characterEdits = overrides.Fields
            .Where(field => !field.Removed
                && field.ElementKey.StartsWith("character:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (characterEdits.Count == 0) return semanticInputSnapshotJson;

        return ApplySnapshot(
            semanticInputSnapshotJson,
            new ScenePromptOverrides { Fields = characterEdits }).SnapshotJson;
    }

    /// <summary>Applies overrides/removals to a raw semantic snapshot JSON string (testable core).</summary>
    public static ScenePromptEffectiveSnapshot ApplySnapshot(string semanticInputSnapshotJson, ScenePromptOverrides? overrides)
    {
        ArgumentNullException.ThrowIfNull(semanticInputSnapshotJson);

        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appearanceOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? appearanceElementOverrides = null;
        if (overrides is null || overrides.IsEmpty)
            return new ScenePromptEffectiveSnapshot(semanticInputSnapshotJson, NoAppearanceOverrides, removed);

        var root = JsonNode.Parse(semanticInputSnapshotJson) as JsonObject
            ?? throw new InvalidOperationException("The compiled Still brief semantic snapshot is not a JSON object.");
        var frozen = root["frozenState"] as JsonObject;
        var moment = root["moment"] as JsonObject;
        var continuity = root["continuity"] as JsonObject;

        foreach (var key in overrides.RemovedCharacters)
        {
            if (!string.IsNullOrWhiteSpace(key)) removed.Add(key.Trim());
        }

        foreach (var field in overrides.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.ElementKey)) continue;
            var key = field.ElementKey.Trim();

            // B-167 D2: the image payload carries one owner per fact. A key that names a dropped restatement, or a
            // leaf the brief never carries, is refused HERE with a named diagnostic instead of being written into the
            // snapshot as a brand-new property — which is how a stale draft silently reintroduced a duplicate.
            StillBriefElements.RequireSelectable(key);

            if (key.StartsWith("character:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = key["character:".Length..];
                var (characterKey, property) = SplitCharacterElement(rest, frozen);
                if (property.Length == 0) continue;

                // B-166 D10: `appearance.face` / `appearance.build` are independently ownable. A face reference
                // supplies the face and says nothing about the build, so a face-only binding must not delete the
                // build prose (the defect the operator reported as "the characters are off a little").
                if (property.StartsWith("appearance.", StringComparison.OrdinalIgnoreCase))
                {
                    var element = property["appearance.".Length..];
                    appearanceElementOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    appearanceElementOverrides[
                        $"{SuppressionIdentityOf(frozen, characterKey, field.ElementKey)}|{element}"] =
                        field.Removed ? string.Empty : (field.Value ?? string.Empty);
                    continue;
                }

                if (string.Equals(property, "appearance", StringComparison.OrdinalIgnoreCase))
                {
                    // The suppression is written against the character's RESOLVED identity, never the raw key.
                    // CanonicalCharacterAppearance reads this map by a frozen character's `characterId` and then its
                    // name, so a key that resolves to neither suppressed NOTHING — silently — and the DEPICTED
                    // CHARACTER APPEARANCE block went on describing the very appearance a reference image was
                    // supplying. Measured live 2026-10-12: the removal carried the character's identity-owner id
                    // while the brief addresses participants as `p0`/`p1`, so the prompt ordered the model to forget
                    // an appearance it was still being handed.
                    appearanceOverrides[SuppressionIdentityOf(frozen, characterKey, field.ElementKey)] =
                        field.Removed ? string.Empty : (field.Value ?? string.Empty);
                }
                else
                {
                    var character = FindCharacter(frozen, characterKey);
                    if (character is not null) ApplyScalar(character, property, field);
                }
                continue;
            }

            // Scopes mirror the real snapshot shape (lineage | moment | frozenState | continuity |
            // typedReferences | videoKeyState). An unrecognized scope is a UI/contract drift bug and
            // fails fast rather than silently dropping the user's instruction.
            var separator = key.IndexOf('.');
            var scope = (separator < 0 ? key : key[..separator]).ToLowerInvariant();
            var remainder = separator < 0 ? string.Empty : key[(separator + 1)..];

            switch (scope)
            {
                case "scene":
                    if (remainder.Length == 0) break;
                    if (string.Equals(remainder, "objects", StringComparison.OrdinalIgnoreCase)) ApplyObjects(frozen, field);
                    else ApplyScalar(frozen, remainder, field);
                    break;
                case "moment":
                    if (remainder.Length == 0) break;
                    ApplyScalar(moment, remainder, field);
                    break;
                case "frozenstate":
                    if (remainder.Length == 0) break;
                    if (string.Equals(remainder, "contacts", StringComparison.OrdinalIgnoreCase)) ApplyContacts(frozen, field);
                    else ApplyScalar(frozen, remainder, field);
                    break;
                case "continuity":
                    ApplyContinuity(root, continuity, remainder, field);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Prompt element key '{field.ElementKey}' does not name a payload element in the compiled Still brief.");
            }
        }

        if (removed.Count > 0)
            RemoveCharacters(root, frozen, moment, continuity, removed);

        return new ScenePromptEffectiveSnapshot(root.ToJsonString(), appearanceOverrides, removed, appearanceElementOverrides);
    }

    /// <summary>
    /// Splits <c>&lt;characterKey&gt;.&lt;propertyPath&gt;</c>. The character key is resolved against the characters the
    /// brief actually carries (longest match first), so a key containing a dot still addresses the right person, and
    /// the remainder is a PROPERTY PATH that may nest (<c>at.posture</c>, <c>facing.toward</c>,
    /// <c>appearance.face</c>).
    /// </summary>
    private static (string CharacterKey, string Property) SplitCharacterElement(string rest, JsonObject? frozen)
    {
        string? best = null;
        if (frozen?["characters"] is JsonArray characters)
        {
            foreach (var node in characters)
            {
                if (node is not JsonObject character) continue;
                foreach (var property in (string[])["characterId", "name", "profileKey"])
                {
                    if (StringProperty(character, property) is not { Length: > 0 } candidate) continue;
                    if (!rest.StartsWith(candidate + ".", StringComparison.OrdinalIgnoreCase)) continue;
                    if (best is null || candidate.Length > best.Length) best = candidate;
                }
            }
        }

        if (best is not null)
        {
            return (best, rest[(best.Length + 1)..]);
        }

        var dot = rest.IndexOf('.');
        return dot <= 0 ? (rest, string.Empty) : (rest[..dot], rest[(dot + 1)..]);
    }

    private static void ApplyScalar(JsonObject? target, string property, ScenePromptFieldOverride field)
    {
        if (target is null) return;

        // A nested property path (`at.posture`, `facing.toward`, `event.line`) writes the leaf, creating the parent
        // object when a v3 brief does not carry it — an operator fact the row shows is a fact worth recording.
        var separator = property.IndexOf('.');
        if (separator > 0)
        {
            var head = property[..separator];
            var tail = property[(separator + 1)..];
            var headName = ResolvePropertyName(target, head);
            if (target[headName] is not JsonObject child)
            {
                // A removal of a nested leaf that does not exist removes nothing; an EDIT creates the parent so the
                // operator's fact is recorded (a v3 brief has no `at`/`facing` object yet).
                if (field.Removed) return;
                if (target[headName] is not null)
                {
                    throw new InvalidOperationException(
                        $"Prompt element '{property}' nests under '{head}', but the compiled Still brief records "
                        + $"'{head}' as a value rather than an object. Remove '{head}' first or edit it directly.");
                }

                child = new JsonObject();
                target[headName] = child;
            }

            ApplyScalar(child, tail, field);
            return;
        }

        var name = ResolvePropertyName(target, property);
        if (field.Removed)
        {
            target.Remove(name);
            return;
        }
        target[name] = field.Value ?? string.Empty;
    }

    /// <summary>
    /// Writes <c>frozenState.contacts</c> from one line per contact:
    /// <c>from|part|to|targetPart|manner</c>. The format is lossless and unambiguous, which is what an operator row
    /// over a structured fact needs; a blank value removes every contact (an instant in which bodies do not touch).
    /// </summary>
    private static void ApplyContacts(JsonObject? frozen, ScenePromptFieldOverride field)
    {
        if (frozen is null) return;
        if (field.Removed)
        {
            frozen.Remove(ResolvePropertyName(frozen, "contacts"));
            return;
        }

        var array = new JsonArray();
        foreach (var line in (field.Value ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length != 5 || parts.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException(
                    $"Contact '{line}' must be written as 'from|part|to|targetPart|manner' with all five values "
                    + "(for example 'p1|hands|p0|hips|grip').");
            }

            array.Add(new JsonObject
            {
                ["from"] = parts[0],
                ["part"] = parts[1],
                ["to"] = parts[2],
                ["targetPart"] = parts[3],
                ["manner"] = parts[4]
            });
        }

        frozen[ResolvePropertyName(frozen, "contacts")] = array;
    }

    /// <summary>
    /// Resolves a property name against the properties actually present on the target so element
    /// keys stay case-tolerant (`scene.timeOfDay` / `scene.timeofday`) and never add a parallel key.
    /// </summary>
    private static string ResolvePropertyName(JsonObject target, string property)
    {
        foreach (var pair in target)
        {
            if (string.Equals(pair.Key, property, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        }
        return property;
    }

    /// <summary>Handles `continuity`, `continuity.start`, and `continuity.start.stateSummary`.</summary>
    private static void ApplyContinuity(JsonObject root, JsonObject? continuity, string remainder, ScenePromptFieldOverride field)
    {
        if (remainder.Length == 0)
        {
            if (field.Removed) root.Remove(ResolvePropertyName(root, "continuity"));
            return;
        }

        if (continuity is null) return;
        var separator = remainder.IndexOf('.');
        if (separator <= 0)
        {
            // `continuity.<block>` is an object block (start/end); only its scalar leaves are editable.
            return;
        }

        var blockName = ResolvePropertyName(continuity, remainder[..separator]);
        ApplyScalar(continuity[blockName] as JsonObject, remainder[(separator + 1)..], field);
    }

    private static void ApplyObjects(JsonObject? frozen, ScenePromptFieldOverride field)
    {
        if (frozen is null) return;
        if (field.Removed)
        {
            frozen.Remove("objects");
            return;
        }
        var array = new JsonArray();
        foreach (var item in (field.Value ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            array.Add(item);
        frozen["objects"] = array;
    }

    private static JsonObject? FindCharacter(JsonObject? frozen, string characterKey)
    {
        if (frozen?["characters"] is not JsonArray characters) return null;
        foreach (var node in characters)
        {
            if (node is JsonObject character && CharacterMatches(character, characterKey)) return character;
        }
        return null;
    }

    /// <summary>
    /// The key an appearance suppression has to be written against: the depicted character's <c>characterId</c>, or
    /// its name when it carries none.
    ///
    /// <para>
    /// <see cref="CanonicalCharacterAppearance"/> reads the suppression map by exactly those two properties. A key
    /// that resolves to neither suppresses nothing, and it does so SILENTLY - which is how a bound face and a bound
    /// body lost to the appearance block that re-described them. An unresolvable key means the caller is addressing a
    /// character the brief does not contain, so this fails fast and names both the key and the characters the brief
    /// does contain rather than emitting a suppression that does nothing.
    /// </para>
    /// </summary>
    private static string SuppressionIdentityOf(JsonObject? frozen, string characterKey, string elementKey)
    {
        var character = FindCharacter(frozen, characterKey)
            ?? throw new InvalidOperationException(
                $"Prompt element key '{elementKey}' addresses character '{characterKey}', which the compiled Still "
                + $"brief does not contain; it addresses {DescribeCharacters(frozen)}. A removal that resolves to "
                + "nobody suppresses nothing, and the prompt would go on describing an element a reference image "
                + "already supplies.");

        foreach (var property in (string[])["characterId", "name"])
        {
            if (StringProperty(character, property) is { Length: > 0 } identity) return identity.Trim();
        }

        throw new InvalidOperationException(
            $"Character '{characterKey}' in the compiled Still brief carries neither a characterId nor a name, so "
            + $"prompt element key '{elementKey}' cannot be addressed.");
    }

    /// <summary>The brief's own characters, as the keys a removal may address them by, for a refusal message.</summary>
    private static string DescribeCharacters(JsonObject? frozen)
    {
        if (frozen?["characters"] is not JsonArray characters || characters.Count == 0)
        {
            return "no frozen characters at all";
        }

        var described = new List<string>();
        foreach (var node in characters)
        {
            if (node is not JsonObject character) continue;
            var profileKey = StringProperty(character, "profileKey");
            if (string.IsNullOrWhiteSpace(profileKey)) continue;
            var name = StringProperty(character, "name");
            described.Add(string.IsNullOrWhiteSpace(name) ? profileKey : $"{profileKey} ({name})");
        }

        return described.Count == 0 ? "no frozen characters at all" : string.Join(", ", described);
    }

    /// <summary>
    /// Removes every trace of a character, not just the cast entry. The frozen cast carries
    /// `profileKey` (`p0`, `p1`, ...) while the composer addresses characters by id or name, so the
    /// removed entries are resolved to their profile keys and names BEFORE the cast is mutated —
    /// otherwise the participant summary, the continuity state and the typed references keep
    /// asserting that the removed people are present.
    /// </summary>
    private static void RemoveCharacters(
        JsonObject root,
        JsonObject? frozen,
        JsonObject? moment,
        JsonObject? continuity,
        IReadOnlySet<string> removed)
    {
        var profileKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (frozen?["characters"] is JsonArray characters)
        {
            foreach (var node in characters)
            {
                if (node is not JsonObject character || !MatchesAnyKey(character, removed)) continue;
                if (StringProperty(character, "profileKey") is { Length: > 0 } profileKey)
                    profileKeys.Add(profileKey.Trim());
                if (StringProperty(character, "name") is { Length: > 0 } name)
                    names.Add(name.Trim());
            }

            for (var i = characters.Count - 1; i >= 0; i--)
            {
                if (characters[i] is JsonObject character && MatchesAnyKey(character, removed))
                    characters.RemoveAt(i);
            }

            foreach (var node in characters)
            {
                if (node is not JsonObject character) continue;
                if (character["visibleCharacterNames"] is JsonArray visible)
                    RemoveMatchingValues(visible, removed, names);
            }
        }

        if (moment?["participantSummary"] is JsonArray participants)
            RemoveMatchingEntries(participants, removed, profileKeys, names);

        if (continuity is not null)
        {
            foreach (var blockName in (string[])["start", "end"])
            {
                if (continuity[blockName] is not JsonObject block) continue;
                if (block["characterStates"] is JsonArray states)
                    RemoveMatchingEntries(states, removed, profileKeys, names);
                if (block["wardrobeStates"] is JsonArray wardrobeStates)
                    RemoveMatchingEntries(wardrobeStates, removed, profileKeys, names);
            }
        }

        if (root["typedReferences"] is JsonArray references)
            RemoveMatchingEntries(references, removed, profileKeys, names);
    }

    /// <summary>Drops keyed array entries (participants, continuity state, typed references).</summary>
    private static void RemoveMatchingEntries(
        JsonArray array,
        IReadOnlySet<string> removed,
        IReadOnlySet<string> profileKeys,
        IReadOnlySet<string> names)
    {
        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (array[i] is not JsonObject entry) continue;
            if (MatchesEntry(entry, removed, profileKeys, names)) array.RemoveAt(i);
        }
    }

    private static void RemoveMatchingValues(JsonArray array, IReadOnlySet<string> removed, IReadOnlySet<string> names)
    {
        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (StringProperty(array[i], null) is not { Length: > 0 } value) continue;
            if (removed.Contains(value) || names.Contains(value)) array.RemoveAt(i);
        }
    }

    private static bool MatchesEntry(
        JsonObject entry,
        IReadOnlySet<string> removed,
        IReadOnlySet<string> profileKeys,
        IReadOnlySet<string> names)
    {
        foreach (var property in KeyProperties)
        {
            if (StringProperty(entry, property) is not { Length: > 0 } value) continue;
            var candidate = value.Trim();
            if (removed.Contains(candidate) || profileKeys.Contains(candidate) || names.Contains(candidate)) return true;
        }
        return false;
    }

    /// <summary>
    /// Properties that can identify a character inside a keyed array entry: `profileKey` and
    /// `characterId`/`name` for the participant summary, `key` for the continuity state, and
    /// `subjectKey` for the typed references.
    /// </summary>
    private static readonly string[] KeyProperties = ["profileKey", "characterId", "name", "key", "subjectKey"];

    private static string? StringProperty(JsonNode? node, string? property)
    {
        var value = property is null ? node : (node as JsonObject)?[property];
        return value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : null;
    }

    private static bool CharacterMatches(JsonObject character, string characterKey)
        => Matches(characterKey, StringProperty(character, "characterId"))
            || Matches(characterKey, StringProperty(character, "name"))
            || Matches(characterKey, StringProperty(character, "profileKey"));

    private static bool MatchesAnyKey(JsonObject node, IReadOnlySet<string> keys)
    {
        foreach (var property in (string[])["characterId", "name", "profileKey"])
        {
            if (StringProperty(node, property) is { Length: > 0 } candidate && keys.Contains(candidate.Trim())) return true;
        }
        return false;
    }

    private static bool Matches(string key, string? candidate)
        => !string.IsNullOrWhiteSpace(candidate) && string.Equals(key, candidate.Trim(), StringComparison.OrdinalIgnoreCase);
}
