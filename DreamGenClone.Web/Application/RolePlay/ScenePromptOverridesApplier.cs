using System.Text.Json.Nodes;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>The effective prompt payload after user overrides/removals are applied.</summary>
public sealed record ScenePromptPayload(
    CompiledMediaBrief Brief,
    IReadOnlyDictionary<string, string> AppearanceOverrides,
    IReadOnlySet<string> RemovedCharacters);

/// <summary>The transformed semantic snapshot plus the appearance/removal side-channels.</summary>
public sealed record ScenePromptEffectiveSnapshot(
    string SnapshotJson,
    IReadOnlyDictionary<string, string> AppearanceOverrides,
    IReadOnlySet<string> RemovedCharacters);

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
            effective.RemovedCharacters);
    }

    /// <summary>Applies overrides/removals to a raw semantic snapshot JSON string (testable core).</summary>
    public static ScenePromptEffectiveSnapshot ApplySnapshot(string semanticInputSnapshotJson, ScenePromptOverrides? overrides)
    {
        ArgumentNullException.ThrowIfNull(semanticInputSnapshotJson);

        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appearanceOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

            if (key.StartsWith("character:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = key["character:".Length..];
                var dot = rest.LastIndexOf('.');
                if (dot <= 0) continue;
                var characterKey = rest[..dot];
                var property = rest[(dot + 1)..];
                if (string.Equals(property, "appearance", StringComparison.OrdinalIgnoreCase))
                {
                    appearanceOverrides[characterKey] = field.Removed ? string.Empty : (field.Value ?? string.Empty);
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
                    ApplyScalar(frozen, remainder, field);
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

        return new ScenePromptEffectiveSnapshot(root.ToJsonString(), appearanceOverrides, removed);
    }

    private static void ApplyScalar(JsonObject? target, string property, ScenePromptFieldOverride field)
    {
        if (target is null) return;
        var name = ResolvePropertyName(target, property);
        if (field.Removed)
        {
            target.Remove(name);
            return;
        }
        target[name] = field.Value ?? string.Empty;
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
