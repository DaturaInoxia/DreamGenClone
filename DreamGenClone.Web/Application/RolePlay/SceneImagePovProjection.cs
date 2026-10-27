using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Projects a compiled Still brief into the frame a chosen POV actually sees.
///
/// <para>
/// The canonical brief is deliberately POV-independent: identity, wardrobe, pose and the frozen instant describe the
/// world, not a camera. POV is a <em>framing</em> dimension, and until this type existed the canonical prompt builders
/// handed the pre-processor the same brief plus a bare <c>PRODUCTION POV: Dean</c> line — so an Omniscient, a Dean and a
/// Becky prompt described one wide shot with a different name on it. This type is the single decision path that turns
/// the brief's own frozen state into the frame for one POV, using only recorded facts:
/// </para>
///
/// <list type="bullet">
/// <item>A participant POV gets a first-person camera at that character's recorded stance and eye position, aimed along
/// that character's recorded sightline. Only the characters that character's own <c>visibleCharacterNames</c> records are
/// in frame; everyone else is explicitly out of frame. The viewpoint is never depicted (debug 027/028: naming or
/// describing the camera holder made their anatomy compositionally salient and put them in their own shot). Recorded
/// facts that name the viewpoint are rephrased as "the unseen viewpoint" rather than dropped (debug 028).</item>
/// <item>An Omniscient POV gets the external camera plus the brief's frame-space blocking, so every person's exact
/// LEFT/CENTER/RIGHT placement and foreground/midground/background distance is stated instead of left to a wide
/// description (B-157).</item>
/// </list>
///
/// <para>
/// <b>Which brief this reads, and why it matters.</b> Frames are projected from the COMPILED brief (the world), never
/// from the prompt payload that has had elements removed. A bound location image deliberately removes
/// <c>frozenState.location/environment/lighting</c> from the payload, a wardrobe image removes a character's
/// <c>clothing</c> and a pose image removes <c>position</c> — those removals mean "the prompt need not DESCRIBE this,
/// an image supplies it", not "the scene has no place or stance". Projecting from the stripped payload failed prompt
/// generation outright once a location was bound (reported live 2026-10-20). <paramref name="suppression"/> carries
/// those removals so the projected TEXT stops restating an element an image owns, while the geometry it is derived from
/// stays truthful.
/// </para>
///
/// <para>
/// Nothing here is derived from a profile or defaulted: a missing frozen state, a POV that is not part of the cast, a
/// visible name that names no frozen character, or a blocking subject that maps to no character are all refusals that
/// name the offender.
/// </para>
/// </summary>
public static class SceneImagePovProjection
{
    /// <summary>The placeholder a participant POV uses for the camera holder in recorded facts.</summary>
    /// <remarks>
    /// It is "the camera", not "the unseen viewpoint": the phrase has to survive into a caption a MODEL reads, and
    /// "his fist closed in the unseen viewpoint's hair" describes nothing. A fact about the camera holder's own body is
    /// dropped instead of rephrased (see <see cref="OmitCameraHolderBodyClauses"/>), because it is not in frame — and
    /// the compiler standards require the caption to hold only what is renderable at that instant (§2.3 rule 3).
    /// </remarks>
    public const string UnseenViewpoint = "the camera";

    /// <summary>
    /// The system-prompt rule that makes a PARTICIPANT POV a first-person photograph rather than a wide description.
    /// </summary>
    /// <remarks>
    /// Measured live 2026-10-20: the Dean and Becky prompts were indistinguishable because the brief handed the compiler
    /// BOTH characters (including the camera holder) and the family instruction asks for a full account of the picture,
    /// so the model wrote an external two-person scene and treated the POV projection as one more paragraph of context.
    /// The user prompt cannot outrank the element order on its own; this states the frame as a rule.
    /// </remarks>
    public const string ParticipantPovRule = """

        THIS IS A FIRST-PERSON PHOTOGRAPH, and the caption is a SHOT DESCRIPTION: the camera first (shot size, angle,
        and whether it is taken from below, above or at eye level), then the in-frame subject and what that subject is
        doing relative to the camera. Write it in the THIRD PERSON: never "I", "me", "my", "we" or "our". The person
        holding the camera is never depicted or described, and no noun is invented for them unless the frame's
        CAMERA HOLDER'S VISIBLE BODY section names their parts. Depict ONLY the people listed as in frame. A recorded fact
        about the camera holder's own body is not visible from this camera: leave it out rather than rewording it. State
        no camera pitch, height or
        distance this request does not record — do not write "eye level" or any other invented vantage. Describe the
        appearance of every person whose frame entry says their appearance comes from the text, and describe none of the
        people whose appearance a bound reference supplies. Where this block and the general observer procedure
        disagree, THIS block wins.
        """;

    /// <summary>
    /// The system-prompt rule that makes a reference-supplied room authoritative over the compiler's own element order.
    /// </summary>
    /// <remarks>
    /// The family instructions require an environment/lighting phrase in every caption, so a prompt that merely omits
    /// the room still gets one written from whatever words are left — measured live 2026-10-20, where a bound location
    /// produced "corrugated metal walls, wooden floorboards showing cracks, and a pitched roof" and almost nothing about
    /// the people. Stating the ownership in the SYSTEM prompt as well as the user prompt is what takes the room out of
    /// the required element order.
    /// </remarks>
    public const string ReferenceSuppliedRoomRule = """

        THE ROOM IS A REFERENCE IMAGE IN THIS REQUEST: the setting's structure — its walls, floor, surfaces, fixtures and
        the objects standing in it — arrives as a bound image, so write no sentence describing them. LIGHTING, however,
        is NOT supplied by that image and stays the request's to state: when the facts above record a lighting or
        atmosphere value, write it (bars of slat light across the boards, a shaded interior, dawn light) because the same
        room under a different light is a different photograph. Spend the rest of the caption on the people: how many,
        each person's gender and appearance, EXACTLY where each one is in the frame (LEFT / CENTER / RIGHT and
        foreground/midground/background), what each one is doing, and what each one is wearing.
        """;

    /// <summary>
    /// The system-prompt rule that makes the camera holder's visible body a part of the caption, and states the spatial
    /// relation in a form the writer cannot invert.
    /// </summary>
    /// <remarks>
    /// Appended AFTER <see cref="ParticipantPovRule"/>, so its specific allowance wins over "never depicted".
    ///
    /// <para>
    /// The parts used to travel in a block appended to the FINISHED prompt instead, so the caption writer could not
    /// re-word "behind" into "in front". That protected the wording and lost the body: the operator's own sentence
    /// ("a man who is clearly a separate person entering the frame from behind … his hands grip her hips …") was
    /// rendered as a trailing addendum the image model ignored, and the caption described the woman alone (SceneImage
    /// 3bbad8c9 follow-up, 2026-10-23). The operator's requirement is that this content is part of the MAIN prompt.
    /// </para>
    /// </summary>
    public const string CameraHolderPartsRule = """

        CAMERA HOLDER'S VISIBLE BODY — PART OF THE CAPTION, NOT AN ADDENDUM: this is a FIRST-PERSON POV frame and it SHOWS
        specific body parts of the camera holder (their hands, arms, torso, genitals, legs or feet), given to you as
        CAMERA HOLDER'S VISIBLE BODY. The operator writes that note in their own words and it may be as short as "hands
        on her hips": it is a NOTE TO EXPAND, not a finished clause — state it INSIDE the caption, in the same
        continuous paragraph as the in-frame subject, at the point where their body sits in the frame, saying where those
        parts enter the frame and how much of them is visible. Never a separate sentence at the end, never a numbered
        list, and never a note about the camera. They are the CAMERA's OWN BODY: his hands come into the picture from
        BELOW THE BOTTOM EDGE of the frame, in the near foreground and foreshortened toward the subject, and the rest of
        him — face, head, hair, shoulders, chest — is behind the camera and not in the picture. Name the parts with their
        owner ("his hands", "his hips"), never as growths on the subject, never merging the two bodies, never describing
        the subject's own hands where his are, and never inventing a part the note does not name. The camera holder
        stands behind the subject and nearer the camera than she is; the subject is NEVER behind, beside or between the
        camera and the holder, and is never described as behind the camera, which is impossible in a photograph taken
        from the camera. Never write that anyone is "alone".
        """;

    /// <summary>
    /// The participant system rule for one projection: the base first-person rule, plus the body-parts allowance when
    /// the frame shows them. One call site in each builder instead of two copies of the conditional.
    /// </summary>
    public static string PovSystemRule(Projection? projection)
    {
        if (projection is null || projection.IsOmniscient) return string.Empty;
        return string.IsNullOrWhiteSpace(projection.CameraHolderParts)
            ? ParticipantPovRule
            : ParticipantPovRule + CameraHolderPartsRule;
    }

    private const string ObserverInvolvement = "observer";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Which way a recorded cast member faces relative to the camera, derived from their own facts.</summary>
    public enum SubjectFacing
    {
        /// <summary>The person's facts name the camera holder ("up at Dean", "toward the camera") — their face is toward the lens.</summary>
        TowardCamera = 1,

        /// <summary>The person's facts place their face on a surface or away from the camera ("down at the boards", "her back to…") — show their back.</summary>
        AwayFromCamera = 2
    }

    /// <summary>One recorded cast member as the POV sees them. A null field is an element the prompt suppresses.</summary>
    public sealed record ProjectedCharacter(
        string Name,
        string ProfileKey,
        string? PhysicalLocation,
        string? Position,
        string? ActionOrObservation,
        string? Sightline,
        string? Clothing,
        /// <summary>
        /// Whether the caption must STATE this person's appearance from the DEPICTED CHARACTER APPEARANCE block.
        /// True unless a bound face AND a bound body reference supply it (B-166 D10).
        /// </summary>
        /// <remarks>
        /// Measured live 2026-10-20 (production group 449e9148, Becky POV): Dean had no face or body reference, his
        /// authored appearance WAS handed to the compiler, and the caption still described only "rugged casual
        /// clothing" — no hair, eyes, skin or build. The facts were present but nothing in the frame said they were
        /// required, so the per-person line now says which of the two it is.
        /// </remarks>
        bool AppearanceFromText = true,
        /// <summary>
        /// Which way this person faces relative to the camera, derived deterministically from their recorded facts.
        /// Null means the brief did not record it, and the prompt states NO facing rather than guessing one.
        /// </summary>
        SubjectFacing? Facing = null,
        /// <summary>
        /// B-167 v4: the recorded world body position ("bent forward at the workbench") and facing target. Null on a
        /// v3 record, and the prompt then states no world fact rather than reading one out of prose.
        /// </summary>
        string? WorldBody = null,
        /// <summary>B-167 v4: the recorded world facing target, verbatim (a cast key, a place feature or a direction).</summary>
        string? WorldFacing = null,
        /// <summary>
        /// How much of this body the composed frame shows. A listed in-frame person is <c>InFrame</c>; the value exists so
        /// a partial body is a DATA change rather than a new code path, and so the camera holder's state is decided in
        /// the same place as everyone else's (B-167).
        /// </summary>
        SceneSubjectVisibility Visibility = SceneSubjectVisibility.InFrame);

    /// <summary>The resolved frame for one POV.</summary>
    public sealed record Projection(
        string Pov,
        bool IsOmniscient,
        string SceneLocation,
        bool SuppressSceneLocation,
        bool SceneSuppliedByReference,
        string? UnseenViewpointLocation,
        string? UnseenViewpointStance,
        string? UnseenViewpointSightline,
        IReadOnlyList<ProjectedCharacter> InFrame,
        IReadOnlyList<string> OutOfFrame,
        IReadOnlyList<string> DirectlyAhead,
        SceneBlocking? Frame,
        /// <summary>
        /// The recorded setting as its KIND of place ("the shed", "the beach"), derived from the brief's own location
        /// text. B-166 D5: when a location image owns the room the caption still has to say what KIND of place it is,
        /// or the scene reads as if it floats in nowhere. Null when the location text names no kind.
        /// </summary>
        string? LocationType = null,
        /// <summary>The camera holder's recorded pitch (D11), or null when the brief does not record one.</summary>
        string? ViewpointPitch = null,
        /// <summary>How far the in-frame subject is from the camera holder (D11), or null when not recorded.</summary>
        string? ViewpointSubjectDistance = null,
        /// <summary>
        /// The camera holder's profile key (B-166 POV fix). The viewpoint is the CAMERA, not a subject, so the semantic
        /// snapshot handed to the compiler must not contain them — otherwise the model reads a full description of the
        /// person holding the camera and depicts them anyway, which is why Dean and Becky prompts read the same.
        /// </summary>
        string? UnseenViewpointProfileKey = null,
        /// <summary>
        /// Whether the camera holder is a REMOTE OBSERVER outside the active scene (the recorded involvement says
        /// "observer" and they stand somewhere else). False for an ordinary participant whose location phrase simply
        /// reads differently from the scene's — comparing the two strings concluded "outside the scene" for someone
        /// standing in the middle of it, and told the model to move the camera away.
        /// </summary>
        bool UnseenViewpointOutsideScene = false,
        /// <summary>
        /// The camera this POV is framed with: shot size and angle, AFTER the operator's framing override. A participant
        /// POV reads the camera and nothing else from the blocking — the external frame's placements describe somebody
        /// else's shot — and the shot size is what makes the Framing control reach the prompt at all.
        /// </summary>
        SceneBlockingCamera? Camera = null,
        /// <summary>
        /// The vertical aim of the camera, from the brief's recorded pitch or, when that is absent, from the recorded
        /// sightline ("directed up" / "directed down"). Null when the brief records neither — never assumed.
        /// </summary>
        string? EffectivePitch = null,
        /// <summary>
        /// The camera the OPERATOR picked in the framing control, when they picked one. It outranks the POV's own
        /// geometry, so it has to be distinguishable from the compiled camera the brief already carries.
        /// </summary>
        SceneBlockingCameraOverride? OperatorCamera = null,
        /// <summary>
        /// The camera holder's VISIBLE body parts for this participant POV, when the render asked to show them (hands,
        /// arms, genitals, feet). Null means the holder is never depicted — the default. When non-null the SHOT block
        /// states exactly these parts and nothing else of the holder.
        /// </summary>
        string? CameraHolderParts = null)
    {
        /// <summary>
        /// How much of the CAMERA HOLDER the frame shows (B-167). The single typed answer to "is the holder in the
        /// picture": <c>PartiallyVisible</c> exactly when recorded contacts put parts of their body in frame, otherwise
        /// <c>OutOfFrame</c>. An omniscient frame depicts a camera holder too, so the state is always meaningful.
        /// </summary>
        public SceneSubjectVisibility CameraHolderVisibility
            => string.IsNullOrWhiteSpace(CameraHolderParts)
                ? SceneSubjectVisibility.OutOfFrame
                : SceneSubjectVisibility.PartiallyVisible;

        /// <summary>The authoritative POV section handed to the pre-processor model.</summary>
        public string ToPromptText()
            => IsOmniscient ? BuildOmniscientText(this) : BuildParticipantText(this);
    }

    /// <summary>
    /// Projects <paramref name="pov"/> into the frame of the brief's frozen state. <paramref name="brief"/> must be the
    /// COMPILED brief, and <paramref name="blocking"/> its frame-space blocking AFTER the operator's own staging override
    /// has been applied — the caller owns that one resolution, and a participant POV ignores it because an external
    /// frame cannot describe an egocentric camera. <paramref name="suppression"/> is what the prompt deliberately no
    /// longer describes (user overrides plus reference-binding removals); it filters who is depicted and which facts the
    /// projection restates, and it never changes the geometry the frame is derived from.
    /// </summary>
    public static Projection Resolve(
        CompiledMediaBrief brief,
        string pov,
        SceneBlocking? blocking,
        ScenePromptOverrides? suppression = null,
        bool sceneSuppliedByReference = false,
        SceneBlockingCameraOverride? operatorCamera = null,
        CameraHolderPartsMode cameraHolderPartsMode = CameraHolderPartsMode.None,
        string? cameraHolderPartsText = null)
    {
        ArgumentNullException.ThrowIfNull(brief);
        if (string.IsNullOrWhiteSpace(pov))
            throw new InvalidOperationException("A POV is required to project the scene image frame.");

        var frozen = ReadFrozenState(brief, suppression);
        var characters = ReadCast(frozen);
        var removed = RemovedKeys(suppression);
        var cast = characters.Where(character => !IsRemoved(character, removed)).ToList();
        if (cast.Count == 0)
            throw new InvalidOperationException(
                $"Every character in brief '{brief.Id}' has been removed by a prompt override, so no POV frame can be projected.");
        // World facts (facing targets, contacts) are recorded with profile keys. The prompt may only contain words, so
        // every fact that reaches it is translated through this map and never through the key itself.
        var namesByKey = characters
            .Where(character => !string.IsNullOrWhiteSpace(character.ProfileKey) && !string.IsNullOrWhiteSpace(character.Name))
            .GroupBy(character => character.ProfileKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
        var removedProfileKeys = characters
            .Where(character => IsRemoved(character, removed))
            .Select(character => character.ProfileKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var location = Require(frozen.Location, "frozen state 'location'");
        var suppressLocation = sceneSuppliedByReference || IsSceneElementSuppressed(suppression, "location");
        var isOmniscient = string.Equals(pov.Trim(), SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase);

        if (isOmniscient)
        {
            var omniscientInFrame = cast
                .Where(character => !IsRemoteObserver(character, location))
                .Select(character => ToProjected(character, anonymizedName: null, suppression, namesByKey: namesByKey))
                .ToList();
            if (omniscientInFrame.Count == 0)
                throw new InvalidOperationException("The frozen state records no character inside the scene location, so the Omniscient frame has nobody to show.");
            var omniscientOutOfFrame = cast
                .Where(character => IsRemoteObserver(character, location))
                .Select(character => character.Name)
                .ToList();
            // Only the people this frame SHOWS keep a seat: a remote observer is named as out of frame, so leaving
            // them seated would describe the one visible person standing at the LEFT edge of their own frame.
            var omniscientFrame = DropRemovedSubjects(blocking, removedProfileKeys) is { } frame
                ? SceneBlockingResolver.ReseatForCast(
                    frame,
                    omniscientInFrame
                        .Select(character => character.ProfileKey)
                        .Where(key => !string.IsNullOrWhiteSpace(key))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)) is { Subjects.Count: > 0 } seated
                        ? seated
                        : null
                : null;
            return new Projection(
                pov.Trim(), true, location, suppressLocation, sceneSuppliedByReference, null, null, null,
                omniscientInFrame, omniscientOutOfFrame, [], omniscientFrame,
                LocationTypeOf(location));
        }

        var viewpoint = characters.FirstOrDefault(character => string.Equals(character.Name, pov.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"POV character '{pov.Trim()}' is not associated with the frozen state of brief '{brief.Id}'.");
        if (IsRemoved(viewpoint, removed))
            throw new InvalidOperationException(
                $"POV character '{viewpoint.Name}' was removed from this prompt by an override, so its camera has no holder.");

        var stance = IsElementSuppressed(suppression, viewpoint, "position")
            ? null
            : Require(viewpoint.Position, $"frozen character '{viewpoint.Name}' field 'position'");
        var sightline = IsElementSuppressed(suppression, viewpoint, "sightline")
            ? null
            : Require(viewpoint.Sightline, $"frozen character '{viewpoint.Name}' field 'sightline'");
        var viewpointLocation = IsElementSuppressed(suppression, viewpoint, "physicalLocation")
            ? null
            : Require(viewpoint.PhysicalLocation, $"frozen character '{viewpoint.Name}' field 'physicalLocation'");

        var byName = characters
            .GroupBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var visibleNames = viewpoint.VisibleCharacterNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Where(name => !byName.TryGetValue(name, out var listed) || !IsRemoved(listed, removed))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var inFrame = new List<ProjectedCharacter>(visibleNames.Count);
        foreach (var name in visibleNames)
        {
            if (!byName.TryGetValue(name, out var visible))
                throw new InvalidOperationException(
                    $"Frozen character '{viewpoint.Name}' lists '{name}' as visible, but the frozen state has no such character.");
            inFrame.Add(ToProjected(visible, viewpoint.Name, suppression, viewpoint.ProfileKey, namesByKey));
        }

        var inFrameNames = inFrame.Select(character => character.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outOfFrame = cast
            .Where(character => !inFrameNames.Contains(character.Name)
                && !string.Equals(character.Name, viewpoint.Name, StringComparison.OrdinalIgnoreCase))
            .Select(character => character.Name)
            .ToList();

        // A character the viewpoint's own facts name lies directly in front of that camera. B-167: when the v4
        // `facing` fact is recorded it is an exact key comparison; a v3 record has no `facing`, and its recorded
        // sightline sentence is the fact the classification reads instead. Neither path invents a direction.
        var facingTarget = viewpoint.Facing?.Toward?.Trim();
        var directlyAhead = !string.IsNullOrWhiteSpace(facingTarget)
            ? inFrame
                .Where(character => NamesTarget(facingTarget, character.Name, character.ProfileKey))
                .Select(character => character.Name)
                .ToList()
            : sightline is null
                ? []
                : inFrame
                    .Where(character => sightline.Contains(character.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(character => character.Name)
                    .ToList();

        // The camera holder's visible body parts, when the render asked to show them. Custom fails fast on an empty
        // text; Derived extracts from the moment data and yields null when it records none — neither invents a limb.
        // It reads the SAME operator-edited snapshot the frame was built from, so a contact the operator replaced is
        // never reported back from the enrichment's original.
        var cameraHolderParts = AppendHolderWardrobe(
            ResolveCameraHolderParts(
                ProjectionSnapshot(brief, suppression), pov, cameraHolderPartsMode, cameraHolderPartsText),
            viewpoint,
            suppression);

        return new Projection(
            viewpoint.Name,
            false,
            location,
            suppressLocation,
            sceneSuppliedByReference,
            viewpointLocation,
            stance is null ? null : Anonymize(stance, viewpoint.Name),
            sightline is null ? null : Anonymize(sightline, viewpoint.Name),
            inFrame,
            outOfFrame,
            directlyAhead,
            null,
            LocationTypeOf(location),
            RecordedOrNull(viewpoint.ViewpointPitch),
            RecordedOrNull(viewpoint.ViewpointSubjectDistance),
            viewpoint.ProfileKey,
            IsRemoteObserver(viewpoint, location),
            blocking?.Camera,
            // The VERTICAL aim is the sightline's own fact, so it is read even when a `facing` target is recorded: a
            // facing names a HORIZONTAL target ("toward Becky's back") and says nothing about pitch. Gating the sightline
            // on the absence of a facing left a looking-down POV with "Camera aim: straight ahead" and an eye-level
            // camera — the one geometry in which the camera holder's own hands have nowhere to enter the frame from
            // (SceneImages 5b6a1cdf / b5ea6fb4, 2026-10-27).
            RecordedOrNull(viewpoint.ViewpointPitch) ?? PitchFromSightline(sightline),
            operatorCamera,
            cameraHolderParts);
    }

    /// <summary>
    /// Resolves the camera holder's visible body parts for one POV, independently of the full projection — the render
    /// path calls this to append the same parts block the prompt was projected with, without re-projecting the frame.
    /// </summary>
    /// <remarks>
    /// B-167: the parts come from the moment's recorded <c>contacts</c> — a fact, not a sentence to scrape. The holder's
    /// own body is what can appear in a first-person frame, so a contact the holder begins contributes its
    /// <c>part</c>, and a contact someone else begins contributes the <c>targetPart</c> it puts on the holder.
    /// A record with no <c>contacts</c> (a v3 moment) or an empty list yields null: "not recorded", so nothing is
    /// invented. The retired <see cref="CameraHolderPartsMode.Custom"/> text is refused by name rather than used,
    /// because a persisted draft carrying it would otherwise silently outrank the recorded facts.
    /// <para>
    /// It takes the SNAPSHOT rather than the brief on purpose: the caller passes the operator-edited snapshot the frame
    /// was projected from (<c>ScenePromptOverridesApplier.ApplyOperatorProjectionEdits</c>). Reading the brief here
    /// would let one reader see the operator's contacts and another read the enrichment's original — which is exactly
    /// what happened live on 2026-10-25, where a contact the operator had deleted still reached the image.
    /// </para>
    /// </remarks>
    public static string? ResolveCameraHolderParts(
        string semanticInputSnapshotJson,
        string pov,
        CameraHolderPartsMode mode,
        string? customText)
    {
        if (mode == CameraHolderPartsMode.None)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(customText))
        {
            throw new InvalidOperationException(
                "The custom camera-holder parts text was retired in favour of the moment's recorded contacts. "
                + "Clear the text and choose Derived; edit 'frozenState.contacts' to change which parts are in frame.");
        }

        if (mode == CameraHolderPartsMode.Custom)
        {
            throw new InvalidOperationException(
                "Custom camera-holder body parts were retired: the parts now come from the moment's recorded contacts. "
                + "Choose Derived, or edit 'frozenState.contacts' to change which parts are in frame.");
        }

        if (string.IsNullOrWhiteSpace(semanticInputSnapshotJson))
        {
            throw new InvalidOperationException(
                "No semantic snapshot was supplied, so the camera holder's visible body cannot be resolved from the "
                + "moment's recorded contacts.");
        }

        string? profileKey = null;
        try
        {
            if (JsonNode.Parse(semanticInputSnapshotJson) is JsonObject root
                && root["frozenState"] is JsonObject frozen
                && frozen["characters"] is JsonArray characters)
            {
                foreach (var node in characters)
                {
                    if (node is not JsonObject character) continue;
                    var name = StringMember(character, "name");
                    var id = StringMember(character, "characterId");
                    if (string.Equals(name, pov.Trim(), StringComparison.OrdinalIgnoreCase)
                        || string.Equals(id, pov.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        profileKey = StringMember(character, "profileKey");
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(profileKey)
            ? null
            : CameraHolderPartsFromContacts.Extract(semanticInputSnapshotJson, profileKey);
    }

    /// <summary>
    /// Adds the holder's recorded WARDROBE to the note about their visible body, when that body is in the frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A part in the frame brings what covers it with it. "the camera holder's thighs on Becky's ass (pressed tightly)"
    /// says where his thighs are but not that the jeans are undone over them, and the projection cannot say it any other
    /// way: a participant POV drops the camera holder's own character rows (they are the camera, so they are not in the
    /// cast), so his <c>clothing</c> fact is otherwise unreachable — an operator editing it would see nothing change
    /// (requested live 2026-10-25: "keeping what he is wearing (undone jeans)").
    /// </para>
    /// <para>
    /// The fact is read from his OWN operator-editable row, is emitted only when the frame shows a part of him, and is
    /// omitted entirely when it is blank or the operator removed it — so nothing is invented and the wardrobe keeps
    /// exactly one owner.
    /// </para>
    /// </remarks>
    private static string? AppendHolderWardrobe(
        string? parts,
        SceneMomentFrozenCharacter viewpoint,
        ScenePromptOverrides? suppression)
    {
        if (string.IsNullOrWhiteSpace(parts)) return parts;
        if (IsElementSuppressed(suppression, viewpoint, "clothing")) return parts;

        var clothing = viewpoint.Clothing?.Trim();
        return string.IsNullOrWhiteSpace(clothing)
            ? parts
            : $"{parts}; the camera holder's wardrobe: {clothing}";
    }

    private static string BuildParticipantText(Projection projection)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SHOT (authoritative frame for this POV — open the caption with it):");

        // The camera line is the single most load-bearing sentence in a photographic prompt (compiler standards §2.1:
        // "Perspective/Viewpoint — camera distance/angle; distance is load-bearing"), so it is stated first and in the
        // vocabulary the model family understands.
        var framing = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(projection.Camera?.ShotSize)) framing.Add(projection.Camera!.ShotSize.Trim());
        if (CameraAngleText(projection) is { Length: > 0 } angle) framing.Add(angle);
        framing.Add("first-person point of view");
        sb.AppendLine($"- Camera: {string.Join(", ", framing)}.");

        // The height is stated RELATIVE TO THE SUBJECT and never as "the eye height of a person <stance phrase>": that
        // phrasing made the model describe a second person standing in the frame — "Dean stands clothed above a kneeling
        // person's eye level" (record 5365dfe5, 2026-10-22). A camera has a height; nobody in the picture is kneeling.
        // The operator's chosen height outranks the stance-derived one — it is the one control that says "put the lens
        // HERE", and the stances are only able to say where the two bodies put it. Both resolve to the same five
        // concepts, so the line below never mixes vocabularies.
        if (CameraHeights.IsDefined(projection.OperatorCamera?.Height))
        {
            sb.AppendLine(
                $"- Camera height: {CameraHeights.Wording(CameraHeights.Require(projection.OperatorCamera!.Height, "The framing override"))}.");
        }
        else if (CameraHeightText(projection.UnseenViewpointStance, projection.InFrame) is { Length: > 0 } height)
        {
            sb.AppendLine($"- Camera height: {height}.");
        }
        // The aim is stated as a DIRECTION, not as the recorded sightline sentence: that sentence names the camera
        // holder ("directed down toward Becky"), and a name there is what puts her back in the picture.
        sb.AppendLine($"- Camera aim: {AimText(projection.EffectivePitch)}.");
        if (projection.ViewpointSubjectDistance is { } distance)
            sb.AppendLine($"- Subject distance: {distance} from the camera; scale the in-frame people by it.");
        // Only the ROOM part of the recorded position. The tail is the holder's own stance ("Kneeling before Dean"),
        // which names a second body exactly the way the old height line did.
        if (RoomOnly(projection.UnseenViewpointLocation) is { Length: > 0 } viewpointRoom)
            sb.AppendLine($"- Camera is at: {viewpointRoom.TrimEnd('.', ' ')}.");
        if (!projection.SuppressSceneLocation)
            sb.AppendLine($"- Scene location: {projection.SceneLocation}");
        if (!string.IsNullOrWhiteSpace(projection.UnseenViewpointLocation) && projection.UnseenViewpointOutsideScene)
            sb.AppendLine($"- The camera holder is a REMOTE OBSERVER outside the active scene: keep the camera at {projection.UnseenViewpointLocation} and do not move it into {projection.SceneLocation}.");

        AppendRoomOwnership(sb, projection);

        // What the frame CONTAINS, stated before anyone is described. A shot size says how much of the picture the
        // subject occupies; it never decides which parts of them exist in it. Without this line a caption writer
        // invented "her head is not visible … the image cuts off at about waist height" for a close overhead shot of a
        // body that was entirely frameable (SceneImage 8bb09d13, 2026-10-25).
        //
        // B-167 2026-10-27: when the holder's parts are in frame, "every person in frame is from crown to feet" is a
        // demand for a COMPLETE second body that the request never describes — the caption names their hands and hips, and
        // the render built the hands out of the subject's hips (SceneImage 516cc462). The holder is the camera and is
        // cropped by the frame by definition, so the inclusion sentence is scoped to the SUBJECT for that case.
        sb.AppendLine(projection.CameraHolderVisibility == SceneSubjectVisibility.PartiallyVisible
            ? $"- Inclusion: {FrameInclusion.ParticipantSentence(CameraHolderBodyStatement.PartsPhrase(projection.CameraHolderParts))}"
            : $"- Inclusion: {FrameInclusion.Sentence} A shot size never removes a body part — frame the whole person "
                + "and let them fill the frame.");
        sb.AppendLine("- In frame (this is the whole cast — describe these and nobody else):");
        foreach (var character in projection.InFrame)
        {
            sb.AppendLine($"  - {Describe(character)}");
            if (character.Facing is { } facing)
            {
                sb.AppendLine(facing == SubjectFacing.AwayFromCamera
                    ? "    FACING — this person faces AWAY from the camera: show their BACK, never their face."
                    : "    FACING — this person faces the camera: show their face.");
            }
        }
        if (projection.CameraHolderVisibility == SceneSubjectVisibility.PartiallyVisible)
        {
            // The operator's own note about the holder's body, stated BEFORE the caption is written. It is a NOTE, not
            // a finished clause: the operator types what they want to see ("hands on her hips"), and turning that into
            // framing language is this compiler's job. Asking them for entry edges and visible extents would be asking
            // them to write an image prompt, which is the one thing the field exists to avoid.
            sb.AppendLine("- CAMERA HOLDER'S VISIBLE BODY (part of the caption, not an addendum — state it IN the paragraph, as a separate person behind the in-frame subject). This is the operator's own short note, NOT a finished clause:");
            sb.AppendLine($"  \"{projection.CameraHolderParts}\"");
            sb.AppendLine("  Expand it into one or two clauses of the caption: where those parts enter the frame and how much of them is visible (a hand at the frame edge, forearms only, his hips level with hers), and what they touch. Keep every body part it names, add no body part it does not name, and add no face, head or hair. Never re-word the relation as the subject being behind the camera, never attach these parts to the subject, and never write that anyone is alone.");
        }
        sb.AppendLine(projection.OutOfFrame.Count == 0
            ? "- Nobody else is in the picture."
            : $"- Not in the picture: {string.Join(", ", projection.OutOfFrame)}. Do not depict or name them.");

        // Phrased AFFIRMATIVELY and without the nouns a caption model echoes back. The old wording ("The person holding
        // the camera is never depicted or described", "Out of frame: …") came back as picture content: "likely looking up
        // at the person holding the camera (not visible)" and "The background remains out of frame" (record eabff5a2,
        // 2026-10-22). A caption describes what IS there; a negative list becomes a list of things to mention.
        sb.AppendLine("- Write the caption as a photograph in the THIRD PERSON. Never write \"I\", \"me\", \"my\", \"we\" or \"our\".");
        sb.AppendLine("- Describe only the people listed above, exactly as their recorded facts state them. Anything not listed is not in this photograph.");
        sb.AppendLine("- State the camera first, then each person: where they sit in the frame, what they are doing, and how they are oriented towards the lens.");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The angle half of the camera line. The operator's own pick wins; a PARTICIPANT POV's angle then comes from the
    /// holder's recorded pitch, because the angle is a fact about that body rather than a style inherited from the
    /// scene's authored frame; only then does the compiled camera angle apply.
    ///
    /// <para>Public because the render-facing framing clause states the same camera, and two statements of one frame
    /// must not disagree ("close shot at eye level" beneath "low angle, viewed from below").</para>
    /// </summary>
    /// <remarks>
    /// Record 4da992ba (Dean POV, 2026-10-22): the shot block said "Camera aim: downward — from above the subject's
    /// eye line" while its own first line said "low angle", and the appended framing repeated "low angle". The
    /// compiled camera angle had been authored for the scene's own frame and was overriding the POV's geometry, so
    /// the two POVs of one moment were not opposites as they must be.
    /// </remarks>
    public static string? CameraAngleText(Projection projection)
    {
        // What the operator picked is an override and outranks everything.
        if (projection.OperatorCamera?.Angle is { Length: > 0 } chosen && !string.IsNullOrWhiteSpace(chosen))
        {
            return chosen.Trim();
        }

        // A chosen HEIGHT carries the aim that height implies, so "floor level" cannot sit above "eye level".
        if (CameraHeights.IsDefined(projection.OperatorCamera?.Height))
        {
            var pitch = CameraHeights.PitchOf(
                CameraHeights.Require(projection.OperatorCamera!.Height, "The framing override"));
            return pitch switch
            {
                "up" => "low angle, viewed from below",
                "down" => "high angle, viewed from above",
                "level" => "eye level",
                _ => null
            };
        }

        if (!projection.IsOmniscient && projection.EffectivePitch?.Trim().ToLowerInvariant() is { Length: > 0 } participantPitch)
        {
            return participantPitch switch
            {
                "up" => "low angle, viewed from below",
                "down" => "high angle, viewed from above",
                "level" => "eye level",
                _ => null
            };
        }

        if (projection.Camera?.Angle is { Length: > 0 } recorded
            && !string.Equals(recorded.Trim(), "eye level", StringComparison.OrdinalIgnoreCase))
        {
            return recorded.Trim();
        }

        return projection.EffectivePitch?.Trim().ToLowerInvariant() switch
        {
            "up" => "low angle, viewed from below",
            "down" => "high angle, viewed from above",
            "level" => "eye level",
            _ => string.IsNullOrWhiteSpace(projection.Camera?.Angle) ? null : projection.Camera!.Angle.Trim()
        };
    }

    /// <summary>Reads the vertical aim out of the recorded sightline ("directed up", "toward … up", "down toward …"), or null.</summary>
    private static string? PitchFromSightline(string? sightline)
    {
        if (string.IsNullOrWhiteSpace(sightline)) return null;
        var text = sightline.Trim();

        bool up = text.Contains(" upward", StringComparison.OrdinalIgnoreCase)
            || text.Contains(" up ", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith(" up", StringComparison.OrdinalIgnoreCase)
            || text.Contains("upward", StringComparison.OrdinalIgnoreCase)
            || StartsWithWord(text, "up");
        bool down = text.Contains(" downward", StringComparison.OrdinalIgnoreCase)
            || text.Contains(" down ", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith(" down", StringComparison.OrdinalIgnoreCase)
            || text.Contains("downward", StringComparison.OrdinalIgnoreCase)
            // A sightline is often recorded as the bare direction it starts with — "down toward Becky's back" — which
            // the spaced forms above missed, so a looking-down POV came out as "Camera aim: straight ahead".
            || StartsWithWord(text, "down");

        if (up == down) return null;
        return up ? "up" : "down";
    }

    /// <summary>Whether the text begins with this whole word (so "upper" is not "up").</summary>
    private static bool StartsWithWord(string text, string word)
        => text.StartsWith(word, StringComparison.OrdinalIgnoreCase)
           && (text.Length == word.Length || !char.IsLetter(text[word.Length]));

    /// <summary>The camera aim as a direction a photograph can be taken in.</summary>
    private static string AimText(string? effectivePitch) => effectivePitch?.ToLowerInvariant() switch
    {
        "up" => "upward — the shot is taken from below the subject's eye line",
        "down" => "downward — the shot is taken from above the subject's eye line",
        _ => "straight ahead"
    };

    private static string BuildOmniscientText(Projection projection)
    {
        var sb = new StringBuilder();
        sb.AppendLine("POV PROJECTION (authoritative frame for this POV — apply it literally):");
        sb.AppendLine("- Camera: external third-person camera, not through any character's eyes. Every person below is in the frame.");
        // Omniscient is a viewpoint of its OWN (operator 2026-10-25: "Omniscient POV needs its own camera angle; it is
        // not Dean's view or Becky's view"). The failure it prevents: the record says Dean stands directly behind her,
        // and the caption wrote "her back to the camera" — a position relative to ANOTHER PERSON converted into an
        // orientation toward the lens that nothing recorded.
        sb.AppendLine("- This camera is nobody's eyes, so a person's position relative to ANOTHER PERSON (\"standing directly behind her\") never becomes an orientation toward the lens. State a person's facing only when the record states it, and then as the world target it names; if the record states no facing, describe where they are in the frame and what they are doing, and say nothing about their back or their face.");
        if (!projection.SuppressSceneLocation)
            sb.AppendLine($"- Scene location: {projection.SceneLocation}");
        if (projection.Frame is null)
        {
            sb.AppendLine("- Exact frame placement: not recorded on this brief. Describe each person's recorded position and do not state a left/right placement that was not recorded.");
        }
        else
        {
            var namesBySubjectKey = projection.InFrame
                .ToDictionary(character => character.ProfileKey, character => character.Name, StringComparer.OrdinalIgnoreCase);
            sb.AppendLine($"- Framing: {projection.Frame.Camera.Angle}, {projection.Frame.Camera.ShotSize}{(string.IsNullOrWhiteSpace(projection.Frame.Camera.Side) ? string.Empty : $", {projection.Frame.Camera.Side}")}.");
            sb.AppendLine("- Exact frame placement (state LEFT / CENTER / RIGHT and the depth for every person in the caption):");
            // A picture with ONE person in it cannot place them "to the left of" anybody: they are the frame's subject,
            // centred. Stating the compiled block's two-shot slot for a one-person frame is how a lone figure ended up
            // pushed to the LEFT edge (measured 2026-10-25).
            var singleSubject = projection.InFrame.Count == 1;
            foreach (var subject in projection.Frame.Subjects)
            {
                if (!namesBySubjectKey.TryGetValue(subject.SubjectKey, out var name))
                    throw new InvalidOperationException(
                        $"Frame blocking subject '{subject.SubjectKey}' maps to no character in the brief's frozen state.");
                sb.AppendLine(singleSubject
                    ? $"  - {name} — {FrameHorizontal.Center} {FrameDepth.Foreground}, the only person in the picture."
                    : $"  - {name} — {RoleText(subject)}.");
            }
        }
        sb.AppendLine("- Recorded arrangement:");
        foreach (var character in projection.InFrame)
            sb.AppendLine($"  - {Describe(character)}");
        AppendRoomOwnership(sb, projection);
        // Out-of-frame people are NEVER written into this block, not even as a negative list. Measured 2026-10-27
        // (Omniscient record 1e4a0666): "Not in the active scene: Ken" was paraphrased by the caption writer as "Ken
        // stands out of frame", which the contract refuses on both counts — naming a person the projection excludes and
        // describing what is outside the picture. The in-frame list plus "Do not add any person who is not listed
        // above" already say who is in the photograph; a name here only puts that person back in the prompt.
        sb.AppendLine("- Do not add any person who is not listed above, and do not replace an exact placement with a vague word such as 'near' or 'above'.");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// When the room's prose is suppressed, say where the room came from and what the text budget is for instead. A
    /// bound location image owning the room is what makes the people (and their positions) the only things left to
    /// describe; a room that was removed for any other reason is simply not described.
    /// </summary>
    private static void AppendRoomOwnership(StringBuilder sb, Projection projection)
    {
        if (!projection.SuppressSceneLocation) return;
        if (projection.SceneSuppliedByReference)
        {
            // B-166 D5: the geometry is the image's, but the KIND of place still has to be named or the caption reads
            // as if the people float in nowhere ("two people in the yard", not just "two people").
            sb.AppendLine(projection.LocationType is { Length: > 0 } type
                ? $"- The setting is {Quote(type)}: name it only as that kind of place, never its structure, "
                    + "surfaces, fixtures or objects — the bound location reference supplies those. Any LIGHTING or "
                    + "atmosphere fact recorded above is still yours to write."
                : "- The setting's structure arrives with the bound location reference: do not describe its structure, "
                    + "surfaces, fixtures or objects. Any LIGHTING or atmosphere fact recorded above is still yours to write.");
            sb.AppendLine("- Describe only the people — each person's appearance, exactly where they are in the frame (LEFT / CENTER / RIGHT and foreground/midground/background), what each is doing, and what each is wearing.");
            return;
        }

        sb.AppendLine(projection.LocationType is { Length: > 0 } other
            ? $"- The setting is {Quote(other)}: name it only as that kind of place, and do not add room description of your own."
            : "- The room is not described in this request: describe the people and their positions, and do not add room description of your own.");
    }

    /// <summary>
    /// One person's recorded facts, omitting whatever the prompt deliberately does not describe. An element a bound
    /// reference supplies is not restated here, for the same reason it was removed from the brief text.
    /// </summary>
    private static string Describe(ProjectedCharacter character)
    {
        var parts = new List<string>();
        // B-167 v4: a recorded world fact is stated before the prose copy, so the one fact has one home in the frame.
        if (!string.IsNullOrWhiteSpace(character.WorldBody)) parts.Add($"body: {Quote(character.WorldBody)}");
        if (!string.IsNullOrWhiteSpace(character.WorldFacing)) parts.Add($"facing: {Quote(character.WorldFacing)}");
        if (!string.IsNullOrWhiteSpace(character.Position)) parts.Add($"position: {Quote(character.Position)}");
        if (!string.IsNullOrWhiteSpace(character.ActionOrObservation)) parts.Add($"action: {Quote(character.ActionOrObservation)}");
        if (!string.IsNullOrWhiteSpace(character.Sightline)) parts.Add($"sightline: {Quote(character.Sightline)}");
        if (!string.IsNullOrWhiteSpace(character.Clothing)) parts.Add($"clothing: {Quote(character.Clothing)}");
        // Only the NEGATIVE is stated here. Whether appearance must be written is decided where the appearance text
        // actually is (the builder appends one requirement beside the block it emits), so this cannot point at a block
        // that was never emitted.
        if (!character.AppearanceFromText)
        {
            parts.Add("appearance: supplied by the bound face/body reference — do not describe it");
        }
        return $"{character.Name} — {string.Join("; ", parts)}.";
    }

    private static string RoleText(SceneBlockingSubject subject)
    {
        var across = subject.Position.Horizontal switch
        {
            FrameHorizontal.Left => "LEFT",
            FrameHorizontal.Center => "CENTER",
            FrameHorizontal.Right => "RIGHT",
            _ => throw new InvalidOperationException($"Frame position '{subject.Position.Horizontal}' is not a place a subject can stand.")
        };
        var into = subject.Position.Depth switch
        {
            FrameDepth.Foreground => "the foreground, nearest the camera",
            FrameDepth.Midground => "the midground, at a middle distance from the camera",
            FrameDepth.Background => "the background, farthest from the camera",
            _ => throw new InvalidOperationException($"Frame depth '{subject.Position.Depth}' is not a recorded depth.")
        };
        return $"{across}, in {into}";
    }

    private static ProjectedCharacter ToProjected(
        SceneMomentFrozenCharacter character,
        string? anonymizedName,
        ScenePromptOverrides? suppression,
        string? viewpointProfileKey = null,
        IReadOnlyDictionary<string, string>? namesByKey = null)
    {
        var name = Require(character.Name, "frozen character 'name'");
        var profileKey = Require(character.ProfileKey, $"frozen character '{name}' field 'profileKey'");

        string? Read(string property, string? value, string description)
        {
            if (IsElementSuppressed(suppression, character, property))
                return null;
            var required = Require(value, description);
            return string.IsNullOrWhiteSpace(anonymizedName) ? required : Anonymize(required, anonymizedName);
        }

        return new ProjectedCharacter(
            name,
            profileKey,
            Read("physicalLocation", character.PhysicalLocation, $"frozen character '{name}' field 'physicalLocation'"),
            // The position keeps its POSTURE and SPATIAL facts and loses the clauses that state the camera holder's
            // own body. Anonymising the holder by name substitution alone left a sentence that mixed "the camera" with
            // the pronouns that meant the same person, and a caption writer resolved the ambiguity by INVERTING the
            // relation — "the camera holder stands directly in front of her" for a frame taken from behind (reported
            // live 2026-10-25, SceneImage 665618db).
            OmitHolderRelationClauses(
                Read("position", character.Position, $"frozen character '{name}' field 'position'"),
                participantPov: !string.IsNullOrWhiteSpace(anonymizedName)),
            // The ACTION is the one fact that describes what this person does WITH the camera holder. "his fist closed
            // in her hair" is not renderable from her own eyes and reading it back as "the camera's hair" is nonsense,
            // so the clauses that concern the camera holder's own body are dropped (compiler standards §2.3 rule 3).
            OmitCameraHolderBodyClauses(
                Read("actionOrObservation", character.ActionOrObservation, $"frozen character '{name}' field 'actionOrObservation'"),
                anonymizedName is not null),
            SightlineDirectionOnly(
                Read("sightline", character.Sightline, $"frozen character '{name}' field 'sightline'")),
            Read("clothing", character.Clothing, $"frozen character '{name}' field 'clothing'"),
            AppearanceFromText(suppression, character),
            FacingOf(character, anonymizedName, viewpointProfileKey),
            // B-167 v4 world facts are recorded in WORLD terms, so they still name the camera holder ("standing over
            // Becky", "toward p0") and the profile key. Both reach the caption writer verbatim unless the same
            // anonymisation the prose facts get is applied here — it wrote "toward p0" into a live caption (reported
            // 2026-10-27, prompt record 8655924e).
            WorldBody: KeyFree(
                Anonymize(
                    DescribeWorldBody(character, suppression),
                    anonymizedName),
                viewpointProfileKey,
                namesByKey),
            WorldFacing: IsElementSuppressed(suppression, character, "facing")
                ? null
                : KeyFree(
                    Anonymize(character.Facing?.Toward, anonymizedName),
                    viewpointProfileKey,
                    namesByKey));
    }

    /// <summary>
    /// Replaces a participant key with the word a caption may contain: the recorded name, or "the camera" for the
    /// viewpoint holder, or "the other person" when the record gives no name.
    /// </summary>
    /// <remarks>
    /// A profile key is an internal identifier (and "(p0)" is weight syntax in a diffusion prompt). World facts are
    /// recorded with keys so contacts stay unambiguous, so the translation has to happen where those facts are turned
    /// into prompt text; the caption writer cannot resolve a key and copies it instead.
    /// </remarks>
    private static string? KeyFree(
        string? value,
        string? viewpointProfileKey,
        IReadOnlyDictionary<string, string>? namesByKey)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        return ProfileKeyToken.Replace(value, match =>
        {
            var key = match.Value;
            if (!string.IsNullOrWhiteSpace(viewpointProfileKey)
                && string.Equals(key, viewpointProfileKey.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return UnseenViewpoint;
            }

            return namesByKey is not null && namesByKey.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : "the other person";
        });
    }

    private static readonly Regex ProfileKeyToken = new(
        @"(?<![A-Za-z0-9])p\d{1,2}(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The v4 body fact as one phrase — "posture at anchor" — or null when the record carries neither. A v3 record has
    /// no `at`, so this is null and nothing about world position is claimed.
    /// </summary>
    private static string? DescribeWorldBody(
        SceneMomentFrozenCharacter character,
        ScenePromptOverrides? suppression)
    {
        if (suppression is not null && (IsElementSuppressed(suppression, character, "at.anchor")
            || IsElementSuppressed(suppression, character, "at.posture")))
        {
            return null;
        }

        var posture = NullIfBlank(character.At?.Posture);
        var anchor = NullIfBlank(character.At?.Anchor);
        return (posture, anchor) switch
        {
            (null, null) => null,
            (null, _) => $"at {anchor}",
            (_, null) => posture,
            _ => $"{posture} at {anchor}"
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Which way this recorded cast member faces relative to the camera, from their own recorded facts. Derived, never
    /// assumed: a fact that names the camera holder ("up at Dean", "toward the camera") means their face is toward the
    /// lens; a fact that puts their face on a surface or away from the holder ("down at the boards", "her back to Dean",
    /// "facing the wall") means the camera sees their back. A brief that records neither yields null, and the prompt
    /// states no facing rather than inventing one.
    /// </summary>
    /// <remarks>
    /// This is what the "straight-on view" defect was missing: the position text said "facing forward toward the boards"
    /// (away), but the pre-processor rephrased it into "a straight-on view", which the image model read as facing the
    /// lens. A deterministic conclusion stated in the SHOT block cannot be rephrased away.
    /// </remarks>
    private static SubjectFacing? FacingOf(
        SceneMomentFrozenCharacter character,
        string? viewpointName,
        string? viewpointProfileKey = null)
    {
        if (string.IsNullOrWhiteSpace(viewpointName)) return null;

        // B-167: a recorded v4 `facing.toward` is exact. Naming the viewpoint (by name, profile key or "the camera")
        // means the face is toward the lens; naming anything else means it is not — a conclusion the world fact
        // supports without guessing, because the target is stated. A direction word names no target, so it claims
        // nothing and the pre-v4 prose reading is skipped for a v4 record.
        var toward = character.Facing?.Toward?.Trim();
        if (!string.IsNullOrWhiteSpace(toward))
        {
            var target = FacingTargetOf(toward);
            if (NamesTarget(target, viewpointName, viewpointProfileKey) || IsCameraTarget(target)) return SubjectFacing.TowardCamera;
            return IsDirectionWord(target) ? null : SubjectFacing.AwayFromCamera;
        }

        var sightline = character.Sightline?.Trim() ?? string.Empty;
        var position = character.Position?.Trim() ?? string.Empty;
        var action = character.ActionOrObservation?.Trim() ?? string.Empty;

        // Toward the camera: the subject's own sightline or action names the camera holder with a direction word.
        foreach (var fact in (string[])[sightline, action])
        {
            if (fact.Length == 0 || !fact.Contains(viewpointName, StringComparison.OrdinalIgnoreCase)) continue;
            var lower = fact.ToLowerInvariant();
            if (lower.Contains(" at ") || lower.Contains("up at") || lower.Contains("toward") || lower.Contains("towards"))
            {
                return SubjectFacing.TowardCamera;
            }
        }

        // Away from the camera: the face is on a surface, down, or turned away.
        var combined = $"{position} {sightline} {action}".ToLowerInvariant();
        if (combined.Contains("away", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("back to", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("face down", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("facing the ", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("down at", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("facing forward", StringComparison.OrdinalIgnoreCase))
        {
            return SubjectFacing.AwayFromCamera;
        }

        return null;
    }

    /// <summary>
    /// Whether a recorded v4 facing target names a given person — by profile key, name or character id. World facts are
    /// recorded with the target the enrichment chose, so all three forms have to resolve.
    /// </summary>
    /// <remarks>
    /// The enrichment can also wrap the target in a preposition ("toward p0", "facing Becky"). That phrase is the same
    /// fact as the bare key, so it is unwrapped before matching; otherwise "toward p0" compared unequal to "p0" and was
    /// read as facing AWAY from the camera, inverting the recorded geometry.
    /// </remarks>
    private static bool NamesTarget(string target, params string?[] candidates)
    {
        var normalized = FacingTargetOf(target);
        return candidates.Any(candidate =>
            !string.IsNullOrWhiteSpace(candidate)
            && string.Equals(normalized, candidate.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The thing a recorded facing target names, with a leading preposition ("toward", "facing") removed.</summary>
    private static string FacingTargetOf(string target)
    {
        var value = target.Trim().TrimEnd('.', ',', ';', ':');
        foreach (var prefix in FacingPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..].Trim();
                break;
            }
        }

        return value;
    }

    private static readonly string[] FacingPrefixes =
        ["facing toward ", "facing towards ", "towards ", "toward ", "facing ", "to ", "at "];

    /// <summary>Whether a facing target states the camera itself rather than a person or place.</summary>
    private static bool IsCameraTarget(string target)
        => target.Contains("camera", StringComparison.OrdinalIgnoreCase)
           || target.Contains("viewer", StringComparison.OrdinalIgnoreCase)
           || target.Contains("lens", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a facing target is a bare direction rather than a named thing. A direction names no target, so it cannot
    /// establish which side of the subject a camera is on.
    /// </summary>
    private static bool IsDirectionWord(string target)
        => target is
            "forward" or "forwards" or "ahead" or "backward" or "backwards" or "back"
            or "up" or "down" or "left" or "right" or "straight" or "outward" or "inward";

    /// <summary>
    /// The camera's height expressed RELATIVE TO THE SUBJECT, from the holder's recorded stance AND the subject's.
    /// </summary>
    /// <remarks>
    /// "at the eye height of a person "Kneeling before Dean"" reads to a caption model as a PERSON standing in the frame,
    /// and it wrote exactly that: "Dean stands clothed above a kneeling person's eye level" (record 5365dfe5,
    /// 2026-10-22). The stances are still the only source — this states where they place the lens relative to the
    /// subject. One stance alone cannot do it: a standing holder framing a KNEELING subject is above them (Dean's POV),
    /// while a kneeling holder framing a standing subject is below (Becky's POV), and those two POVs of one moment are
    /// opposites. A pair outside this vocabulary yields no height claim: the aim line still gives the direction, and
    /// inventing a measurement would be worse than omitting one.
    /// </remarks>
    /// <summary>
    /// The height this projection's recorded geometry implies, as a <see cref="CameraHeights"/> value, or null when the
    /// geometry does not decide one.
    /// </summary>
    /// <remarks>
    /// The render-facing camera line must state the SAME height the compile-time SHOT block states, or the two halves of
    /// one prompt disagree ("high angle, viewed from above" above "eye level perspective"). The operator's own pick wins;
    /// otherwise a participant POV's height is derived from the recorded stances. An Omniscient frame states its own
    /// camera in the compiled blocking and is deliberately untouched.
    /// </remarks>
    public static string? CameraHeightKey(Projection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (CameraHeights.IsDefined(projection.OperatorCamera?.Height))
        {
            return CameraHeights.Require(projection.OperatorCamera!.Height, "The framing override");
        }

        return projection.IsOmniscient ? null : HeightFor(projection.UnseenViewpointStance, projection.InFrame).Key;
    }

    /// <summary>The height RELATION in prose and its <see cref="CameraHeights"/> value, for one stance pair.</summary>
    private static (string? Key, string? Text) HeightFor(string? holderStance, IReadOnlyList<ProjectedCharacter> inFrame)
    {
        var holder = StanceOf(holderStance);
        var subject = inFrame.Select(character => StanceOf(character.Position)).FirstOrDefault(stance => stance != Stance.Unknown);

        if (holder == Stance.Unknown || subject == Stance.Unknown) return (null, null);

        return (holder, subject) switch
        {
            (Stance.Lie, _) => (CameraHeights.Floor, "very low — near the floor"),
            (Stance.Kneel or Stance.Sit, Stance.Stand) => (CameraHeights.Waist, "low — below the subject's waist"),
            (Stance.Kneel or Stance.Sit, Stance.Kneel or Stance.Sit or Stance.Bent) => (CameraHeights.Eye, "the subject's own eye height"),
            (Stance.Kneel or Stance.Sit, _) => (CameraHeights.Waist, "low — near the subject's height"),
            (Stance.Bent, Stance.Stand) => (CameraHeights.Waist, "low — below the subject's waist"),
            (Stance.Bent, Stance.Bent or Stance.Kneel or Stance.Sit) => (CameraHeights.Eye, "the subject's own eye height"),
            (Stance.Bent, _) => (CameraHeights.Waist, "low — near the subject's height"),
            // A standing holder framing a KNEELING, SEATED, LYING or BENT-OVER subject is ABOVE them. "bent over at the
            // waist" used to classify as Stand because the word "over" was read as one, so a subject bent over the bench
            // resolved to "the subject's own eye height" — and the camera holder's hands then had nowhere to enter the
            // picture from (SceneImages 5b6a1cdf / b5ea6fb4, 2026-10-27). The operator's own "overhead" override was the
            // only thing that made that scene render correctly, which is what this derivation now states by itself.
            (Stance.Stand, Stance.Bent or Stance.Kneel or Stance.Sit or Stance.Lie) => (CameraHeights.Above, "above the subject — the shot looks down on them"),
            (Stance.Stand, Stance.Stand) => (CameraHeights.Eye, "the subject's own eye height"),
            _ => (null, null)
        };
    }

    /// <summary>
    /// Where the lens sits relative to the subject, in words. A pair outside this vocabulary yields no height claim: the
    /// aim line still gives the direction, and inventing a measurement would be worse than omitting one.
    /// </summary>
    private static string? CameraHeightText(string? holderStance, IReadOnlyList<ProjectedCharacter> inFrame)
        => HeightFor(holderStance, inFrame).Text;

    private enum Stance { Unknown, Stand, Bent, Kneel, Sit, Lie }

    private static Stance StanceOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Stance.Unknown;
        // BENT is its own stance, and is checked FIRST: "bent over at the waist" was reading as STAND because of the word
        // "over", which collapsed a looking-down frame into eye level. "Standing over her" still has no "bent" and stays
        // Stand.
        if (Contains(text, "bent") || Contains(text, "on all fours") || Contains(text, "doubled over")
            || Contains(text, "folded over")) return Stance.Bent;
        if (Contains(text, "kneel")) return Stance.Kneel;
        if (Contains(text, "crouch") || Contains(text, "squat") || Contains(text, "sit")) return Stance.Sit;
        if (Contains(text, "lie") || Contains(text, "lying") || Contains(text, "lay")
            || Contains(text, "prone") || Contains(text, "supine")) return Stance.Lie;
        if (Contains(text, "stand") || Contains(text, "over")) return Stance.Stand;
        return Stance.Unknown;

        static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The ROOM part of a recorded position — "Inside the shed, Kneeling before Dean" → "Inside the shed".
    /// </summary>
    /// <remarks>
    /// The trailing clause is the holder's own stance, and stating it next to the camera re-introduced the second person
    /// the height line no longer does.
    /// </remarks>
    private static string? RoomOnly(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var comma = location.IndexOf(',');
        var room = (comma < 0 ? location : location[..comma]).Trim();
        return room.Length == 0 ? null : room;
    }

    /// <summary>
    /// The direction of an in-frame character's recorded sightline, without its target.
    /// </summary>
    /// <remarks>
    /// "Directed down toward the camera at his hips" is a sentence about the CAMERA HOLDER's body — not in frame — and
    /// after the name scrub its possessive dangles ("at HIS hips"), which the model rendered as "directed downward at
    /// their hips" (record 5365dfe5, 2026-10-22). Only the direction survives, which is all a frame can use.
    /// </remarks>
    private static string? SightlineDirectionOnly(string? sightline)
    {
        if (string.IsNullOrWhiteSpace(sightline)) return null;
        if (!sightline.Contains(UnseenViewpoint, StringComparison.OrdinalIgnoreCase)) return sightline;

        // Keep the DIRECTION and drop the target. "Directed forward and up toward the camera at his hips" becomes
        // "Directed forward and up" — the "and up" is a recorded fact the caption needs (she IS looking up at the lens),
        // and cutting at the first direction word left the model hedging with "likely looking up at the person holding
        // the camera" (record eabff5a2, 2026-10-22).
        var trimmed = sightline.Trim();
        foreach (var marker in (string[])[" toward", " at ", " towards"])
        {
            var index = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0) return trimmed[..index].Trim();
        }

        return null;
    }

    /// <summary>
    /// Drops the clauses of a recorded action that concern the CAMERA HOLDER's own body, keeping the rest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Standing clothed above the camera, his fist closed in the camera's hair" keeps the spatial fact the shot needs
    /// and loses the one the model cannot render. Two passes, both deterministic:
    /// </para>
    /// <list type="number">
    /// <item><description>a clause that takes a POSSESSIVE of the camera holder ("the camera's hair") describes their
    /// body, which is not in frame — dropped;</description></item>
    /// <item><description>when any surviving clause relates the subject to the CAMERA ("above the camera"), the other
    /// clauses describe what the subject does with the camera holder — dropped, leaving the spatial fact alone.</description></item>
    /// </list>
    /// <para>
    /// A clause set that mentions neither is the subject's own action and is kept whole.
    /// </para>
    /// </remarks>
    private static string? OmitCameraHolderBodyClauses(string? action, bool participantPov)
    {
        if (string.IsNullOrWhiteSpace(action)) return action;
        if (!participantPov) return action;

        var possessive = $"{UnseenViewpoint}'s";
        var clauses = action
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(clause => !clause.Contains(possessive, StringComparison.OrdinalIgnoreCase))
            // A body described against another body is contact with the camera holder, and at a POV that body is not in
            // frame: "her mouth on him" can only be rendered by inventing who owns it (record 4da992ba, Dean's POV).
            .Where(clause => !CameraContact.IsMatch(clause) && !SomeonesBodyPart.IsMatch(clause))
            .ToList();

        var spatial = clauses.Where(clause => clause.Contains(UnseenViewpoint, StringComparison.OrdinalIgnoreCase)).ToList();
        if (spatial.Count > 0) clauses = spatial;

        return clauses.Count == 0 ? null : string.Join(", ", clauses);
    }

    /// <summary>
    /// Makes a PICTURED person's position safe to read at a participant POV: the camera holder is named once, and the
    /// clauses that describe their BODY are dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The holder is anonymised by name substitution ("Dean" → "the camera"), which left a sentence mixing that
    /// placeholder with the pronouns that meant the same person: "Becky is bent over … directly in front of the camera
    /// and facing away from him toward the tin wall … pressed tightly against the camera's hips". A caption writer
    /// cannot resolve that, and resolved it by inversion — "The camera holder stands directly in front of her" for a
    /// frame taken from behind (reported live 2026-10-25, SceneImage 665618db).
    /// </para>
    /// <para>
    /// So the object pronoun is resolved to the placeholder ("away from him" → "away from the camera" — a true spatial
    /// fact), and only the clauses that put her body against the HOLDER's body go: a POV never shows that body, the
    /// frame already states where the camera is ("Camera is at: Behind Becky"), and the holder's own visible body is
    /// stated once, in its own note. What survives is the subject's posture and their measurable place under the lens.
    /// </para>
    /// </remarks>
    private static string? OmitHolderRelationClauses(string? position, bool participantPov)
    {
        if (string.IsNullOrWhiteSpace(position) || !participantPov) return position;

        var resolved = DanglingHolderPronoun.Replace(position, $" {UnseenViewpoint}");
        var clauses = Split(resolved)
            .Where(clause => !CameraHolderBodyPart.IsMatch(clause))
            .Where(clause => !CameraHolderContact.IsMatch(clause))
            .ToList();

        return clauses.Count == 0 ? null : string.Join(", ", clauses);
    }

    /// <summary>The camera holder's own body, named through the placeholder — "the camera's hips".</summary>
    /// <remarks>
    /// "waist" is deliberately absent: "her hips are raised at about the camera's waist height" is a HEIGHT relation
    /// the render needs, not a body the frame can show.
    /// </remarks>
    private static readonly Regex CameraHolderBodyPart = new(
        @"\bthe camera'?s?\s+(?:hair|hips?|mouth|lips?|face|head|hands?|fist|fingers?|arms?|legs?|thighs?|neck|throat|shoulders?|back|chest|breasts?|eyes|wrists?|skin|penis|testicles?|body)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>One body put AGAINST the camera holder's, at the placeholder rather than at a name.</summary>
    private static readonly Regex CameraHolderContact = new(
        @"\b(?:against|onto|on|in|into|inside|between|around)\s+(?:the camera|camera)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// An object pronoun that can only refer to the camera holder at a participant POV: the pictured person is the
    /// subject of their own sentence, so "him" is the only other body in the scene.
    /// </summary>
    private static readonly Regex DanglingHolderPronoun = new(
        @"\shim\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Whether a bound FACE and a bound BODY reference together supply this person's appearance, in which case the
    /// caption must not describe it (B-166 D10). Anything less — no reference, or only one of the two — leaves
    /// appearance to the text, because a face image says nothing about the build and a body image at one angle is not
    /// a substitute for a chosen face.
    /// </summary>
    private static bool AppearanceFromText(ScenePromptOverrides? suppression, SceneMomentFrozenCharacter character)
        => !(IsElementSuppressed(suppression, character, "appearance.face")
             && IsElementSuppressed(suppression, character, "appearance.build"));

    /// <summary>Replaces the camera holder's name in a recorded fact, keeping the fact (debug 028).</summary>
    private static string? Anonymize(string? value, string? name)
        => string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(name)
            ? value
            : value.Replace(name.Trim(), UnseenViewpoint, StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> RemovedKeys(ScenePromptOverrides? suppression)
        => new(suppression?.RemovedCharacters?.Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()) ?? [],
            StringComparer.OrdinalIgnoreCase);

    private static bool IsRemoved(SceneMomentFrozenCharacter character, HashSet<string> removed)
        => removed.Count > 0
           && (Contains(removed, character.CharacterId) || Contains(removed, character.Name) || Contains(removed, character.ProfileKey));

    private static bool Contains(HashSet<string> keys, string? candidate)
        => !string.IsNullOrWhiteSpace(candidate) && keys.Contains(candidate.Trim());

    /// <summary>
    /// Whether the prompt suppresses an element for one character. Element keys are the SAME keys
    /// <see cref="ScenePromptOverridesApplier"/> applies: <c>character:&lt;id|name|profileKey&gt;.&lt;field&gt;</c>.
    /// </summary>
    private static bool IsElementSuppressed(ScenePromptOverrides? suppression, SceneMomentFrozenCharacter character, string property)
    {
        if (suppression is null || suppression.Fields.Count == 0) return false;
        foreach (var field in suppression.Fields)
        {
            if (!field.Removed || string.IsNullOrWhiteSpace(field.ElementKey)) continue;
            var key = field.ElementKey.Trim();
            foreach (var identity in (string?[])[character.CharacterId, character.Name, character.ProfileKey])
            {
                if (!string.IsNullOrWhiteSpace(identity)
                    && string.Equals(key, $"character:{identity.Trim()}.{property}", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Whether the prompt suppresses a scene-level frozen element. Both spellings the applier accepts are honoured
    /// (<c>frozenState.*</c> and <c>scene.*</c>), so a key cannot suppress the brief text and silently not the frame.
    /// </summary>
    private static bool IsSceneElementSuppressed(ScenePromptOverrides? suppression, string property)
    {
        if (suppression is null || suppression.Fields.Count == 0) return false;
        foreach (var field in suppression.Fields)
        {
            if (!field.Removed || string.IsNullOrWhiteSpace(field.ElementKey)) continue;
            var key = field.ElementKey.Trim();
            if (string.Equals(key, $"frozenState.{property}", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, $"scene.{property}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The compiled frame with the placements of characters the prompt removed. A removed character is not depicted,
    /// so a placement for them would describe somebody the picture does not contain.
    /// </summary>
    private static SceneBlocking? DropRemovedSubjects(SceneBlocking? blocking, HashSet<string> removed)
    {
        if (blocking is null || removed.Count == 0) return blocking;
        var subjects = blocking.Subjects.Where(subject => !removed.Contains(subject.SubjectKey)).ToList();
        if (subjects.Count == 0) return null;
        return blocking with
        {
            Subjects = subjects,
            Layering = blocking.Layering.Where(subjectKey => !removed.Contains(subjectKey)).ToList()
        };
    }

    /// <summary>
    /// Removes the camera holder from the semantic snapshot handed to the compiler, so a participant POV cannot be
    /// compiled into a two-person external scene (B-166, measured live 2026-10-20).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately a REMOVAL and not a note. The projection already says "the unseen viewpoint is never in the
    /// frame", and the model ignored it while reading the viewpoint's full frozen entry — position, action, clothing and
    /// sightline — two paragraphs earlier. A fact that is handed over as data outranks a sentence about it, so the data
    /// has to go.
    /// </para>
    /// <para>
    /// Removed: the character's <c>frozenState.characters</c> entry, their <c>wardrobeStates</c> / <c>characterStates</c>
    /// entries in both continuity blocks, and their name in every other character's <c>visibleCharacterNames</c> (a
    /// visible-cast list is what the compiler uses to decide who is in the picture). The camera facts are carried by the
    /// projection instead, which is why nothing about the viewpoint is lost.
    /// </para>
    /// </remarks>
    public static string DropViewpoint(
        string semanticInputSnapshotJson,
        string? viewpointProfileKey,
        IReadOnlySet<string>? operatorEditedMembers = null)
    {
        if (string.IsNullOrWhiteSpace(semanticInputSnapshotJson) || string.IsNullOrWhiteSpace(viewpointProfileKey))
        {
            return semanticInputSnapshotJson ?? string.Empty;
        }

        try
        {
            if (JsonNode.Parse(semanticInputSnapshotJson) is not JsonObject root
                || root["frozenState"] is not JsonObject frozen)
            {
                return semanticInputSnapshotJson;
            }

            var key = viewpointProfileKey.Trim();
            string? viewpointName = null;

            // 1. EVERY per-character entry goes. The projection already states the in-frame cast and their facts; the
            //    brief's copies are the only place the camera holder's name can survive ("Standing over Becky").
            if (frozen["characters"] is JsonArray characters)
            {
                foreach (var node in characters)
                {
                    if (node is not JsonObject character) continue;
                    if (string.Equals(StringMember(character, "profileKey"), key, StringComparison.OrdinalIgnoreCase))
                    {
                        viewpointName = StringMember(character, "name");
                    }
                }

                frozen.Remove("characters");
            }

            // 2. Whole-event prose. "Becky works him orally … as Dean grips her hair" describes the camera holder, and
            //    it is the sentence the model turned into a second person in frame. A member the OPERATOR rewrote is
            //    kept: the operator's own text outranks the scrub, and only the camera holder's NAME is taken out of it
            //    (the operator's chosen policy — a name is scrubbed, never obeyed and never silently obeyed past).
            ScrubOrRemove(frozen, "frozenState", "visualDescription", viewpointName, operatorEditedMembers);
            ScrubOrRemove(frozen, "frozenState", "continuityState", viewpointName, operatorEditedMembers);

            // B-167: the selected event sentence and the recorded contacts are the two facts the caption now reads, so
            // they have to obey the same rule. The event line is scrubbed exactly like the prose copies it replaced
            // (an operator edit survives, the holder's name does not), and a contact that names the camera holder is
            // removed outright — it describes the person BEHIND the lens, and the visible parts that remain in frame are
            // stated by the projection's own CAMERA HOLDER'S VISIBLE BODY section.
            if (frozen["event"] is JsonObject eventNode)
            {
                ScrubOrRemove(eventNode, "frozenState", "line", viewpointName, operatorEditedMembers);
                ScrubOrRemove(eventNode, "frozenState", "anchor", viewpointName, operatorEditedMembers);
            }

            if (frozen["contacts"] is JsonArray contacts)
            {
                for (var index = contacts.Count - 1; index >= 0; index--)
                {
                    if (contacts[index] is not JsonObject contact) continue;
                    if (string.Equals(StringMember(contact, "from"), key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(StringMember(contact, "to"), key, StringComparison.OrdinalIgnoreCase))
                    {
                        contacts.RemoveAt(index);
                    }
                }
            }
            // MOOD is a narrative label, not a photographic fact, and a caption model pads a bare setting with it:
            // "within a small, confined space that gives an intimate feeling" was invented out of mood "Charged, tense,
            // intimate" once the location image owned the room (record eabff5a2, 2026-10-22). It goes for a participant
            // POV unless the OPERATOR set it themselves.
            if (operatorEditedMembers is null || !operatorEditedMembers.Contains("frozenState.mood"))
            {
                RemoveMember(frozen, "mood");
            }

            if (root["moment"] is JsonObject moment)
            {
                ScrubOrRemove(moment, "moment", "visibleAction", viewpointName, operatorEditedMembers);
                ScrubOrRemove(moment, "moment", "compositionRationale", viewpointName, operatorEditedMembers);
                // The moment's own frozen-state line is WHOLE-EVENT prose: "Becky kneels on gritty boards, nude, both
                // hands braced on Dean's hips, her mouth on him; Dean stands clothed above her, his fist closed in her
                // hair". That single string is what the compiler turned into "the kneeling viewpoint character … her
                // hair … she lies nude beneath him" (measured 2026-10-21/22).
                ScrubOrRemove(moment, "moment", "frozenState", viewpointName, operatorEditedMembers);

                // The participant list is what told the model there were two people regardless of the prose.
                if (moment["participantSummary"] is JsonArray summary)
                {
                    for (var index = summary.Count - 1; index >= 0; index--)
                    {
                        if (summary[index] is JsonObject entry
                            && string.Equals(StringMember(entry, "profileKey"), key, StringComparison.OrdinalIgnoreCase))
                        {
                            summary.RemoveAt(index);
                        }
                    }
                }
            }

            // 3. Continuity prose that names the camera holder.
            if (root["continuity"] is JsonObject continuity)
            {
                foreach (var block in continuity)
                {
                    if (block.Value is not JsonObject blockObject) continue;
                    foreach (var property in (string[])["stateSummary", "characterStates", "wardrobeStates"])
                    {
                        if (blockObject[property] is JsonArray entries)
                        {
                            for (var index = entries.Count - 1; index >= 0; index--)
                            {
                                if (entries[index] is not JsonObject entry) continue;
                                // The entry key targets the camera holder's own state; the VALUE can name them from
                                // another character's angle ("facing Becky", "Hauling Becky up by the arm"), which is
                                // just as visible to the compiler as the key was (measured on brief dd2d8c8f).
                                if (string.Equals(StringMember(entry, "key"), key, StringComparison.OrdinalIgnoreCase)
                                    || NamesTheViewpoint(StringMember(entry, "value"), viewpointName))
                                {
                                    entries.RemoveAt(index);
                                }
                            }
                        }
                        else
                        {
                            RemoveIfItNames(blockObject, property, viewpointName);
                        }
                    }
                }
            }

            // 4. Typed references addressed to the camera holder: the role clause names the images that are actually
            //    sent, so a leftover "identity-p0" only tells the model to depict somebody it cannot see.
            if (root["typedReferences"] is JsonArray references)
            {
                for (var index = references.Count - 1; index >= 0; index--)
                {
                    if (references[index] is JsonObject reference
                        && string.Equals(StringMember(reference, "subjectKey"), key, StringComparison.OrdinalIgnoreCase))
                    {
                        references.RemoveAt(index);
                    }
                }
            }

            return root.ToJsonString(JsonOptions);
        }
        catch (JsonException)
        {
            return semanticInputSnapshotJson;
        }
    }

    /// <summary>Removes a string member, matching the property name case-insensitively.</summary>
    private static void RemoveMember(JsonObject target, string property)
    {
        var member = target.Select(pair => pair.Key)
            .FirstOrDefault(pairKey => string.Equals(pairKey, property, StringComparison.OrdinalIgnoreCase));
        if (member is not null) target.Remove(member);
    }

    /// <summary>Removes a string member when it names the camera holder; leaves it alone otherwise.</summary>
    private static void RemoveIfItNames(JsonObject target, string property, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var member = target.Select(pair => pair.Key)
            .FirstOrDefault(pairKey => string.Equals(pairKey, property, StringComparison.OrdinalIgnoreCase));
        if (member is null) return;
        if (target[member] is not JsonValue value || !value.TryGetValue<string>(out var text) || text is null) return;
        if (NamesTheViewpoint(text, name)) target.Remove(member);
    }

    /// <summary>
    /// Removes a recorded prose member that names the camera holder, and for one the operator rewrote, keeps only the
    /// segments that are renderable from this camera.
    /// </summary>
    /// <remarks>
    /// A name substitution was tried first and made things worse (record 4438d917): "Becky kneels nude … her mouth is
    /// sucking his dick" became "the camera kneels nude … her mouth is sucking his dick" and the model invented a person
    /// to own it. A camera cannot kneel or have its mouth anywhere. Two conditions drop a segment:
    /// <list type="bullet">
    /// <item><description>it names the camera holder; or</description></item>
    /// <item><description>it records CONTACT with somebody's body — "in her hair", "on his hips" — because at a POV the
    /// camera holder's body is never in frame, and the model's only way to render that contact is to put a second
    /// person in the picture ("his fist is closed in the kneeling person's hair", record 5365dfe5, 2026-10-22).</description></item>
    /// </list>
    /// Everything else is kept verbatim, which is the detail the operator was adding ("his pants undone with the fly
    /// wide open", "his long and thick erection visible").
    /// </remarks>
    private static void ScrubOrRemove(
        JsonObject target,
        string scope,
        string property,
        string? name,
        IReadOnlySet<string>? operatorEditedMembers)
    {
        var member = target.Select(pair => pair.Key)
            .FirstOrDefault(pairKey => string.Equals(pairKey, property, StringComparison.OrdinalIgnoreCase));
        if (member is null) return;
        if (target[member] is not JsonValue value || !value.TryGetValue<string>(out var text) || text is null) return;

        var operatorEdited = operatorEditedMembers is not null
            && operatorEditedMembers.Contains($"{scope}.{member}");
        var namesTheHolder = !string.IsNullOrWhiteSpace(name) && NamesTheViewpoint(text, name);
        if (!operatorEdited && namesTheHolder)
        {
            target.Remove(member);
            return;
        }

        var segments = Split(text);
        var kept = segments.Where(segment => !NamesTheViewpoint(segment, name))
            .Where(segment => !CameraContact.IsMatch(segment) && !SomeonesBodyPart.IsMatch(segment))
            .ToList();
        if (kept.Count == 0) target.Remove(member);
        else if (kept.Count != segments.Count) target[member] = string.Join(", ", kept) + ".";
    }

    private static List<string> Split(string text)
        => text.Split(['.', ';', '!', '?', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    /// <summary>Contact markers: a preposition that puts one body against another, then the body part.</summary>
    private static readonly Regex CameraContact = new(
        @"\b(?:in|into|on|onto|around|against|inside|between)\s+(?:her|his|their|[A-Z][\w'-]*'s)\s+(?:hair|hips?|waist|mouth|lips?|face|head|hands?|fist|arms?|legs?|thighs?|neck|throat|shoulders?|back|chest|breasts?|eyes|fingers?|wrists?|skin)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>A body part owned by a possessive — "her mouth", "his fist" — whether or not it is contact.</summary>
    private static readonly Regex SomeonesBodyPart = new(
        @"\b(?:her|his|their|its)\s+(?:hair|hips?|waist|mouth|lips?|face|head|hands?|fist|arms?|legs?|thighs?|neck|throat|shoulders?|back|chest|breasts?|eyes|fingers?|wrists?|skin)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The body rule is deliberately blunt. A body part with an unnamed possessive ("her mouth on him") cannot be
    /// attributed to the frame without knowing whose pronoun it is, and the brief records no gender — so at a POV it is
    /// dropped. What survives is the subject's WARDROBE and situation ("his pants undone with the fly wide open", "his
    /// long and thick erection visible"), which is the detail an operator adds here, while the projection states the
    /// in-frame person's own body facts from their character record.
    /// </remarks>
    private static bool NamesTheViewpoint(string? text, string? name)
        => !string.IsNullOrWhiteSpace(name)
           && !string.IsNullOrWhiteSpace(text)
           && text.Contains(name, StringComparison.OrdinalIgnoreCase);

    private static string? StringMember(JsonObject? target, string property)
    {
        if (target is null) return null;
        foreach (var pair in target)
        {
            if (!string.Equals(pair.Key, property, StringComparison.OrdinalIgnoreCase)) continue;
            return pair.Value is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }

        return null;
    }

    /// <summary>
    /// Removes each frozen character's <c>physicalLocation</c> from the semantic snapshot handed to the compiler.
    /// </summary>
    /// <remarks>
    /// Used only when the room is supplied by a bound location image. A character's physical location is a ROOM anchor
    /// ("Inside the shed, on the gritty boards"): the place is the image's, so repeating it as a character fact is how a
    /// reference-supplied room still leaks back into a prompt that was told not to describe it. Nothing about the
    /// person, their stance, action, sightline or clothing is touched — those are exactly what the text must describe.
    /// The snapshot is left unchanged when it cannot be parsed, matching the other JSON touches on this path.
    /// </remarks>
    public static string DropCharacterLocations(string semanticInputSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(semanticInputSnapshotJson))
        {
            return semanticInputSnapshotJson ?? string.Empty;
        }

        try
        {
            if (JsonNode.Parse(semanticInputSnapshotJson) is not JsonObject root
                || root["frozenState"] is not JsonObject frozen
                || frozen["characters"] is not JsonArray characters)
            {
                return semanticInputSnapshotJson;
            }

            foreach (var node in characters)
            {
                if (node is not JsonObject character) continue;
                var member = character
                    .Select(pair => pair.Key)
                    .FirstOrDefault(key => string.Equals(key, "physicalLocation", StringComparison.OrdinalIgnoreCase));
                if (member is not null) character.Remove(member);
            }

            return root.ToJsonString(JsonOptions);
        }
        catch (JsonException)
        {
            return semanticInputSnapshotJson;
        }
    }

    /// <summary>
    /// The semantic snapshot the projection works from: the enrichment's facts with the operator's VALUE edits applied
    /// to everything the projection reads as content.
    /// </summary>
    /// <remarks>
    /// ONE read for the whole projection. It used to be two: the frozen state was built from
    /// <c>ApplyOperatorCharacterEdits</c> while the camera holder's visible body was resolved from the RAW brief, so an
    /// operator who replaced <c>frozenState.contacts</c> changed the fact sheet and left the holder note reading the
    /// enrichment's original contact (reported live 2026-10-25, SceneImage 665618db: "buried to the root" survived the
    /// removal).
    /// </remarks>
    private static string ProjectionSnapshot(CompiledMediaBrief brief, ScenePromptOverrides? suppression)
    {
        if (string.IsNullOrWhiteSpace(brief.SemanticInputSnapshotJson))
            throw new InvalidOperationException($"Brief '{brief.Id}' carries no semantic input, so no POV frame can be projected.");

        // A per-character row the operator edited is the value the frame must be built from: the compiler never sees
        // the raw entry for a participant POV (DropViewpoint removes the cast), so the projection is the ONLY carrier
        // of "what Dean is doing". Reading the un-edited enrichment here silently discarded the operator's text
        // (reported 2026-10-22: "I add it to a section and it never reaches the prompt").
        return ScenePromptOverridesApplier.ApplyOperatorProjectionEdits(brief.SemanticInputSnapshotJson, suppression);
    }

    private static SceneMomentFrozenStateContract ReadFrozenState(CompiledMediaBrief brief, ScenePromptOverrides? suppression = null)
    {
        var snapshot = ProjectionSnapshot(brief, suppression);

        try
        {
            using var document = JsonDocument.Parse(snapshot);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("frozenState", out var frozenState)
                || frozenState.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Brief '{brief.Id}' carries no 'frozenState', so the POV frame cannot be projected from recorded facts.");
            }

            return frozenState.Deserialize<SceneMomentFrozenStateContract>(JsonOptions)
                ?? throw new InvalidOperationException($"Brief '{brief.Id}' carries an unreadable 'frozenState'.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Brief '{brief.Id}' carries an unreadable semantic input snapshot.", exception);
        }
    }

    /// <summary>
    /// The frame this POV actually SHOWS, as blocking the reference clause can name people from — or null when the POV
    /// has no external frame at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A participant POV is the camera, so no external placement exists and nothing may be called "the person on the
    /// LEFT": the clause names the reference by what it is instead. An Omniscient still keeps only the people inside the
    /// scene, so a remote observer never holds a seat in a frame they are not in.
    /// </para>
    /// <para>
    /// This is the ONE place the render and the compiler agree on which seats exist; a render that re-derived them from
    /// the brief alone would name a person "the person on the LEFT" in a frame where they stand alone.
    /// </para>
    /// </remarks>
    public static SceneBlocking? BlockingForFrame(CompiledMediaBrief brief, string? pov, SceneBlocking? blocking)
    {
        ArgumentNullException.ThrowIfNull(brief);
        if (blocking is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(pov))
        {
            // No POV means no projection was made, so no frame-space facts (in-frame cast, remote observers) exist to
            // re-seat against. The compiled blocking is then the only frame recorded, and it is passed through unchanged
            // — the behaviour a legacy record that predates POV-framed renders has always had.
            return blocking;
        }

        if (!string.Equals(pov.Trim(), SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var frozen = ReadFrozenState(brief);
        var location = Require(frozen.Location, "frozen state 'location'");
        var inFrameKeys = ReadCast(frozen)
            .Where(character => !IsRemoteObserver(character, location))
            .Select(character => character.ProfileKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seated = SceneBlockingResolver.ReseatForCast(blocking, inFrameKeys);
        return seated.Subjects.Count == 0 ? null : seated;
    }

    private static IReadOnlyList<SceneMomentFrozenCharacter> ReadCast(SceneMomentFrozenStateContract frozen)
    {
        var characters = frozen.Characters ?? [];
        if (characters.Count == 0)
            throw new InvalidOperationException("The brief's frozen state records no characters, so no POV frame can be projected.");
        return characters;
    }

    /// <summary>
    /// Whether this cast member is a remote observer: present in the evidence but physically outside the active space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A recorded "observer" is not automatically off-camera. The involvement says the person has no meaningful part in
    /// the beat's action; it says nothing about which room they are in. Comparing the location phrases for EXACT equality
    /// made that mistake on a live Omniscient composition (2026-10-27, prompt record 1e4a0666): Ken stood at the kitchen
    /// counter a few feet from Becky on the living-room couch in the same trailer, but "by the kitchen counter" is not
    /// the string "… the living room couch", so he was dropped from the frame while the compiled blocking still seated
    /// him in the background. The prompt then said he was out of the picture, named him in that negative line, and the
    /// caption wrote him "out of frame".
    /// </para>
    /// <para>
    /// A mismatch alone therefore no longer makes a remote observer. The record has to state a SEPARATION — a place
    /// beyond a boundary (outside, the yard, another room, through the glass). Without one, the observer is treated as
    /// present in the active space, which is the only reading where the cast and the authored composition agree.
    /// </para>
    /// </remarks>
    private static bool IsRemoteObserver(SceneMomentFrozenCharacter character, string sceneLocation)
    {
        if (!string.Equals(character.Involvement, ObserverInvolvement, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(character.PhysicalLocation, sceneLocation, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return NameASeparation(
            character.PhysicalLocation, character.Position, character.ActionOrObservation, character.Sightline);
    }

    /// <summary>
    /// Wording that puts a recorded place beyond a boundary — a different room, the outdoors, or the far side of glass.
    /// </summary>
    /// <remarks>
    /// Deliberately a closed list of explicit separation facts rather than a guess about distances: "by the kitchen
    /// counter" and "the living-room couch" are two spots in one open space, while "the yard outside the window" is a
    /// place the camera in the room cannot see into. A record that says neither stays IN the scene, because an
    /// observer with no separation fact is present, and inventing an absence would remove a person the frame seated.
    /// </remarks>
    private static readonly string[] SeparationPhrases =
    [
        "outside", "outdoors", "out of doors", "exterior", "the yard", "in the yard", "backyard", "front yard",
        "next room", "other room", "another room", "adjacent room", "separate room", "down the hall",
        "through the window", "through the glass", "behind the glass", "through the doorway", "across the street",
        "upstairs", "downstairs", "in the car", "in the vehicle"
    ];

    private static bool NameASeparation(params string?[] statements)
        => statements.Any(statement =>
            !string.IsNullOrWhiteSpace(statement)
            && SeparationPhrases.Any(phrase => statement.Contains(phrase, StringComparison.OrdinalIgnoreCase)));

    private static string Require(string? value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"The brief's {description} is empty, so the POV frame cannot be projected.");
        return value.Trim();
    }

    /// <summary>Words that introduce a place or a relation rather than name it ("Inside the shed" → "the shed").</summary>
    private static readonly string[] LocationLeadIns =
    [
        "inside", "in", "at", "on", "outside", "beyond", "within", "through", "by", "near",
        "under", "above", "behind", "before", "from", "across", "down", "up", "over", "with",
        "the", "a", "an"
    ];

    /// <summary>
    /// The KIND of place the brief records, taken mechanically from its own location text: the head noun phrase of the
    /// first clause, stopped at the first preposition. It invents nothing — "Shed behind the trailers - gritty boards"
    /// becomes "shed", which is what lets a caption read "in a shed" while the image owns the room (B-166 D5).
    /// </summary>
    /// <remarks>
    /// Capping at four words instead of stopping at a preposition produced "Shed behind the trailers", and the caption
    /// model padded around that label with its own room guess — "within a small, confined space that gives an intimate
    /// feeling" (record eabff5a2, 2026-10-22). The KIND is the noun phrase: "living room" and "front porch" survive
    /// because neither is a preposition, while "shed behind the trailers" stops at "behind".
    /// </remarks>
    private static string? LocationTypeOf(string location)
    {
        var clause = location.Split(',', 2)[0].Trim();
        if (clause.Length == 0) return null;

        var words = clause
            .Replace(" - ", " - ", StringComparison.Ordinal)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        while (words.Count > 1 && LocationLeadIns.Contains(words[0], StringComparer.OrdinalIgnoreCase))
        {
            words.RemoveAt(0);
        }

        var kind = new List<string>();
        foreach (var word in words)
        {
            // "Shed behind…" / "Yard - the gate" stop at the relation, not after a word count.
            if (word is "-" or "–" or "—") break;
            if (LocationLeadIns.Contains(word, StringComparer.OrdinalIgnoreCase)) break;
            kind.Add(word);
            if (kind.Count == 3) break;
        }

        var type = string.Join(' ', kind).Trim();
        return type.Length == 0 ? null : type;
    }

    /// <summary>A recorded optional fact, trimmed, or null when the brief did not record it.</summary>
    private static string? RecordedOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Quote(string value) => $"\"{value}\"";
}
