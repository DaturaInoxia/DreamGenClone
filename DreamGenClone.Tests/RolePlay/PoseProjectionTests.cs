using System.Numerics;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The projection is the piece that makes a requested angle exact, so these tests pin the three things that make
/// it trustworthy: the rig is rigid, a yaw foreshortens instead of tilting, and the 5° arrow composes.
/// </summary>
public sealed class PoseProjectionTests
{
    private const int NoseIndex = 0;
    private const int NeckIndex = 1;
    private const int RightShoulderIndex = 2;
    private const int RightWristIndex = 4;
    private const int LeftShoulderIndex = 5;
    private const int RightHipIndex = 8;
    private const int RightAnkleIndex = 10;
    private const int RightEyeIndex = 14;
    private const int LeftEyeIndex = 15;
    private const int RightEarIndex = 16;
    private const int LeftEarIndex = 17;

    /// <summary>
    /// A front view shows every joint. Visibility is derived from which way each joint faces rather than being
    /// hardcoded, so this is the guard that a facing limit that is too strict cannot quietly hide part of a
    /// front-facing figure.
    /// </summary>
    [Fact]
    public void Project_FrontViewShowsEveryJoint()
    {
        var person = Project();

        Assert.Equal(PoseMannequin.CocoJointCount, person.Body.Count);
        Assert.All(person.Body, point => Assert.Equal(1.0, point.Confidence));
    }

    /// <summary>
    /// The face is the rig's only facing information, and a standing figure is nearly left-right symmetric, so a
    /// front view and a view turned away would otherwise project to the same picture. Measured 2026-09-24 with every
    /// joint hardcoded to confidence 1: the front skeleton differed from its own mirror by 1.2% of pixels and
    /// Qwen-2.1 rendered the front pose facing away from the camera. A model cannot read a facing that is not there.
    /// </summary>
    [Fact]
    public void Project_AHeadTurnedAwayHidesTheFace_ButKeepsTheEars()
    {
        var front = Project().Body;
        var away = Project(new PoseView(YawDegrees: 180)).Body;

        Assert.True(front[NoseIndex].Confidence > 0, "a front view must show the nose");
        Assert.True(
            front[RightEyeIndex].Confidence > 0 && front[LeftEyeIndex].Confidence > 0,
            "a front view must show both eyes");

        Assert.Equal(0.0, away[NoseIndex].Confidence);
        Assert.Equal(0.0, away[RightEyeIndex].Confidence);
        Assert.Equal(0.0, away[LeftEyeIndex].Confidence);

        // Ears sit on the outside of the head and stay visible from behind. That is what makes a back view read as
        // a back view rather than as a head with nothing on it, and it matches how a real OpenPose back view looks.
        Assert.True(
            away[RightEarIndex].Confidence > 0 && away[LeftEarIndex].Confidence > 0,
            "a view turned away must still show both ears");
    }

    /// <summary>
    /// The sign of the yaw decides which side of a profile faces the camera, and a silent flip here would be
    /// invisible except as a wrong render, so it is pinned: at +yaw the subject's right side comes toward the
    /// camera, so the right eye survives and the left one is hidden.
    /// </summary>
    [Fact]
    public void Project_AProfileHidesExactlyTheFarEye()
    {
        var profileRight = Project(new PoseView(YawDegrees: 90)).Body;
        Assert.True(profileRight[RightEyeIndex].Confidence > 0, "a right profile must keep the right eye");
        Assert.Equal(0.0, profileRight[LeftEyeIndex].Confidence);

        var profileLeft = Project(new PoseView(YawDegrees: -90)).Body;
        Assert.True(profileLeft[LeftEyeIndex].Confidence > 0, "a left profile must keep the left eye");
        Assert.Equal(0.0, profileLeft[RightEyeIndex].Confidence);

        // The nose is the silhouette's forward point, so unlike an eye it survives all the way round to profile.
        Assert.True(
            profileRight[NoseIndex].Confidence > 0 && profileLeft[NoseIndex].Confidence > 0,
            "a profile view still shows the nose");
    }

    /// <summary>
    /// A three-quarter view is not far enough round to lose an eye: both stay visible, which is what a real
    /// three-quarter photograph shows and what the pack's own DWPose readings show.
    /// </summary>
    [Theory]
    [InlineData(-45.0)]
    [InlineData(45.0)]
    public void Project_AThreeQuarterViewKeepsBothEyes(double yaw)
    {
        var body = Project(new PoseView(YawDegrees: yaw)).Body;

        Assert.True(body[RightEyeIndex].Confidence > 0, $"yaw {yaw} must keep the right eye");
        Assert.True(body[LeftEyeIndex].Confidence > 0, $"yaw {yaw} must keep the left eye");
        Assert.True(body[NoseIndex].Confidence > 0, $"yaw {yaw} must keep the nose");
    }

    /// <summary>
    /// The stance is what makes a side view readable. With the bare rest pose the arms hang straight down against
    /// the torso, so in a profile the arm projects on top of it and the figure collapses to a vertical line —
    /// present, but saying nothing to a person reading the preview or to a model conditioning on the skeleton.
    /// Swinging the arms forward is the fix precisely because a sideways offset is along the view axis and would
    /// project to nothing.
    /// </summary>
    [Fact]
    public void TheStandingStanceLiftsTheArmOffTheTorsoInAProfileView()
    {
        var profile = new PoseView(YawDegrees: 90);
        var bare = ArmOffsetFromTorso(Project(profile));
        var stance = ArmOffsetFromTorso(Project(profile, useStance: true));

        Assert.True(bare < 0.03, $"the bare rest pose should leave the arm on the torso but measured {bare:0.000}");
        Assert.True(stance > 0.05, $"the stance should separate the arm from the torso but measured {stance:0.000}");
    }

    [Fact]
    public void Project_RestPose_IsAnUprightFigure()
    {
        var body = Project().Body;

        Assert.True(body[NoseIndex].Y < body[NeckIndex].Y, "the nose should be above the neck");
        Assert.True(body[NeckIndex].Y < body[RightHipIndex].Y, "the neck should be above the hips");
        Assert.True(body[RightHipIndex].Y < body[RightAnkleIndex].Y, "the hips should be above the ankles");

        // The subject's right shoulder is on the image's left, which is the convention every skeleton follows.
        Assert.True(body[RightShoulderIndex].X < body[LeftShoulderIndex].X);
    }

    [Fact]
    public void Project_YawForeshortenensTheShoulderSpan_ItDoesNotTiltIt()
    {
        var front = Project().Body;
        var profile = Project(new PoseView(YawDegrees: 90)).Body;

        var frontSpan = Math.Abs(front[LeftShoulderIndex].X - front[RightShoulderIndex].X);
        var profileSpan = Math.Abs(profile[LeftShoulderIndex].X - profile[RightShoulderIndex].X);
        var profileVertical = Math.Abs(profile[LeftShoulderIndex].Y - profile[RightShoulderIndex].Y);

        Assert.True(profileSpan < frontSpan * 0.2,
            $"at profile the shoulder span should collapse, but it went from {frontSpan:0.#} to {profileSpan:0.#}");

        // The discriminating check against an in-plane rotation: that would have turned the horizontal span into
        // an equal vertical span. Here it stays small, and the only vertical difference is the perspective shift
        // of the shoulder that is now nearer the camera — legitimate, and about a third of the original span.
        Assert.True(profileVertical < frontSpan * 0.5,
            $"an in-plane rotation would have produced a vertical span of ~{frontSpan:0.#}, but it is {profileVertical:0.#}");
    }

    [Fact]
    public void Project_AtProfile_TheShouldersCoincideInX()
    {
        // A true profile is degenerate: both shoulders sit at the same horizontal position, which is exactly why
        // the profile skeletons in the library had to be annotated from a profile plate rather than derived.
        var body = Project(new PoseView(YawDegrees: 90)).Body;

        Assert.Equal(body[LeftShoulderIndex].X, body[RightShoulderIndex].X, 3);
    }

    [Fact]
    public void Project_TurningPastProfile_SwapsTheShouldersAcrossTheFigureCentre()
    {
        var front = Project().Body;
        var turned = Project(new PoseView(YawDegrees: 120)).Body;

        Assert.True(front[RightShoulderIndex].X < front[LeftShoulderIndex].X);
        Assert.Equal(front[RightShoulderIndex].X < front[LeftShoulderIndex].X,
            !(turned[RightShoulderIndex].X < turned[LeftShoulderIndex].X));
    }

    [Fact]
    public void Step_EighteenPresses_MatchesOneNinetyDegreeTurn()
    {
        var settings = Settings();
        var pressed = new PoseView();
        for (var press = 0; press < 18; press++)
        {
            pressed = PoseProjection.Step(pressed, PoseRotationAxis.Yaw, 1, settings);
        }

        var direct = PoseProjection.Rotate(new PoseView(), PoseRotationAxis.Yaw, 90);

        Assert.Equal(direct.YawDegrees, pressed.YawDegrees, 6);

        var steppedBody = Project(pressed, settings).Body;
        var directBody = Project(direct, settings).Body;
        for (var index = 0; index < PoseMannequin.CocoJointCount; index++)
        {
            Assert.Equal(directBody[index].X, steppedBody[index].X, 6);
            Assert.Equal(directBody[index].Y, steppedBody[index].Y, 6);
        }
    }

    [Fact]
    public void Step_UsesTheConfiguredSize()
    {
        var settings = Settings(step: 5);

        var oneStep = PoseProjection.Step(new PoseView(), PoseRotationAxis.Yaw, 1, settings);

        Assert.Equal(5, oneStep.YawDegrees, 6);
    }

    [Fact]
    public void Step_ZeroSteps_LeavesTheViewAlone()
    {
        var view = new PoseView(12, 3, 4);

        Assert.Equal(view, PoseProjection.Step(view, PoseRotationAxis.Yaw, 0, Settings()));
    }

    [Fact]
    public void Rotate_YawRollAndPitch_AccumulateOnTheirOwnAxis()
    {
        var view = PoseProjection.Rotate(new PoseView(), PoseRotationAxis.Yaw, 30);
        view = PoseProjection.Rotate(view, PoseRotationAxis.Roll, -10);
        view = PoseProjection.Rotate(view, PoseRotationAxis.Pitch, 20);

        Assert.Equal(30, view.YawDegrees, 6);
        Assert.Equal(-10, view.RollDegrees, 6);
        Assert.Equal(20, view.PitchDegrees, 6);
    }

    [Fact]
    public void Rotate_PitchIsClampedShortOfThePole()
    {
        var view = PoseProjection.Rotate(new PoseView(), PoseRotationAxis.Pitch, 400);

        Assert.Equal(PoseProjection.PitchLimitDegrees, view.PitchDegrees, 6);
    }

    [Fact]
    public void ForwardKinematics_KeepsEveryBoneLengthWhateverThePose()
    {
        var mannequin = PoseMannequin.Standing();
        var rotations = mannequin.RestRotations();
        rotations[8] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.2f);   // bend the right elbow
        rotations[13] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.7f); // swing the right hip

        var positions = mannequin.ForwardKinematics(rotations);

        for (var index = 0; index < mannequin.Joints.Count; index++)
        {
            var joint = mannequin.Joints[index];
            if (joint.Parent < 0) continue;

            var actual = (positions[index] - positions[joint.Parent]).Length();
            Assert.Equal(joint.BoneLength, actual, 6);
        }
    }

    [Fact]
    public void Project_ACameraTooClose_IsRefusedByNameInsteadOfInverting()
    {
        var settings = Settings(distance: 0.05);

        var error = Assert.Throws<InvalidOperationException>(() => Project(settings: settings));

        Assert.Contains("CameraDistance", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(PoseStudioOptions.FocalLengthPx))]
    [InlineData(nameof(PoseStudioOptions.CameraDistance))]
    [InlineData(nameof(PoseStudioOptions.Canvas))]
    public void Project_MissingProjectionSettings_AreRefusedByName(string missing)
    {
        var settings = Settings();
        switch (missing)
        {
            case nameof(PoseStudioOptions.FocalLengthPx): settings.FocalLengthPx = null; break;
            case nameof(PoseStudioOptions.CameraDistance): settings.CameraDistance = null; break;
            case nameof(PoseStudioOptions.Canvas): settings.Canvas = null; break;
            default: throw new InvalidOperationException($"Unhandled setting '{missing}'.");
        }

        var error = Assert.Throws<InvalidOperationException>(() => Project(settings: settings));

        Assert.Contains(missing, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Step_MissingTheStepSize_IsRefusedByName()
    {
        var settings = Settings();
        settings.RotationStepDegrees = null;

        var error = Assert.Throws<InvalidOperationException>(() =>
            PoseProjection.Step(new PoseView(), PoseRotationAxis.Yaw, 1, settings));

        Assert.Contains(nameof(PoseStudioOptions.RotationStepDegrees), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectedSkeletons_RenderAndNarrowWithYaw()
    {
        // End to end through the real renderer: the projection has to produce something ControlNet can be
        // conditioned on, and the narrowing has to survive rendering. It does, because the renderer's fit is
        // uniform and a standing figure is height-limited, so the angle is not undone by the fit.
        var frontWidth = RenderedWidth(new PoseView());
        var threeQuarterWidth = RenderedWidth(new PoseView(YawDegrees: 45));
        var profileWidth = RenderedWidth(new PoseView(YawDegrees: 90));

        Assert.True(frontWidth > threeQuarterWidth,
            $"3/4 ({threeQuarterWidth}px) should be narrower than front ({frontWidth}px)");
        Assert.True(threeQuarterWidth > profileWidth,
            $"profile ({profileWidth}px) should be narrower than 3/4 ({threeQuarterWidth}px)");
    }

    /// <summary>The rendered figure's width in pixels: how many columns hold any non-black pixel.</summary>
    private static int RenderedWidth(PoseView view)
    {
        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(
            PoseSkeletonRenderer.RenderPng(Project(view)));

        var columns = 0;
        for (var x = 0; x < image.Width; x++)
        {
            for (var y = 0; y < image.Height; y++)
            {
                if (image[x, y].R != 0 || image[x, y].G != 0 || image[x, y].B != 0)
                {
                    columns++;
                    break;
                }
            }
        }

        return columns;
    }

    [Fact]
    public void TheRigArmIsProportionateToTheFigure()
    {
        // The arm was 20% short until a handshake test exposed it, so the proportion is pinned: shoulder to wrist
        // should be roughly a third of the figure's height (upper arm ~0.186 H plus forearm ~0.146 H).
        var mannequin = PoseMannequin.Standing();
        var rest = mannequin.ForwardKinematics(mannequin.RestRotations());
        var byName = mannequin.Joints
            .Select((joint, index) => (joint.Name, index))
            .ToDictionary(entry => entry.Name, entry => entry.index);

        var arm = (rest[byName["wrist_l"]] - rest[byName["shoulder_l"]]).Length();
        var noseToAnkle = rest[byName["nose"]].Y - rest[byName["ankle_l"]].Y;

        Assert.True(noseToAnkle > 0, "the nose should be above the ankles");
        Assert.InRange(arm / noseToAnkle, 0.33, 0.38);
    }

    /// <summary>
    /// Measures the profile span against real renders, then pins the cause. Rendered profiles read a shoulder span
    /// of about 2% of figure height (measured 2026-09-02 through the pose-angle probe against DWPose on plates
    /// from juggernautXL + OpenPoseXL2), while the rig at the configured camera distance keeps 7.3%. The
    /// difference is perspective, not pose: at profile the two shoulders are separated in depth, and a camera
    /// close enough to make them project 7% apart is closer than any real portrait lens. Moving the camera back
    /// must therefore shrink the span monotonically, which is the invariant worth pinning.
    /// </summary>
    [Fact]
    public void AProfileSpanShrinksTowardTheRealRenderAsTheCameraMovesBack()
    {
        var profile = new PoseView(YawDegrees: 90);

        var spans = new[] { 4.5, 12.0, 40.0 }
            .Select(distance => ShoulderSpan(Project(profile, Settings(distance: distance))) / FigureHeight(Project(profile, Settings(distance: distance))))
            .ToArray();

        Assert.True(
            spans[0] > spans[1] && spans[1] > spans[2],
            $"a longer camera must flatten the profile span, but got {spans[0]:P2}, {spans[1]:P2}, {spans[2]:P2}");

        // The configured distance is the one that has to improve, so it is asserted to be the widest of the three.
        Assert.True(spans[0] > 0.05, $"the configured distance should still show the 7% span it was measured at, got {spans[0]:P2}");
    }

    private static double ShoulderSpan(PosePerson person)
    {
        var right = person.Body[OpenPosePoseJson.RightShoulderIndex];
        var left = person.Body[OpenPosePoseJson.LeftShoulderIndex];
        return Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));
    }

    private static double FigureHeight(PosePerson person)
    {
        var visible = person.Body.Where(point => point.Confidence > OpenPosePoseJson.VisibilityFloor).ToArray();
        return visible.Max(point => point.Y) - visible.Min(point => point.Y);
    }

    /// <summary>
    /// How far the wrist sits from the neck across the screen, as a fraction of figure height. In a side view the
    /// screen's horizontal axis is the figure's depth axis, so this number is the arm's separation from the torso.
    /// </summary>
    private static double ArmOffsetFromTorso(PosePerson person)
    {
        var neck = person.Body[NeckIndex];
        var wrist = person.Body[RightWristIndex];
        var visible = person.Body.Where(point => point.Confidence > OpenPosePoseJson.VisibilityFloor).ToArray();
        var height = visible.Max(point => point.Y) - visible.Min(point => point.Y);

        return Math.Abs(wrist.X - neck.X) / height;
    }

    private static PosePerson Project(
        PoseView? view = null, PoseStudioOptions? settings = null, bool useStance = false)
    {
        var mannequin = PoseMannequin.Standing();
        return PoseProjection.Project(
            mannequin,
            useStance ? mannequin.StandingStance() : mannequin.RestRotations(),
            view ?? new PoseView(),
            settings ?? Settings());
    }

    private static PoseStudioOptions Settings(
        double? focal = 1600, double? distance = 4.5, int? canvas = 1024, double? step = 5) => new()
        {
            FocalLengthPx = focal,
            CameraDistance = distance,
            Canvas = canvas,
            RotationStepDegrees = step
        };
}
