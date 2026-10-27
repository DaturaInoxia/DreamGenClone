using System.Globalization;
using System.Text.RegularExpressions;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.Evaluation;

/// <summary>One way a generated prompt failed the REQUEST it was compiled from (B-166 D2/D9).</summary>
/// <param name="Element">The element that is missing or forbidden, in the operator's vocabulary.</param>
/// <param name="Person">The person the element belongs to, or null for a scene-level element.</param>
/// <param name="Detail">What to say to the model on the corrective pass, and to the operator if it still fails.</param>
/// <param name="Blocking">
/// Whether this violation REFUSES the prompt, or is recorded as a diagnostic only.
///
/// <para>
/// Only contradictions that are certainly wrong block a render: a person the frame excludes being depicted, or a name
/// with nothing to anchor it. Placement wording and "is the setting named" are judged from prose, and prose paraphrases —
/// blocking on them turned a slightly loose caption into a failed compile (measured 2026-10-20, three failed records on
/// one composition). Those are reported, never fatal.
/// </para>
/// </param>
public sealed record PromptContractViolation(
    string Element,
    string? Person,
    string Detail,
    bool Blocking = false);

/// <summary>
/// Validates a generated image prompt against the REQUEST it came from — the POV projection, the bindings, and what
/// the bound images own — rather than against itself.
/// </summary>
/// <remarks>
/// <para>
/// This exists because every rule in the prompt pipeline was ADVISORY until now: the request stated where each person
/// stands and what they wear, the pre-processor could drop all of it, and nothing noticed. The operator saw a wrong
/// picture and had no way to tell whether the input was wrong or the model ignored it (B-166 §1).
/// </para>
/// <para>
/// The checks are deliberately the ones that can be decided from RECORDED facts. A check that guessed would produce
/// false failures, and a false failure costs a retry and then a refused render — worse than the silence it replaces.
/// </para>
/// </remarks>
public static class SceneImagePromptContractValidator
{
    /// <summary>
    /// The violations in <paramref name="prompt"/>. An empty list means the prompt satisfies every check that applies.
    /// </summary>
    /// <param name="prompt">The prompt the pre-processor wrote.</param>
    /// <param name="projection">The frame this request asked for, or null when the request has no brief-backed frame.</param>
    /// <param name="bindings">The references this render will send, in send order.</param>
    /// <param name="textDescribedNames">
    /// Characters whose appearance the prompt is allowed to describe in words (their appearance element was NOT
    /// supplied by a reference). A name is qualified by a reference OR by this description (D9).
    /// </param>
    public static IReadOnlyList<PromptContractViolation> Validate(
        string prompt,
        SceneImagePovProjection.Projection? projection,
        IReadOnlyList<ReferenceApplicationSelection>? bindings,
        IReadOnlySet<string>? textDescribedNames = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        if (projection is null)
        {
            return [];
        }

        var violations = new List<PromptContractViolation>();
        var referencedKeys = ReferencedActorKeys(bindings);
        var textDescribed = textDescribedNames is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(textDescribedNames, StringComparer.OrdinalIgnoreCase);

        // D9 — a name is an anchor only when something resolves it.
        foreach (var character in projection.InFrame)
        {
            if (!Names(prompt, character.Name)) continue;
            var qualified = referencedKeys.Contains(character.ProfileKey)
                || textDescribed.Contains(character.Name)
                || textDescribed.Contains(character.ProfileKey);
            if (!qualified)
            {
                violations.Add(new PromptContractViolation(
                    "the character's name",
                    character.Name,
                    $"The prompt names {character.Name}, but nothing in this request anchors that name: no reference "
                    + "resolves to them and their appearance is not described. Refer to them by their place in the "
                    + "frame and their traits instead, or describe their appearance.",
                    Blocking: true));
            }
        }

        // A person the projection puts OUT of frame must not be named or depicted at all.
        foreach (var name in projection.OutOfFrame)
        {
            if (Names(prompt, name))
            {
                violations.Add(new PromptContractViolation(
                    "a person who is out of frame",
                    name,
                    $"The prompt names {name}, who is not in this POV's frame. Do not depict or name them.",
                    Blocking: true));
            }
        }

        // The frame placement the request stated has to appear in the caption's vocabulary.
        if (projection.Frame is { Subjects.Count: > 0 } frame)
        {
            foreach (var horizontal in frame.Subjects.Select(subject => subject.Position.Horizontal).Distinct())
            {
                var token = horizontal switch
                {
                    FrameHorizontal.Left => "LEFT",
                    FrameHorizontal.Center => "CENTER",
                    FrameHorizontal.Right => "RIGHT",
                    _ => null
                };
                if (token is not null && !prompt.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new PromptContractViolation(
                        "frame placement",
                        null,
                        $"This frame places someone {token} of centre, but the prompt never states a {token} placement. "
                        + "State each person's LEFT / CENTER / RIGHT position and their depth."));
                }
            }
        }

        if (projection.SceneSuppliedByReference && projection.LocationType is { Length: > 0 } type)
        {
            // D5 — the KIND of place should still be named, or the caption reads as if the people float in nowhere.
            // ADVISORY only: whether a paraphrase names the setting is a judgement about prose, and making it fatal
            // turned a slightly loose caption into a failed compile (measured 2026-10-20).
            if (!ContainsAnyWord(prompt, type))
            {
                violations.Add(new PromptContractViolation(
                    "the setting's kind",
                    null,
                    $"The setting is supplied by a location reference, but the prompt never names it as {type}. Name "
                    + "it only as that kind of place."));
            }
        }

        return violations;
    }

    /// <summary>
    /// Camera and framing vocabulary, as the compiler standards name it (§2.1 "Perspective/Viewpoint — camera
    /// distance/angle; distance is load-bearing").
    /// </summary>
    private static readonly string[] CameraPhrases =
    [
        "close-up", "close up", "tight shot", "tight", "extreme close", "wide shot", "wide establishing",
        "establishing shot", "medium shot", "full body", "full-body", "portrait",
        "from below", "from above", "from behind", "from the side",
        "low angle", "high angle", "eye level", "overhead", "bird's eye", "worm's eye",
        "looking up", "looking down"
    ];

    /// <summary>The camera instructions an operator wrote into their own intent, in vocabulary order.</summary>
    public static IReadOnlyList<string> CameraInstructionsIn(string? operatorIntent)
    {
        if (string.IsNullOrWhiteSpace(operatorIntent)) return [];
        return CameraPhrases
            .Where(phrase => operatorIntent.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// One camera DECISION (a shot size or an angle) and the wordings that express it, per the compiler standards §2.1.
    /// </summary>
    /// <remarks>
    /// The operator's intent is the decision, not the lexeme. Validating the literal phrase failed a legitimate compile
    /// (prompt record 178650a5): the operator wrote "from below his waist … low angle looking up", the prompt stated
    /// "low angle", and the check demanded the literal "from below" on top of it. "from below", "low angle" and
    /// "looking up" are the SAME camera instruction; repeating one of them is not a defect.
    /// </remarks>
    private sealed record CameraConcept(string Name, string[] OperatorWording, string[] AnyWording);

    private static readonly CameraConcept[] CameraConcepts =
    [
        new("shot size (close)", ["close up", "close-up", "close range", "extreme close", "tight shot"],
            ["close up", "close-up", "closeup", "close range", "tight shot", "tight framing", "extreme close", "macro"]),
        new("shot size (medium)", ["medium shot", "mid shot"], ["medium shot", "mid shot", "medium close"]),
        new("shot size (wide)", ["wide shot", "wide establishing", "establishing shot", "long shot", "wide angle"],
            ["wide shot", "wide establishing", "establishing shot", "long shot", "wide frame", "wide-angle", "wide angle"]),
        new("full body", ["full body", "full-body", "head to toe"], ["full body", "full-body", "head to toe", "whole body", "full length", "full-length"]),
        new("portrait", ["portrait"], ["portrait", "head and shoulders"]),
        new("angle from below", ["from below", "low angle", "worm's eye", "looking up", "from under"],
            ["from below", "low angle", "low-angle", "worm", "looking up", "looking upward", "upward view", "from under", "canted up"]),
        new("angle from above", ["from above", "high angle", "overhead", "bird's eye", "looking down"],
            ["from above", "high angle", "high-angle", "overhead", "bird's eye", "top-down", "looking down", "downward view"]),
        new("behind the subject", ["from behind", "rear view", "over the shoulder"],
            ["from behind", "rear view", "over the shoulder", "over-the-shoulder", "behind the subject"]),
        new("side on", ["from the side", "side view", "profile view"], ["from the side", "side view", "side-on", "profile view"]),
        new("eye level", ["eye level", "eye height"], ["eye level", "eye-level", "eye height"])
    ];

    /// <summary>
    /// The operator's own camera instruction must survive the compile (B-166).
    /// </summary>
    /// <remarks>
    /// Measured live 2026-10-21 (prompt record 8466c595): the operator wrote "close up tight shot viewed from below the
    /// mans waist looking up at his body and face", the reference-bound intent selected the ACTION-ONLY contract, and
    /// the compiled prompt contained no shot at all — it described the body plate instead. A contract that removes what
    /// the images supply must never remove the CAMERA, because no image supplies the camera.
    /// </remarks>
    public static IReadOnlyList<PromptContractViolation> ValidateOperatorCamera(string prompt, string? operatorIntent)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var required = CameraConcepts
            .Where(concept => Matches(operatorIntent, concept.OperatorWording))
            .ToList();
        if (required.Count == 0) return [];

        var missing = required
            .Where(concept => !Matches(prompt, concept.AnyWording))
            .Select(concept => concept.Name)
            .ToList();
        if (missing.Count == 0) return [];

        return
        [
            new PromptContractViolation(
                "the operator's camera instruction",
                null,
                "The request asked for: " + string.Join(", ", missing) + ". No reference image supplies the camera, so "
                + "the prompt must state it in its camera sentence, in any standard wording for that shot size or angle.",
                Blocking: true)
        ];
    }

    private static bool Matches(string? text, string[] wording)
        => !string.IsNullOrWhiteSpace(text)
           && wording.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A place mentioned with a <c>&lt;imageN&gt;</c> tag: "in a shed &lt;image2&gt;", "inside the trailer &lt;image3&gt;".
    /// </summary>
    /// <remarks>
    /// Deliberately narrow — the preposition, one to three words of noun phrase, then the tag — so it only fires on a
    /// place being POINTED AT an image. Relation phrases ("in front of", "in the hands of") are filtered by the caller,
    /// because those are facts about two people, not about the room.
    /// </remarks>
    private static readonly Regex PlaceTagMention = new(
        @"\b(?:in|inside|within)\s+(?:the|a|an)\s+(?<place>[A-Za-z][A-Za-z\-']{0,24}(?:\s+[A-Za-z][A-Za-z\-']{0,24}){0,2})\s*<image(?<tag>\d+)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Words that describe WHERE IN THE FRAME something sits rather than what KIND of place it is. "In the foreground
    /// &lt;image1&gt;" is a placement statement about a person, not a claim that the person's photograph is the room.
    /// </summary>
    private static readonly string[] FramePlacementWords =
    [
        "foreground", "midground", "background", "frame", "picture", "shot", "view", "left", "right",
        "centre", "center", "middle", "edge", "corner", "focus"
    ];

    /// <summary>
    /// The caption's <c>&lt;imageN&gt;</c> tags must name the images this request binds, at the numbers the request gives
    /// them — and a place must never be pointed at a person's tag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured 2026-10-27 on SceneImage a53cd560 (Qwen-Image-2.1, two bound bodies plus a bound location). The compiled
    /// caption read "…of a woman &lt;image1&gt; bending over a workbench in a shed &lt;image2&gt;…" while the render sent
    /// the location as &lt;image3&gt;: the compiler had copied the number from the anchored contract's worked example
    /// (written for a one-person scene, where the place really is &lt;image2&gt;) instead of from the REFERENCE IMAGES
    /// block in its own request. The finished prompt then named a person's body plate as "the shed" and, a few lines
    /// later, named &lt;image3&gt; as the place — two contradictory instructions about the same element of the picture.
    /// </para>
    /// <para>
    /// Pointing the place at the WRONG tag is BLOCKING, because the failure is silent: a caption whose tags disagree with
    /// its own sentences renders without error and simply produces a picture nobody asked for. Range alone is not enough
    /// — &lt;image2&gt; was in range and pointed at a real image, just the wrong one. Leaving the location tag out
    /// altogether is ADVISORY: a reference-bound User Intent draft is action-only by contract and legitimately never
    /// names the place, and a place named in words without a tag is still anchored correctly at render time.
    /// </para>
    /// <para>
    /// Only the Qwen-Image-2.1 family is checked: it is the family whose contract writes &lt;imageN&gt; tags into the
    /// CAPTION. The other families state the reference block at render time and their compiled prose legitimately
    /// carries no tags, so a missing tag there would be a false failure.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PromptContractViolation> ValidateReferenceTags(
        string prompt,
        IReadOnlyList<ReferenceApplicationSelection>? bindings,
        SceneImageModelFamily family)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (family != SceneImageModelFamily.QwenImage21)
        {
            return [];
        }

        var roles = ReferenceRoleClauses.RolesFor(bindings);
        if (!ReferenceRoleClauses.SendsTags(roles.Count))
        {
            // One image in total is never numbered (the vendor's N = 1 rule), so there is no tag to check.
            return [];
        }

        var locationIndex = -1;
        for (var position = 0; position < roles.Count; position++)
        {
            if (roles[position].SlotKind == ImageStepSlotKind.Location)
            {
                locationIndex = position;
                break;
            }
        }

        if (locationIndex < 0)
        {
            // No location reference bound: the setting is the prose's own business, as it is for a text-only model.
            return [];
        }

        var violations = new List<PromptContractViolation>();
        var locationTag = ReferenceRoleClauses.TagFor(locationIndex + 1);

        foreach (Match match in PlaceTagMention.Matches(prompt))
        {
            var place = match.Groups["place"].Value.Trim();
            if (place.Contains(" of ", StringComparison.OrdinalIgnoreCase)
                || IsFramePlacement(place))
            {
                continue;
            }

            var number = int.Parse(match.Groups["tag"].Value, CultureInfo.InvariantCulture);
            var index = number - 1;
            if (index < 0 || index >= roles.Count || roles[index].SlotKind == ImageStepSlotKind.Location)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(roles[index].Label)
                ? "a person reference"
                : $"the {roles[index].Label!.Trim()} reference";
            violations.Add(new PromptContractViolation(
                "which image the setting points at",
                roles[index].SubjectKey,
                $"The prompt puts the setting \"{place}\" on <image{number}>, but <image{number}> is {label}, not the "
                + $"room. Name the place with the LOCATION reference's tag, {locationTag}, and never with a person's tag: "
                + "the image model takes the room from whichever image the caption points at.",
                Blocking: true));
        }

        if (!prompt.Contains(locationTag, StringComparison.Ordinal)
            && Regex.IsMatch(prompt, @"<image\d+>"))
        {
            // ADVISORY, not blocking. A reference-bound User Intent draft is ACTION ONLY by contract — the location image
            // supplies the room, so a caption that never names the place is correct there, and refusing it would fail
            // every such compile. A place named in words without any tag is survivable too: the render prepends the
            // location anchor and appends the role block, which together point the room at the right image. What is NOT
            // survivable is a place pointed at the WRONG tag, and that is the blocking check above.
            violations.Add(new PromptContractViolation(
                "which image the setting points at",
                null,
                $"The prompt never names the LOCATION reference {locationTag}. That image supplies the setting, so where "
                + $"the caption names the place it should point at it by that tag; a caption that describes the place "
                + $"without naming {locationTag} leaves the render free to take the room from a person reference."));
        }

        return violations;
    }

    private static bool IsFramePlacement(string place)
        => FramePlacementWords.Any(word => place.StartsWith(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The POV holder is the CAMERA, so their name has no place in the prompt (B-166).
    /// </summary>
    /// <remarks>
    /// Measured 2026-10-21 on the Becky POV intent path: roughly one generated prompt in four came back naming her
    /// ("as seen by Becky"), which describes a person the picture does not contain. The rule is stated to the compiler
    /// and checked here, because a rule alone was being obeyed three times out of four.
    /// </remarks>
    public static IReadOnlyList<PromptContractViolation> ValidatePovNotNamed(string prompt, string? pov)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (string.IsNullOrWhiteSpace(pov)) return [];
        if (!Names(prompt, pov)) return [];

        return
        [
            new PromptContractViolation(
                "the camera holder's name",
                pov,
                $"The prompt names {pov}, who is the camera and is not in the picture. Remove the name and describe only "
                + "what is in front of that camera.",
                Blocking: true)
        ];
    }

    /// <summary>
    /// The corrective instruction appended to the user prompt for the single retry (D2). Empty when nothing BLOCKING
    /// was found, so an advisory note never costs a second generation or lengthens the prompt.
    /// </summary>
    public static string BuildCorrectiveInstruction(IReadOnlyList<PromptContractViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        var blocking = violations.Where(violation => violation.Blocking).ToList();
        if (blocking.Count == 0)
        {
            return string.Empty;
        }

        var lines = blocking.Select(violation =>
            $"- {violation.Detail}" + (violation.Person is null ? string.Empty : $" (person: {violation.Person})"));

        return """

            CORRECTIVE PASS — the previous attempt violated the request contract. Rewrite the prompt so that EVERY
            point below is satisfied, keeping everything the request already required. Keep the result within the
            length the request states; do not add new detail to compensate.
            """ + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The caption's SHAPE: no bullet/numbered list, and no per-person element recap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every natural-language family contract says "one continuous paragraph; do not add line breaks", and a small
    /// compiler model still returned a three-part answer: the caption, then a semicolon list of one person's elements
    /// ("Becky: bent over the workbench …; completely naked …"), then the framing line. The list restates the paragraph
    /// in the image model's input, and its values had already drifted from it — the recap said the subject was
    /// "positioned directly behind the camera" while the paragraph described her in front of it (SceneImage
    /// 3bbad8c9, 2026-10-23).
    /// </para>
    /// <para>
    /// <b>Line breaks alone are NOT a defect.</b> The parser collapses a produced caption to one paragraph before it
    /// reaches this check, because an image prompt's line breaks carry no meaning: a five-line answer whose five lines
    /// are five sentences is the right content in a shape the image model ignores. Refusing it failed a live compile
    /// with "one corrective pass did not fix it" (reported 2026-10-24) even though nothing was wrong with the caption.
    /// What is still refused is the CONTENT shape — an explicit bullet or numbering marker, or a per-person element
    /// recap — since those are re-statements of the paragraph, not wrapping.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PromptContractViolation> ValidateCaptionShape(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var single = CollapseToParagraph(prompt);
        var marker = ListMarker(single);
        if (marker is not null)
        {
            return
            [
                new PromptContractViolation(
                    "the caption's shape",
                    null,
                    "The prompt must be ONE continuous paragraph of prose, but it contains a list item "
                    + $"(\"{Shorten(marker)}\"). Return the caption as a single paragraph: no bullet points, no "
                    + "numbering, no per-person summary.",
                    Blocking: true)
            ];
        }

        var recap = ElementRecap(single);
        if (recap is not null)
        {
            return
            [
                new PromptContractViolation(
                    "the caption's shape",
                    null,
                    "The prompt must be ONE continuous paragraph, but it contains the element recap "
                    + $"\"{Shorten(recap)}\", which repeats facts the paragraph already states. Return the caption as "
                    + "a single paragraph: no line breaks, no bullet lists, no per-person summary.",
                    Blocking: true)
            ];
        }

        var key = ParticipantKey(single);
        return key is null
            ? []
            :
            [
                new PromptContractViolation(
                    "the caption's wording",
                    null,
                    "The prompt contains the internal participant key "
                    + $"\"{Shorten(key)}\". A profile key is not a word the image model knows, and a parenthesised one "
                    + "is weight syntax in a diffusion prompt. Name the person instead.",
                    Blocking: true)
            ];
    }

    /// <summary>
    /// The first participant key (<c>p0</c>, <c>p1</c>, …) that reached the caption.
    /// </summary>
    /// <remarks>
    /// A key identifies a body inside the brief; it is never a word of the caption. One leaked live (SceneImage
    /// 57fcc461, 2026-10-25: "his penis buried to the root (p0)") from a contact note, and in ComfyUI syntax that
    /// parenthesis weights the token instead of being ignored — so this is refused structurally rather than left to
    /// the caption writer's judgement.
    /// </remarks>
    private static string? ParticipantKey(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(?<![A-Za-z0-9])p\d{1,2}(?![A-Za-z0-9])");
        if (!match.Success)
        {
            return null;
        }

        var start = Math.Max(0, match.Index - 20);
        return text[start..Math.Min(text.Length, match.Index + match.Length + 20)];
    }

    /// <summary>Collapses newlines and whitespace runs, so line shape cannot decide anything here.</summary>
    private static string CollapseToParagraph(string prompt)
        => System.Text.RegularExpressions.Regex.Replace(
            prompt.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' '),
            @"\s{2,}",
            " ").Trim();

    /// <summary>
    /// The first explicit list item — a bullet or a numbered marker at the start of the text or of a sentence. Prose
    /// does not open a sentence with one; a list does.
    /// </summary>
    private static string? ListMarker(string text)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
            text, @"(?:^|(?<=[.!?;]\s))([-*•–]|\d{1,2}[.)])\s"))
        {
            var start = match.Index;
            return text[start..Math.Min(text.Length, start + 30)];
        }

        return null;
    }

    /// <summary>
    /// The first ELEMENT RECAP in the text — a bare name, a colon, then semicolon-separated elements ("Becky: bent over
    /// the workbench; facing away; naked"). Prose does not open a clause that way; the element list does.
    /// </summary>
    private static string? ElementRecap(string text)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
            text, @"(?:^|(?<=[.!?]\s))([A-Z][\w'-]{1,20}):\s"))
        {
            var start = match.Index;
            var window = text[start..Math.Min(text.Length, start + 120)];
            var colon = window.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0) continue;
            var tail = window[(colon + 1)..];
            if (tail.Contains(';', StringComparison.Ordinal)) return window.Split(';')[0].TrimEnd('.');
        }

        return null;
    }

    /// <summary>
    /// A caption may not describe what is OUTSIDE the frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Her head is not visible due to being positioned below the frame line where the image cuts off at about waist
    /// height" was a caption for a close overhead shot of a body that was entirely frameable (SceneImage 8bb09d13,
    /// 2026-10-25). The image model obeyed the sentence and cropped her. A shot size describes how much of the picture a
    /// subject occupies — it is never an instruction to remove part of somebody, and naming the absence is what turns a
    /// framing choice into a mutilation.
    /// </para>
    /// <para>
    /// What IS in frame stays describable, including a back instead of a face ("her back is to the lens") — the rule
    /// refuses statements about the EDGE of the picture, not about which way somebody is turned.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PromptContractViolation> ValidateNoFrameExclusions(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var match = FrameExclusion.Match(prompt);
        if (!match.Success)
        {
            return [];
        }

        return
        [
            new PromptContractViolation(
                "the frame's inclusion",
                null,
                $"The caption describes what is outside the picture (\"{Shorten(match.Value)}\"). A shot size never removes "
                + "a body part: state what IS in frame and never mention a crop, a cut-off edge, or a part that is not "
                + "shown. Every person in frame is in frame whole.",
                Blocking: true)
        ];
    }

    /// <summary>
    /// Phrasings that state a crop. Deliberately about the EDGE of the picture — a body part that is hidden behind a
    /// bench is a recorded occlusion, not a frame exclusion, and reads differently.
    /// </summary>
    private static readonly Regex FrameExclusion = new(
        @"\b(?:not\s+visible\b|is\s+not\s+shown\b|are\s+not\s+shown\b|cuts?\s+off\b|cut\s+off\b|below\s+the\s+frame"
        + @"|above\s+the\s+frame|outside\s+the\s+(?:frame|picture)|out\s+of\s+(?:frame|shot)|off[-\s]?screen"
        + @"|frame\s+line|edge\s+of\s+the\s+(?:frame|picture)|hidden\s+by\s+the\s+(?:frame|crop))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The camera holder's visible body has to appear INSIDE the caption.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The operator's note about the holder ("hands on her hips") was previously appended to the finished prompt, and the
    /// compiled caption described the woman alone — the render followed the caption (SceneImage 3bbad8c9, 2026-10-23).
    /// Once the content moved into the caption's input, the only thing keeping it there is this check.
    /// </para>
    /// <para>
    /// The note is SHORT because it is not a prompt: the operator types the fact they want filled in, and expanding it
    /// into framing language is the compiler's job. So the check has to match the note's weight rather than its wording:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>A sentence</b> (three or more distinctive words) is checked by COVERAGE of the operator's own words, because a
    /// sentence that vanishes is certainly absent — and a paraphrase still carries some of them.
    /// </description></item>
    /// <item><description>
    /// <b>A terse note</b> ("hands on her hips") cannot be word-matched without punishing a legitimate re-phrasing
    /// ("his palms at her waist"), so it is checked for what the operator actually asked for: ANY off-frame body named in
    /// the caption. Zero means nobody is behind her, which is the failure being guarded.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static IReadOnlyList<PromptContractViolation> ValidateCameraHolderBody(string prompt, string? parts)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (string.IsNullOrWhiteSpace(parts))
        {
            return [];
        }

        var distinctive = parts
            .Split([' ', ',', ';', '.', ':', '—', '-', '(', ')', '"', '\'', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim().ToLowerInvariant())
            .Where(word => word.Length >= 4 && !CaptureStopWords.Contains(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctive.Count == 0)
        {
            return [];
        }

        // The parts must be IN the caption (coverage), and they must belong to a SECOND PERSON (attribution). Coverage
        // alone passed a caption that said "hands clenching tightly around her thighs" — the subject's own hands — and
        // another that mentioned no hands at all (SceneImages f2acd4fb / 34b95f2f, 2026-10-25). A part word without an
        // owner is the failure this guards. A terse note cannot be word-matched without punishing a legitimate
        // re-phrasing, so for it BOTH questions are answered by the same attribution test.
        var attributed = OtherBodyInFrame.IsMatch(prompt);
        var covered = distinctive.Count >= 3
            ? distinctive.Any(word => prompt.Contains(word, StringComparison.OrdinalIgnoreCase))
            : attributed;

        if (covered && attributed)
        {
            return [];
        }

        if (!covered)
        {
            return
            [
                new PromptContractViolation(
                    "the camera holder's visible body",
                    null,
                    "The request states that this first-person frame SHOWS the camera holder's body ("
                    + Shorten(parts)
                    + "), but the caption never mentions it. State it INSIDE the caption, in the same paragraph as "
                    + "the in-frame subject, as a second, separate person behind them — not as a sentence added at the end.",
                    Blocking: true)
            ];
        }

        return
        [
            new PromptContractViolation(
                "who the visible body belongs to",
                null,
                "The caption mentions the camera holder's body ("
                + Shorten(parts)
                + ") but never says it belongs to a SECOND PERSON: it reads as the pictured subject's own body. Write it "
                + "as someone else's — \"a man stands behind her, his hands on her thighs\".",
                Blocking: true)
        ];
    }

    /// <summary>
    /// A participant caption must be written in the THIRD person: the camera is never "I".
    /// </summary>
    /// <remarks>
    /// The projection's own system rule already says so ("Write it in the THIRD PERSON: never I, me, my, we or our"),
    /// and the compiler still produced "In front of me is Becky … my eye-level camera as I stand behind her … near the
    /// tin wall behind us" (SceneImage prompt 50e778e5, 2026-10-25). A first-person caption reads as a selfie to the
    /// image model, which is the opposite of a photograph taken from that person's eyes, so it is refused
    /// structurally rather than trusted to the rule.
    /// </remarks>
    public static IReadOnlyList<PromptContractViolation> ValidateThirdPerson(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var match = FirstPerson.IsMatch(prompt)
            ? FirstPerson.Match(prompt)
            : null;
        if (match is null || !match.Success)
        {
            return [];
        }

        return
        [
            new PromptContractViolation(
                "the caption's point of view",
                null,
                $"The caption is written in the FIRST person (\"{Shorten(match.Value)}\"), but it describes a photograph "
                + "taken from that person's eyes: the camera is never \"I\". Rewrite it in the third person — describe "
                + "what the picture shows, not what the person taking it sees.",
                Blocking: true)
        ];
    }

    /// <summary>
    /// First-person pronouns that make a shot description read as a selfie. "I" is matched case-sensitively because the
    /// lowercase word is not a pronoun; the others are word-bounded, so "my" inside a longer word is never matched.
    /// </summary>
    private static readonly Regex FirstPerson = new(
        @"\bI\b|\b(?:me|my|mine|myself|we|our|ours)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Whether the caption shows a SECOND body — the holder's — rather than only the in-frame subject's own.
    /// </summary>
    /// <remarks>
    /// A terse operator note ("hands on her hips") is matched this way instead of by word coverage, because punishing a
    /// legitimate re-phrasing ("his palms at her waist") would fail a compile over wording the operator never wrote. A
    /// bare list of body nouns is not enough on its own: "her bare back toward the lens" is the SUBJECT's body and passed
    /// that test, which is the exact failure being guarded. What makes the holder present is a body part attributed to
    /// someone else ("his forearms", "a man's palms") or a second person stated outright ("a man behind her").
    /// </remarks>
    private static readonly Regex OtherBodyInFrame = new(
        @"\b(?:(?:his|their)\b[^.]{0,40}?\b(?:hands?|palms?|fingers?|arms?|forearms?|torso|chest|hips?|thighs?|"
        + @"cock|penis|groin|pelvis|feet|foot|legs?|knees?)\b"
        + @"|a\s+man\b|the\s+man\b|a\s+man's\b|a\s+second\s+(?:person|body)\b|another\s+person\b"
        + @"|a\s+pair\s+of\s+hands\b|someone\s+behind\b|a\s+body\s+behind\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Words that carry no content of their own, so matching one proves nothing about the holder's body.</summary>
    private static readonly HashSet<string> CaptureStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "above", "after", "again", "against", "another", "behind", "below", "between", "clearly", "could",
        "frame", "from", "their", "there", "these", "those", "through", "under", "where", "which", "while", "would",
        "person", "people", "visible", "camera", "separate", "entering", "standing", "running", "rests",
        "never", "shows", "shown", "same", "only", "theirs", "also", "more", "less", "make", "show", "nsfw", "add",
        "with", "onto", "over", "hers", "hims", "them", "that", "this", "some", "very", "just", "like", "want"
    };

    private static string Shorten(string text)
        => text.Length <= 70 ? text : text[..70].TrimEnd() + "…";

    private static HashSet<string> ReferencedActorKeys(IReadOnlyList<ReferenceApplicationSelection>? bindings)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (bindings is null) return keys;
        foreach (var binding in bindings)
        {
            if (!string.IsNullOrWhiteSpace(binding.ActorKey)) keys.Add(binding.ActorKey.Trim());
            if (!string.IsNullOrWhiteSpace(binding.ReferenceLabel)) keys.Add(binding.ReferenceLabel.Trim());
        }

        return keys;
    }

    /// <summary>Whether the prompt names this person as a whole word (not as a fragment of another word).</summary>
    private static bool Names(string prompt, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Regex.IsMatch(
            prompt,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name.Trim())}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Whether the prompt contains at least one MEANINGFUL word of the setting kind.</summary>
    private static bool ContainsAnyWord(string prompt, string text)
    {
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (word.Length < 4) continue;
            if (StopWords.Contains(word)) continue;
            if (prompt.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>Words that name no place, so matching one proves nothing about the setting being named.</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "with", "from", "into", "over", "near", "that", "this", "some", "their", "there",
        "behind", "above", "below", "under", "left", "right", "center", "centre", "foreground", "background",
        "midground", "stands", "standing", "kneels", "kneeling"
    };
}
