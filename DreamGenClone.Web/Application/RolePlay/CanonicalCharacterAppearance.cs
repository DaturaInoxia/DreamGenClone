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
/// depicted character's fixed visual identity (age, weight, body type, iris colour, figure,
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
    /// </summary>
    internal static string BuildBlock(
        CompiledMediaBrief brief,
        string pov,
        IReadOnlyList<Character>? characters,
        IReadOnlyDictionary<string, string>? appearanceOverrides = null)
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
        sb.AppendLine("DEPICTED CHARACTER APPEARANCE (AUTHORITATIVE FIXED IDENTITY — describe each person by these traits, never by name or relationship):");
        var emittedAny = false;
        foreach (var frozenCharacter in depicted)
        {
            var character = ResolveCharacter(frozenCharacter, charactersById, charactersByName);
            var appearance = character is null
                ? string.Empty
                : PhysicalAttributesFormatter.FormatVisualBlock(character.PhysicalAttributes);
            if (string.IsNullOrWhiteSpace(appearance) && character is not null && !string.IsNullOrWhiteSpace(character.Description))
                appearance = "Description — " + Truncate(character.Description, AppearanceDescriptionMaxChars);

            // A user-authored override replaces this character's appearance text (single authoritative source).
            if (ResolveAppearanceOverride(frozenCharacter, appearanceOverrides) is { Length: > 0 } overrideText)
                appearance = overrideText;

            if (string.IsNullOrWhiteSpace(appearance))
                continue;

            var label = string.IsNullOrWhiteSpace(frozenCharacter.Name) ? frozenCharacter.CharacterId : frozenCharacter.Name;
            sb.AppendLine($"- {label}: {appearance}");
            emittedAny = true;
        }

        return emittedAny ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Resolves a user-authored appearance override for a frozen character by character id or name.
    /// Returns null when no override exists, an empty string when the appearance is to be removed.
    /// </summary>
    private static string? ResolveAppearanceOverride(
        FrozenCharacterRef frozen,
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
