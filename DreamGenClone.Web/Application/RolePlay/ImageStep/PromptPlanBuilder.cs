using System.Text.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Builds the typed <see cref="PromptPlan"/> for one (moment, POV) from the facts a render already has: the
/// operator-edited brief, the resolved <see cref="SceneImagePovProjection.Projection"/>, and the frame.
/// </summary>
/// <remarks>
/// <para>
/// P1 of <c>specs/Planning/B-167-scene-data-facts-and-prompt-compiler/holistic-prompt-pipeline.md</c>. It runs in SHADOW
/// mode first: the plan is built and recorded for every prompt generation but nothing downstream consumes it, so a plan
/// defect cannot damage a render while the family renderers are still being written.
/// </para>
/// <para>
/// It is PURE: no model, no repository, no clock. Every fact it states comes from the same operator-edited snapshot the
/// projection reads (<c>ScenePromptOverridesApplier.ApplyOperatorProjectionEdits</c>) or from the projection itself, so
/// the plan cannot disagree with the frame the render will use — which is the property that made the old fact sheet
/// drift from the projection (a deleted contact survived in the camera-holder note, reported 2026-10-25).
/// </para>
/// </remarks>
public static class PromptPlanBuilder
{
    /// <summary>Builds the plan.</summary>
    /// <param name="semanticInputSnapshotJson">
    /// The operator-edited brief snapshot — the SAME string the projection was built from.
    /// </param>
    /// <param name="projection">The resolved POV frame.</param>
    /// <param name="blocking">The compiled frame, for per-subject placement; null for a participant POV, whose frame is egocentric.</param>
    /// <param name="overrides">The operator's overrides, so the plan can record which slots they touched.</param>
    public static PromptPlan Build(
        string semanticInputSnapshotJson,
        SceneImagePovProjection.Projection projection,
        SceneBlocking? blocking = null,
        ScenePromptOverrides? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(semanticInputSnapshotJson);
        ArgumentNullException.ThrowIfNull(projection);

        var root = JsonNode.Parse(semanticInputSnapshotJson) as JsonObject
            ?? throw new InvalidOperationException(
                "The compiled Still brief semantic snapshot is not a JSON object, so no prompt plan can be built.");
        var frozen = FrozenState(root);
        var moment = root["moment"] as JsonObject;
        var momentEvent = frozen["event"] as JsonObject;
        var names = ParticipantNames.FromFrozenState(frozen);
        var extentOverrides = ExtentOverrides(overrides, frozen);

        var subjects = projection.InFrame
            .Select(character => new PromptPlanSubject(
                character.ProfileKey,
                character.Name,
                // A bound face AND body reference supplies the appearance, so the plan must not carry prose for it:
                // that is how the text stopped competing with the reference (B-166 D10).
                character.AppearanceFromText ? AppearanceOf(frozen, character.ProfileKey) : null,
                character.Clothing ?? string.Empty,
                character.WorldBody ?? character.Position ?? string.Empty,
                character.WorldFacing,
                PlacementOf(blocking, [], character.ProfileKey, !projection.IsOmniscient),
                extentOverrides.TryGetValue(character.ProfileKey, out var extent)
                    ? extent
                    : FrameInclusionExtent.WholeBody)
            {
                // Every fact the still brief's fact sheet states has a typed home here. P1's acceptance gate: a fact the
                // sheet can state and the plan cannot carry is a fact the family renderer can silently drop, because
                // the renderer stops reading the sheet and starts reading the plan.
                Action = RecordedOrNull(character.ActionOrObservation),
                Sightline = RecordedOrNull(character.Sightline),
                Place = RecordedOrNull(character.PhysicalLocation)
            })
            .ToList();

        // Placement needs the FINAL in-frame count, so it is a second pass: one subject in the picture is centred, and a
        // participant POV is egocentric (the subject is dead ahead), whatever the compiled blocking staged.
        subjects = subjects
            .Select(subject => subject with
            {
                FramePlacement = PlacementOf(blocking, subjects, subject.ProfileKey, !projection.IsOmniscient)
            })
            .ToList();

        var inFrameKeys = subjects.Select(subject => subject.ProfileKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contacts = ReadContacts(frozen, names, inFrameKeys);

        return new PromptPlan
        {
            ActionLine = momentEvent is null ? null : RecordedOrNull(Scalar(momentEvent, "line")),
            ActionAnchor = momentEvent is null ? null : RecordedOrNull(Scalar(momentEvent, "anchor")),
            Setting = RecordedOrNull(projection.SceneLocation),
            SettingKind = projection.LocationType,
            SettingSuppliedByReference = projection.SceneSuppliedByReference,
            TimeOfDay = RecordedOrNull(Scalar(frozen, "timeOfDay")),
            TemporalAnchor = moment is null ? null : RecordedOrNull(Scalar(moment, "temporalAnchor")),
            Mood = RecordedOrNull(Scalar(frozen, "mood")),
            Objects = ReadStringArray(frozen?["objects"]),
            Lighting = RecordedOrNull(Scalar(frozen, "lighting")),
            Atmosphere = RecordedOrNull(Scalar(frozen, "environment")),
            Subjects = subjects,
            Contacts = contacts,
            HolderBody = HolderBodyOf(projection, frozen, names),
            Frame = FrameOf(projection, blocking, subjects),
            MustAppear = MustAppearOf(subjects, contacts, projection, frozen),
            OperatorEditedKeys = EditedKeys(overrides)
        };
    }

    /// <summary>
    /// A one-line summary for the shadow debug event, so a live render can be compared plan-to-caption without reading
    /// a whole plan.
    /// </summary>
    public static object Summarise(PromptPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new
        {
            subjects = plan.Subjects.Select(subject => new
            {
                key = subject.ProfileKey,
                name = subject.Name,
                wardrobe = subject.Wardrobe,
                extent = subject.InFrameExtent,
                appearance = subject.Appearance is null ? "reference" : "text",
                placement = subject.FramePlacement,
                facing = subject.Facing,
                action = subject.Action,
                sightline = subject.Sightline,
                place = subject.Place
            }),
            contacts = plan.Contacts.Select(contact => contact.IsSelfContact
                ? $"{contact.FromName} {contact.Part} on own {contact.TargetPart} ({contact.Manner})"
                : $"{contact.FromName} {contact.Part} on {contact.ToName}'s {contact.TargetPart} ({contact.Manner})"),
            holderBody = plan.HolderBody is { } holder
                ? new { parts = holder.Parts, wardrobe = holder.Wardrobe }
                : null,
            frame = new { plan.Frame.ShotSize, plan.Frame.Angle, plan.Frame.Height, plan.Frame.Inclusion },
            setting = plan.Setting,
            settingKind = plan.SettingKind,
            settingFromReference = plan.SettingSuppliedByReference,
            timeOfDay = plan.TimeOfDay,
            temporalAnchor = plan.TemporalAnchor,
            mood = plan.Mood,
            objects = plan.Objects,
            lighting = plan.Lighting,
            atmosphere = plan.Atmosphere,
            action = plan.ActionLine,
            actionAnchor = plan.ActionAnchor,
            mustAppear = plan.MustAppear,
            edited = plan.OperatorEditedKeys
        };
    }

    private static PromptPlanFrame FrameOf(
        SceneImagePovProjection.Projection projection,
        SceneBlocking? blocking,
        IReadOnlyList<PromptPlanSubject> subjects)
    {
        var camera = projection.OperatorCamera is { } chosen
            ? new SceneBlockingCamera(chosen.ShotSize, chosen.Angle, null, null, chosen.Height)
            : projection.Camera ?? blocking?.Camera;

        return new PromptPlanFrame(
            camera?.ShotSize,
            camera?.Angle,
            camera?.Height,
            SharedExtent(subjects),
            FirstPerson: !projection.IsOmniscient);
    }

    /// <summary>
    /// The extent every subject shares, or null when they differ. A frame that stated "crown to feet" for one person and
    /// "waist up" for another would be the same self-contradiction the plan exists to remove, so the disagreeing case
    /// states nothing globally and lets each subject's own extent speak.
    /// </summary>
    private static string? SharedExtent(IReadOnlyList<PromptPlanSubject> subjects)
    {
        if (subjects.Count == 0) return null;
        var first = subjects[0].InFrameExtent;
        return subjects.All(subject => string.Equals(subject.InFrameExtent, first, StringComparison.OrdinalIgnoreCase))
            ? first
            : null;
    }

    /// <summary>
    /// The holder's own visible body, or null when the frame shows none of them.
    /// </summary>
    /// <remarks>
    /// Derived from the CONTACTS rather than parsed back out of the note string, so the plan states parts and wardrobe as
    /// data. The note the prompt carries is realised from this in P3.
    /// </remarks>
    private static PromptPlanHolderBody? HolderBodyOf(
        SceneImagePovProjection.Projection projection,
        JsonObject? frozen,
        IReadOnlyDictionary<string, string> names)
    {
        if (string.IsNullOrWhiteSpace(projection.CameraHolderParts)
            || string.IsNullOrWhiteSpace(projection.UnseenViewpointProfileKey))
        {
            return null;
        }

        var key = projection.UnseenViewpointProfileKey!;
        var parts = new List<string>();
        foreach (var contact in ContactsOf(frozen))
        {
            var from = Scalar(contact, "from");
            var to = Scalar(contact, "to");
            var isFrom = string.Equals(from, key, StringComparison.OrdinalIgnoreCase);
            var isTo = string.Equals(to, key, StringComparison.OrdinalIgnoreCase);
            var part = isFrom ? Scalar(contact, "part") : Scalar(contact, "targetPart");
            if ((isFrom || isTo) && !string.IsNullOrWhiteSpace(part) && !parts.Contains(part, StringComparer.Ordinal))
            {
                parts.Add(part);
            }
        }

        var wardrobe = CharacterOf(frozen, key) is { } character ? Scalar(character, "clothing") : null;
        _ = names;
        return new PromptPlanHolderBody(parts, string.IsNullOrWhiteSpace(wardrobe) ? null : wardrobe);
    }

    private static IReadOnlyList<PromptPlanContact> ReadContacts(
        JsonObject? frozen,
        IReadOnlyDictionary<string, string> names,
        IReadOnlySet<string> inFrameKeys)
    {
        var contacts = new List<PromptPlanContact>();
        foreach (var contact in ContactsOf(frozen))
        {
            var from = Scalar(contact, "from");
            var to = Scalar(contact, "to");
            // A contact belongs in the plan's cast list only when BOTH ends are pictured. A contact with one end on the
            // camera holder is the HOLDER's body, which the plan states separately — never as two pictured people
            // touching, and never as the subject touching herself.
            if (!inFrameKeys.Contains(from) || !inFrameKeys.Contains(to))
            {
                continue;
            }

            contacts.Add(new PromptPlanContact(
                from,
                ParticipantNames.Of(names, from),
                Scalar(contact, "part"),
                to,
                ParticipantNames.Of(names, to),
                Scalar(contact, "targetPart"),
                Scalar(contact, "manner")));
        }

        return contacts;
    }

    /// <summary>
    /// The coverage set: every fact class the realised prompt must contain. A missing slot is a refused prompt, which
    /// replaces "did one of the note's words appear somewhere" (a caption saying "her hands on her thighs" passed that).
    /// </summary>
    private static IReadOnlyList<string> MustAppearOf(
        IReadOnlyList<PromptPlanSubject> subjects,
        IReadOnlyList<PromptPlanContact> contacts,
        SceneImagePovProjection.Projection projection,
        JsonObject? frozen)
    {
        var required = new List<string> { $"subject-count:{subjects.Count}" };
        required.AddRange(subjects.Select(subject => $"wardrobe:{subject.Name}"));

        if (contacts.Count > 0)
        {
            required.Add("contacts");
        }

        if (projection.LocationType is { Length: > 0 })
        {
            required.Add("setting-kind");
        }

        if (!string.IsNullOrWhiteSpace(Scalar(frozen, "lighting")))
        {
            required.Add("lighting");
        }

        if (!string.IsNullOrWhiteSpace(Scalar(frozen, "mood")))
        {
            required.Add("mood");
        }

        if (ReadStringArray(frozen?["objects"]).Count > 0)
        {
            required.Add("objects");
        }

        if (!string.IsNullOrWhiteSpace(projection.CameraHolderParts))
        {
            required.Add("holder-body");
        }

        if (!string.IsNullOrWhiteSpace(projection.Camera?.ShotSize))
        {
            required.Add("frame");
        }

        return required;
    }

    private static IReadOnlyList<string> EditedKeys(ScenePromptOverrides? overrides)
        => overrides is null
            ? []
            : overrides.Fields
                .Where(field => !field.Removed && !string.IsNullOrWhiteSpace(field.ElementKey))
                .Select(field => field.ElementKey.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>
    /// The per-subject in-frame extent, keyed by profile key.
    /// </summary>
    /// <remarks>
    /// The operator's row is <c>character:{key}.framing.extent</c> (P1b). The value is validated through
    /// <see cref="FrameInclusionExtent.Require"/>, so an extent the pipeline has no vocabulary for fails fast instead of
    /// being written into a prompt as words the model must interpret. Absent means the derived default — the whole
    /// person — which is the frame's own rule, not a substitute for a missing setting.
    /// </remarks>
    private static Dictionary<string, string> ExtentOverrides(ScenePromptOverrides? overrides, JsonObject frozen)
    {
        var extents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (overrides is null) return extents;

        const string suffix = ".framing.extent";
        foreach (var field in overrides.Fields)
        {
            if (field.Removed || string.IsNullOrWhiteSpace(field.ElementKey)) continue;
            var key = field.ElementKey.Trim();
            if (!key.StartsWith(StillBriefElements.CharacterPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

            var characterKey = key[StillBriefElements.CharacterPrefix.Length..^suffix.Length];
            var profileKey = ProfileKeyOf(frozen, characterKey) ?? characterKey;
            extents[profileKey] = FrameInclusionExtent.Require(field.Value, $"Prompt element '{key}'");
        }

        return extents;
    }

    /// <summary>The profile key a <c>character:</c> override key addresses, resolved the way the applier resolves it.</summary>
    private static string? ProfileKeyOf(JsonObject frozen, string characterKey)
    {
        if (frozen["characters"] is not JsonArray characters) return null;
        foreach (var character in characters.OfType<JsonObject>())
        {
            var profileKey = Scalar(character, "profileKey");
            if (profileKey.Length == 0) continue;
            if (string.Equals(profileKey, characterKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Scalar(character, "name"), characterKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Scalar(character, "characterId"), characterKey, StringComparison.OrdinalIgnoreCase))
            {
                return profileKey;
            }
        }

        return null;
    }

    /// <summary>
    /// Where this subject sits in the picture — derived from THIS POV's frame, never inherited from the compiled
    /// blocking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A compiled blocking stages the WHOLE cast for the external/omniscient frame (Becky LEFT, Dean RIGHT). Copying
    /// that placement into a POV that shows only one of them states "LEFT" for a subject who stands alone — and a
    /// participant POV is egocentric, so the person in frame is dead ahead of the lens, i.e. CENTER. Measured
    /// 2026-10-25: a Dean-POV render inherited "Left Foreground" for Becky, who is directly in front of the camera.
    /// </para>
    /// </remarks>
    private static string? PlacementOf(
        SceneBlocking? blocking,
        IReadOnlyList<PromptPlanSubject> subjects,
        string profileKey,
        bool participantPov)
    {
        // One person in the picture cannot be "to the left of" anyone: the frame has a single subject, centred.
        if (subjects.Count <= 1 || participantPov)
        {
            return subjects.Count == 0 ? null : $"{FrameHorizontal.Center} {FrameDepth.Foreground}";
        }

        var subject = blocking?.Subjects.FirstOrDefault(candidate =>
            string.Equals(candidate.SubjectKey, profileKey, StringComparison.OrdinalIgnoreCase));
        return subject is null ? null : $"{subject.Position.Horizontal} {subject.Position.Depth}";
    }

    private static string? AppearanceOf(JsonObject? frozen, string profileKey)
        => CharacterOf(frozen, profileKey) is { } character ? Scalar(character, "appearance") : null;

    private static JsonObject? CharacterOf(JsonObject? frozen, string profileKey)
    {
        if (frozen?["characters"] is not JsonArray characters)
        {
            return null;
        }

        return characters
            .OfType<JsonObject>()
            .FirstOrDefault(character => string.Equals(Scalar(character, "profileKey"), profileKey, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<JsonObject> ContactsOf(JsonObject? frozen)
        => frozen?["contacts"] is JsonArray contacts ? contacts.OfType<JsonObject>() : [];

    private static JsonObject FrozenState(JsonObject root)
    {
        if (root["frozenState"] is not JsonObject frozen)
        {
            throw new InvalidOperationException(
                "The brief carries no 'frozenState', so no prompt plan can be built from recorded facts.");
        }

        return frozen;
    }

    private static string? RecordedOrNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> ReadStringArray(JsonNode? node)
        => node is JsonArray array
            ? array
                .Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text?.Trim() : null)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .ToList()
            : [];

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

    /// <summary>Serialises a plan for a debug event without the caller having to know its shape.</summary>
    public static string ToJson(PromptPlan plan)
        => JsonSerializer.Serialize(Summarise(plan), new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
