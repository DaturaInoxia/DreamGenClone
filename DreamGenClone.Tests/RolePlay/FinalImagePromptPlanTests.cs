using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-157 — the single assembly + validation point for a final image prompt, and the reader that gets the brief's
/// framing back out of its snapshot.
/// </summary>
public sealed class FinalImagePromptPlanTests
{
    private static ReferenceRoleClauses.ReferenceRole Role(
        ImageStepSlotKind kind, string? subjectKey, string label) => new(kind, label, subjectKey);

    private static SceneBlocking Blocking() => new(
        "2",
        [
            new SceneBlockingSubject(
                "p0", new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), "the person in the CENTER in the foreground"),
            new SceneBlockingSubject(
                "p1", new FramePosition(FrameHorizontal.Left, FrameDepth.Background), "the person on the LEFT in the background")
        ],
        new SceneBlockingCamera("eye level", "medium shot", null),
        ["p0", "p1"],
        "A wide two-shot.");

    private static IReadOnlyList<ReferenceRoleClauses.ReferenceRole> TwoCharacters() =>
    [
        Role(ImageStepSlotKind.Face, "p0", "Front · Clothed"),
        Role(ImageStepSlotKind.Body, "p0", "Body · Front"),
        Role(ImageStepSlotKind.Face, "p1", "Front · Clothed"),
        Role(ImageStepSlotKind.Location, null, "The shed")
    ];

    [Fact]
    public void Build_DerivesTheMapFromTheSameOrderedListAsTheClause()
    {
        var plan = FinalImagePromptPlanBuilder.Build("two figures at the bench", TwoCharacters(), blocking: Blocking());

        Assert.True(plan.IsValid);
        Assert.Equal(4, plan.References.Count);
        Assert.Equal("<image1>", plan.References[0].Tag);
        Assert.Equal("p0", plan.References[0].SubjectKey);
        Assert.Equal(new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), plan.References[0].Position);
        Assert.Equal("<image3>", plan.References[2].Tag);
        Assert.Equal("p1", plan.References[2].SubjectKey);
        // A scene-level reference belongs to no subject and holds no position.
        Assert.Null(plan.References[3].SubjectKey);
        Assert.Null(plan.References[3].Position);
        Assert.Contains("is the person in the CENTER", plan.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithNoBlocking_KeepsTheEstablishedPromptAndAnEmptyMapPositions()
    {
        var plan = FinalImagePromptPlanBuilder.Build("two figures at the bench", TwoCharacters());

        Assert.Contains("is the FACE reference", plan.Prompt, StringComparison.Ordinal);
        Assert.All(plan.References, entry => Assert.Null(entry.Position));
    }

    [Fact]
    public void Build_WithFewerThanTwoReferences_EmitsNoBlock()
    {
        var plan = FinalImagePromptPlanBuilder.Build(
            "a lone figure", [Role(ImageStepSlotKind.Face, "p0", "Front · Clothed")], blocking: Blocking());

        Assert.Empty(plan.References);
        Assert.StartsWith("a lone figure", plan.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<image", plan.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("REFERENCE IMAGES", plan.Prompt, StringComparison.Ordinal);

        // The frame's CAMERA is stated even with no reference block, because it belongs to the frame rather than to
        // the references: a render with one image (or none) is still taken at a configured distance, and that distance
        // is what decides how large the people come out (B-161).
        Assert.Contains("CAMERA — AUTHORITATIVE", plan.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HonoursTheEditOffsetInTheMap()
    {
        var plan = FinalImagePromptPlanBuilder.Build(
            "instruction", TwoCharacters(), ReferenceRoleClauses.LeadingImageCount.Edit, Blocking());

        // On an edit the source image is slot 1, so the first reference is <image2>.
        Assert.Equal(2, plan.References[0].Index);
        Assert.Equal("<image2>", plan.References[0].Tag);
        Assert.Equal("<image5>", plan.References[3].Tag);
    }

    [Fact]
    public void Build_RefusesBlockingThatPlacesTwoSubjectsTogether()
    {
        var broken = new SceneBlocking(
            "2",
            [
                new SceneBlockingSubject("p0", new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), "the person in the CENTER"),
                new SceneBlockingSubject("p1", new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), "the person in the CENTER")
            ],
            new SceneBlockingCamera("eye level", "medium shot", null),
            ["p0", "p1"],
            string.Empty);

        var error = Assert.Throws<InvalidOperationException>(
            () => FinalImagePromptPlanBuilder.Build("p", TwoCharacters(), blocking: broken));

        Assert.Contains("share the frame position", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RefusesOneSubjectSupplyingASlotTwice()
    {
        var roles = new List<ReferenceRoleClauses.ReferenceRole>
        {
            Role(ImageStepSlotKind.Face, "p0", "Front · Clothed"),
            Role(ImageStepSlotKind.Face, "p0", "Profile · Clothed")
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => FinalImagePromptPlanBuilder.Build("p", roles, blocking: Blocking()));

        Assert.Contains("supplies the Face slot 2 times", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RefusesMoreReferencesThanTheStepsBudget()
    {
        // The budget is NOT re-derived here: it comes from ReferenceSlotPlanner.EffectiveBudget, which caps a
        // pose-carrying step at 6 no matter what the model's acceptance limit says.
        var roles = new List<ReferenceRoleClauses.ReferenceRole>
        {
            Role(ImageStepSlotKind.Face, "p0", "a"),
            Role(ImageStepSlotKind.Body, "p0", "b"),
            Role(ImageStepSlotKind.Face, "p1", "c"),
            Role(ImageStepSlotKind.Body, "p1", "d"),
            Role(ImageStepSlotKind.Wardrobe, "p0", "e"),
            Role(ImageStepSlotKind.Location, null, "f"),
            Role(ImageStepSlotKind.Pose, null, "g")
        };

        var error = Assert.Throws<InvalidOperationException>(() => FinalImagePromptPlanBuilder.Build(
            "p", roles, blocking: Blocking(), maxReferences: 10,
            filledSlotKinds: roles.Select(role => role.SlotKind!.Value).ToList()));

        Assert.Contains("budget is 6", error.Message, StringComparison.Ordinal);
        Assert.Contains("pose reference is carried", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ReportsWithoutThrowing_SoThePreviewCanShowIt()
    {
        var roles = new List<ReferenceRoleClauses.ReferenceRole>
        {
            Role(ImageStepSlotKind.Face, "p0", "a"),
            Role(ImageStepSlotKind.Face, "p0", "b")
        };

        var issues = FinalImagePromptPlanBuilder.Validate(roles, blocking: Blocking());

        Assert.Single(issues);
    }

    /// <summary>
    /// ONE reference that has something stated about it: the plan describes it and shows it in the map WITHOUT a
    /// number, because the model receives no tag.
    ///
    /// <para>
    /// The map row is the point — the preview is where an operator checks what the prompt says about each image before
    /// spending a render, and a step with a single, unnumbered reference is exactly the case where the prompt and the
    /// map can disagree about whether that image is described at all (B-160 follow-up, 2026-10-13).
    /// </para>
    /// </summary>
    [Fact]
    public void Build_WithASingleStatementCarryingReference_ShowsItUntagged()
    {
        ReferenceRoleClauses.ReferenceRole[] roles =
        [
            new(ImageStepSlotKind.Location, "The yard",
                Directions: new ReferenceDirections(null, null, "a clothesline down the middle"))
        ];

        var plan = FinalImagePromptPlanBuilder.Build("wide shot", roles);

        Assert.True(plan.IsValid);
        Assert.Single(plan.References);
        Assert.Equal(ReferenceRoleClauses.UntaggedImageLabel, plan.References[0].Tag);
        Assert.Equal(ImageStepSlotKind.Location, plan.References[0].SlotKind);
        Assert.Contains("a clothesline down the middle", plan.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<image", plan.Prompt, StringComparison.Ordinal);
    }

    /// <summary>A single reference with nothing stated keeps the map empty and the prompt untouched.</summary>
    [Fact]
    public void Build_WithASingleUnstatedReference_LeavesThePromptAndTheMapAlone()
    {
        ReferenceRoleClauses.ReferenceRole[] roles = [Role(ImageStepSlotKind.Face, "p0", "Front · Clothed")];

        var plan = FinalImagePromptPlanBuilder.Build("wide shot", roles);

        Assert.Empty(plan.References);
        Assert.Equal("wide shot", plan.Prompt);
    }

    [Fact]
    public void SceneBlockingJson_ReadsBackWhatTheCompilerWrites()
    {
        // The exact shape the compiler writes: camelCase members, enums as strings (JsonStringEnumConverter).
        const string snapshot = """
            {"frozenState":{},"typedReferences":[],"blocking":{"profileVersion":"2","subjects":[{"subjectKey":"p0","position":{"horizontal":"Center","depth":"Foreground"},"roleText":"the person in the CENTER in the foreground"}],"camera":{"angle":"eye level","shotSize":"medium shot","side":null},"layering":["p0"],"rationale":"A wide two-shot."}}
            """;

        var blocking = SceneBlockingJson.TryRead(snapshot);

        Assert.NotNull(blocking);
        Assert.Equal("2", blocking!.ProfileVersion);
        Assert.Equal(new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), Assert.Single(blocking.Subjects).Position);
        Assert.Equal("medium shot", blocking.Camera.ShotSize);
        Assert.Equal("A wide two-shot.", blocking.Rationale);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"frozenState\":{}}")]
    [InlineData("{\"blocking\":null}")]
    [InlineData("{\"blocking\":{\"profileVersion\":\"1\",\"subjects\":[]}}")]
    public void SceneBlockingJson_ReturnsNullWhenTheBriefCarriesNoFrame(string? snapshot)
    {
        Assert.Null(SceneBlockingJson.TryRead(snapshot));
    }

    /// <summary>
    /// B-161: the operator's framing is written into the brief the prompt-writing model reads, so the prose it writes
    /// names the camera the render will actually use.
    ///
    /// <para>
    /// The reported failure was exactly this duplication: the model read the compiled "medium shot" out of the brief
    /// and wrote it into the prompt, while the render appended the operator's frame — one prompt, two cameras, and a
    /// subject marked "in the background" still rendered at full height.
    /// </para>
    /// </summary>
    [Fact]
    public void SceneBlockingJson_ReplaceWritesTheEffectiveFrameAndKeepsEveryOtherMember()
    {
        const string snapshot = """
            {"frozenState":{"location":"the yard"},"typedReferences":["identity-p0"],"blocking":{"profileVersion":"2","subjects":[{"subjectKey":"p0","position":{"horizontal":"Center","depth":"Foreground"},"roleText":"the person in the CENTER in the foreground"}],"camera":{"angle":"eye level","shotSize":"medium shot","side":null},"layering":["p0"],"rationale":"A wide two-shot."}}
            """;

        var effective = SceneBlockingResolver.ApplyOverride(
            SceneBlockingJson.TryRead(snapshot),
            new SceneBlockingOverride([], new SceneBlockingCameraOverride("wide establishing shot")));

        var replaced = SceneBlockingJson.Replace(snapshot, effective);

        Assert.Contains("\"shotSize\":\"wide establishing shot\"", replaced, StringComparison.Ordinal);
        Assert.DoesNotContain("medium shot", replaced, StringComparison.Ordinal);

        // Everything this class does not own survives untouched, because the render's provenance points at this brief.
        Assert.Contains("\"frozenState\":{\"location\":\"the yard\"}", replaced, StringComparison.Ordinal);
        Assert.Contains("\"typedReferences\":[\"identity-p0\"]", replaced, StringComparison.Ordinal);
        Assert.Contains("\"rationale\":\"A wide two-shot.\"", replaced, StringComparison.Ordinal);
        Assert.Equal("wide establishing shot", SceneBlockingJson.TryRead(replaced)!.Camera.ShotSize);
    }

    /// <summary>
    /// The writer only ever replaces a frame that is there. A brief with no <c>blocking</c> member, or one this code
    /// cannot parse, is returned EXACTLY as it was: there is no frame to correct, and inventing one would be the
    /// substitution this codebase forbids.
    /// </summary>
    [Theory]
    [InlineData("{\"frozenState\":{}}")]
    [InlineData("not json")]
    [InlineData("")]
    public void SceneBlockingJson_ReplaceLeavesASnapshotWithNoFrameExactlyAsItIs(string snapshot)
    {
        var effective = new SceneBlocking(
            "2",
            [new SceneBlockingSubject("p0", new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), "the person in the CENTER")],
            new SceneBlockingCamera("eye level", "wide establishing shot", null),
            ["p0"],
            "rationale");

        Assert.Equal(snapshot, SceneBlockingJson.Replace(snapshot, effective));
    }

    /// <summary>A null frame means there is nothing to write, so the brief passes through unchanged.</summary>
    [Fact]
    public void SceneBlockingJson_ReplaceWithNoFrameReturnsTheSnapshot()
    {
        const string snapshot = """{"blocking":{"profileVersion":"2"}}""";

        Assert.Equal(snapshot, SceneBlockingJson.Replace(snapshot, null));
    }

    /// <summary>
    /// B-161: the frame's camera is stated to the model at the END of the prompt, with what it means for scale WHEN THE
    /// SHOT CONTAINS DISTANCE.
    /// </summary>
    /// <remarks>
    /// The scale paragraph was measured on a WIDE shot; on a medium or close shot there is no distance to scale, so it
    /// is noise beside the subject (2026-10-21: the operator asked whether any of it was research-backed — the principle
    /// is, the paragraph is B-161's finding and only applies to a shot that contains distance).
    /// </remarks>
    [Fact]
    public void Build_StatesTheFramesCameraAndWhatItMeansForScale()
    {
        var plan = FinalImagePromptPlanBuilder.Build("two figures at the bench", TwoCharacters(), blocking: Blocking());

        Assert.Contains("CAMERA — AUTHORITATIVE", plan.Prompt, StringComparison.Ordinal);
        Assert.Contains("medium shot", plan.Prompt, StringComparison.Ordinal);
        // A medium shot has no distance to scale, so the wide-shot paragraph is not restated.
        Assert.DoesNotContain("scaled by how far they are from that camera", plan.Prompt, StringComparison.Ordinal);

        // The camera is the LAST thing the model reads, which is where the prompt's authority sits — the same place the
        // reference block already is.
        Assert.EndsWith("The subject fills the frame.", plan.Prompt.TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>
    /// ONE camera statement per prompt. A compiled prompt may already carry it (a participant POV states the camera,
    /// and the operator's framing is appended deterministically); appending a second from the compiled frame put two
    /// different cameras in one prompt and the LOWER one won — measured 2026-10-21, where the bottom line was always the
    /// profile's "medium shot" while the control read "close shot".
    /// </summary>
    [Fact]
    public void Build_DoesNotAppendASecondCameraWhenThePromptAlreadyStatesOne()
    {
        const string compiled = "Close shot at low angle. A man stands over the camera.\n\n"
            + "CAMERA — AUTHORITATIVE: this frame is a close shot at low angle. The subject fills the frame.";

        var plan = FinalImagePromptPlanBuilder.Build(compiled, [], blocking: Blocking());

        var occurrences = plan.Prompt.Split("CAMERA — AUTHORITATIVE").Length - 1;
        Assert.Equal(1, occurrences);
        // The FIRST statement survives — it is the one written from the POV projection and the operator's framing.
        Assert.Contains("this frame is a close shot", plan.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("this frame is a medium shot", plan.Prompt, StringComparison.Ordinal);
    }

    /// <summary>A shot that DOES contain distance keeps the scale instruction, which is what it was measured for.</summary>
    [Fact]
    public void Build_OnAWideShot_StillStatesTheScaleRule()
    {
        var blocking = Blocking() with { Camera = new SceneBlockingCamera("eye level", "wide establishing shot", null) };

        var plan = FinalImagePromptPlanBuilder.Build("two figures at the bench", TwoCharacters(), blocking: blocking);

        Assert.Contains("scaled by how far they are from that camera", plan.Prompt, StringComparison.Ordinal);
        Assert.EndsWith("at close range.", plan.Prompt.TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The framing clause states the frame the OPERATOR chose, not the compiled one, and it never invents a camera for
    /// a brief that carries no frame.
    /// </summary>
    [Fact]
    public void Build_StatesTheOverriddenCameraAndNothingWhenThereIsNoFrame()
    {
        var overridden = FinalImagePromptPlanBuilder.Build(
            "p",
            TwoCharacters(),
            blocking: SceneBlockingResolver.ApplyForPrompt(
                Blocking(),
                new SceneBlockingOverride([], new SceneBlockingCameraOverride("wide establishing shot"))));

        Assert.Contains("wide establishing shot", overridden.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("medium shot", overridden.Prompt, StringComparison.Ordinal);

        // No frame means no camera claim at all rather than a guessed one.
        Assert.DoesNotContain(
            "CAMERA — AUTHORITATIVE",
            FinalImagePromptPlanBuilder.Build("p", TwoCharacters()).Prompt,
            StringComparison.Ordinal);
        Assert.Empty(SceneFramingClause.LineFor(null));
    }
}
