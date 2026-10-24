using System.Text.RegularExpressions;
using DreamGenClone.Domain.RolePlay;
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
    /// The caption's SHAPE: one continuous paragraph, with no per-person element recap.
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
    /// This is certainly wrong rather than a prose judgement: the contract states the shape, and a second line is not
    /// a paraphrase of it. It therefore blocks, and the corrective pass restates the shape rule.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PromptContractViolation> ValidateCaptionShape(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var lines = prompt
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length <= 1)
        {
            return [];
        }

        var recap = lines.FirstOrDefault(IsElementRecapLine);
        return
        [
            new PromptContractViolation(
                "the caption's shape",
                null,
                $"The prompt must be ONE continuous paragraph, but it came back as {lines.Length} separate lines"
                + (recap is null
                    ? string.Empty
                    : $", including the element recap \"{Shorten(recap)}\", which repeats facts the paragraph already states")
                + ". Return the caption as a single paragraph: no line breaks, no bullet lists, no per-person summary.",
                Blocking: true)
        ];
    }

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

        var present = distinctive.Count >= 3
            ? distinctive.Any(word => prompt.Contains(word, StringComparison.OrdinalIgnoreCase))
            : OtherBodyInFrame.IsMatch(prompt);

        if (present)
        {
            return [];
        }

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

    /// <summary>
    /// Whether a line is an ELEMENT RECAP — a bare name, a colon, then semicolon-separated elements ("Becky: bent over
    /// the workbench; facing away; naked"). Prose does not open a sentence that way; the element list does.
    /// </summary>
    private static bool IsElementRecapLine(string line)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon is <= 0 or > 40)
        {
            return false;
        }

        var head = line[..colon].Trim();
        if (head.Length == 0 || head.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        var tail = line[(colon + 1)..];
        return tail.Contains(';', StringComparison.Ordinal) || tail.Count(character => character == ':') >= 2;
    }

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
