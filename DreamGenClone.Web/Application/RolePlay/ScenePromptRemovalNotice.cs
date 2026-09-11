using System.Text;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Builds the authoritative "USER REMOVALS" notice that accompanies the canonical Still brief.
/// The payload reaching the compiler has the removed elements stripped, but a neighbouring element
/// often restates the same instant (the moment continuity line, the visual description, the
/// composition rationale). Without an explicit signal the pre-processor is free to re-derive what
/// the user deliberately removed — this notice is that signal, and it names what was removed in
/// plain language so the model cannot mistake the absence for missing information.
/// </summary>
internal static class ScenePromptRemovalNotice
{
    public static string Build(ScenePromptOverrides? overrides, IReadOnlyList<Character>? characters)
    {
        if (overrides is null || overrides.IsEmpty) return string.Empty;

        var elements = new List<string>();
        foreach (var field in overrides.Fields)
        {
            if (!field.Removed || string.IsNullOrWhiteSpace(field.ElementKey)) continue;
            var description = Describe(field.ElementKey.Trim(), characters);
            if (!elements.Contains(description, StringComparer.OrdinalIgnoreCase)) elements.Add(description);
        }

        var names = new List<string>();
        foreach (var key in overrides.RemovedCharacters)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            var name = ResolveCharacterName(key.Trim(), characters);
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        }

        if (elements.Count == 0 && names.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("USER REMOVALS — AUTHORITATIVE (the user removed these from the brief before this request; they are deliberately absent):");
        if (elements.Count > 0) sb.AppendLine($"- Removed elements: {string.Join(", ", elements)}");
        if (names.Count > 0) sb.AppendLine($"- Removed characters: {string.Join(", ", names)}");
        sb.AppendLine("Do NOT render, describe, mention, infer, or re-derive any removed item, and do NOT substitute an equivalent taken from another element. Render only what the brief still contains.");
        return sb.ToString();
    }

    private static string Describe(string elementKey, IReadOnlyList<Character>? characters)
    {
        if (elementKey.StartsWith("character:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = elementKey["character:".Length..];
            var dot = rest.LastIndexOf('.');
            if (dot <= 0) return elementKey;
            var character = ResolveCharacterName(rest[..dot], characters);
            var field = CharacterFieldDescriptions.TryGetValue(rest[(dot + 1)..], out var label)
                ? label
                : rest[(dot + 1)..];
            return $"{field} for {character}";
        }

        return ElementDescriptions.TryGetValue(elementKey, out var description) ? description : elementKey;
    }

    private static string ResolveCharacterName(string characterKey, IReadOnlyList<Character>? characters)
    {
        var match = characters?.FirstOrDefault(c =>
            string.Equals(c.Id, characterKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.Name, characterKey, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(match?.Name) ? characterKey : match!.Name!;
    }

    private static readonly IReadOnlyDictionary<string, string> ElementDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["scene.location"] = "the location",
            ["scene.environment"] = "the environment",
            ["scene.timeOfDay"] = "the time of day",
            ["scene.lighting"] = "the lighting",
            ["scene.mood"] = "the mood",
            ["scene.objects"] = "the objects and props",
            ["moment.temporalAnchor"] = "the temporal anchor",
            ["moment.frozenState"] = "the moment continuity line",
            ["moment.visibleAction"] = "the visible action",
            ["moment.compositionRationale"] = "the composition rationale",
            ["moment.productionRoles"] = "the production roles",
            ["frozenState.visualDescription"] = "the visual description",
            ["frozenState.continuityState"] = "the global continuity state",
            ["continuity.start.stateSummary"] = "the opening continuity summary",
            ["continuity.end.stateSummary"] = "the closing continuity summary",
        };

    private static readonly IReadOnlyDictionary<string, string> CharacterFieldDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["physicalLocation"] = "the physical location",
            ["position"] = "the position",
            ["actionOrObservation"] = "the action or observation",
            ["sightline"] = "the sightline",
            ["clothing"] = "the wardrobe",
            ["appearance"] = "the appearance",
        };
}
