using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The deterministic MiniMax H3 (Ref2VA) prompt compiler: operator intent plus ordered reference bindings become the
/// six-section document the node documents (B-152, C-3). No ComfyUI and no model call is involved, so every rule is
/// unit-testable.
/// </summary>
/// <remarks>
/// <para>
/// The rule set is <c>specs/Planning/B-152-scene-video-composer/COMPILER-RESEARCH.md</c> §13 (33 rules); the official
/// worked example in its §17.1 is the golden fixture. Rules are enforced in CODE as well as by the validator: the
/// compiler emits untimed <c>[Shot 1]</c>, strictly increasing cut times, closed-vocabulary camera phrasing, stable
/// <c>(Sx)</c> ids assigned by vocal-event order, verbatim quoted on-screen text, and the <c>N/A</c> sentinel.
/// </para>
/// <para>
/// A failing rule is a FAILURE that blocks queueing, never a warning: the cheapest place to catch a bad document is
/// here, and the most expensive is after a ~25 minute render.
/// </para>
/// </remarks>
public sealed class MiniMaxH3SceneVideoPromptCompiler
{
    /// <summary>Stable identity of the compiler, carried with every render for provenance.</summary>
    public const string CompilerKey = "minimax-h3-ref2va-six-section";

    /// <summary>
    /// Version of the rule set this compiler implements. Bump when a rule changes.
    /// 1.1.0 - rule severity split (only malformed-document rules block queueing) and the N/A sentinel for an
    /// unauthored soundscape or score, which changes the emitted document.
    /// </summary>
    public const string CompilerVersion = "1.1.0";

    /// <summary>The documented sentinel for "there is deliberately none of this".</summary>
    public const string NotApplicableSentinel = "N/A";

    private static readonly string[] CameraMotionTypes =
    [
        "Zoom In", "Zoom Out", "Push In", "Pull Out", "Pan Left", "Pan Right",
        "Truck Left", "Truck Right", "Tilt Up", "Tilt Down", "Pedestal Up", "Pedestal Down",
        "Arc Shot", "Tracking Shot", "Static Shot", "Shake Slightly", "Shake Strongly",
        "POV", "Roll Clockwise", "Roll Counterclockwise"
    ];

    private static readonly string[] VisibleRetentionMarkers =
        ["fully_preserved", "partially_preserved", "attribute_transfer", "weak_reference"];

    private static readonly string[] AbstractDescriptors =
    [
        "cinematic", "beautiful", "stunning", "gorgeous", "atmospheric", "moody", "dramatic",
        "epic", "tasteful", "artistic", "aesthetic", "vibe", "beautifully"
    ];

    private static readonly string[] ConcreteAnchors =
    [
        "camera", "shot", "frame", "close-up", "medium", "wide", "angle", "lens", "focus",
        "wall", "floor", "ceiling", "window", "door", "table", "chair", "sofa", "bed", "sheet",
        "fabric", "cloth", "hair", "skin", "hand", "hands", "arm", "face", "eyes", "lips", "mouth",
        "light", "lamp", "shadow", "sun", "neon", "sign", "street", "room", "sky", "water", "glass",
        "metal", "wood", "stone", "leather", "silk", "denim", "sound", "footstep", "breath", "voice"
    ];

    private static readonly string[] NegativePhrases =
    [
        "no text", "no watermark", "no ", "not ", "without ", "avoid ", "avoids ", "don't ", "do not ",
        "never ", "nothing ", "none ", "cannot ", "can't ", "won't ", "isn't ", "aren't ", "doesn't "
    ];

    private static readonly string[] MoodWords =
    [
        "beautiful", "sad", "happy", "emotional", "tense", "romantic", "moody", "triumphant",
        "melancholy", "uplifting", "dramatic", "scary", "joyful", "somber", "nostalgic"
    ];

    private static readonly string[] DiegeticMusicWords =
    ["radio", "television", "tv", "phone speaker", "record player", "live band", "juke box", "jukebox"];

    public SceneVideoCompilationResult Compile(SceneVideoCompilationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var labels = BuildLabels(input);
        var sections = new Sections(
            SubjectDefinitions: BuildSubjectDefinitions(input, labels),
            Summary: BuildSummary(input, labels),
            RetentionAnalysis: BuildRetentionAnalysis(input, labels),
            DetailedDescription: BuildDetailedDescription(input, labels),
            Soundscape: BuildSoundscape(input),
            NonDiegeticMusic: BuildMusic(input));

        var prompt = Assemble(sections);
        var findings = Validate(input, labels, sections, prompt);

        return new SceneVideoCompilationResult(prompt, findings, CompilerKey, CompilerVersion);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Label assignment (rules 4, 7, 27, 30)
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// One picture label per reference, in the operator's order - the order IS the node's <c>ref_images</c> slot
    /// order, so reordering renumbers. Subject labels are assigned in the order the subject-defining references
    /// appear, so the same inputs always produce the same document.
    /// </summary>
    private sealed record LabelPlan(
        IReadOnlyList<ReferenceLabel> References,
        IReadOnlyList<SubjectLabel> Subjects);

    private sealed record ReferenceLabel(int PictureNumber, SceneVideoReference Reference);

    private sealed record SubjectLabel(int SubjectNumber, int PictureNumber, SceneVideoReference Reference);

    private static LabelPlan BuildLabels(SceneVideoCompilationInput input)
    {
        var references = input.References
            .Select((reference, index) => new ReferenceLabel(index + 1, reference))
            .ToList();

        var subjects = new List<SubjectLabel>();
        foreach (var reference in references)
        {
            if (reference.Reference.Role is SceneVideoReferenceRole.SubjectDefinition
                or SceneVideoReferenceRole.CharacterSheet)
            {
                subjects.Add(new SubjectLabel(subjects.Count + 1, reference.PictureNumber, reference.Reference));
            }
        }

        return new LabelPlan(references, subjects);
    }

    private static string SubjectLabelFor(LabelPlan labels, int pictureNumber) =>
        labels.Subjects.FirstOrDefault(subject => subject.PictureNumber == pictureNumber) is { } subject
            ? $"<Subject {subject.SubjectNumber}>"
            : $"<Picture {pictureNumber}>";

    // ---------------------------------------------------------------------------------------------------------
    // Section builders
    // ---------------------------------------------------------------------------------------------------------

    private static string BuildSubjectDefinitions(SceneVideoCompilationInput input, LabelPlan labels)
    {
        var lines = new List<string>();

        foreach (var reference in labels.References)
        {
            var description = string.IsNullOrWhiteSpace(reference.Reference.SubjectDescription)
                ? "the referenced content"
                : reference.Reference.SubjectDescription.Trim();

            switch (reference.Reference.Role)
            {
                case SceneVideoReferenceRole.SubjectDefinition:
                    lines.Add(
                        $"{SubjectLabelFor(labels, reference.PictureNumber)} is {description} in "
                        + $"<Picture {reference.PictureNumber}>. Reference role: subject definition "
                        + $"({RetentionMarker(reference.Reference)}: referenced content).");
                    break;

                case SceneVideoReferenceRole.CharacterSheet:
                    lines.Add(
                        $"{SubjectLabelFor(labels, reference.PictureNumber)} is {description}, shown in "
                        + $"<Picture {reference.PictureNumber}>, a reference sheet whose panels are numbered from 1 "
                        + "in reading order.");
                    lines.Add(
                        $"<Picture {reference.PictureNumber}> (character sheet): {RetentionMarker(reference.Reference)} "
                        + $"- the sheet's numbered panels each show one view of "
                        + $"{SubjectLabelFor(labels, reference.PictureNumber)}; shots cite the panel they use.");
                    break;

                default:
                    lines.Add(
                        $"<Picture {reference.PictureNumber}> ([Shot 1] first frame): "
                        + $"{RetentionMarker(reference.Reference)} - {description}.");
                    break;
            }
        }

        return lines.Count == 0 ? string.Empty : string.Join("\n", lines);
    }

    private static string BuildSummary(SceneVideoCompilationInput input, LabelPlan labels)
    {
        var tokens = new List<string>();
        if (labels.References.Count > 0)
        {
            tokens.Add("reference generation");
        }

        if (input.References.Any(reference => reference.Role == SceneVideoReferenceRole.FrameAnchor))
        {
            tokens.Add("keyframe completion");
        }

        var duration = (input.FrameLength / (double)input.Fps).ToString("0.0", CultureInfo.InvariantCulture);
        var subjects = labels.Subjects.Count == 0
            ? "the supplied references"
            : string.Join(", ", labels.Subjects.Select(subject => $"<Subject {subject.SubjectNumber}>"));

        var lead = FirstSentence(input.SceneDescription);
        var summary =
            $"[{string.Join(" + ", tokens)}] The target video is rendered from "
            + $"{DescribePi(labels.References.Count)} and carries {subjects}. It runs {duration} seconds at "
            + $"{input.Fps} fps. {lead}";

        return summary.Trim();
    }

    private static string DescribePi(int count) => count switch
    {
        0 => "no reference images",
        1 => "<Picture 1>",
        _ => $"<Picture 1> through <Picture {count}>"
    };

    private static string BuildRetentionAnalysis(SceneVideoCompilationInput input, LabelPlan labels)
    {
        var lines = new List<string>();

        foreach (var subject in labels.Subjects)
        {
            var shots = ShotsCiting(input, SubjectLabelFor(labels, subject.PictureNumber));
            lines.Add(
                $"{SubjectLabelFor(labels, subject.PictureNumber)} ({DescribeAppearance(shots)}): "
                + $"{RetentionMarker(subject.Reference)} - the referenced subject's defining features are retained "
                + "as described in the subject definitions.");
        }

        foreach (var reference in labels.References.Where(reference =>
                     reference.Reference.Role == SceneVideoReferenceRole.FrameAnchor))
        {
            var shots = ShotsCiting(input, $"<Picture {reference.PictureNumber}>");
            lines.Add(
                $"<Picture {reference.PictureNumber}> ({DescribeAppearance(shots)}): "
                + $"{RetentionMarker(reference.Reference)} - the frame anchor's composition is followed at the shot "
                + "it anchors.");
        }

        return lines.Count == 0 ? string.Empty : string.Join("\n", lines);
    }

    private static string DescribeAppearance(IReadOnlyList<int> shots) =>
        shots.Count == 0
            ? "appears throughout"
            : $"appears in {string.Join(", ", shots.Select(shot => $"[Shot {shot}]"))}";

    private static List<int> ShotsCiting(SceneVideoCompilationInput input, string label)
    {
        var shots = new List<int>();
        foreach (var shot in input.Shots.OrderBy(shot => shot.Number))
        {
            if (shot.Description.Contains(label, StringComparison.Ordinal))
            {
                shots.Add(shot.Number);
            }
        }

        return shots;
    }

    private static string BuildDetailedDescription(SceneVideoCompilationInput input, LabelPlan labels)
    {
        var builder = new StringBuilder();

        // Rule 10: the style declaration precedes [Shot 1].
        if (!string.IsNullOrWhiteSpace(input.StyleDeclaration))
        {
            AppendSentenceBlock(builder, input.StyleDeclaration);
        }

        // The scene/action body follows the style and precedes the shot list (spec §12: the description input IS the
        // body of the document). Shots then add the cut structure and the camera phrasing.
        if (!string.IsNullOrWhiteSpace(input.SceneDescription))
        {
            AppendSentenceBlock(builder, input.SceneDescription);
        }

        var speakers = AssignSpeakerIds(input);

        foreach (var shot in input.Shots.OrderBy(shot => shot.Number))
        {
            var paragraph = new StringBuilder();
            paragraph.Append(shot.Number == 1
                ? "[Shot 1] "
                : $"[Shot {shot.Number}] At {shot.CutTime}, ");

            paragraph.Append(shot.Description.Trim());

            var camera = CameraClause(shot);
            if (camera.Length > 0)
            {
                paragraph.Append(' ');
                paragraph.Append(camera);
            }

            foreach (var text in input.OnScreenText.Where(text => text.ShotNumber == shot.Number))
            {
                paragraph.Append(' ');
                paragraph.Append($"Text visible in frame reads \"{text.Text}\".");
            }

            foreach (var line in input.Dialogue
                         .Where(line => line.ShotNumber == shot.Number)
                         .OrderBy(line => line.Order))
            {
                paragraph.Append(' ');
                paragraph.Append(RenderDialogue(input, line, speakers, labels));
            }

            builder.Append(paragraph.ToString().Trim());
            builder.Append('\n');
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Appends one block as its own sentence(s). The terminator matters: two blocks merged into one "sentence" would
    /// hide an abstract-only sentence behind the concrete detail of the block before it (rules 32 and 33).
    /// </summary>
    private static void AppendSentenceBlock(StringBuilder builder, string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        builder.Append(trimmed);
        if (char.IsLetterOrDigit(trimmed[^1]))
        {
            builder.Append('.');
        }

        builder.Append('\n');
    }

    private static string CameraClause(SceneVideoShot shot)
    {
        if (string.IsNullOrWhiteSpace(shot.CameraMotionType))
        {
            return string.Empty;
        }

        var clause = shot.CameraMotionType.Trim() switch
        {
            "Zoom In" => "The camera zooms in",
            "Zoom Out" => "The camera zooms out",
            "Push In" => "The camera pushes in",
            "Pull Out" => "The camera pulls out",
            "Pan Left" => "The camera pans left",
            "Pan Right" => "The camera pans right",
            "Truck Left" => "The camera trucks left",
            "Truck Right" => "The camera trucks right",
            "Tilt Up" => "The camera tilts up",
            "Tilt Down" => "The camera tilts down",
            "Pedestal Up" => "The camera pedestals up",
            "Pedestal Down" => "The camera pedestals down",
            "Arc Shot" => "The camera arcs around the subject",
            "Tracking Shot" => "The camera tracks the subject",
            "Static Shot" => "The camera holds still",
            "Shake Slightly" => "The camera shakes slightly",
            "Shake Strongly" => "The camera shakes strongly",
            "POV" => "The shot is framed as POV",
            "Roll Clockwise" => "The camera rolls clockwise",
            "Roll Counterclockwise" => "The camera rolls counterclockwise",
            _ => string.Empty
        };

        if (clause.Length == 0)
        {
            return string.Empty;
        }

        var suffix = new StringBuilder(clause);
        if (string.Equals(shot.CameraAmplitude, "small", StringComparison.OrdinalIgnoreCase))
        {
            suffix.Append(" with small amplitude");
        }
        else if (string.Equals(shot.CameraAmplitude, "large", StringComparison.OrdinalIgnoreCase))
        {
            suffix.Append(" with large amplitude");
        }

        if (string.Equals(shot.CameraSpeed, "slow", StringComparison.OrdinalIgnoreCase))
        {
            suffix.Append(" at slow speed");
        }
        else if (string.Equals(shot.CameraSpeed, "fast", StringComparison.OrdinalIgnoreCase))
        {
            suffix.Append(" at fast speed");
        }

        suffix.Append('.');
        return suffix.ToString();
    }

    /// <summary>
    /// Stable <c>(Sx)</c> ids assigned by VOCAL-EVENT order, not subject order: a subject introduced first but
    /// speaking second is <c>(S2)</c>. Deterministic for the same inputs (rule 16 / §14).
    /// </summary>
    private static Dictionary<string, int> AssignSpeakerIds(SceneVideoCompilationInput input)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in input.Dialogue.OrderBy(line => line.Order))
        {
            var key = line.SpeakerName.Trim();
            if (key.Length > 0 && !ids.ContainsKey(key))
            {
                ids[key] = ids.Count + 1;
            }
        }

        return ids;
    }

    private static int SpeakerIdOf(Dictionary<string, int> speakers, string name) =>
        speakers.TryGetValue(name.Trim(), out var id) ? id : 0;

    private static bool IsFirstAppearance(SceneVideoCompilationInput input, SceneVideoDialogueLine line)
    {
        var ordered = input.Dialogue.OrderBy(candidate => candidate.Order).ToList();
        var index = ordered.FindIndex(candidate => ReferenceEquals(candidate, line));
        return index < 0
            || ordered.Take(index).All(candidate =>
                !string.Equals(candidate.SpeakerName.Trim(), line.SpeakerName.Trim(), StringComparison.Ordinal));
    }

    private static string RenderDialogue(
        SceneVideoCompilationInput input,
        SceneVideoDialogueLine line,
        Dictionary<string, int> speakers,
        LabelPlan labels)
    {
        var id = SpeakerIdOf(speakers, line.SpeakerName);
        var speaker = string.IsNullOrWhiteSpace(line.SubjectLabel)
            ? $"{line.SpeakerName.Trim()} (S{id})"
            : $"{line.SubjectLabel.Trim()} (S{id})";

        var builder = new StringBuilder();
        if (line.Continuity == SceneVideoCutContinuity.CrossesCut)
        {
            builder.Append("<scenetrans> ");
        }

        builder.Append(speaker);

        if (IsFirstAppearance(input, line))
        {
            builder.Append($", {line.IdentityInfo.Trim()},");
        }

        if (line.OffScreen && !line.Voiceover)
        {
            builder.Append(" off-screen");
        }

        if (line.Voiceover)
        {
            builder.Append($" says in an off-screen voiceover, {line.Delivery.Trim()}, ");
        }
        else
        {
            builder.Append($" says, {line.Delivery.Trim()}, ");
        }

        builder.Append($"<d>[{line.LanguageCode.Trim()}] {line.Content}</d>");

        if (line.Continuity == SceneVideoCutContinuity.CrossesCut)
        {
            builder.Append(" <scenetrans>");
        }

        if (line.Continuity == SceneVideoCutContinuity.Truncated)
        {
            builder.Append(" <cutoff>");
        }

        if (line.Voiceover)
        {
            builder.Append(
                " The on-screen character's lips remain completely closed while the voiceover continues.");
        }
        else
        {
            builder.Append($" {line.SpeakerName.Trim()}'s lips close as the line ends.");
        }

        return builder.ToString();
    }

    private static string BuildSoundscape(SceneVideoCompilationInput input) =>
        SentinelOrDefault(input.Soundscape);

    private static string BuildMusic(SceneVideoCompilationInput input) =>
        SentinelOrDefault(input.NonDiegeticMusic);

    private static string SentinelOrDefault(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsSentinel(value) ? NotApplicableSentinel : value.Trim();

    private static bool IsSentinel(string? value) =>
        string.Equals(value?.Trim(), NotApplicableSentinel, StringComparison.OrdinalIgnoreCase);

    private static string RetentionMarker(SceneVideoReference reference) =>
        reference.RetentionMarker?.Trim() ?? string.Empty;

    private static string FirstSentence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var end = trimmed.IndexOf('.');
        return end < 0 ? trimmed : trimmed[..(end + 1)];
    }

    private sealed record Sections(
        string SubjectDefinitions,
        string Summary,
        string RetentionAnalysis,
        string DetailedDescription,
        string Soundscape,
        string NonDiegeticMusic);

    private static string Assemble(Sections sections) =>
        new StringBuilder()
            .AppendLine("subject_definitions:")
            .AppendLine(sections.SubjectDefinitions)
            .AppendLine()
            .AppendLine("summary:")
            .AppendLine(sections.Summary)
            .AppendLine()
            .AppendLine("retention_analysis:")
            .AppendLine(sections.RetentionAnalysis)
            .AppendLine()
            .AppendLine("detailed_description:")
            .AppendLine(sections.DetailedDescription)
            .AppendLine()
            .AppendLine("overall_soundscape:")
            .AppendLine(sections.Soundscape)
            .AppendLine()
            .AppendLine("non_diegetic_music:")
            .AppendLine(sections.NonDiegeticMusic)
            .ToString()
            .TrimEnd() + "\n";

    // ---------------------------------------------------------------------------------------------------------
    // Validation (the 33 rules)
    // ---------------------------------------------------------------------------------------------------------

    private List<SceneVideoValidationFinding> Validate(
        SceneVideoCompilationInput input,
        LabelPlan labels,
        Sections sections,
        string prompt)
    {
        var findings = new List<SceneVideoValidationFinding>();
        void Add(string rule, string section, bool passed, string message, bool advisory = false) =>
            findings.Add(new SceneVideoValidationFinding(rule, section, passed, advisory, message));

        // 1 - six sections, present, in order.
        var order = new[] { "subject_definitions:", "summary:", "retention_analysis:", "detailed_description:", "overall_soundscape:", "non_diegetic_music:" };
        var positions = order.Select(header => prompt.IndexOf(header, StringComparison.Ordinal)).ToList();
        Add("R01", "document",
            positions.All(position => position >= 0) && positions.SequenceEqual(positions.OrderBy(position => position)),
            "The six sections must be present and in the documented order (subject_definitions, summary, retention_analysis, detailed_description, overall_soundscape, non_diegetic_music).");

        // 2 - English outside <d> and quoted text.
        var stripped = Regex.Replace(prompt, @"<d>.*?</d>", string.Empty, RegexOptions.Singleline);
        stripped = Regex.Replace(stripped, "\"[^\"]*\"", string.Empty);
        var nonAscii = stripped.Where(character => character > 127).Distinct().ToArray();
        Add("R02", "document", nonAscii.Length == 0,
            nonAscii.Length == 0
                ? "All six sections are in English (non-English text is confined to <d> blocks and visible quoted text)."
                : $"Non-English characters appear outside <d> blocks and quoted text: {string.Join(", ", nonAscii.Select(character => $"'{character}'"))}.");

        // 3 - every label referenced later is defined in subject_definitions.
        var defined = Regex.Matches(sections.SubjectDefinitions, "<(?:Subject|Picture) \\d+>")
            .Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        var referenced = Regex.Matches(
                string.Join("\n", sections.Summary, sections.RetentionAnalysis, sections.DetailedDescription),
                "<(?:Subject|Picture|Audio|Video) \\d+>")
            .Select(match => match.Value).Distinct();
        var undefined = referenced.Where(label => !defined.Contains(label)).ToList();
        Add("R03", "subject_definitions", undefined.Count == 0,
            undefined.Count == 0
                ? "Every referenced label is defined in subject_definitions."
                : $"Undefined labels cited elsewhere: {string.Join(", ", undefined)}.");

        // 4 - numbering is contiguous and stable for the input set.
        var pictureNumbers = labels.References.Select(reference => reference.PictureNumber).ToList();
        var subjectNumbers = labels.Subjects.Select(subject => subject.SubjectNumber).ToList();
        Add("R04", "labels",
            pictureNumbers.SequenceEqual(Enumerable.Range(1, pictureNumbers.Count))
            && subjectNumbers.SequenceEqual(Enumerable.Range(1, subjectNumbers.Count)),
            "Picture and subject numbering must be contiguous from 1 in reference order (the same inputs must produce the same labels).");

        // 5 - retention markers from the closed set.
        var badMarkers = input.References
            .Select(reference => reference.RetentionMarker?.Trim() ?? string.Empty)
            .Where(marker => !VisibleRetentionMarkers.Contains(marker, StringComparer.Ordinal))
            .Distinct()
            .ToList();
        Add("R05", "retention_analysis", badMarkers.Count == 0,
            badMarkers.Count == 0
                ? "Every reference carries a retention marker from the closed set."
                : $"Retention markers must be one of {string.Join(", ", VisibleRetentionMarkers)}; found: "
                  + string.Join(", ", badMarkers.Select(marker => marker.Length == 0 ? "(none set)" : marker)));

        // 6 - no (Sx) in retention_analysis.
        Add("R06", "retention_analysis",
            !Regex.IsMatch(sections.RetentionAnalysis, @"\(S\d+(?:,S\d+)*\)"),
            "retention_analysis must not contain speaker ids; they belong in detailed_description.");

        // 7 - standalone <Picture N> only for frame anchors.
        var standalonePictures = Regex.Matches(sections.SubjectDefinitions, @"^<Picture (\d+)>", RegexOptions.Multiline)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        var anchorPictures = labels.References
            .Where(reference => reference.Reference.Role == SceneVideoReferenceRole.FrameAnchor)
            .Select(reference => reference.PictureNumber).ToList();
        var sheetPictures = labels.References
            .Where(reference => reference.Reference.Role == SceneVideoReferenceRole.CharacterSheet)
            .Select(reference => reference.PictureNumber).ToList();
        var illegalStandalone = standalonePictures.Except(anchorPictures.Concat(sheetPictures)).ToList();
        Add("R07", "subject_definitions", illegalStandalone.Count == 0,
            illegalStandalone.Count == 0
                ? "Only frame anchors and character sheets get a standalone <Picture N> line."
                : $"Definition-only images must be cited inside their <Subject N> line; standalone lines found for "
                  + string.Join(", ", illegalStandalone.Select(number => $"<Picture {number}>")));

        // 8 - [Shot 1] has no timestamp.
        Add("R08", "detailed_description",
            !Regex.IsMatch(sections.DetailedDescription, @"\[Shot 1\]\s*At \d"),
            "[Shot 1] must not carry a cut timestamp.");

        // 9 - later shots carry strictly increasing cut times inside the duration.
        var duration = input.FrameLength / (double)input.Fps;
        var cutTimes = new List<double>();
        var cutTimeValid = true;
        var cutMessage = "Every shot after the first carries a strictly increasing cut time inside the clip.";
        for (var index = 0; index < input.Shots.Count; index++)
        {
            var shot = input.Shots[index];
            if (shot.Number == 1)
            {
                continue;
            }

            if (!TryParseTimecode(shot.CutTime, out var seconds))
            {
                cutTimeValid = false;
                cutMessage = $"[Shot {shot.Number}] needs a cut time in MM:SS.mmm form.";
                break;
            }

            if (seconds > duration + 0.001)
            {
                cutTimeValid = false;
                cutMessage = $"[Shot {shot.Number}] cuts at {shot.CutTime}, which is past the clip's {duration:0.0}s duration.";
                break;
            }

            if (cutTimes.Count > 0 && seconds <= cutTimes[^1])
            {
                cutTimeValid = false;
                cutMessage = $"[Shot {shot.Number}] must cut later than [Shot {input.Shots[index - 1].Number}].";
                break;
            }

            cutTimes.Add(seconds);
        }

        Add("R09", "detailed_description", cutTimeValid, cutMessage);

        // 10 - style declared in one or two sentences before [Shot 1]. ADVISORY: the guide requires a style opening
        // in full-reference mode, but a missing one improvises rather than malforming the document.
        var style = input.StyleDeclaration?.Trim() ?? string.Empty;
        var styleSentences = CountSentences(style);
        var styleOk = style.Length > 0 && styleSentences is >= 1 and <= 2;
        Add("R10", "detailed_description", styleOk,
            styleOk
                ? $"The style declaration is {styleSentences} sentence(s) and precedes [Shot 1]."
                : style.Length == 0
                    ? "No style sentence was supplied. The guide asks for one or two sentences before [Shot 1] so the "
                      + "render's look is directed rather than improvised."
                    : $"The style declaration must be one or two sentences; found {styleSentences}.",
            advisory: true);

        // 11 - word count inside the configured band (dialogue-dense content may prioritise the spoken timeline).
        // ADVISORY: the band is a conditioning target, not a correctness rule.
        var bodyWords = CountWords(Regex.Replace(sections.DetailedDescription, @"<d>.*?</d>", string.Empty, RegexOptions.Singleline));
        var dialogueWords = input.Dialogue.Sum(line => CountWords(line.Content));
        var dialogueDominant = dialogueWords > 0 && dialogueWords * 4 >= bodyWords;
        var withinBand = bodyWords >= input.MinWordCount && bodyWords <= input.MaxWordCount;
        var wordCountOk = withinBand || dialogueDominant;
        Add("R11", "detailed_description", wordCountOk,
            withinBand
                ? $"The description is {bodyWords} words, inside the target band of {input.MinWordCount}-{input.MaxWordCount}."
                : dialogueDominant
                    ? $"The description is {bodyWords} words; dialogue-dense content lets the spoken timeline lead."
                    : $"The description is {bodyWords} words against a target band of {input.MinWordCount}-{input.MaxWordCount}. "
                      + "A longer body conditions H3 better (the B-150 proof was ~150 words and lost conditioning), but a "
                      + "shorter one still renders: this is a target, not a requirement.",
            advisory: true);

        // 12 - camera vocabulary.
        var badMotion = input.Shots
            .Where(shot => !string.IsNullOrWhiteSpace(shot.CameraMotionType)
                           && !CameraMotionTypes.Contains(shot.CameraMotionType.Trim(), StringComparer.Ordinal))
            .Select(shot => shot.CameraMotionType.Trim())
            .Distinct()
            .ToList();
        var badAmplitude = input.Shots
            .Where(shot => shot.CameraAmplitude is not null
                           && shot.CameraAmplitude.Trim() is not ("small" or "large"))
            .Select(shot => shot.CameraAmplitude!.Trim())
            .Distinct()
            .ToList();
        var badSpeed = input.Shots
            .Where(shot => shot.CameraSpeed is not null && shot.CameraSpeed.Trim() is not ("slow" or "fast"))
            .Select(shot => shot.CameraSpeed!.Trim())
            .Distinct()
            .ToList();
        Add("R12", "detailed_description", badMotion.Count == 0 && badAmplitude.Count == 0 && badSpeed.Count == 0,
            badMotion.Count == 0 && badAmplitude.Count == 0 && badSpeed.Count == 0
                ? "Every camera motion uses the closed vocabulary."
                : $"Unsupported camera values: motions [{string.Join(", ", badMotion)}], amplitudes "
                  + $"[{string.Join(", ", badAmplitude)}], speeds [{string.Join(", ", badSpeed)}].");

        // 13 - on-screen text in English double quotes.
        var unquoted = input.OnScreenText
            .Where(text => !sections.DetailedDescription.Contains($"\"{text.Text}\"", StringComparison.Ordinal))
            .Select(text => text.Text)
            .ToList();
        Add("R13", "detailed_description", unquoted.Count == 0,
            unquoted.Count == 0
                ? "Every on-screen text entry is rendered verbatim inside English double quotes."
                : $"On-screen text must appear in English double quotes: {string.Join(" | ", unquoted.Select(text => $"'{text}'"))}.");

        // 14 - every <d> block carries a non-empty language tag.
        var badLanguageBlocks = Regex.Matches(sections.DetailedDescription, @"<d>\[([^\]]*)\]")
            .Select(match => match.Groups[1].Value)
            .Where(tag => string.IsNullOrWhiteSpace(tag))
            .Count();
        var untaggedBlocks = Regex.Matches(sections.DetailedDescription, @"<d>(?!\[)")
            .Count;
        Add("R14", "detailed_description", badLanguageBlocks == 0 && untaggedBlocks == 0,
            badLanguageBlocks == 0 && untaggedBlocks == 0
                ? "Every <d> block is <d>[Language] content</d>."
                : "Every <d> block needs a non-empty [Language] tag immediately after <d>.");

        // 15 - every <d> block is preceded by an identifying phrase and an (Sx) id.
        var speakerless = Regex.Matches(sections.DetailedDescription, @"<d>")
            .Cast<Match>()
            .Select(match => sections.DetailedDescription[..match.Index])
            .Where(prefix =>
            {
                var sentenceStart = prefix.LastIndexOf('.', Math.Max(0, prefix.Length - 1));
                var sentence = prefix[(sentenceStart + 1)..];
                return !Regex.IsMatch(sentence, @"\(S\d+(?:,S\d+)*\)");
            })
            .Count();
        Add("R15", "detailed_description", speakerless == 0,
            speakerless == 0
                ? "Every <d> block is preceded by an identifying phrase and its speaker id."
                : $"{speakerless} <d> block(s) are not preceded by a speaker id in the same sentence.");

        // 16 - speaker ids assigned once, reused.
        var idSequence = Regex.Matches(sections.DetailedDescription, @"\(S(\d+)\)")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        var distinctIds = idSequence.Distinct().OrderBy(id => id).ToList();
        var idsContiguous = distinctIds.SequenceEqual(Enumerable.Range(1, distinctIds.Count));
        var expectedIds = input.Dialogue.Select(line => line.SpeakerName.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal).Count();
        Add("R16", "detailed_description", idsContiguous && distinctIds.Count == expectedIds,
            idsContiguous && distinctIds.Count == expectedIds
                ? "Speaker ids are assigned once, in vocal-event order, and reused (no compound vocal events in this composition)."
                : $"Speaker ids must be assigned once in vocal-event order; found {distinctIds.Count} id(s) for "
                  + $"{expectedIds} speaking character(s).");

        // 17 - first appearance carries identity information. ADVISORY: voice stability quality, not document form.
        var thinIdentities = input.Dialogue
            .Where(line => IsFirstAppearance(input, line) && CountWords(line.IdentityInfo) < 3)
            .Select(line => line.SpeakerName)
            .Distinct()
            .ToList();
        Add("R17", "detailed_description", thinIdentities.Count == 0,
            thinIdentities.Count == 0
                ? "Every speaker's first appearance carries identity information."
                : $"A speaker's first appearance carries little identity information (character type, age, gender, "
                  + $"pitch, timbre, rate or accent steady the voice): {string.Join(", ", thinIdentities)}.",
            advisory: true);

        // 18 - characters with no vocal event have no (Sx).
        var silentSpeakers = Regex.Matches(sections.DetailedDescription, @"\(S(\d+)\)")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .Where(id => !AssignSpeakerIds(input).Values.Contains(id))
            .ToList();
        Add("R18", "detailed_description", silentSpeakers.Count == 0,
            silentSpeakers.Count == 0
                ? "No character without a vocal event carries an id."
                : $"Ids assigned to characters with no vocal event: {string.Join(", ", silentSpeakers.Select(id => $"(S{id})"))}.");

        // 19 - voiceover phrasing and the lips-closed statement.
        var voiceoverLines = input.Dialogue.Where(line => line.Voiceover).ToList();
        var voiceoverOk = voiceoverLines.All(line =>
            sections.DetailedDescription.Contains("says in an off-screen voiceover", StringComparison.Ordinal)
            && sections.DetailedDescription.Contains(
                "The on-screen character's lips remain completely closed", StringComparison.Ordinal));
        Add("R19", "detailed_description", voiceoverOk,
            voiceoverOk
                ? "Voiceover lines use the exact phrase and state that the on-screen lips remain closed."
                : "A voiceover line must use 'says in an off-screen voiceover' and be followed by the statement that "
                  + "the on-screen character's lips remain completely closed.");

        // 20 - continuity markers where they apply.
        var continuityOk = input.Dialogue.All(line => line.Continuity switch
        {
            SceneVideoCutContinuity.CrossesCut => sections.DetailedDescription.Contains("<scenetrans>", StringComparison.Ordinal),
            SceneVideoCutContinuity.Truncated => sections.DetailedDescription.Contains("<cutoff>", StringComparison.Ordinal),
            _ => true
        });
        Add("R20", "detailed_description", continuityOk,
            continuityOk
                ? "Continuity and truncation markers are present wherever they apply."
                : "A dialogue line crossing a cut needs <scenetrans> at both connection points, and a truncated line needs <cutoff>.");

        // 21 - soundscape 1-4 sentences, no dialogue/singing/music. ADVISORY: an untouched Audio tab must not trap the
        // operator, so an unauthored soundscape is sent as the N/A sentinel and reported as a gap to fill later.
        var soundscapeSentinel = IsSentinel(sections.Soundscape);
        var soundscapeAuthored = !string.IsNullOrWhiteSpace(input.Soundscape);
        var soundscapeSentences = CountSentences(sections.Soundscape);
        var soundscapeHasSpeech = sections.Soundscape.Contains("<d>", StringComparison.Ordinal)
            || Regex.IsMatch(sections.Soundscape, @"\b(says|singing|sings|dialogue)\b", RegexOptions.IgnoreCase)
            || DiegeticMusicWords.Any(word => sections.Soundscape.Contains(word, StringComparison.OrdinalIgnoreCase));
        var soundscapeOk = soundscapeAuthored
            && (soundscapeSentinel || (soundscapeSentences is >= 1 and <= 4 && !soundscapeHasSpeech));
        Add("R21", "overall_soundscape", soundscapeOk,
            soundscapeHasSpeech
                ? "overall_soundscape must not repeat dialogue, singing or diegetic music; only ambience, physical action sounds and non-verbal human sound belong here."
                : !soundscapeAuthored
                    ? "No soundscape was authored, so the section is sent as N/A and H3 gets no ambience or physical-action direction. Name a concrete object and a physical action to direct it."
                    : soundscapeSentinel
                        ? "Deliberate silence (N/A) was chosen for the soundscape."
                        : $"overall_soundscape is {soundscapeSentences} sentence(s) against a 1-4 sentence target.",
            advisory: true);

        // 22 - music: N/A or 1-3 sentences of instrumentation, no mood words. ADVISORY: wording quality.
        var musicSentences = CountSentences(sections.NonDiegeticMusic);
        var moodFound = MoodWords
            .Where(word => Regex.IsMatch(sections.NonDiegeticMusic, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase))
            .Distinct()
            .ToList();
        var musicAuthored = !string.IsNullOrWhiteSpace(input.NonDiegeticMusic);
        Add("R22", "non_diegetic_music",
            (IsSentinel(sections.NonDiegeticMusic) || musicSentences is >= 1 and <= 3) && moodFound.Count == 0,
            moodFound.Count == 0
                ? musicAuthored
                    ? "non_diegetic_music is the N/A sentinel or 1-3 sentences of instrumentation, tempo, rhythm and dynamics."
                    : "No non-diegetic music was authored, so the section is sent as N/A (deliberate silence)."
                : $"non_diegetic_music must describe instrumentation, tempo, rhythm and dynamics rather than mood; found: "
                  + string.Join(", ", moodFound),
            advisory: true);

        // 23 - diegetic music belongs in the description. ADVISORY: placement quality.
        var diegeticMoved = !DiegeticMusicWords.Any(word =>
            sections.NonDiegeticMusic.Contains(word, StringComparison.OrdinalIgnoreCase));
        Add("R23", "non_diegetic_music", diegeticMoved,
            diegeticMoved
                ? "Music the characters can hear is kept out of the non-diegetic section."
                : "Music the characters can hear is diegetic and belongs in detailed_description.",
            advisory: true);

        // 24 - audio copy/reference relationships belong in the layer they are audible in. No reference audio is
        // bound in this slice, so the rule passes with an explicit statement rather than a silent skip.
        Add("R24", "retention_analysis", true,
            "No reference audio is bound in this composition, so there is no audio copy/reference relationship to place.");

        // 25 - no negative phrasing anywhere (CFG 1).
        var searchable = Regex.Replace(prompt, @"<d>.*?</d>", " ", RegexOptions.Singleline);
        searchable = Regex.Replace(searchable, "\"[^\"]*\"", " ");
        var negativeHits = NegativePhrases
            .Where(phrase => searchable.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
        Add("R25", "document", negativeHits.Count == 0,
            negativeHits.Count == 0
                ? "The document contains no negative phrasing (CFG 1 leaves no negative branch)."
                : $"Negative phrasing is not rendered (there is no negative branch): {string.Join(", ", negativeHits.Select(phrase => $"'{phrase.Trim()}'"))}. Re-express each as a positive.");

        // 26 - frame length matches the node's acceptance rule.
        var acceptedLength = input.FramePolicy.IsAccepted(input.FrameLength);
        Add("R26", "sampling", acceptedLength,
            acceptedLength
                ? $"length {input.FrameLength} satisfies the node's {input.FramePolicy.MinFrames} + n*{input.FramePolicy.FrameStep} acceptance rule."
                : $"length {input.FrameLength} is not a value the node accepts: it must be between "
                  + $"{input.FramePolicy.MinFrames} and {input.FramePolicy.MaxFrames} congruent to "
                  + $"{input.FramePolicy.MinFrames} modulo {input.FramePolicy.FrameStep}.");

        // 27 - labels match the node's reference slot order.
        var slotOrderOk = labels.References
            .Select((reference, index) => reference.PictureNumber == index + 1)
            .All(ok => ok);
        Add("R27", "references", slotOrderOk,
            "Picture numbers follow the reference slot order, so reordering references renumbers the labels consistently.");

        // 28 - a video VAE is bound whenever references are supplied (and an audio VAE whenever reference audio is).
        var vaeOk = input.References.Count == 0 || !string.IsNullOrWhiteSpace(input.VideoVaeName);
        Add("R28", "references", vaeOk,
            vaeOk
                ? "The video VAE is configured for the bound references (the node's inputs are optional and omitting them would silently degrade conditioning)."
                : "Reference images are bound but no video VAE is configured; the node would quietly condition on the text encoder only.");

        // 29 - trained range, with a deliberate override.
        var trained = input.FramePolicy.IsTrained(input.FrameLength);
        Add("R29", "sampling", trained || input.AllowUntrainedLength,
            trained
                ? $"length {input.FrameLength} is inside the trained band {input.FramePolicy.TrainedMinFrames}-{input.FramePolicy.TrainedMaxFrames}."
                : input.AllowUntrainedLength
                    ? $"length {input.FrameLength} is outside the trained band {input.FramePolicy.TrainedMinFrames}-"
                      + $"{input.FramePolicy.TrainedMaxFrames} and was OVERRIDDEN by the operator."
                    : $"length {input.FrameLength} is outside the trained band {input.FramePolicy.TrainedMinFrames}-"
                      + $"{input.FramePolicy.TrainedMaxFrames}; the node accepts it but the model was not trained on it. Override deliberately to render anyway.");

        // 30 - a reference sheet is numbered and its panels are named.
        var sheetOk = labels.Subjects
            .Where(subject => subject.Reference.Role == SceneVideoReferenceRole.CharacterSheet)
            .All(subject =>
                sections.SubjectDefinitions.Contains($"<Picture {subject.PictureNumber}> (character sheet)", StringComparison.Ordinal)
                && sections.SubjectDefinitions.Contains("panels are numbered", StringComparison.Ordinal));
        Add("R30", "subject_definitions", sheetOk,
            sheetOk
                ? "Every character sheet gets its own <Picture N> entry naming its numbered panels."
                : "A reference sheet must get its own <Picture N> entry that numbers the panels, and shots must point at a panel.");

        // 31 - the description's timeline matches the requested duration.
        var timelineEnd = cutTimes.Count > 0 ? cutTimes[^1] : 0d;
        var declaredOk = true;
        var declaredMessage = $"The described timeline ends inside the clip's {duration:0.0}s duration.";
        if (!string.IsNullOrWhiteSpace(input.DeclaredTimelineEnd))
        {
            if (!TryParseTimecode(input.DeclaredTimelineEnd, out var declared))
            {
                declaredOk = false;
                declaredMessage = "The declared timeline end must be in MM:SS.mmm form.";
            }
            else if (Math.Abs(declared - duration) > 0.25)
            {
                declaredOk = false;
                declaredMessage = $"The description declares a timeline ending at {input.DeclaredTimelineEnd} but the "
                    + $"clip runs {duration:0.0}s; the two must match.";
            }
        }
        else if (timelineEnd > 0 && duration - timelineEnd > duration)
        {
            declaredOk = false;
            declaredMessage = "The last cut time must leave time inside the clip.";
        }

        // ADVISORY: the described timeline is conditioning guidance; a mismatch nudges rather than malforms.
        Add("R31", "detailed_description", declaredOk, declaredMessage, advisory: true);

        // 32 - no abstract-only descriptors. ADVISORY: prose quality.
        var abstractOnly = AbstractOnlySentences(sections.DetailedDescription);
        Add("R32", "detailed_description", abstractOnly.Count == 0,
            abstractOnly.Count == 0
                ? "Every abstract descriptor is backed by concrete visual or audio detail in the same sentence."
                : $"Abstract descriptors without concrete detail: {string.Join(" | ", abstractOnly)}",
            advisory: true);

        // 33 - the description must not be a plot summary. ADVISORY: prose quality.
        var summarySentences = SentencesWithoutAnchors(sections.DetailedDescription);
        Add("R33", "detailed_description", summarySentences.Count == 0,
            summarySentences.Count == 0
                ? "Every sentence carries composition, subject state, environment, action, camera, sound or a reference point."
                : $"Sentences that carry no visual, camera, sound or reference detail: {string.Join(" | ", summarySentences)}",
            advisory: true);

        return findings;
    }

    private static List<string> AbstractOnlySentences(string description)
    {
        var offenders = new List<string>();
        foreach (var sentence in AnalyzableSentences(description))
        {
            var lower = sentence.ToLowerInvariant();
            var hasAbstract = AbstractDescriptors.Any(word => Regex.IsMatch(lower, $@"\b{Regex.Escape(word)}\b"));
            if (!hasAbstract)
            {
                continue;
            }

            var hasConcrete = ConcreteAnchors.Any(word => lower.Contains(word, StringComparison.Ordinal))
                              || char.IsDigit(lower.FirstOrDefault());
            if (!hasConcrete)
            {
                offenders.Add(sentence.Trim());
            }
        }

        return offenders;
    }

    /// <summary>
    /// The description's sentences with spoken content and the <c>[Shot N]</c> markers removed. The markers are
    /// stripped because "[Shot 1]" itself contains an anchor word and would otherwise mask an abstract-only sentence
    /// that opens a shot.
    /// </summary>
    private static IEnumerable<string> AnalyzableSentences(string description)
    {
        var withoutDialogue = Regex.Replace(description, @"<d>.*?</d>", " ", RegexOptions.Singleline);
        var withoutMarkers = Regex.Replace(
            withoutDialogue, @"\[Shot \d+\](\s*At\s+\d{1,2}:\d{2}\.\d{3},)?", " ", RegexOptions.IgnoreCase);
        return SplitSentences(withoutMarkers);
    }

    private static List<string> SentencesWithoutAnchors(string description)
    {
        var offenders = new List<string>();
        foreach (var sentence in AnalyzableSentences(description))
        {
            var trimmed = sentence.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var lower = trimmed.ToLowerInvariant();
            var hasAnchor = ConcreteAnchors.Any(word => lower.Contains(word, StringComparison.Ordinal))
                            || Regex.IsMatch(trimmed, "<(?:Subject|Picture) \\d+>")
                            || Regex.IsMatch(lower, @"\b(cuts|cut|transition|dissolve)\b")
                            || char.IsDigit(lower.FirstOrDefault());
            if (!hasAnchor)
            {
                offenders.Add(trimmed);
            }
        }

        return offenders;
    }

    private static IEnumerable<string> SplitSentences(string text) =>
        text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(sentence => sentence.Length > 0);

    private static int CountSentences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsSentinel(text))
        {
            return 0;
        }

        return text.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static int CountWords(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool TryParseTimecode(string? value, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var remainder)
            || minutes < 0 || remainder < 0)
        {
            return false;
        }

        seconds = minutes * 60 + remainder;
        return true;
    }
}
