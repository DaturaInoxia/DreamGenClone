using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Evaluation;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-166 D2/D9 — the required-output contract. The request states the frame and the references; the finished prompt is
/// checked against THAT, so a prompt that silently drops a placement, names an unanchored person, or restates a room the
/// location image owns is caught and corrected instead of rendered.
/// </summary>
public sealed class SceneImagePromptContractValidatorTests
{
    /// <summary>
    /// The reported failure (SceneImage 3bbad8c9, 2026-10-23): a caption, then a per-person element recap on its own
    /// line, then the framing line. The recap restates the paragraph — and its own copy had already drifted from it,
    /// saying the subject stood "directly behind the camera". The contract asks for ONE continuous paragraph, and a
    /// second line is not a paraphrase of that.
    /// </summary>
    [Fact]
    public void ValidateCaptionShape_RefusesAnElementRecapOnItsOwnLine()
    {
        const string prompt = "A photorealistic medium shot of a gritty shed where the woman bends over the workbench.\n\n"
            + "Becky: bent over the workbench with her back to the camera; completely naked without any clothing; "
            + "positioned directly behind the camera.\n\n"
            + "CAMERA — AUTHORITATIVE: this frame is a medium shot at eye level.";

        var violation = Assert.Single(SceneImagePromptContractValidator.ValidateCaptionShape(prompt));

        Assert.Equal("the caption's shape", violation.Element);
        Assert.True(violation.Blocking);
        Assert.Contains("ONE continuous paragraph", violation.Detail, StringComparison.Ordinal);
        // The recap is quoted, so the corrective pass can see what to delete rather than being told "be shorter".
        Assert.Contains("element recap", violation.Detail, StringComparison.Ordinal);
        Assert.Contains("Becky:", violation.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCaptionShape_AcceptsOneContinuousParagraph()
    {
        const string prompt =
            "A photorealistic medium shot of a gritty shed where a woman in her thirties bends over the workbench, her "
            + "palms flat on the boards and her back to the lens, the camera at her own eye level behind her.";

        Assert.Empty(SceneImagePromptContractValidator.ValidateCaptionShape(prompt));
    }

    /// <summary>
    /// A five-sentence answer returned one sentence per line is the right CONTENT in a shape the image model ignores:
    /// an image prompt's line breaks carry no meaning. Refusing it failed a live compile with "one corrective pass did
    /// not fix it" (reported 2026-10-24) although the caption itself was fine, so line breaks are normalised by the
    /// parser and are not a defect here.
    /// </summary>
    [Fact]
    public void ValidateCaptionShape_AcceptsAHardWrappedAnswer()
    {
        const string prompt =
            "A photorealistic medium shot inside a gritty shed.\n"
            + "A woman bends over the workbench, palms flat on the boards.\n"
            + "Her back is to the lens, her hair hanging forward.\n"
            + "The camera sits at her own eye level behind her.\n"
            + "Slatted light falls across her shoulders.";

        Assert.Empty(SceneImagePromptContractValidator.ValidateCaptionShape(prompt));
    }

    /// <summary>
    /// A profile key identifies a body inside the brief; it is never a word of the caption. One leaked live
    /// (SceneImage 57fcc461, 2026-10-25: "his penis buried to the root (p0)") from a contact note — and in ComfyUI
    /// syntax that parenthesis WEIGHTS the token rather than being ignored, so the leak changes the picture.
    /// </summary>
    [Theory]
    [InlineData("A photorealistic shot inside the shed, his penis buried to the root (p0).")]
    [InlineData("A photorealistic shot where p1 stands behind her, hands on her hips.")]
    public void ValidateCaptionShape_RefusesAParticipantKey(string prompt)
    {
        var violation = Assert.Single(SceneImagePromptContractValidator.ValidateCaptionShape(prompt));
        Assert.True(violation.Blocking);
        Assert.Contains("participant key", violation.Detail, StringComparison.Ordinal);
        Assert.Contains("Name the person instead", violation.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCaptionShape_AcceptsDigitsThatAreNotParticipantKeys()
    {
        const string prompt =
            "A photorealistic 35mm shot of the shed at p.m. light, the workbench in the foreground.";

        Assert.Empty(SceneImagePromptContractValidator.ValidateCaptionShape(prompt));
    }

    [Fact]
    public void ValidateCaptionShape_RefusesABulletedOrNumberedList()
    {
        const string bulleted =
            "A photorealistic medium shot. Elements: - the woman, bent over the workbench. - the slatted light. "
            + "- the workbench boards.";

        var bullet = Assert.Single(SceneImagePromptContractValidator.ValidateCaptionShape(bulleted));
        Assert.True(bullet.Blocking);
        Assert.Contains("list item", bullet.Detail, StringComparison.Ordinal);

        const string numbered =
            "A photorealistic medium shot. 1. the woman, bent over the workbench. 2. the slatted light.";

        var numbering = Assert.Single(SceneImagePromptContractValidator.ValidateCaptionShape(numbered));
        Assert.True(numbering.Blocking);
    }

    [Fact]
    public void CollapseToSingleParagraph_JoinsLinesAndSqueezesWhitespace()
    {
        Assert.Equal(
            "one two three four",
            NaturalLanguageSceneImagePromptBuilder.CollapseToSingleParagraph("one two\n\nthree\r\n  four  "));
    }

    /// <summary>
    /// The camera holder's body must be IN the caption. Before this check the operator's own sentence about the man
    /// behind her was appended to the finished prompt, and the compiled caption described the woman alone.
    /// </summary>
    [Fact]
    public void ValidateCameraHolderBody_RefusesACaptionThatDescribesNobodyBehindTheSubject()
    {
        const string parts = "a man is clearly a separate person entering the frame from behind, his hands grip her "
            + "hips, and his penis rests against the cleft of her buttocks.";
        const string caption =
            "A medium shot in eye level, first-person point of view, captures a woman bent over a gritty workbench with "
            + "her palms flat on the boards, her bare back toward the lens.";

        var violation = Assert.Single(SceneImagePromptContractValidator.ValidateCameraHolderBody(caption, parts));

        Assert.Equal("the camera holder's visible body", violation.Element);
        Assert.True(violation.Blocking);
        Assert.Contains("INSIDE the caption", violation.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCameraHolderBody_AcceptsTheBodyStatedInsideTheCaption()
    {
        const string parts = "a man is clearly a separate person entering the frame from behind, his hands grip her "
            + "hips, and his penis rests against the cleft of her buttocks.";
        const string caption =
            "A medium shot at eye level from behind a woman bent over a gritty workbench, and a man behind her whose "
            + "hands grip her hips while his penis rests against the cleft of her buttocks.";

        Assert.Empty(SceneImagePromptContractValidator.ValidateCameraHolderBody(caption, parts));
    }

    [Fact]
    public void ValidateCameraHolderBody_IsSilentWhenTheFrameShowsNoHolderBody()
    {
        Assert.Empty(SceneImagePromptContractValidator.ValidateCameraHolderBody("a shed", parts: null));
        Assert.Empty(SceneImagePromptContractValidator.ValidateCameraHolderBody("a shed", "   "));
    }

    /// <summary>
    /// The operator writes a NOTE, not a prompt: "hands on her hips". The check must not demand those exact words — it
    /// asks whether a second body is in the caption at all, so a legitimate re-phrasing still passes.
    /// </summary>
    [Fact]
    public void ValidateCameraHolderBody_AcceptsATerseNoteExpandedInDifferentWords()
    {
        const string caption =
            "A medium shot from behind a woman bent over a gritty workbench, with a man's palms at her waist in the "
            + "lower part of the frame, his forearms crossing into the picture from behind her.";

        Assert.Empty(SceneImagePromptContractValidator.ValidateCameraHolderBody(caption, "hands on her hips"));
    }

    [Fact]
    public void ValidateCameraHolderBody_RefusesATerseNoteWhenNobodyIsBehindTheSubject()
    {
        const string caption =
            "A medium shot from behind a woman bent over a gritty workbench with her palms flat on the boards, her bare "
            + "back toward the lens, alone in the frame.";

        var violation = Assert.Single(SceneImagePromptContractValidator.ValidateCameraHolderBody(caption, "hands on her hips"));

        Assert.True(violation.Blocking);
        Assert.Contains("INSIDE the caption", violation.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The caption must attribute the holder's body to a SECOND person. Live failure 2026-10-25: the note said "hands on
    /// Becky's thighs" and the caption wrote "hands clenching tightly around her thighs to steady herself" — her own
    /// hands — and a sibling render drew no hands at all.
    /// </summary>
    [Fact]
    public void ValidateCameraHolderBody_RefusesPartsAttributedToTheSubject()
    {
        const string caption =
            "In front of me is Becky bent over at the waist with her torso low over the workbench boards; she has raised "
            + "hips, hands clenching tightly around her thighs to steady herself against a climax.";

        var violation = Assert.Single(SceneImagePromptContractValidator.ValidateCameraHolderBody(
            caption, "the camera holder's hands on Becky's thighs (clenching tightly)"));

        Assert.True(violation.Blocking);
        Assert.Contains("SECOND PERSON", violation.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCameraHolderBody_AcceptsPartsGivenToASecondPerson()
    {
        const string caption =
            "A tightly framed shot from behind Becky as she bends over the workbench, when a man stands behind her with "
            + "his hands closing on her thighs, his hips level with hers.";

        Assert.Empty(SceneImagePromptContractValidator.ValidateCameraHolderBody(
            caption, "the camera holder's hands on Becky's thighs (clenching tightly)"));
    }

    /// <summary>
    /// The projection's system rule says third person; the compiler wrote "In front of me … my eye-level camera … behind
    /// us" anyway (prompt 50e778e5, 2026-10-25). A first-person caption reads as a selfie to the image model.
    /// </summary>
    [Fact]
    public void ValidateNoFrameExclusions_RefusesACaptionThatDescribesTheCrop()
    {
        // The live failure (SceneImage 8bb09d13, 2026-10-25): the caption told the image model to cut her head off for a
        // frame that could contain the whole body.
        const string caption =
            "A close-up shot from above. Her head is not visible due to being positioned below the frame line where the "
            + "image cuts off at about waist height.";

        var violation = Assert.Single(SceneImagePromptContractValidator.ValidateNoFrameExclusions(caption));

        Assert.True(violation.Blocking);
        Assert.Contains("outside the picture", violation.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A close view from above in which her whole body lies across the frame, head low over the bench.")]
    [InlineData("Her back is to the lens and her face is turned away toward the tin wall.")]
    public void ValidateNoFrameExclusions_AcceptsWhatTheFrameContains(string caption)
        => Assert.Empty(SceneImagePromptContractValidator.ValidateNoFrameExclusions(caption));

    [Fact]
    public void ValidateThirdPerson_RefusesAFirstPersonShotDescription()
    {
        const string caption =
            "In front of me is Becky bent over at the waist; her back faces away from my eye-level camera as I stand "
            + "behind her, the tin wall close behind us.";

        var violation = Assert.Single(SceneImagePromptContractValidator.ValidateThirdPerson(caption));

        Assert.True(violation.Blocking);
        Assert.Contains("FIRST person", violation.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A tightly framed shot from behind Becky as she bends over the workbench, her back to the lens.")]
    [InlineData("Camera: medium shot at eye level from behind Becky; the workbench boards fill the lower frame.")]
    public void ValidateThirdPerson_AcceptsAThirdPersonDescription(string caption)
        => Assert.Empty(SceneImagePromptContractValidator.ValidateThirdPerson(caption));

    [Fact]
    public void Validate_NamesACharacterWithNoAnchor()
    {
        var violations = SceneImagePromptContractValidator.Validate(
            "Becky kneels in the CENTER while the other stands LEFT.",
            Projection(),
            bindings: null,
            textDescribedNames: null);

        var violation = Assert.Single(violations);
        Assert.Equal("the character's name", violation.Element);
        Assert.Equal("Becky", violation.Person);
    }

    [Fact]
    public void Validate_AcceptsANameAnchoredByABoundReference()
    {
        var violations = SceneImagePromptContractValidator.Validate(
            "The person on the LEFT, Becky, kneels in the CENTER.",
            Projection(),
            bindings: [Binding("p-becky")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Validate_AcceptsANameThePromptDescribes()
    {
        var violations = SceneImagePromptContractValidator.Validate(
            "Becky, a woman with red hair, kneels in the CENTER beside the person on the LEFT.",
            Projection(),
            bindings: null,
            textDescribedNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Becky" });

        Assert.Empty(violations);
    }

    [Fact]
    public void Validate_RefusesAPersonTheFramePutsOutOfFrame()
    {
        var violations = SceneImagePromptContractValidator.Validate(
            "The person on the LEFT, Becky, kneels in the CENTER; Dean watches from the yard.",
            Projection(deanOutOfFrame: true),
            bindings: [Binding("p-becky"), Binding("p-dean")],
            textDescribedNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Becky", "Dean" });

        var violation = Assert.Single(violations);
        Assert.Equal("a person who is out of frame", violation.Element);
        Assert.Equal("Dean", violation.Person);
    }

    [Fact]
    public void Validate_RefusesAMissingLeftPlacement()
    {
        var violations = SceneImagePromptContractValidator.Validate(
            "The person in the CENTER kneels; the other stands behind them.",
            Projection(),
            bindings: [Binding("p-becky")],
            textDescribedNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Becky", "Dean" });

        Assert.Contains(violations, violation => violation.Element == "frame placement" && violation.Detail.Contains("LEFT"));
    }

    /// <summary>
    /// A room RESTATEMENT is not a blocking fault: the braindump requires the setting to be mentionable
    /// ("two people in the yard"), and judging a paraphrase is not something prose supports. Only contradictions that
    /// are certainly wrong block a render.
    /// </summary>
    [Fact]
    public void Validate_WithARestatedRoom_ReturnsNoBlockingViolation()
    {
        var violations = SceneImagePromptContractValidator.Validate(
            "The person on the LEFT kneels in the CENTER of the shed behind the trailers.",
            Projection(sceneSuppliedByReference: true),
            bindings: [Binding("p-becky")],
            textDescribedNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Becky", "Dean" });

        Assert.DoesNotContain(violations, violation => violation.Blocking);
    }

    [Fact]
    public void Validate_RefusesAMissingSettingKindWhenTheLocationImageOwnsTheRoom()
    {
        var violations = SceneImagePromptContractValidator.Validate(
            "The person on the LEFT kneels in the CENTER; another figure waits at the edge.",
            Projection(sceneSuppliedByReference: true),
            bindings: [Binding("p-becky")],
            textDescribedNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Becky", "Dean" });

        Assert.Contains(violations, violation => violation.Element == "the setting's kind");
    }

    [Fact]
    public void Validate_WithNoProjection_ChecksNothing()
    {
        Assert.Empty(SceneImagePromptContractValidator.Validate("Anything at all.", null, null));
    }

    /// <summary>
    /// The operator's own camera wording must survive the compile (measured 2026-10-21, prompt record 8466c595: the
    /// intent said "close up tight shot … viewed from below … looking up" and the compiled prompt had no shot at all).
    /// </summary>
    [Fact]
    public void ValidateOperatorCamera_RequiresTheOperatorsShotWordingToSurvive()
    {
        const string intent = "a man standing in a shed, close up tight shot viewed from below looking up at his body";

        var missing = SceneImagePromptContractValidator.ValidateOperatorCamera(
            "A man stands in a shed wearing a dark shirt.", intent);

        Assert.Contains(missing, violation => violation.Blocking);
        Assert.Contains(missing, violation => violation.Element.Contains("camera", StringComparison.OrdinalIgnoreCase));

        var kept = SceneImagePromptContractValidator.ValidateOperatorCamera(
            "Close up tight shot viewed from below looking up: a man stands in a shed.", intent);

        Assert.Empty(kept);
    }

    [Fact]
    public void ValidateOperatorCamera_WithNoCameraWordingInTheIntent_ChecksNothing()
    {
        Assert.Empty(SceneImagePromptContractValidator.ValidateOperatorCamera(
            "A man stands in a shed.", "a man standing in a shed"));
    }

    /// <summary>
    /// The operator's intent is a camera DECISION, not a lexeme. Record 178650a5 failed a legitimate compile because
    /// the operator wrote "from below his waist … low angle looking up" and the prompt stated "low angle" — the same
    /// instruction in different words.
    /// </summary>
    [Theory]
    [InlineData("low angle")]
    [InlineData("from below")]
    [InlineData("looking up")]
    [InlineData("shot from a low angle looking up")]
    public void ValidateOperatorCamera_AcceptsAnyWordingForTheSameAngle(string promptWording)
    {
        const string intent = "Dean stands directly over the camera, shot from below his waist at close range, low angle looking up.";

        Assert.Empty(SceneImagePromptContractValidator.ValidateOperatorCamera(
            $"Close up {promptWording}: a man stands in a shed.", intent));
    }

    [Fact]
    public void ValidateOperatorCamera_StillRequiresTheOppositeAngleNotJustAnyCameraWord()
    {
        const string intent = "a man standing in a shed, shot from below looking up";

        var violations = SceneImagePromptContractValidator.ValidateOperatorCamera(
            "High angle from above: a man stands in a shed.", intent);

        Assert.Contains(violations, violation => violation.Blocking);
        Assert.Contains("angle from below", violations[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void CameraInstructionsIn_ReadsTheStandardsVocabulary()
    {
        var phrases = SceneImagePromptContractValidator.CameraInstructionsIn(
            "close up tight shot viewed from below, looking up at his face");

        Assert.Contains("close up", phrases);
        Assert.Contains("from below", phrases);
        Assert.Contains("looking up", phrases);
    }

    [Fact]
    public void BuildCorrectiveInstruction_NamesEveryBlockingViolation()
    {
        var instruction = SceneImagePromptContractValidator.BuildCorrectiveInstruction(
        [
            new PromptContractViolation("frame placement", null, "State a LEFT placement.", Blocking: true),
            new PromptContractViolation("the character's name", "Becky", "Do not name Becky.", Blocking: true)
        ]);

        Assert.Contains("CORRECTIVE PASS", instruction, StringComparison.Ordinal);
        Assert.Contains("State a LEFT placement.", instruction, StringComparison.Ordinal);
        Assert.Contains("Do not name Becky.", instruction, StringComparison.Ordinal);
        Assert.Contains("person: Becky", instruction, StringComparison.Ordinal);
    }

    /// <summary>
    /// An advisory violation must never cost a second generation or lengthen the prompt — that is exactly what turned
    /// a slightly loose caption into a failed compile and then into an over-length one (measured 2026-10-20).
    /// </summary>
    [Fact]
    public void BuildCorrectiveInstruction_IgnoresAdvisoryViolations()
    {
        var instruction = SceneImagePromptContractValidator.BuildCorrectiveInstruction(
        [
            new PromptContractViolation("frame placement", null, "State a LEFT placement."),
            new PromptContractViolation("the setting's kind", null, "Name the setting.")
        ]);

        Assert.Empty(instruction);
    }

    /// <summary>
    /// The reported failure (SceneImage a53cd560, 2026-10-27): two people bound plus a location, so the location is the
    /// THIRD tag. The compiler copied the anchored contract's example — written for a one-person scene, where the place
    /// is &lt;image2&gt; — and pointed the shed at a person's body plate; the render, independently, named &lt;image3&gt; as
    /// the place. One prompt, two contradictory claims about which image is the room.
    /// </summary>
    [Fact]
    public void ValidateReferenceTags_RefusesThePlacePointedAtAPersonsPlate()
    {
        var violations = SceneImagePromptContractValidator.ValidateReferenceTags(
            "A photorealistic medium shot at eye level of a woman <image1> bending over a workbench in a shed <image2>, "
            + "her feet flat on the ground and her forearms along the boards.",
            TwoPeopleAndALocation(),
            SceneImageModelFamily.QwenImage21);

        Assert.Equal(2, violations.Count);

        // The mis-pointed place is what REFUSES the caption: that is the contradiction the image model would obey.
        var blocking = Assert.Single(violations, violation => violation.Blocking);
        Assert.Contains("puts the setting \"shed\" on <image2>", blocking.Detail, StringComparison.Ordinal);
        // The corrective pass is told the tag that IS the room, not merely that something is wrong.
        Assert.Contains("<image3>", blocking.Detail, StringComparison.Ordinal);

        // The omission is recorded as a diagnostic so the operator sees it, but it does not refuse a compile: an
        // action-only User Intent draft legitimately never names the place.
        Assert.Single(violations, violation => !violation.Blocking);
    }

    /// <summary>
    /// A reference-bound User Intent draft is ACTION ONLY by contract — the location image supplies the room, so a
    /// caption that never names the place is correct and must not cost a retry or a failed compile.
    /// </summary>
    [Fact]
    public void ValidateReferenceTags_DoesNotRefuseAnActionOnlyDraftThatNeverNamesThePlace()
    {
        var violations = SceneImagePromptContractValidator.ValidateReferenceTags(
            "A photorealistic close shot of the woman <image1> turned away from the man <image2>, her shoulders hunched.",
            TwoPeopleAndALocation(),
            SceneImageModelFamily.QwenImage21);

        Assert.DoesNotContain(violations, violation => violation.Blocking);
    }

    [Fact]
    public void ValidateReferenceTags_AcceptsThePlaceNamedWithTheLocationTag()
    {
        var violations = SceneImagePromptContractValidator.ValidateReferenceTags(
            "A photorealistic medium shot at eye level of a woman <image1> beside a man <image2> in a shed <image3>.",
            TwoPeopleAndALocation(),
            SceneImageModelFamily.QwenImage21);

        Assert.Empty(violations);
    }

    /// <summary>
    /// A relation phrase is a fact about two people, not a claim that a person's photograph is the room. "In front of
    /// &lt;image2&gt;" must not read as "the setting is &lt;image2&gt;".
    /// </summary>
    [Fact]
    public void ValidateReferenceTags_DoesNotTreatARelationPhraseAsAPlace()
    {
        var violations = SceneImagePromptContractValidator.ValidateReferenceTags(
            "A photorealistic shot of a woman <image1> standing in front of a man <image2>, the room <image3> behind them.",
            TwoPeopleAndALocation(),
            SceneImageModelFamily.QwenImage21);

        Assert.Empty(violations);
    }

    /// <summary>
    /// Only the family whose CONTRACT writes tags into the caption is checked. The other families state the reference
    /// block at render time and their compiled prose is tag-free by design, so a missing tag there is not a defect.
    /// </summary>
    [Fact]
    public void ValidateReferenceTags_IsNotAppliedToATagFreeFamily()
    {
        Assert.Empty(SceneImagePromptContractValidator.ValidateReferenceTags(
            "A photorealistic shot of a woman bending over a bench in a shed.",
            TwoPeopleAndALocation(),
            SceneImageModelFamily.Krea2));
    }

    /// <summary>
    /// One image in total is sent UNTAGGED (the vendor's N = 1 rule), so there is no number for the caption to point
    /// anywhere and nothing to check.
    /// </summary>
    [Fact]
    public void ValidateReferenceTags_SkipsASingleReferenceThatIsNeverNumbered()
    {
        Assert.Empty(SceneImagePromptContractValidator.ValidateReferenceTags(
            "A photorealistic shot of a gritty shed interior.",
            [LocationBinding(1)],
            SceneImageModelFamily.QwenImage21));
    }

    private static IReadOnlyList<ReferenceApplicationSelection> TwoPeopleAndALocation() =>
    [
        new ReferenceApplicationSelection
        {
            ElementKey = "Body",
            Kind = nameof(ImageStepSlotKind.Body),
            SemanticRole = "character body",
            ActorKey = "p0",
            Source = "IdentityPackAsset",
            Strategy = "NativeMultiReference",
            Ordinal = 1,
            IdentityPackId = "pack-1",
            ReferenceAssetId = "asset-p0",
            ReferenceLabel = "Profile Left · Unclothed"
        },
        new ReferenceApplicationSelection
        {
            ElementKey = "Body",
            Kind = nameof(ImageStepSlotKind.Body),
            SemanticRole = "character body",
            ActorKey = "p1",
            Source = "IdentityPackAsset",
            Strategy = "NativeMultiReference",
            Ordinal = 2,
            IdentityPackId = "pack-1",
            ReferenceAssetId = "asset-p1",
            ReferenceLabel = "Profile Left · Clothed"
        },
        LocationBinding(3)
    ];

    private static ReferenceApplicationSelection LocationBinding(int ordinal) => new()
    {
        ElementKey = "Location",
        Kind = nameof(ImageStepSlotKind.Location),
        SemanticRole = "location continuity",
        Strategy = "NativeMultiReference",
        Ordinal = ordinal,
        SceneAssetId = "asset-loc",
        SceneAssetImageId = "img-loc",
        ReferenceLabel = "Indoor Back"
    };

    private static ReferenceApplicationSelection Binding(string actorKey) => new()
    {
        ElementKey = "Identity",
        Kind = nameof(ImageStepSlotKind.Face),
        ActorKey = actorKey,
        Source = "IdentityPackAsset",
        Strategy = "NativeMultiReference",
        ReferenceAssetId = "asset-1",
        IdentityPackId = "pack-1"
    };

    private static SceneImagePovProjection.Projection Projection(
        bool sceneSuppliedByReference = false,
        bool deanOutOfFrame = false)
    {
        var inFrame = new List<SceneImagePovProjection.ProjectedCharacter>
        {
            new("Becky", "p-becky", "the shed", "Kneeling", "Kneeling", "forward", "Nude")
        };
        var outOfFrame = new List<string> { "Carter" };
        var subjects = new List<SceneBlockingSubject>
        {
            new("p-becky", new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), "the person in the CENTER")
        };

        if (deanOutOfFrame)
        {
            outOfFrame.Add("Dean");
        }
        else
        {
            inFrame.Add(new SceneImagePovProjection.ProjectedCharacter(
                "Dean", "p-dean", "the shed", "Standing", "Standing", "down", "Jeans"));
            subjects.Add(new SceneBlockingSubject(
                "p-dean", new FramePosition(FrameHorizontal.Left, FrameDepth.Foreground), "the person on the LEFT"));
        }

        return new SceneImagePovProjection.Projection(
            Pov: SceneImagePovFramer.Omniscient,
            IsOmniscient: true,
            SceneLocation: "Shed behind the trailers",
            SuppressSceneLocation: sceneSuppliedByReference,
            SceneSuppliedByReference: sceneSuppliedByReference,
            UnseenViewpointLocation: null,
            UnseenViewpointStance: null,
            UnseenViewpointSightline: null,
            InFrame: inFrame,
            OutOfFrame: outOfFrame,
            DirectlyAhead: [],
            Frame: new SceneBlocking(
                "1",
                subjects,
                new SceneBlockingCamera("eye level", "medium shot", null),
                subjects.Select(subject => subject.SubjectKey).ToList(),
                "rationale"),
            LocationType: "Shed behind the trailers");
    }
}
