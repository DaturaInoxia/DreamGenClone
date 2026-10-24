using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-157 — the frame-space blocking resolver.
///
/// <para>
/// These tests pin the two things a prompt cannot recover from: the acting subject takes the head of the configured
/// ladder (so it is in front), and no two subjects share a position. They also pin the refusals — an involvement the
/// beat did not declare, a participant with no key, or more subjects than the profile can seat must all fail loudly,
/// because a guessed placement is invisible in the finished image.
/// </para>
/// </summary>
public sealed class SceneBlockingResolverTests
{
    private static ImageStagingProfile Profile() => new()
    {
        Id = "staging-test",
        DisplayName = "Test staging",
        Surface = ImageStagingSurface.Composition,
        PlacementLadderJson = """["Center","Left","Right"]""",
        ActiveDepth = FrameDepth.Foreground,
        ObserverDepth = FrameDepth.Background,
        DefaultCameraAngle = "eye level",
        DefaultShotSize = "medium shot",
        OmniscientShotSize = "wide establishing shot",
        DefaultCameraHeight = CameraHeights.Eye,
        CameraKeywordMapJson = """
            [
              {"keyword":"wide","shotSize":"wide establishing shot"},
              {"keyword":"low","angle":"low angle"}
            ]
            """,
        Version = 3
    };

    private static SceneBlockingSubjectInput Active(string key) =>
        new(key, SceneBlockingResolver.ActiveInvolvement, "the shed", "at the workbench", "toward the door");

    /// <summary>
    /// The camera height is CONFIGURATION on the profile, snapshotted into the brief the way the shot size is — so a
    /// later profile edit cannot rewrite how a past render was framed.
    /// </summary>
    [Fact]
    public void Resolve_SnapshotsTheCameraHeightFromTheProfile()
    {
        var blocking = SceneBlockingResolver.Resolve(
            new SceneBlockingInput("A level composition.", [Active("p0")]), Profile());

        Assert.Equal(CameraHeights.Eye, blocking.Camera.Height);
    }

    /// <summary>
    /// A chosen height IS the aim: "floor level" and "low angle" are one instruction, and a frame that says one while
    /// stating the other is two instructions the model has to reconcile.
    /// </summary>
    [Fact]
    public void ApplyOverride_StatesTheAimThatAChosenHeightImplies()
    {
        var blocking = SceneBlockingResolver.Resolve(
            new SceneBlockingInput("A level composition.", [Active("p0")]), Profile());

        var applied = SceneBlockingResolver.ApplyOverride(
            blocking,
            new SceneBlockingOverride([], new SceneBlockingCameraOverride("medium shot", null, CameraHeights.Floor)));

        Assert.Equal(CameraHeights.Floor, applied.Camera.Height);
        Assert.Equal("low angle, viewed from below", applied.Camera.Angle);
    }

    /// <summary>An angle the operator named explicitly is their decision and outranks the height's implication.</summary>
    [Fact]
    public void ApplyOverride_KeepsAnExplicitAngleOverTheOneAHeightImplies()
    {
        var blocking = SceneBlockingResolver.Resolve(
            new SceneBlockingInput("A level composition.", [Active("p0")]), Profile());

        var applied = SceneBlockingResolver.ApplyOverride(
            blocking,
            new SceneBlockingOverride([], new SceneBlockingCameraOverride("medium shot", "high angle", CameraHeights.Floor)));

        Assert.Equal("high angle", applied.Camera.Angle);
        Assert.Equal(CameraHeights.Floor, applied.Camera.Height);
    }

    private static SceneBlockingSubjectInput Watching(string key) =>
        new(key, SceneBlockingResolver.ObserverInvolvement, "the shed", "by the slider", "toward the workbench");

    [Fact]
    public void Resolve_SeatsTheActiveSubjectFirstAndTheObserverBehind()
    {
        var input = new SceneBlockingInput(
            "The framing keeps both figures readable.",
            [Watching("p1"), Active("p0")]);

        var blocking = SceneBlockingResolver.Resolve(input, Profile());

        var active = blocking.Subjects.Single(subject => subject.SubjectKey == "p0");
        var observer = blocking.Subjects.Single(subject => subject.SubjectKey == "p1");
        Assert.Equal(new FramePosition(FrameHorizontal.Left, FrameDepth.Foreground), active.Position);
        Assert.Equal(new FramePosition(FrameHorizontal.Right, FrameDepth.Background), observer.Position);
        Assert.Equal("the person on the LEFT in the foreground, nearest the camera", active.RoleText);
        Assert.Equal("the person on the RIGHT in the background, farthest from the camera", observer.RoleText);
        Assert.Equal("3", blocking.ProfileVersion);
    }

    [Fact]
    public void Resolve_CentresALoneSubject()
    {
        var blocking = SceneBlockingResolver.Resolve(new SceneBlockingInput(null, [Active("p0")]), Profile());

        Assert.Equal(new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), Assert.Single(blocking.Subjects).Position);
    }

    [Fact]
    public void Resolve_PlacesAPairOnTheOppositeEdges() =>
        Assert.Equal(
            new[] { FrameHorizontal.Left, FrameHorizontal.Right },
            SceneBlockingResolver.Resolve(new SceneBlockingInput(null, [Active("p0"), Watching("p1")]), Profile())
                .Subjects.Select(subject => subject.Position.Horizontal));

    [Fact]
    public void Resolve_FillsLeftCentreRightForThreeSubjects() =>
        Assert.Equal(
            new[] { FrameHorizontal.Left, FrameHorizontal.Center, FrameHorizontal.Right },
            SceneBlockingResolver
                .Resolve(new SceneBlockingInput(null, [Active("p0"), Watching("p1"), Watching("p2")]), Profile())
                .Subjects.Select(subject => subject.Position.Horizontal));

    [Fact]
    public void ReseatForCast_LeavesALoneSubjectCentredAndSaysTheSeatsWereFitted()
    {
        // The compiled frame seats two people LEFT/RIGHT. One of them turns out not to be in the picture (a participant
        // POV is the camera; an Omniscient still drops a remote observer), so the survivor belongs in the CENTRE.
        var compiled = SceneBlockingResolver.Resolve(
            new SceneBlockingInput(null, [Active("p0"), Active("p1")]), Profile());
        Assert.Equal(FrameHorizontal.Left, compiled.Subjects[0].Position.Horizontal);

        var fitted = SceneBlockingResolver.ReseatForCast(compiled, ["p0"]);

        var subject = Assert.Single(fitted.Subjects);
        Assert.Equal(FrameHorizontal.Center, subject.Position.Horizontal);
        Assert.Equal("the person in the CENTER in the foreground, nearest the camera", subject.RoleText);
        Assert.Equal($"{compiled.ProfileVersion}+frame", fitted.ProfileVersion);
        Assert.Equal(["p0"], fitted.Layering);
    }

    [Fact]
    public void ReseatForCast_KeepsAPairOnTheOppositeEdges()
    {
        var compiled = SceneBlockingResolver.Resolve(
            new SceneBlockingInput(null, [Active("p0"), Watching("p1"), Watching("p2")]), Profile());

        var fitted = SceneBlockingResolver.ReseatForCast(compiled, ["p0", "p1"]);

        Assert.Equal(
            new[] { FrameHorizontal.Left, FrameHorizontal.Right },
            fitted.Subjects.Select(subject => subject.Position.Horizontal));
        // The acting subject keeps the head seat.
        Assert.Equal("p0", fitted.Subjects[0].SubjectKey);
    }

    [Fact]
    public void ReseatForCast_ReturnsTheSameFrameWhenNobodyIsDropped()
    {
        var compiled = SceneBlockingResolver.Resolve(
            new SceneBlockingInput(null, [Active("p0"), Watching("p1")]), Profile());

        Assert.Same(compiled, SceneBlockingResolver.ReseatForCast(compiled, ["p0", "p1"]));
    }

    [Fact]
    public void Resolve_RefusesAProfileWhoseLadderCannotBalanceTheCast()
    {
        // A pair belongs on the opposite edges, so a ladder with no RIGHT cannot seat one without seating both
        // subjects on the same side of the frame — which is the framing this rule exists to stop.
        var profile = Profile();
        profile.PlacementLadderJson = """["Left","Center"]""";

        var error = Assert.Throws<InvalidOperationException>(
            () => SceneBlockingResolver.Resolve(new SceneBlockingInput(null, [Active("p0"), Active("p1")]), profile));

        Assert.Contains("Right", error.Message, StringComparison.Ordinal);
        Assert.Contains("placement ladder", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_GivesTwoActiveSubjectsDistinctPositions()
    {
        var input = new SceneBlockingInput(null, [Active("p0"), Active("p1")]);

        var blocking = SceneBlockingResolver.Resolve(input, Profile());

        Assert.Equal(FrameHorizontal.Left, blocking.Subjects[0].Position.Horizontal);
        Assert.Equal(FrameHorizontal.Right, blocking.Subjects[1].Position.Horizontal);
        Assert.Equal(
            blocking.Subjects.Count,
            blocking.Subjects.Select(subject => subject.Position).Distinct().Count());
    }

    [Fact]
    public void Resolve_LayersForegroundBeforeBackground()
    {
        var input = new SceneBlockingInput(null, [Watching("p1"), Active("p0")]);

        var blocking = SceneBlockingResolver.Resolve(input, Profile());

        Assert.Equal(new[] { "p0", "p1" }, blocking.Layering);
    }

    [Fact]
    public void Resolve_CarriesTheRationaleVerbatim()
    {
        const string rationale = "  A wide two-shot from the doorway.  ";
        var input = new SceneBlockingInput(rationale, [Active("p0")]);

        var blocking = SceneBlockingResolver.Resolve(input, Profile());

        Assert.Equal(rationale.Trim(), blocking.Rationale);
    }

    [Fact]
    public void Resolve_TakesTheShotSizeFromARecognisedKeyword()
    {
        var input = new SceneBlockingInput("A wide two-shot from the doorway.", [Active("p0")]);

        var blocking = SceneBlockingResolver.Resolve(input, Profile());

        Assert.Equal("wide establishing shot", blocking.Camera.ShotSize);
        Assert.Equal("eye level", blocking.Camera.Angle);
    }

    [Fact]
    public void Resolve_TakesTheAngleFromARecognisedKeywordAndKeepsTheDefaultShot()
    {
        var input = new SceneBlockingInput("A low view across the floor.", [Active("p0")]);

        var blocking = SceneBlockingResolver.Resolve(input, Profile());

        Assert.Equal("low angle", blocking.Camera.Angle);
        Assert.Equal("medium shot", blocking.Camera.ShotSize);
    }

    [Fact]
    public void Resolve_FallsBackToTheConfiguredCameraWhenNoKeywordMatches()
    {
        var input = new SceneBlockingInput("Two figures at the bench.", [Active("p0")]);

        var blocking = SceneBlockingResolver.Resolve(input, Profile());

        Assert.Equal("eye level", blocking.Camera.Angle);
        Assert.Equal("medium shot", blocking.Camera.ShotSize);
        Assert.Null(blocking.Camera.Side);
    }

    [Fact]
    public void ApplyForPrompt_AppliesTheConfiguredOmniscientLeanWhenTheOperatorNamesNoFraming()
    {
        // B-166 D7: the omniscient POV leans wide/establishing, from configured data.
        var blocking = SceneBlockingResolver.Resolve(
            new SceneBlockingInput("The framing keeps both figures readable.", [Active("p0")]), Profile());

        var leaned = SceneBlockingResolver.ApplyForPrompt(blocking, null, "wide establishing shot");

        Assert.Equal("wide establishing shot", leaned.Camera.ShotSize);
    }

    [Fact]
    public void ApplyForPrompt_KeepsTheOperatorsFramingOverTheOmniscientLean()
    {
        var blocking = SceneBlockingResolver.Resolve(
            new SceneBlockingInput("The framing keeps both figures readable.", [Active("p0")]), Profile());
        var overrides = new SceneBlockingOverride([], new SceneBlockingCameraOverride("close shot"));

        var leaned = SceneBlockingResolver.ApplyForPrompt(blocking, overrides, "wide establishing shot");

        Assert.Equal("close shot", leaned.Camera.ShotSize);
    }

    [Fact]
    public void ApplyForPrompt_WithoutAnOmniscientLean_KeepsTheCompiledShotSize()
    {
        var blocking = SceneBlockingResolver.Resolve(
            new SceneBlockingInput("The framing keeps both figures readable.", [Active("p0")]), Profile());

        var unchanged = SceneBlockingResolver.ApplyForPrompt(blocking, null);

        Assert.Equal(blocking.Camera.ShotSize, unchanged.Camera.ShotSize);
    }

    [Fact]
    public void Resolve_SnapshotsTheOmniscientShotSizeOntoTheCamera()
    {
        var blocking = SceneBlockingResolver.Resolve(
            new SceneBlockingInput("The framing keeps both figures readable.", [Active("p0")]), Profile());

        Assert.Equal("wide establishing shot", blocking.Camera.OmniscientShotSize);
    }

    [Fact]
    public void Resolve_RefusesMoreSubjectsThanTheProfileCanSeat()
    {
        var input = new SceneBlockingInput(null, [Active("p0"), Active("p1"), Active("p2"), Active("p3")]);

        var error = Assert.Throws<InvalidOperationException>(() => SceneBlockingResolver.Resolve(input, Profile()));

        // The refusal names the subject that could not be placed, so the operator knows what to change.
        Assert.Contains("p3", error.Message, StringComparison.Ordinal);
        Assert.Contains("placement ladder", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RefusesAnInvolvementTheBeatDidNotDeclare()
    {
        var input = new SceneBlockingInput(null, [new SceneBlockingSubjectInput("p0", "bystander", null, null, null)]);

        var error = Assert.Throws<InvalidOperationException>(() => SceneBlockingResolver.Resolve(input, Profile()));

        Assert.Contains("bystander", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RefusesASubjectWithNoKey()
    {
        var input = new SceneBlockingInput(null, [new SceneBlockingSubjectInput("  ", "active", null, null, null)]);

        var error = Assert.Throws<InvalidOperationException>(() => SceneBlockingResolver.Resolve(input, Profile()));

        Assert.Contains("no subject key", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RefusesASceneWithNoSubjects()
    {
        var input = new SceneBlockingInput("A wide establishing shot.", []);

        var error = Assert.Throws<InvalidOperationException>(() => SceneBlockingResolver.Resolve(input, Profile()));

        Assert.Contains("no subjects", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_IsDeterministicForTheSameInput()
    {
        var input = new SceneBlockingInput("A wide two-shot.", [Watching("p1"), Active("p0")]);

        var first = SceneBlockingResolver.Resolve(input, Profile());
        var second = SceneBlockingResolver.Resolve(input, Profile());

        // Compare the derived shape rather than the record instance: a record holding lists compares those lists by
        // reference, so structural equality is asserted field by field.
        Assert.Equal(first.ProfileVersion, second.ProfileVersion);
        Assert.Equal(first.Camera, second.Camera);
        Assert.Equal(first.Rationale, second.Rationale);
        Assert.Equal(first.Layering, second.Layering);
        Assert.Equal(
            first.Subjects.Select(subject => (subject.SubjectKey, subject.Position, subject.RoleText)),
            second.Subjects.Select(subject => (subject.SubjectKey, subject.Position, subject.RoleText)));
    }

    // ------------------------------------------------------------------ T7.1: the operator's placement override

    private static SceneBlocking Blocking() => SceneBlockingResolver.Resolve(
        new SceneBlockingInput("A wide two-shot.", [Active("p0"), Watching("p1")]),
        Profile());

    [Fact]
    public void ApplyOverride_MovesOnlyTheNamedSubject()
    {
        var blocking = Blocking();

        var moved = SceneBlockingResolver.ApplyOverride(
            blocking,
            new SceneBlockingOverride([new SceneBlockingSubjectOverride("p1", FrameHorizontal.Center, null)]));

        var first = moved.Subjects.Single(subject => subject.SubjectKey == "p0");
        var second = moved.Subjects.Single(subject => subject.SubjectKey == "p1");
        Assert.Equal(new FramePosition(FrameHorizontal.Left, FrameDepth.Foreground), first.Position);
        Assert.Equal(FrameHorizontal.Center, second.Position.Horizontal);
        // The axis the operator did not name keeps the compiled value, and so does everything else.
        Assert.Equal(FrameDepth.Background, second.Position.Depth);
        Assert.Equal(blocking.Camera, moved.Camera);
        Assert.Equal(blocking.Rationale, moved.Rationale);
        Assert.Equal($"{blocking.ProfileVersion}+operator", moved.ProfileVersion);
    }

    [Fact]
    public void ApplyOverride_UpdatesTheRoleTextToMatchTheNewPosition()
    {
        var moved = SceneBlockingResolver.ApplyOverride(
            Blocking(),
            new SceneBlockingOverride([new SceneBlockingSubjectOverride("p1", null, FrameDepth.Foreground)]));

        // The clause reads the role text, so a moved subject whose text still said "background" would contradict the
        // map beside it. Depth states a DISTANCE relative to the camera and says nothing about absolute size (B-161):
        // naming a size here fought the frame's own camera, so a distant person kept coming out readable.
        Assert.Equal(
            "the person on the RIGHT in the foreground, nearest the camera",
            moved.Subjects.Single(subject => subject.SubjectKey == "p1").RoleText);
    }

    [Fact]
    public void ApplyOverride_LayersForegroundBeforeBackgroundAfterAMove()
    {
        var moved = SceneBlockingResolver.ApplyOverride(
            Blocking(),
            new SceneBlockingOverride([new SceneBlockingSubjectOverride("p1", null, FrameDepth.Foreground)]));

        Assert.Equal(new[] { "p0", "p1" }, moved.Layering);
    }

    [Fact]
    public void ApplyOverride_IgnoresAnOverrideThatChangesNothing()
    {
        var blocking = Blocking();

        Assert.Same(blocking, SceneBlockingResolver.ApplyOverride(blocking, null));
        Assert.Same(blocking, SceneBlockingResolver.ApplyOverride(blocking, SceneBlockingOverride.None));
    }

    [Fact]
    public void ApplyOverride_RefusesTwoSubjectsInOnePosition()
    {
        var error = Assert.Throws<InvalidOperationException>(() => SceneBlockingResolver.ApplyOverride(
            Blocking(),
            new SceneBlockingOverride([new SceneBlockingSubjectOverride("p1", FrameHorizontal.Left, FrameDepth.Foreground)])));

        // One place cannot belong to two people, so the override is refused rather than written into the clause.
        Assert.Contains("same frame position", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyOverride_RefusesAnEntryThatNamesNoSubject()
    {
        var error = Assert.Throws<InvalidOperationException>(() => SceneBlockingResolver.ApplyOverride(
            Blocking(),
            new SceneBlockingOverride([new SceneBlockingSubjectOverride("  ", FrameHorizontal.Left, null)])));

        Assert.Contains("name the subject", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyOverride_LeavesASubjectTheFrameDoesNotPlace()
    {
        var blocking = Blocking();

        var unchanged = SceneBlockingResolver.ApplyOverride(
            blocking,
            new SceneBlockingOverride([new SceneBlockingSubjectOverride("someone-else", FrameHorizontal.Left, null)]));

        Assert.Equal(blocking.Subjects.Count, unchanged.Subjects.Count);
        Assert.Equal(blocking.Subjects[0].Position, unchanged.Subjects[0].Position);
    }

    /// <summary>
    /// B-161: the operator's framing reaches the camera the render will use, and ONLY the shot size they named —
    /// the compiled angle and side stay as the brief recorded them.
    /// </summary>
    [Fact]
    public void ApplyOverride_ReplacesTheShotSizeAndKeepsTheCompiledAngle()
    {
        var original = Blocking();
        var moved = SceneBlockingResolver.ApplyOverride(
            original,
            new SceneBlockingOverride([], new SceneBlockingCameraOverride("close shot")));

        // The compiled framing comes from the rationale's own keyword ("A wide two-shot." maps to the wide shot), so
        // naming a different shot size is what proves the override is what moved it.
        Assert.Equal("wide establishing shot", original.Camera.ShotSize);
        Assert.Equal("close shot", moved.Camera.ShotSize);
        Assert.Equal(original.Camera.Angle, moved.Camera.Angle);
        Assert.Equal(original.Camera.Side, moved.Camera.Side);
        Assert.Equal($"{original.ProfileVersion}+operator", moved.ProfileVersion);

        // The subjects are untouched: correcting the framing must not move anyone.
        Assert.Equal(
            original.Subjects.Select(subject => subject.Position),
            moved.Subjects.Select(subject => subject.Position));
    }

    /// <summary>An angle the operator names is applied too, and one they leave alone is not restated.</summary>
    [Fact]
    public void ApplyOverride_AppliesAnAngleOnlyWhenOneIsNamed()
    {
        var named = SceneBlockingResolver.ApplyOverride(
            Blocking(),
            new SceneBlockingOverride([], new SceneBlockingCameraOverride("close shot", "high angle")));
        var unnamed = SceneBlockingResolver.ApplyOverride(
            Blocking(),
            new SceneBlockingOverride([], new SceneBlockingCameraOverride("close shot", "   ")));

        Assert.Equal("high angle", named.Camera.Angle);
        Assert.Equal(Blocking().Camera.Angle, unnamed.Camera.Angle);
    }

    /// <summary>
    /// A framing override with no shot size is REFUSED rather than folded back into the compiled value: the operator
    /// would otherwise believe they had changed the framing while the render used the profile's default — the silent
    /// substitution this codebase forbids.
    /// </summary>
    [Fact]
    public void ApplyOverride_RefusesAFramingWithNoShotSize()
    {
        var error = Assert.Throws<InvalidOperationException>(() => SceneBlockingResolver.ApplyOverride(
            Blocking(),
            new SceneBlockingOverride([], new SceneBlockingCameraOverride("   "))));

        Assert.Contains("must name the shot size", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A framing-only override is NOT empty, so the render applies it. An override that counted only its subjects
    /// would drop the operator's framing on the floor and frame the render with the profile's default instead.
    /// </summary>
    [Fact]
    public void AFramingOnlyOverrideIsNotConsideredEmpty()
    {
        var original = Blocking();
        var framingOnly = new SceneBlockingOverride([], new SceneBlockingCameraOverride("close shot"));

        Assert.False(framingOnly.IsEmpty);
        Assert.True(SceneBlockingOverride.None.IsEmpty);

        var applied = SceneBlockingResolver.ApplyOverride(original, framingOnly);
        Assert.Equal("close shot", applied.Camera.ShotSize);
        Assert.NotEqual(original.Camera.ShotSize, applied.Camera.ShotSize);
    }

    /// <summary>
    /// The prompt path re-renders every subject's role text in the CURRENT vocabulary, so a brief compiled before
    /// depth stated a distance stops reading "in the background" with nothing about where that is.
    ///
    /// <para>
    /// That stored wording is the one the operator reported: the prompt said the man was in the background and he still
    /// rendered at full height on the lawn. The POSITION is untouched — only how it is described.
    /// </para>
    /// </summary>
    [Fact]
    public void ApplyForPrompt_RefreshesTheDepthWordingOfABriefCompiledBeforeDepthStatedADistance()
    {
        var stale = Blocking() with
        {
            Subjects =
            [
                new SceneBlockingSubject(
                    "p0", new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground),
                    "the person in the CENTER in the foreground"),
                new SceneBlockingSubject(
                    "p1", new FramePosition(FrameHorizontal.Left, FrameDepth.Background),
                    "the person on the LEFT in the background")
            ]
        };

        var effective = SceneBlockingResolver.ApplyForPrompt(stale, null);

        Assert.Equal(
            "the person on the LEFT in the background, farthest from the camera",
            effective.Subjects.Single(subject => subject.SubjectKey == "p1").RoleText);
        Assert.Equal(
            "the person in the CENTER in the foreground, nearest the camera",
            effective.Subjects.Single(subject => subject.SubjectKey == "p0").RoleText);

        // The positions, the camera, the layering and the rationale are the brief's own — only the words changed.
        Assert.Equal(stale.Subjects.Select(subject => subject.Position), effective.Subjects.Select(subject => subject.Position));
        Assert.Equal(stale.Camera, effective.Camera);
        Assert.Equal(stale.Layering, effective.Layering);
        Assert.Equal(stale.Rationale, effective.Rationale);
    }

    /// <summary>
    /// A frame already described in this vocabulary is passed through UNCHANGED — same instance — so nothing downstream
    /// sees a difference that is not there.
    /// </summary>
    [Fact]
    public void ApplyForPrompt_ReturnsTheSameFrameWhenNothingNeedsRewording()
    {
        var current = SceneBlockingResolver.ApplyForPrompt(Blocking(), null);

        Assert.Same(current, SceneBlockingResolver.ApplyForPrompt(current, null));
    }

    /// <summary>
    /// The prompt path applies the operator's correction AND the current wording together, so prose and block are
    /// built from one frame: a moved subject is described at its new depth and the framing is the operator's.
    /// </summary>
    [Fact]
    public void ApplyForPrompt_AppliesTheOperatorsCorrectionInTheCurrentWording()
    {
        var effective = SceneBlockingResolver.ApplyForPrompt(
            Blocking(),
            new SceneBlockingOverride(
                [new SceneBlockingSubjectOverride("p1", null, FrameDepth.Midground)],
                new SceneBlockingCameraOverride("wide establishing shot")));

        Assert.Equal(
            "the person on the RIGHT in the midground, at a middle distance from the camera",
            effective.Subjects.Single(subject => subject.SubjectKey == "p1").RoleText);
        Assert.Equal("wide establishing shot", effective.Camera.ShotSize);
        Assert.Equal($"{Blocking().ProfileVersion}+operator", effective.ProfileVersion);
    }
}
