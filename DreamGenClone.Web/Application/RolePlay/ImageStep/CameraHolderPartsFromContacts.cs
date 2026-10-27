using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Derives the CAMERA HOLDER's visible body parts for a participant POV from the moment's recorded contacts.
/// </summary>
/// <remarks>
/// <para>
/// This replaces <c>CameraHolderPartsExtractor</c>, which pattern-matched body-part words out of the whole-event prose
/// and guessed whose they were from a possessive pronoun. That guess is unnecessary now that the moment records
/// contacts as facts: a contact names both bodies and both body areas, so the holder's contribution is a lookup, not
/// an inference — and it sees operator edits, which the prose scrape did not.
/// </para>
/// <para>
/// Deliberately conservative about identity: only the recorded <c>part</c> / <c>targetPart</c> values are emitted, so a
/// derived result can never pull the holder's face, head, hair or build into the frame. A record with no contacts yields
/// null — the caller then states nothing rather than inventing a limb the moment never mentions.
/// </para>
/// </remarks>
public static class CameraHolderPartsFromContacts
{
    /// <summary>The holder's visible parts, or null when the record carries no contacts.</summary>
    /// <param name="semanticInputSnapshotJson">The compiled Still brief's semantic snapshot (operator edits applied).</param>
    /// <param name="viewpointProfileKey">The camera holder's profile key.</param>
    public static string? Extract(string semanticInputSnapshotJson, string? viewpointProfileKey)
    {
        if (string.IsNullOrWhiteSpace(semanticInputSnapshotJson) || string.IsNullOrWhiteSpace(viewpointProfileKey))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(semanticInputSnapshotJson) is not JsonObject root
                || root["frozenState"] is not JsonObject frozen
                || frozen["contacts"] is not JsonArray contacts)
            {
                return null;
            }

            var key = viewpointProfileKey.Trim();
            var names = ParticipantNames.FromFrozenState(frozen);
            var parts = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in contacts.OfType<JsonObject>())
            {
                var from = StringMember(node, "from");
                var to = StringMember(node, "to");
                var part = StringMember(node, "part");
                var targetPart = StringMember(node, "targetPart");
                var manner = StringMember(node, "manner");

                var holderIsFrom = string.Equals(from, key, StringComparison.OrdinalIgnoreCase);
                var holderIsTo = string.Equals(to, key, StringComparison.OrdinalIgnoreCase);
                // The note becomes prompt text, so it names people the way a caption does — never their profile key.
                var otherPerson = ParticipantNames.Of(
                    names,
                    holderIsFrom ? to : from,
                    holderIsFrom ? "the other person" : "the camera holder");

                // A SELF-contact (both ends are the holder) is one statement about their own body: the part doing the
                // touching is the part that enters the frame. Emitting both ends would describe the same limb twice.
                if (holderIsFrom && holderIsTo)
                {
                    if (!string.IsNullOrWhiteSpace(part) && seen.Add(part))
                    {
                        parts.Add(string.IsNullOrWhiteSpace(targetPart)
                            ? DescribeOwn(part, manner)
                            : $"{part} ({DescribeOwn(targetPart, manner, onOwn: true)})");
                    }
                    continue;
                }

                if (holderIsFrom && !string.IsNullOrWhiteSpace(part))
                {
                    if (seen.Add(part)) parts.Add(Describe(part, targetPart, manner, otherPerson));
                }

                if (holderIsTo && !string.IsNullOrWhiteSpace(targetPart))
                {
                    if (seen.Add(targetPart)) parts.Add(Describe(targetPart, part, manner, otherPerson));
                }
            }

            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The part NAMES inside a note this class produced — "the camera holder's hands on Becky's thighs; …" → hands — for
    /// a caller that needs the list rather than the sentence.
    /// </summary>
    /// <remarks>
    /// The note format is this class's own, so reading it back is not a scrape of foreign prose; it is the one place that
    /// knows both shapes (a part touching someone else, and a self-contact's bare part name). Every caller that needs
    /// names — the inclusion sentence and the camera-holder statement — goes through this, so they cannot disagree about
    /// which parts are in the picture. Wardrobe rides in the same note but is not a body part, so it is excluded.
    /// </remarks>
    public static IReadOnlyList<string> PartNamesOf(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return [];
        }

        var parts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PrefixedPart.Matches(note))
        {
            Add(match.Groups["part"].Value);
        }

        foreach (Match match in SelfPart.Matches(note))
        {
            Add(match.Groups["part"].Value);
        }

        return parts;

        void Add(string raw)
        {
            var part = raw.Trim();
            if (part.Length == 0
                || part.Equals("wardrobe", StringComparison.OrdinalIgnoreCase)
                || !seen.Add(part))
            {
                return;
            }

            parts.Add(part);
        }
    }

    /// <summary>"the camera holder's &lt;part&gt; on …" / "the camera holder's &lt;part&gt; (…)".</summary>
    private static readonly Regex PrefixedPart = new(
        @"the camera holder's (?<part>[a-z][a-z'\- ]{0,30}?)(?= on | \()",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A self-contact's bare part name: "hand (pressed against own thigh)".</summary>
    private static readonly Regex SelfPart = new(
        @"(?:^|;\s*)(?<part>[a-z][a-z'\-]{0,20})\s+\((?=[^)]*\bown\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// One contact as a note the caption can expand: WHOSE part, what it touches, and how.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other body area is not optional detail. Without it the note read "hands (clenching tightly Becky)" and the
    /// caption writer, having no idea what the hands were clenching, borrowed the nearest body part in the record and
    /// wrote "Hands clenching tightly around her forearm" for a contact that is on her thighs (reported live 2026-10-25,
    /// SceneImage 36857035).
    /// </para>
    /// <para>
    /// And the OWNER is not optional either. A bare "hands on Becky's thighs" reads as easily as Becky's own hands —
    /// the next render wrote "hands clenching tightly around her thighs to steady herself", i.e. her hands on herself,
    /// and the one after drew no hands at all (SceneImages f2acd4fb / 34b95f2f, same day). So the note names the camera
    /// holder, which is the vocabulary the caption rule already uses for the second person behind the subject.
    /// </para>
    /// </remarks>
    private static string Describe(string part, string? otherPart, string? manner, string? otherPerson)
    {
        var verb = string.IsNullOrWhiteSpace(manner) ? "touching" : manner;
        var owner = $"the camera holder's {part}";
        if (string.IsNullOrWhiteSpace(otherPerson) || string.IsNullOrWhiteSpace(otherPart))
        {
            return string.IsNullOrWhiteSpace(otherPerson)
                ? $"{owner} ({verb})"
                : $"{owner} on {otherPerson} ({verb})";
        }

        return $"{owner} on {otherPerson}'s {otherPart} ({verb})";
    }

    /// <summary>One end of a self-contact: the holder's own body, so no other person is named.</summary>
    private static string DescribeOwn(string part, string? manner, bool onOwn = false)
    {
        var verb = string.IsNullOrWhiteSpace(manner) ? "touching" : manner;
        return onOwn ? $"{verb} own {part}" : $"{part} ({verb} own body)";
    }

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
