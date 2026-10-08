using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Pins the H3 six-section compiler (B-152, C-3): the 33 rules of
/// <c>specs/Planning/B-152-scene-video-composer/COMPILER-RESEARCH.md</c> §13, the label/slot-order contract, and the
/// deterministic speaker-id assignment. No ComfyUI and no model call is involved.
/// </summary>
public sealed class MiniMaxH3SceneVideoPromptCompilerTests
{
    private static MiniMaxH3FramePolicy FramePolicy() => new(
        MinFrames: 5, MaxFrames: 3600, FrameStep: 17,
        TrainedMinFrames: 124, TrainedMaxFrames: 362, WarnAboveFrames: 192,
        PresetShortFrames: 124, PresetLongFrames: 192);

    private static readonly string Style =
        "The target video is in a handheld photographic style with warm practical light and shallow depth of field.";

    private static string SceneBody() => string.Join(" ",
        Enumerable.Range(1, 4).Select(index =>
            $"the woman in the red coat turns toward the window as the ceiling light catches the fabric of her sleeve " +
            $"and the camera drifts across the wooden floor at step {index}"));

    private static SceneVideoCompilationInput ValidInput(
        IReadOnlyList<SceneVideoReference>? references = null,
        int frameLength = 124,
        bool allowUntrained = false,
        string? soundscape = null,
        string music = "N/A",
        string? style = null,
        IReadOnlyList<SceneVideoShot>? shots = null,
        IReadOnlyList<SceneVideoDialogueLine>? dialogue = null,
        IReadOnlyList<SceneVideoOnScreenText>? onScreenText = null)
        => new(
            StyleDeclaration: style ?? Style,
            SceneDescription: SceneBody(),
            References: references ??
            [
                new SceneVideoReference(
                    SceneVideoReferenceRole.FrameAnchor, "a/first.png", "first.png",
                    "the opening frame", "fully_preserved"),
                new SceneVideoReference(
                    SceneVideoReferenceRole.SubjectDefinition, "a/face.png", "face.png",
                    "the adult woman with long dark hair and dark eyes", "fully_preserved")
            ],
            Shots: shots ??
            [
                new SceneVideoShot(1, null,
                    "<Subject 1> stands at the window in the red coat while <Picture 1> anchors the opening frame.",
                    "Push In", "small", "slow")
            ],
            Dialogue: dialogue ?? [],
            OnScreenText: onScreenText ?? [],
            Soundscape: soundscape ?? "The radiator ticks beside the window and fabric rustles as she shifts her weight.",
            NonDiegeticMusic: music,
            ContentRegister: SceneVideoContentRegister.Explicit,
            FrameLength: frameLength,
            Fps: 24,
            FramePolicy: FramePolicy(),
            MinWordCount: 100,
            MaxWordCount: 500,
            VideoVaeName: "minimax_h3_video_vae_int8_convrot.safetensors",
            AudioVaeName: "minimax_h3_audio_vae_fp32.safetensors",
            AllowUntrainedLength: allowUntrained);

    private static readonly SceneVideoDialogueLine FirstLine = new(
        Order: 1, ShotNumber: 1, SpeakerName: "Mara", SubjectLabel: "<Subject 1>",
        IdentityInfo: "an adult woman with a low breathy voice and a slow speaking rate",
        LanguageCode: "English", Content: "Close the door.", Delivery: "quietly",
        OffScreen: false, Voiceover: false, Continuity: SceneVideoCutContinuity.None);

    private static readonly SceneVideoDialogueLine SecondLine = new(
        Order: 2, ShotNumber: 1, SpeakerName: "Mara", SubjectLabel: "<Subject 1>",
        IdentityInfo: "an adult woman with a low breathy voice and a slow speaking rate",
        LanguageCode: "English", Content: "Then come here.", Delivery: "with a small laugh",
        OffScreen: false, Voiceover: false, Continuity: SceneVideoCutContinuity.None);

    private static SceneVideoValidationFinding Rule(SceneVideoCompilationResult result, string id) =>
        result.Findings.Single(finding => finding.RuleId == id);

    [Fact]
    public void EveryRuleReportsAFinding()
    {
        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput());

        Assert.Equal(33, result.Findings.Count);
        Assert.Equal(
            Enumerable.Range(1, 33).Select(index => $"R{index:00}").ToList(),
            result.Findings.Select(finding => finding.RuleId).ToList());
        Assert.True(result.IsValid, string.Join(" | ", result.Failures.Select(failure => failure.Message)));
    }

    [Fact]
    public void Compile_EmitsTheSixSectionsInOrder()
    {
        var prompt = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput()).Prompt;

        var headers = new[]
        {
            "subject_definitions:", "summary:", "retention_analysis:",
            "detailed_description:", "overall_soundscape:", "non_diegetic_music:"
        };
        var positions = headers.Select(header => prompt.IndexOf(header, StringComparison.Ordinal)).ToList();

        Assert.All(positions, position => Assert.True(position >= 0, "a section header is missing"));
        Assert.Equal(positions.OrderBy(position => position), positions);
    }

    [Fact]
    public void Compile_AssignsPictureNumbersInReferenceOrder()
    {
        var compiler = new MiniMaxH3SceneVideoPromptCompiler();
        var input = ValidInput();

        var prompt = compiler.Compile(input).Prompt;
        Assert.Contains("<Picture 1>", prompt);
        Assert.Contains("<Picture 2>", prompt);

        var reversed = input with { References = input.References.Reverse().ToList() };
        var reversedPrompt = compiler.Compile(reversed).Prompt;

        // The subject definition is now slot 1 and the frame anchor slot 2, so the numbering follows the new order.
        Assert.Contains("Reference role: subject definition", reversedPrompt);
        Assert.Matches(@"<Picture 2> \(\[Shot 1\] first frame\)", reversedPrompt);
    }

    [Fact]
    public void Compile_CitesADefinitionOnlyImageInsideItsSubjectLine()
    {
        var prompt = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput()).Prompt;

        // Rule 7: the subject-definition image never gets a standalone <Picture N> line of its own.
        Assert.DoesNotContain("<Picture 2> ([Shot 1] first frame)", prompt);
        Assert.Contains("<Subject 1> is the adult woman with long dark hair and dark eyes in <Picture 2>", prompt);
    }

    [Fact]
    public void Compile_GivesACharacterSheetItsOwnNumberedPictureEntry()
    {
        var references = new List<SceneVideoReference>
        {
            new(SceneVideoReferenceRole.CharacterSheet, "a/sheet.png", "sheet.png",
                "the adult woman with long dark hair, shown in four numbered panels from front to profile",
                "fully_preserved")
        };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput(references: references));

        Assert.Contains("panels are numbered from 1 in reading order", result.Prompt);
        Assert.Contains("<Picture 1> (character sheet)", result.Prompt);
        Assert.True(Rule(result, "R30").Passed);
    }

    [Fact]
    public void Compile_AssignsSpeakerIdsInVocalEventOrderAndOnlyOnce()
    {
        var dialogue = new List<SceneVideoDialogueLine>
        {
            new(Order: 2, ShotNumber: 1, SpeakerName: "Ivo", SubjectLabel: "<Subject 2>",
                IdentityInfo: "an adult man with a warm mid-range voice and a quick speaking rate",
                LanguageCode: "English", Content: "Wait.", Delivery: "flatly"),
            new(Order: 1, ShotNumber: 1, SpeakerName: "Mara", SubjectLabel: "<Subject 1>",
                IdentityInfo: "an adult woman with a low breathy voice and a slow speaking rate",
                LanguageCode: "English", Content: "No, you wait.", Delivery: "quietly")
        };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(
            ValidInput(dialogue: dialogue, references:
            [
                new SceneVideoReference(SceneVideoReferenceRole.SubjectDefinition, "a/m.png", "m.png",
                    "the adult woman with long dark hair", "fully_preserved"),
                new SceneVideoReference(SceneVideoReferenceRole.SubjectDefinition, "a/i.png", "i.png",
                    "the adult man with a short beard", "fully_preserved")
            ]));

        // Mara speaks first (order 1) so she is S1 even though she is Subject 1 and appears second in the list order.
        Assert.Contains("<Subject 1> (S1)", result.Prompt);
        Assert.Contains("<Subject 2> (S2)", result.Prompt);
        Assert.True(Rule(result, "R16").Passed);
    }

    [Fact]
    public void Compile_CarriesIdentityInformationOnlyOnFirstAppearance()
    {
        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(
            ValidInput(dialogue: [FirstLine, SecondLine]));

        var description = result.Prompt;
        Assert.Equal(1, CountOccurrences(description, "an adult woman with a low breathy voice"));
        Assert.True(Rule(result, "R17").Passed);
    }

    [Fact]
    public void Compile_LeavesShotOneUntimedAndLaterShotsTimed()
    {
        var shots = new List<SceneVideoShot>
        {
            new(1, null, "She turns toward the window as the ceiling light catches her sleeve.", "Static Shot"),
            new(2, "00:03.000", "The camera finds her hand on the wooden table beside the glass.", "Pan Right", null, "slow")
        };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput(shots: shots));

        Assert.Contains("[Shot 1] She turns", result.Prompt);
        Assert.Contains("[Shot 2] At 00:03.000,", result.Prompt);
        Assert.True(Rule(result, "R08").Passed);
        Assert.True(Rule(result, "R09").Passed);
    }

    [Fact]
    public void Compile_DeclaresStyleBeforeShotOne()
    {
        var prompt = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput()).Prompt;

        var description = prompt[prompt.IndexOf("detailed_description:", StringComparison.Ordinal)..];
        Assert.True(
            description.IndexOf(Style, StringComparison.Ordinal)
            < description.IndexOf("[Shot 1]", StringComparison.Ordinal),
            "the style declaration must precede [Shot 1] inside detailed_description");
    }

    [Fact]
    public void Compile_QuotesOnScreenTextVerbatim()
    {
        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(
            ValidInput(onScreenText: [new SceneVideoOnScreenText("営業中", 1)]));

        Assert.Contains("Text visible in frame reads \"営業中\".", result.Prompt);
        // Rule 2 exempts visible text inside quotes from the English-only requirement.
        Assert.True(Rule(result, "R02").Passed);
        Assert.True(Rule(result, "R13").Passed);
    }

    [Fact]
    public void Compile_UsesTheDocumentedSentinelForMusic()
    {
        var prompt = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput()).Prompt;

        Assert.Matches(@"non_diegetic_music:\r?\nN/A", prompt);
        Assert.DoesNotContain("None.", prompt);
    }

    [Fact]
    public void Compile_EmitsContinuityAndTruncationMarkers()
    {
        var crossing = FirstLine with { Continuity = SceneVideoCutContinuity.CrossesCut };
        var truncated = SecondLine with { Continuity = SceneVideoCutContinuity.Truncated };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(
            ValidInput(dialogue: [crossing, truncated]));

        Assert.Contains("<scenetrans>", result.Prompt);
        Assert.Contains("<cutoff>", result.Prompt);
        Assert.True(Rule(result, "R20").Passed);
    }

    [Fact]
    public void Compile_UsesTheVoiceoverPhraseAndTheLipsClosedStatement()
    {
        var voiceover = FirstLine with { Voiceover = true, OffScreen = true };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput(dialogue: [voiceover]));

        Assert.Contains("says in an off-screen voiceover", result.Prompt);
        Assert.Contains("The on-screen character's lips remain completely closed", result.Prompt);
        Assert.True(Rule(result, "R19").Passed);
    }

    [Fact]
    public void Compile_IsDeterministicForTheSameInput()
    {
        var compiler = new MiniMaxH3SceneVideoPromptCompiler();
        var input = ValidInput(dialogue: [FirstLine, SecondLine]);

        Assert.Equal(compiler.Compile(input).Prompt, compiler.Compile(input).Prompt);
    }

    [Fact]
    public void Validate_FailsNegativePhrasing()
    {
        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(
            ValidInput(soundscape: "The room is quiet, with no music and no other people present."));

        Assert.False(result.IsValid);
        Assert.False(Rule(result, "R25").Passed);
    }

    [Fact]
    public void Validate_RefusesAnUntrainedLengthUnlessOverridden()
    {
        var compiler = new MiniMaxH3SceneVideoPromptCompiler();

        var refused = compiler.Compile(ValidInput(frameLength: 56));
        Assert.False(Rule(refused, "R29").Passed);
        Assert.True(Rule(refused, "R26").Passed); // accepted by the node, outside the trained band

        var overridden = compiler.Compile(ValidInput(frameLength: 56, allowUntrained: true));
        Assert.True(Rule(overridden, "R29").Passed);
        Assert.Contains("OVERRIDDEN", Rule(overridden, "R29").Message);
    }

    [Fact]
    public void Validate_RefusesALengthTheNodeRejects()
    {
        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput(frameLength: 100));

        Assert.False(Rule(result, "R26").Passed);
    }

    [Fact]
    public void Validate_RefusesACutTimePastTheClipDuration()
    {
        var shots = new List<SceneVideoShot>
        {
            new(1, null, "She turns toward the window as the light catches her sleeve.", "Static Shot"),
            new(2, "00:09.000", "Her hand rests on the wooden table beside the glass.", "Static Shot")
        };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput(shots: shots));

        Assert.False(Rule(result, "R09").Passed);
        Assert.Contains("past the clip's", Rule(result, "R09").Message);
    }

    [Fact]
    public void Validate_RefusesCameraValuesOutsideTheClosedVocabulary()
    {
        var shots = new List<SceneVideoShot>
        {
            new(1, null, "She turns toward the window as the light catches her sleeve.", "Drone Sweep", "huge", "medium")
        };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput(shots: shots));

        Assert.False(Rule(result, "R12").Passed);
    }

    [Fact]
    public void Validate_RefusesAMissingRetentionMarker()
    {
        var references = new List<SceneVideoReference>
        {
            new(SceneVideoReferenceRole.FrameAnchor, "a/first.png", "first.png", "the opening frame", null)
        };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput(references: references));

        Assert.False(Rule(result, "R05").Passed);
        Assert.Contains("(none set)", Rule(result, "R05").Message);
    }

    [Fact]
    public void Validate_ReportsAnAbstractOnlyDescriptorAsAdvisory()
    {
        var shots = new List<SceneVideoShot>
        {
            new(1, null, "The result is cinematic and tasteful.", "Static Shot")
        };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput(shots: shots));

        Assert.False(Rule(result, "R32").Passed, "R32 said: " + Rule(result, "R32").Message);
        Assert.True(Rule(result, "R32").IsAdvisory);
        Assert.True(result.IsValid, "an abstract descriptor must not block queueing");
    }

    [Fact]
    public void Validate_RefusesReferencesWithoutAVideoVae()
    {
        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(
            ValidInput() with { VideoVaeName = null });

        Assert.False(Rule(result, "R28").Passed);
        Assert.Contains("text encoder only", Rule(result, "R28").Message);
    }

    [Fact]
    public void Validate_ReportsADiegeticMusicWordInTheScoreAsAdvisory()
    {
        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(
            ValidInput(music: "A radio plays a slow guitar figure in the background."));

        Assert.False(Rule(result, "R23").Passed);
        Assert.True(Rule(result, "R23").IsAdvisory);
        Assert.True(result.IsValid, "a diegetic music word must not block queueing");
    }

    [Fact]
    public void Validate_AllowsADialogueDenseDescriptionToPrioritiseTheSpokenTimeline()
    {
        var longDialogue = new List<SceneVideoDialogueLine>
        {
            new(Order: 1, ShotNumber: 1, SpeakerName: "Mara", SubjectLabel: "<Subject 1>",
                IdentityInfo: "an adult woman with a low breathy voice and a slow speaking rate",
                LanguageCode: "English",
                Content: string.Join(' ', Enumerable.Repeat("word", 300)),
                Delivery: "quietly")
        };

        var input = ValidInput(dialogue: longDialogue) with
        {
            SceneDescription = "She speaks for a long moment while the camera holds.",
            MinWordCount = 100,
            MaxWordCount = 500
        };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(input);

        Assert.True(Rule(result, "R11").Passed);
        Assert.Contains("dialogue-dense", Rule(result, "R11").Message);
    }

    [Fact]
    public void Validate_ReportsAnOutOfBandWordCountAsAdvisory()
    {
        var input = ValidInput() with { SceneDescription = "A short moment." };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(input);

        Assert.False(Rule(result, "R11").Passed);
        Assert.True(Rule(result, "R11").IsAdvisory);
        Assert.Contains("target, not a requirement", Rule(result, "R11").Message);
        Assert.True(result.IsValid, "a short description must not block queueing");
    }

    [Fact]
    public void Validate_AMinimalDraftIsQueueableWithAdvisories()
    {
        var references = new List<SceneVideoReference>
        {
            new(SceneVideoReferenceRole.FrameAnchor, "a/first.png", "first.png",
                "the opening frame", "fully_preserved")
        };
        var shots = new List<SceneVideoShot>
        {
            new(1, null, "<Picture 1> is the opening frame.", "Static Shot")
        };

        var input = ValidInput(references: references, style: string.Empty, soundscape: string.Empty, shots: shots)
            with
            {
                SceneDescription = "She stands at the window."
            };

        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(input);

        Assert.Empty(result.BlockingFailures);
        Assert.True(result.IsValid, string.Join(" | ", result.BlockingFailures.Select(f => f.Message)));

        foreach (var rule in new[] { "R10", "R11", "R21" })
        {
            var finding = Rule(result, rule);
            Assert.True(finding.IsAdvisory, $"{rule} must be advisory");
            Assert.False(finding.Passed, $"{rule} should still report the gap");
            Assert.Contains(finding, result.Advisories);
        }

        // The document still carries the N/A sentinel so the graph is well formed.
        Assert.Matches(@"overall_soundscape:\r?\nN/A", result.Prompt);
    }

    [Fact]
    public void Compile_CarriesTheCompilerKeyAndVersion()
    {
        var result = new MiniMaxH3SceneVideoPromptCompiler().Compile(ValidInput());

        Assert.Equal(MiniMaxH3SceneVideoPromptCompiler.CompilerKey, result.CompilerKey);
        Assert.Equal(MiniMaxH3SceneVideoPromptCompiler.CompilerVersion, result.CompilerVersion);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
