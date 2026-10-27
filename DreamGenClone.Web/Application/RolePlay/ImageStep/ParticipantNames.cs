using System.Text.Json.Nodes;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Resolves the brief's participant profile keys (<c>p0</c>, <c>p1</c>) to the words a caption may contain.
/// </summary>
/// <remarks>
/// <para>
/// A profile key is an INTERNAL identifier. It exists so a contact can name both bodies unambiguously, and it must
/// never reach the image prompt: a diffusion model cannot resolve it, and in ComfyUI/SDXL syntax a parenthesised
/// <c>(p0)</c> is weight syntax, so the token is not merely noise — it edits the prompt.
/// </para>
/// <para>
/// This was reported live (SceneImage 57fcc461, 2026-10-25): the caption read "his penis buried to the root (p0)"
/// because the fact sheet and the camera-holder note both printed the raw key. Every renderer that writes a person
/// into prose asks this type, so a key can only leak if a caller formats one by hand.
/// </para>
/// </remarks>
public static class ParticipantNames
{
    /// <summary>Keys mapped to the person's recorded name, for every cast member that carries both.</summary>
    public static IReadOnlyDictionary<string, string> FromBrief(string semanticInputSnapshotJson)
        => string.IsNullOrWhiteSpace(semanticInputSnapshotJson)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : FromFrozenState(ParseFrozenState(semanticInputSnapshotJson) as JsonObject);

    /// <summary>Keys mapped to names, from an already-parsed <c>frozenState</c> node.</summary>
    public static IReadOnlyDictionary<string, string> FromFrozenState(JsonObject? frozen)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (frozen?["characters"] is not JsonArray characters)
        {
            return map;
        }

        foreach (var node in characters)
        {
            if (node is not JsonObject character) continue;
            var key = Scalar(character, "profileKey");
            var name = Scalar(character, "name");
            if (key.Length > 0 && name.Length > 0)
            {
                map[key] = name;
            }
        }

        return map;
    }

    /// <summary>Keys mapped to names, for the people a resolved frame puts in front of the camera.</summary>
    /// <remarks>
    /// The fact sheet is rendered after the viewpoint character has been dropped from the frozen cast, so its own map is
    /// empty for a participant POV. The projection still knows the in-frame people, and their names are what a caption
    /// may use; the viewpoint itself is resolved to "the camera" by the caller, never to a name.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> FromProjection(SceneImagePovProjection.Projection? projection)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (projection is null) return map;
        foreach (var character in projection.InFrame)
        {
            if (!string.IsNullOrWhiteSpace(character.ProfileKey) && !string.IsNullOrWhiteSpace(character.Name))
            {
                map[character.ProfileKey.Trim()] = character.Name.Trim();
            }
        }

        return map;
    }

    /// <summary>
    /// The word for one participant: their name, or — when the record has no name — "this person" for the camera
    /// holder and "the other person" for anyone else. Never the key.
    /// </summary>
    public static string Of(IReadOnlyDictionary<string, string> names, string? profileKey, string fallback = "the other person")
        => string.IsNullOrWhiteSpace(profileKey)
            ? fallback
            : names.TryGetValue(profileKey.Trim(), out var name) ? name : fallback;

    private static JsonNode? ParseFrozenState(string semanticInputSnapshotJson)
    {
        try
        {
            return JsonNode.Parse(semanticInputSnapshotJson) is JsonObject root ? root["frozenState"] : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string Scalar(JsonObject? node, string property)
    {
        if (node is null) return string.Empty;
        foreach (var pair in node)
        {
            if (string.Equals(pair.Key, property, StringComparison.OrdinalIgnoreCase)
                && pair.Value is JsonValue value
                && value.TryGetValue<string>(out var text))
            {
                return text?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
