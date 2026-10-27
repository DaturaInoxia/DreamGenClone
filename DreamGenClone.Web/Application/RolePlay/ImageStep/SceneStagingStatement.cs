using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// The recorded GEOMETRY of a still — who is where in the picture, which way they face, how their bodies are arranged
/// and which parts touch — stated as DATA at the end of the prompt.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a compile model cannot be trusted to carry a fact it was merely asked to carry. Measured
/// 2026-10-26 (SceneImage b829fa81 / 4b627c8a, Omniscient, both people with identity references bound): the compiler
/// input stated <c>Becky — LEFT, in the foreground</c>, <c>Dean — RIGHT, in the foreground</c>, Becky facing "the tin
/// wall beyond the workbench", Dean facing "Becky's back", hips pressed to her rear, torso low over the boards — and the
/// caption it returned named NONE of it. Two renders then showed Dean standing beside her facing the lens with a gap
/// between their bodies, and one turned her to face the right of the picture. The contract that asked for all of it was
/// in the deployed system prompt, verbatim.
/// </para>
/// <para>
/// So the geometry is not asked for; it is stated, after the caption, the way <see cref="SceneCameraStatement"/> states
/// the camera and the camera holder's visible body is stated. The caption keeps the descriptive surface (light,
/// materials, mood); this block keeps the facts that decide whether the picture is the recorded moment at all.
/// </para>
/// <para>
/// It is PURE and derived only from recorded data — the projection's in-frame cast, its frame placement, and the
/// operator-edited contacts. A fact the record does not hold is omitted rather than guessed: no placement is stated
/// when the brief recorded none, no facing when the record names none, no contact when none is recorded.
/// </para>
/// </remarks>
public static class SceneStagingStatement
{
    /// <summary>The marker a reader uses to identify the block, and the reason it is appended last.</summary>
    public const string Marker = "STAGING — AUTHORITATIVE";

    /// <summary>
    /// The staging block for this POV, or empty when the record carries no geometry to state (a User Intent draft has no
    /// projection) or nobody is in frame.
    /// </summary>
    /// <param name="projection">The resolved POV frame.</param>
    /// <param name="semanticInputSnapshotJson">
    /// The operator-edited brief snapshot — the SAME string the projection was built from, so the contacts stated here
    /// are the operator's, never the enrichment's original. Null states no contact at all.
    /// </param>
    public static string For(
        SceneImagePovProjection.Projection? projection,
        string? semanticInputSnapshotJson)
    {
        if (projection is null || projection.InFrame.Count == 0)
        {
            return string.Empty;
        }

        var labels = Labels(projection);
        var names = NamesByProfileKey(semanticInputSnapshotJson);
        var lines = new List<string>();

        foreach (var character in projection.InFrame)
        {
            var clauses = new List<string>();
            if (labels.TryGetValue(character.ProfileKey, out var label) && label.Placement.Length > 0)
            {
                clauses.Add(label.Placement);
            }

            var body = NullIfBlank(character.WorldBody) ?? NullIfBlank(character.Position);
            if (body is not null)
            {
                clauses.Add(TrimSentence(body));
            }

            var facing = NullIfBlank(character.WorldFacing);
            if (facing is not null)
            {
                clauses.Add($"facing {ResolveFacing(facing, character.ProfileKey, projection, names)}");
            }

            var clothing = NullIfBlank(character.Clothing);
            if (clothing is not null)
            {
                clauses.Add(TrimSentence(clothing));
            }

            if (clauses.Count > 0)
            {
                lines.Add($"- {label.Text}: {string.Join("; ", clauses)}.");
            }
        }

        lines.AddRange(Contacts(projection, semanticInputSnapshotJson, labels));

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        return $"{Marker} (recorded geometry — the picture must show exactly this):{Environment.NewLine}"
            + string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// How each in-frame person is NAMED in this block: by their place in the picture and what they are wearing, never by
    /// a story name. The name means nothing to the image model, and the placement is what the record actually fixes.
    /// </summary>
    private static Dictionary<string, Label> Labels(SceneImagePovProjection.Projection projection)
    {
        var placements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (projection.Frame?.Subjects is { Count: > 0 } subjects)
        {
            foreach (var subject in subjects)
            {
                var placement = $"{subject.Position.Horizontal} {subject.Position.Depth}".Trim();
                if (placement.Length > 0)
                {
                    placements[subject.SubjectKey] = placement;
                }
            }
        }

        // One person in the picture is the picture's subject, centred: a two-shot slot staged for a cast the frame no
        // longer holds would place a lone figure at the edge of their own frame.
        if (projection.InFrame.Count == 1 && placements.Count == 0)
        {
            placements[projection.InFrame[0].ProfileKey] = "Center, foreground";
        }

        var labels = new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var character in projection.InFrame)
        {
            placements.TryGetValue(character.ProfileKey, out var placement);
            var descriptor = IsNude(character.Clothing) ? "the nude person" : "the clothed person";
            counts[descriptor] = counts.TryGetValue(descriptor, out var seen) ? seen + 1 : 1;
            labels[character.ProfileKey] = new Label(
                placement ?? string.Empty,
                descriptor);
        }

        // Two people wearing the same thing cannot share a descriptor: fall back to the placement, which is unique
        // whenever the record placed them.
        foreach (var pair in labels.ToList())
        {
            if (counts[labels[pair.Key].Text] > 1 && labels[pair.Key].Placement.Length > 0)
            {
                labels[pair.Key] = labels[pair.Key] with
                {
                    Text = $"the {labels[pair.Key].Text["the ".Length..]} on the "
                        + labels[pair.Key].Placement.Split(' ')[0].ToLowerInvariant()
                };
            }
        }

        return labels;
    }

    /// <summary>
    /// The recorded contacts, as sentences with an owner, a target and a manner — the shape a caption writer cannot
    /// mis-attribute. Read from the operator-edited snapshot, so a contact the operator deleted is not restated.
    /// </summary>
    private static IEnumerable<string> Contacts(
        SceneImagePovProjection.Projection projection,
        string? semanticInputSnapshotJson,
        IReadOnlyDictionary<string, Label> labels)
    {
        var frozen = FrozenState(semanticInputSnapshotJson);
        if (frozen?["contacts"] is not System.Text.Json.Nodes.JsonArray contacts)
        {
            yield break;
        }

        var inFrame = projection.InFrame
            .Select(character => character.ProfileKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var node in contacts.OfType<System.Text.Json.Nodes.JsonObject>())
        {
            var from = Scalar(node, "from");
            var to = Scalar(node, "to");
            var part = Scalar(node, "part");
            var targetPart = Scalar(node, "targetPart");
            var manner = Scalar(node, "manner");
            if (part.Length == 0 || targetPart.Length == 0) continue;

            // A contact is stated only when BOTH ends are in the picture. One end on the camera holder is the holder's
            // body, which the projection states separately; stating it here would invent a second pictured person.
            if (!inFrame.Contains(from) || !inFrame.Contains(to)) continue;

            if (!labels.TryGetValue(from, out var owner) || !labels.TryGetValue(to, out var target)) continue;

            var verb = manner.Length == 0 ? "touching" : manner;
            yield return string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
                ? $"- {owner.Text}: {part} {verb} against their own {targetPart}."
                : $"- {owner.Text}: {part} {verb} against {target.Text}'s {targetPart}.";
        }
    }

    private static System.Text.Json.Nodes.JsonObject? FrozenState(string? semanticInputSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(semanticInputSnapshotJson))
        {
            return null;
        }

        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(semanticInputSnapshotJson)
                is System.Text.Json.Nodes.JsonObject root
                ? root["frozenState"] as System.Text.Json.Nodes.JsonObject
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<System.Text.Json.Nodes.JsonObject> Characters(string? semanticInputSnapshotJson)
        => FrozenState(semanticInputSnapshotJson)?["characters"]
            is System.Text.Json.Nodes.JsonArray characters
            ? characters.OfType<System.Text.Json.Nodes.JsonObject>()
            : [];

    /// <summary>
    /// A facing target re-expressed without a story name. The image model cannot resolve a name, and the two things a
    /// target can name are already in this block: the CAMERA (a participant POV's holder) and the OTHER PERSON in the
    /// picture. So "Becky's back" becomes "the other person's back" and "Dean's chest" becomes "the camera" — the same
    /// fact, in words the model can act on.
    /// </summary>
    private static string ResolveFacing(
        string facing,
        string profileKey,
        SceneImagePovProjection.Projection projection,
        IReadOnlyDictionary<string, string> namesByProfileKey)
    {
        var resolved = facing;
        foreach (var (key, name) in namesByProfileKey)
        {
            if (name.Length == 0
                || string.Equals(key, profileKey, StringComparison.OrdinalIgnoreCase)
                || !resolved.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isCameraHolder = !projection.IsOmniscient
                && string.Equals(key, projection.UnseenViewpointProfileKey, StringComparison.OrdinalIgnoreCase);
            resolved = Replace(resolved, name, isCameraHolder ? "the camera" : "the other person");
        }

        return TrimSentence(resolved);
    }

    /// <summary>Case-insensitive replace that keeps the possessive readable ("Becky's" → "the other person's").</summary>
    private static string Replace(string text, string name, string replacement)
    {
        var index = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            text = string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + name.Length));
            index = text.IndexOf(name, index + replacement.Length, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    /// <summary>Recorded names by profile key, so a facing target can be scrubbed of the camera holder.</summary>
    private static Dictionary<string, string> NamesByProfileKey(string? semanticInputSnapshotJson)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var character in Characters(semanticInputSnapshotJson))
        {
            var key = Scalar(character, "profileKey");
            var name = Scalar(character, "name");
            if (key.Length > 0 && name.Length > 0)
            {
                names[key] = name;
            }
        }

        return names;
    }

    private static bool IsNude(string? clothing)
    {
        if (string.IsNullOrWhiteSpace(clothing)) return false;
        var text = clothing.ToLowerInvariant();
        return text.Contains("nude") || text.Contains("naked") || text.Contains("unclothed")
            || text.Contains("bare all over");
    }

    private static string TrimSentence(string value)
        => value.Trim().TrimEnd('.');

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Scalar(System.Text.Json.Nodes.JsonObject node, string property)
    {
        foreach (var pair in node)
        {
            if (string.Equals(pair.Key, property, StringComparison.OrdinalIgnoreCase)
                && pair.Value is System.Text.Json.Nodes.JsonValue value
                && value.TryGetValue<string>(out var text))
            {
                return text?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    /// <summary>A person's placement in the picture and the words this block uses for them.</summary>
    private readonly record struct Label(string Placement, string Text);
}
