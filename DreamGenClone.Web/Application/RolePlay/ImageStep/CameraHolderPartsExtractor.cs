using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Derives the CAMERA HOLDER's visible body parts for a participant POV from the moment data, so the operator does not
/// have to hand-write "his hands on her hips" when the brief already records the contact and stance.
/// </summary>
/// <remarks>
/// <para>
/// The compiled brief's frozen state records the holder's own action, position and clothing, their continuity state
/// and wardrobe entries, and the whole-event prose — and <c>DropViewpoint</c> currently deletes all of it. This reads
/// that SAME data (before the drop) and keeps only the clauses that name a VISIBLE body part of the holder, so the
/// first-person frame can show the hands/arms/torso/genitals/feet without ever showing the holder as a second person.
/// </para>
/// <para>
/// Deliberately conservative about identity: face, head, hair, eyes, height and build are never body parts here, so a
/// derived result can never pull the holder's identity into the frame. A brief that records no holder body parts yields
/// null — the caller then states nothing rather than inventing a limb the moment never mentions.
/// </para>
/// </remarks>
public static class CameraHolderPartsExtractor
{
    /// <summary>Body parts that can be VISIBLE in a first-person frame. Face/head/hair/eyes/build are identity and stay out.</summary>
    private static readonly Regex BodyPart = new(
        @"\b(?:hands?|fingers?|arms?|chest|torso|hips?|pelvis|cock|penis|erection|thighs?|legs?|knees?|feet|foot|shoulders?|back|wrists?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Extracts the holder's visible body-part clauses, or null when none are recorded.</summary>
    public static string? Extract(string semanticInputSnapshotJson, string? viewpointProfileKey)
    {
        if (string.IsNullOrWhiteSpace(semanticInputSnapshotJson) || string.IsNullOrWhiteSpace(viewpointProfileKey))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(semanticInputSnapshotJson) is not JsonObject root
                || root["frozenState"] is not JsonObject frozen)
            {
                return null;
            }

            var key = viewpointProfileKey.Trim();
            string? holderName = null;
            string holderAction = string.Empty, holderPosition = string.Empty, holderClothing = string.Empty;

            if (frozen["characters"] is JsonArray characters)
            {
                foreach (var node in characters)
                {
                    if (node is not JsonObject character
                        || !string.Equals(StringMember(character, "profileKey"), key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    holderName = StringMember(character, "name");
                    holderAction = StringMember(character, "actionOrObservation") ?? string.Empty;
                    holderPosition = StringMember(character, "position") ?? string.Empty;
                    holderClothing = StringMember(character, "clothing") ?? string.Empty;
                }
            }

            // The holder's OWN facts are unambiguous — every body-part clause in them is the holder's.
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Collect(string? source, bool unambiguous)
            {
                if (string.IsNullOrWhiteSpace(source)) return;
                foreach (var clause in source.Split([',', '.', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!BodyPart.IsMatch(clause) || !seen.Add(clause)) continue;
                    if (unambiguous || ReferencesHolder(clause, holderName, PossessiveOf(holderAction, holderPosition, holderClothing)))
                    {
                        found.Add(clause);
                    }
                }
            }

            Collect(holderAction, unambiguous: true);
            Collect(holderPosition, unambiguous: true);
            Collect(holderClothing, unambiguous: true);

            // The holder's continuity state and wardrobe entries (both blocks) are also unambiguously theirs.
            if (root["continuity"] is JsonObject continuity)
            {
                foreach (var block in continuity)
                {
                    if (block.Value is not JsonObject blockObject) continue;
                    foreach (var property in (string[])["characterStates", "wardrobeStates"])
                    {
                        if (blockObject[property] is not JsonArray entries) continue;
                        foreach (var node in entries)
                        {
                            if (node is not JsonObject entry
                                || !string.Equals(StringMember(entry, "key"), key, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            Collect(StringMember(entry, "value"), unambiguous: true);
                        }
                    }
                }
            }

            // Whole-event prose: a body-part clause is the holder's when it names the holder or their possessive — the
            // operator's hand-edited "his long thick erect penis can be seen above Becky's ass" is picked up here.
            Collect(StringMember(frozen, "visualDescription"), unambiguous: false);
            Collect(StringMember(frozen, "continuityState"), unambiguous: false);
            if (root["moment"] is JsonObject moment)
            {
                Collect(StringMember(moment, "visibleAction"), unambiguous: false);
                Collect(StringMember(moment, "frozenState"), unambiguous: false);
            }

            return found.Count == 0 ? null : string.Join("; ", found);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether a whole-event clause refers to the camera holder, by name or by their recorded possessive.</summary>
    private static bool ReferencesHolder(string clause, string? holderName, string? holderPossessive)
    {
        if (!string.IsNullOrWhiteSpace(holderName)
            && clause.Contains(holderName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(holderPossessive)
            && Regex.IsMatch(clause, $@"\b{Regex.Escape(holderPossessive)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>The holder's possessive pronoun ("his"/"her"/"their"), derived from their own recorded facts, or null.</summary>
    private static string? PossessiveOf(string? action, string? position, string? clothing)
    {
        var text = $"{action} {position} {clothing}".ToLowerInvariant();
        if (ContainsWord(text, "he") || ContainsWord(text, "his")) return "his";
        if (ContainsWord(text, "she") || ContainsWord(text, "her")) return "her";
        if (ContainsWord(text, "they") || ContainsWord(text, "their")) return "their";
        return null;
    }

    private static bool ContainsWord(string text, string word)
        => Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? StringMember(JsonObject obj, string property)
    {
        foreach (var pair in obj)
        {
            if (string.Equals(pair.Key, property, StringComparison.OrdinalIgnoreCase)
                && pair.Value is JsonValue value
                && value.TryGetValue<string>(out var text))
            {
                return text;
            }
        }
        return null;
    }
}
