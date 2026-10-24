using System.Text;
using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Shared canonical-composition helper that maps the compiled Still brief's frozen characters to
/// their scenario <see cref="Character.PhysicalAttributes"/> and formats a per-character
/// "DEPICTED CHARACTER APPEARANCE" block.
///
/// Both the SDXL (<see cref="SdxlSceneImagePromptBuilder"/>) and Pony
/// (<see cref="PonySceneImagePromptBuilder"/>) canonical prompt paths use this so that each
/// depicted character's fixed visual identity (age, height, body type, iris colour, figure,
/// hair/skin/marks) actually reaches the LLM pre-processor. The compiled brief itself only carries
/// names/poses/clothing — without this injection the pre-processor is told to "describe by
/// appearance" but has no appearance data to use, and per-character likeness is lost.
/// </summary>
internal static class CanonicalCharacterAppearance
{
    private const int AppearanceDescriptionMaxChars = 240;

    private static readonly JsonSerializerOptions FrozenStateJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Builds the block, or <see cref="string.Empty"/> when there is nothing to emit (no scenario
    /// characters, no frozen characters in the brief, every depicted character has no appearance
    /// data, or no depicted characters remain after POV exclusion). The POV character is excluded
    /// for a named observer POV (never in frame); an Omniscient POV includes every frozen character.
    ///
    /// <paramref name="canonicalAppearance"/> is each character's AUTHORED appearance text (B-132), keyed by character
    /// id or name exactly as an override is, or null when the caller resolved none. It WINS over the attribute-derived
    /// block, because it is the text the operator edits in the Character Studio and every other consumer already reads
    /// it - composing a second description here is how the prompt and the operator's own text came to disagree.
    /// <paramref name="appearanceOverrides"/> still outranks both: a deliberate per-render edit is the operator
    /// speaking about THIS image.
    /// </summary>
    internal static string BuildBlock(
        CompiledMediaBrief brief,
        string pov,
        IReadOnlyList<Character>? characters,
        IReadOnlyDictionary<string, string>? appearanceOverrides = null,
        IReadOnlyDictionary<string, string>? canonicalAppearance = null,
        IReadOnlyDictionary<string, string>? appearanceElementOverrides = null)
    {
        if (characters is null || characters.Count == 0)
            return string.Empty;

        var frozen = ReadFrozenCharacters(brief);
        if (frozen.Count == 0)
            return string.Empty;

        var isOmniscient = string.Equals(pov, SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase);
        var depicted = frozen
            .Where(character => isOmniscient
                || (!string.Equals(character.CharacterId, pov, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(character.Name, pov, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var removedByOverride = depicted
            .Where(character => ResolveAppearanceOverride(character, appearanceOverrides) is { Length: 0 })
            .ToHashSet();
        depicted = depicted.Where(character => !removedByOverride.Contains(character)).ToList();
        if (depicted.Count == 0)
            return string.Empty;

        var charactersById = characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Id))
            .ToDictionary(character => character.Id!, StringComparer.OrdinalIgnoreCase);
        var charactersByName = characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Name))
            .GroupBy(character => character.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("DEPICTED CHARACTER APPEARANCE (AUTHORITATIVE FIXED IDENTITY — give each person these traits; use a name only when the request anchors it, otherwise identify them by their place in the frame):");
        var emittedAny = false;
        foreach (var frozenCharacter in depicted)
        {
            var character = ResolveCharacter(frozenCharacter, charactersById, charactersByName);

            // The AUTHORED text first (B-132), then the attribute-derived block. The second is not a fallback in the
            // forbidden sense: a scenario character with no identity pack has no authored text at all, and it still
            // has to be described - this path described it from its attributes before B-132 existed.
            var appearance = ResolveCanonicalAppearance(frozenCharacter, canonicalAppearance) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(appearance) && character is not null)
                appearance = PhysicalAttributesFormatter.FormatVisualBlock(character.PhysicalAttributes);

            if (string.IsNullOrWhiteSpace(appearance) && character is not null && !string.IsNullOrWhiteSpace(character.Description))
                appearance = "Description — " + Truncate(character.Description, AppearanceDescriptionMaxChars);

            // A user-authored override replaces this character's appearance text (single authoritative source).
            if (ResolveAppearanceOverride(frozenCharacter, appearanceOverrides) is { Length: > 0 } overrideText)
                appearance = overrideText;

            var label = string.IsNullOrWhiteSpace(frozenCharacter.Name) ? frozenCharacter.CharacterId : frozenCharacter.Name;

            // B-166 D10: a FACE and a BUILD reference are independently ownable, so when a binding removes only one of
            // them the other must still be written. The authored canonical text is one undivided blob and says nothing
            // about which half is which, so it cannot be split; the attribute-derived face/build sources can. Where
            // neither exists the blob is face+body owned together and a one-sided removal drops the whole line - the
            // documented behaviour rather than a guessed split.
            var faceRemoved = IsAppearanceElementRemoved(frozenCharacter, appearanceElementOverrides, "face");
            var buildRemoved = IsAppearanceElementRemoved(frozenCharacter, appearanceElementOverrides, "build");
            if (faceRemoved || buildRemoved)
            {
                var faceText = ResolveAppearanceElementText(frozenCharacter, appearanceElementOverrides, "face")
                    ?? (faceRemoved ? null : PhysicalAttributesFormatter.FormatVisualFace(character?.PhysicalAttributes));
                var buildText = ResolveAppearanceElementText(frozenCharacter, appearanceElementOverrides, "build")
                    ?? (buildRemoved ? null : PhysicalAttributesFormatter.FormatVisualBody(character?.PhysicalAttributes));

                if (string.IsNullOrWhiteSpace(faceText) && string.IsNullOrWhiteSpace(buildText))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(faceText))
                {
                    sb.AppendLine($"- {label}: Appearance — FACE: {faceText}");
                    emittedAny = true;
                }

                if (!string.IsNullOrWhiteSpace(buildText))
                {
                    sb.AppendLine($"- {label}: Appearance — BUILD: {buildText}");
                    emittedAny = true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(appearance))
                continue;

            sb.AppendLine($"- {label}: {appearance}");
            emittedAny = true;
        }

        return emittedAny
            ? sb.AppendLine("Describe each person listed above in the caption using exactly these traits.").ToString()
            : string.Empty;
    }

    /// <summary>
    /// The character's authored canonical appearance, keyed the same way an override is (character id, then name), or
    /// null when the caller resolved none for this character.
    /// </summary>
    private static string? ResolveCanonicalAppearance(
        FrozenCharacterRef frozen,
        IReadOnlyDictionary<string, string>? canonicalAppearance)
    {
        if (canonicalAppearance is null || canonicalAppearance.Count == 0) return null;
        foreach (var key in new[] { frozen.CharacterId, frozen.Name })
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (canonicalAppearance.TryGetValue(key.Trim(), out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    /// <summary>
    /// Resolves a user-authored appearance override for a frozen character by character id or name.
    /// Returns null when no override exists, an empty string when the appearance is to be removed.
    /// </summary>
    private static string? ResolveAppearanceOverride(        FrozenCharacterRef frozen,
        IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0) return null;
        foreach (var key in new[] { frozen.CharacterId, frozen.Name })
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (overrides.TryGetValue(key.Trim(), out var value)) return value ?? string.Empty;
        }
        return null;
    }

    /// <summary>
    /// Whether a per-element appearance state exists for this character and says "removed". Keys are
    /// <c>&lt;identity&gt;|face</c> / <c>&lt;identity&gt;|build</c>, written by
    /// <see cref="ScenePromptOverridesApplier"/> against the SAME identity (<c>characterId</c>, then <c>name</c>).
    /// </summary>
    private static bool IsAppearanceElementRemoved(
        FrozenCharacterRef frozen,
        IReadOnlyDictionary<string, string>? elementOverrides,
        string element)
        => ResolveAppearanceElementText(frozen, elementOverrides, element) is { Length: 0 };

    /// <summary>
    /// The replacement text for one appearance element, or null when this character carries no state for it. An empty
    /// value means the element is supplied by a reference image and must not be described.
    /// </summary>
    private static string? ResolveAppearanceElementText(
        FrozenCharacterRef frozen,
        IReadOnlyDictionary<string, string>? elementOverrides,
        string element)
    {
        if (elementOverrides is null || elementOverrides.Count == 0) return null;
        foreach (var key in new[] { frozen.CharacterId, frozen.Name })
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (elementOverrides.TryGetValue($"{key.Trim()}|{element}", out var value))
                return value ?? string.Empty;
        }
        return null;
    }

    private static Character? ResolveCharacter(
        FrozenCharacterRef frozen,
        IReadOnlyDictionary<string, Character> charactersById,
        IReadOnlyDictionary<string, Character> charactersByName)
    {
        if (!string.IsNullOrWhiteSpace(frozen.CharacterId)
            && charactersById.TryGetValue(frozen.CharacterId, out var byId))
            return byId;
        if (!string.IsNullOrWhiteSpace(frozen.Name)
            && charactersByName.TryGetValue(frozen.Name.Trim(), out var byName))
            return byName;
        return null;
    }

    private static IReadOnlyList<FrozenCharacterRef> ReadFrozenCharacters(CompiledMediaBrief brief)
    {
        try
        {
            using var document = JsonDocument.Parse(brief.SemanticInputSnapshotJson);
            if (!document.RootElement.TryGetProperty("frozenState", out var frozenState)
                || !frozenState.TryGetProperty("characters", out var characters)
                || characters.ValueKind != JsonValueKind.Array)
                return [];
            return characters.Deserialize<List<FrozenCharacterRef>>(FrozenStateJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record FrozenCharacterRef(string? CharacterId, string? Name);

    private static string Truncate(string value, int maxChars)
        => value.Length <= maxChars ? value : value[..maxChars];
}
