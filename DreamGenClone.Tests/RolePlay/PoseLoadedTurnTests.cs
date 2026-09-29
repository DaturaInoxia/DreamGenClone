using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The rig supplies a loaded library pose's TURN; the pose supplies its SHAPE.
///
/// This exists because the opposite was shipped and measured wrong on 2026-09-27: the fitted rig's projection
/// replaced the loaded pose, so pressing a 5° arrow put a different figure on screen and dropped the hands. The
/// tests here pin the properties that make a press read as a turn — an unturned pose is itself, the hands and the
/// face survive, and the press costs what a turn costs rather than what a replacement costs.
/// </summary>
public sealed class PoseLoadedTurnTests
{
    /// <summary>
    /// The reason the fitted view is a separate argument: at the view the rig was fitted to, the rig's motion is
    /// zero, so the pose must come back untouched. This is what makes "Open in editor" show the pose the operator
    /// clicked instead of the fit's approximation of it.
    /// </summary>
    [Fact]
    public void AtTheFittedView_ALoadedPoseProjectsToItself()
    {
        var settings = Settings();
        var stored = Pack("NSFW_standing/512768/NSFW_standing028.json");
        var fit = PoseRigFit.Fit(stored, settings);

        using var fixture = new PoseLibraryTestFixture();
        var turned = fixture.Service.ProjectLoadedPose(stored, fit.View, fit.View, fit.Rotations);

        Assert.Equal(stored.Body.Count, turned.Body.Count);
        for (var index = 0; index < stored.Body.Count; index++)
        {
            // Identity to the precision the maths has: the rotation IS the identity here, but the point still goes
            // through unproject -> rotate -> project, and that runs in single precision (System.Numerics.Vector3), so a
            // 500-pixel coordinate resolves to about 1e-4. Measured 2026-09-27: the round trip returns the coordinate
            // within 1.1e-5, which is four orders of magnitude below anything a skeleton can express.
            Assert.Equal(stored.Body[index].X, turned.Body[index].X, 3);
            Assert.Equal(stored.Body[index].Y, turned.Body[index].Y, 3);
            Assert.Equal(stored.Body[index].Confidence, turned.Body[index].Confidence);
        }
    }

    /// <summary>
    /// The failure the operator actually reported. The rig is body-only, so a projection of it can never carry a
    /// hand; the turn must leave the library's hands in place and take them along.
    /// </summary>
    [Fact]
    public void ATurnKeepsTheLoadedPosesHands()
    {
        var settings = Settings();
        var stored = Pack("NSFW_standing/512768/NSFW_standing028.json");

        Assert.NotEmpty(stored.LeftHand);
        Assert.NotEmpty(stored.RightHand);

        var fit = PoseRigFit.Fit(stored, settings);

        using var fixture = new PoseLibraryTestFixture();
        var turned = fixture.Service.ProjectLoadedPose(
            stored, fit.View, Stepped(fit.View, settings), fit.Rotations);

        Assert.Equal(stored.LeftHand.Count, turned.LeftHand.Count);
        Assert.Equal(stored.RightHand.Count, turned.RightHand.Count);

        // Carried rather than reshaped. A hand sits at ONE depth, so turning the figure foreshortens it slightly — the
        // bound is what separates that from the per-joint displacement this replaced, which crushed a bone to 0.634 of
        // its length over four presses.
        var leftRatio = Spread(turned.LeftHand) / Spread(stored.LeftHand);
        var rightRatio = Spread(turned.RightHand) / Spread(stored.RightHand);

        Assert.True(
            Math.Abs(leftRatio - 1.0) < 0.15,
            $"the left hand changed shape by {(leftRatio - 1.0) * 100:0.0}%, so it was reshaped rather than carried");

        Assert.True(
            Math.Abs(rightRatio - 1.0) < 0.15,
            $"the right hand changed shape by {(rightRatio - 1.0) * 100:0.0}%, so it was reshaped rather than carried");

        Assert.True(
            Displacement(stored.LeftHand, turned.LeftHand) > 0,
            "the left hand did not move with its wrist, so the turn left a hand behind in the image");
        Assert.True(
            Displacement(stored.RightHand, turned.RightHand) > 0,
            "the right hand did not move with its wrist, so the turn left a hand behind in the image");
    }

    /// <summary>
    /// THE regression test for the reported defect. A 5° press must cost what a 5° turn costs. Measured on this
    /// pose 2026-09-27 the turn moves joints ~1.2% of the figure's height, against ~0.5% for the same press on the
    /// rig's own pose and a total loss of the figure (hands gone, different pose) before this change. The bound is
    /// deliberately far above the measurement and far below a replacement.
    /// </summary>
    [Fact]
    public void ATurnMovesALoadedPoseLikeATurn_NotLikeAReplacement()
    {
        var settings = Settings();
        var stored = Pack("NSFW_standing/512768/NSFW_standing028.json");
        var fit = PoseRigFit.Fit(stored, settings);

        using var fixture = new PoseLibraryTestFixture();
        var turned = fixture.Service.ProjectLoadedPose(
            stored, fit.View, Stepped(fit.View, settings), fit.Rotations);

        var moved = MeanDisplacementPercentOfHeight(stored, turned);

        Assert.True(moved > 0, "the turn did not move the figure at all");
        Assert.True(
            moved < 5.0,
            $"one 5° press moved the figure {moved:0.00}% of its own height, which is a different pose rather "
            + "than a 5° turn");
    }

    /// <summary>
    /// A pose stored at twice the size must be turned by the same amount, in the same place. The stored pose is mapped
    /// onto the studio canvas before it is turned and mapped back, so the turn is expressed in canvas terms and the
    /// resolution the pose happens to be stored at cannot change the angle.
    /// </summary>
    [Fact]
    public void TheTurnIsIndependentOfTheStoredPosesResolution()
    {
        var settings = Settings();
        var stored = Pack("NSFW_standing/512768/NSFW_standing028.json");
        var fit = PoseRigFit.Fit(stored, settings);
        var doubled = new PosePerson
        {
            Body = stored.Body.Select(point => new PoseKeypoint(point.X * 2, point.Y * 2, point.Confidence)).ToArray()
        };

        using var fixture = new PoseLibraryTestFixture();
        var view = Stepped(fit.View, settings);

        var once = fixture.Service.ProjectLoadedPose(stored, fit.View, view, fit.Rotations);
        var twice = fixture.Service.ProjectLoadedPose(doubled, fit.View, view, fit.Rotations);

        // Compared on the canvas, because that is the space the turn is defined in. In their own pixels the two results
        // differ by the 2x they were stored at, which is exactly right.
        var canvas = settings.RequireCanvas();
        var framingOnce = PoseSkeletonRenderer.ComputeFraming(stored, canvas);
        var framingTwice = PoseSkeletonRenderer.ComputeFraming(doubled, canvas);

        for (var index = 0; index < PoseMannequin.CocoJointCount; index++)
        {
            var a = framingOnce.Apply(once.Body[index]);
            var b = framingTwice.Apply(twice.Body[index]);

            Assert.Equal(a.X, b.X, 4);
            Assert.Equal(a.Y, b.Y, 4);
        }
    }

    /// <summary>
    /// The 70-point face channel is not what a bundled pack pose carries, so it is exercised with a synthetic face:
    /// the point is the PROPERTY. A face is one rigid body, so a turn may move and rotate it but must not reshape it.
    /// </summary>
    [Fact]
    public void AFaceRidesTheHeadRigidly()
    {
        var settings = Settings();
        var stored = Pack("NSFW_standing/512768/NSFW_standing028.json");
        var fit = PoseRigFit.Fit(stored, settings);

        var nose = stored.Body[OpenPosePoseJson.NoseIndex];
        var face = Enumerable.Range(0, 8)
            .Select(index => new PoseKeypoint(nose.X + (index * 4), nose.Y + (index * 3), 1.0))
            .ToArray();

        var withFace = new PosePerson { Body = stored.Body, Face = face };

        using var fixture = new PoseLibraryTestFixture();
        var turned = fixture.Service.ProjectLoadedPose(
            withFace, fit.View, Stepped(fit.View, settings), fit.Rotations);

        Assert.Equal(face.Length, turned.Face.Count);

        // Carried rather than reshaped: one rigid body, foreshortened by the turn but not deformed by it.
        var ratio = Spread(turned.Face) / Spread(face);

        Assert.True(
            Math.Abs(ratio - 1.0) < 0.15,
            $"the face changed shape by {(ratio - 1.0) * 100:0.0}%, so it was reshaped rather than carried");

        // And it travelled with the head rather than staying where the source image had it.
        Assert.True(
            Displacement(face, turned.Face) > 0,
            "the face did not move with the head, so the turn left it behind on the original image");
    }

    /// <summary>
    /// Refused by name, not approximated. The turn is a displacement per body joint, so a pose without the 18 COCO
    /// joints has nothing to apply it to — and guessing which joint is which would silently distort the figure.
    /// </summary>
    [Fact]
    public void ALoadedPoseWithoutAFullBody_IsRefusedByName()
    {
        var settings = Settings();
        var truncated = new PosePerson
        {
            Body = Enumerable.Range(0, 12)
                .Select(index => new PoseKeypoint(index * 10, index * 10, 1.0))
                .ToArray()
        };

        using var fixture = new PoseLibraryTestFixture();
        var rotations = PoseMannequin.Standing().StandingStance();

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Service.ProjectLoadedPose(
            truncated, new PoseView(), new PoseView(YawDegrees: 5), rotations));

        Assert.Contains("12", error.Message, StringComparison.Ordinal);
        Assert.Contains("18 body keypoints", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A head turn ADDS to the pose's own head angle instead of replacing it.
    ///
    /// This is the defect reported 2026-09-27 — "the Head does not, it mangled the whole pose". Writing the requested
    /// angle straight over the head joint snapped the head from whatever the source photograph had to the requested
    /// value, so the first press was a jump rather than a step (a 40° jump on a pose whose head sat at −35°).
    ///
    /// The property that separates the two is LINEARITY: two steps must travel twice as far as one. A replacement
    /// instead produces |requested − the pose's own head angle|, whose ratio is decided by that angle and is not 2
    /// whenever the angle is not zero — so this test fails on the old behaviour for any pose not already facing front.
    /// </summary>
    [Fact]
    public void AHeadTurn_AddsToThePosesOwnHeadAngle_RatherThanReplacingIt()
    {
        var settings = Settings();
        var stored = Pack("NSFW_standing/512768/NSFW_standing028.json");
        var fit = PoseRigFit.Fit(stored, settings);

        using var fixture = new PoseLibraryTestFixture();

        var none = fixture.Service.ProjectAuthoredPose(fit.View, null, fit.Rotations);
        var neutral = fixture.Service.ProjectAuthoredPose(fit.View, new PoseHeadRotation(), fit.Rotations);
        var five = fixture.Service.ProjectAuthoredPose(fit.View, new PoseHeadRotation(YawDegrees: 5), fit.Rotations);
        var ten = fixture.Service.ProjectAuthoredPose(fit.View, new PoseHeadRotation(YawDegrees: 10), fit.Rotations);

        // A neutral head changes nothing at all: it means "leave the head as the pose has it".
        for (var index = 0; index < PoseMannequin.CocoJointCount; index++)
        {
            Assert.Equal(none.Body[index].X, neutral.Body[index].X);
            Assert.Equal(none.Body[index].Y, neutral.Body[index].Y);
        }

        var oneStep = HeadTravel(neutral, five);
        var twoSteps = HeadTravel(neutral, ten);

        Assert.True(oneStep > 0, "a 5° head turn did not move the head at all");

        var ratio = twoSteps / oneStep;

        Assert.True(
            ratio is > 1.85 and < 2.15,
            $"two 5° steps travelled {ratio:0.000}x one step (expected ~2), so the head angle is being REPLACED rather "
            + "than added to — the pose's own head angle is leaking into the result");
    }

    /// <summary>
    /// Mean movement of the joints the head joint actually drives — the nose, both eyes and both ears. The neck is
    /// excluded: it is the head's parent, so it does not move when the head turns and would only dilute the measure.
    /// </summary>
    private static double HeadTravel(PosePerson before, PosePerson after) =>
        new[] { 0, 14, 15, 16, 17 }.Average(index => Distance(before.Body[index], after.Body[index]));

    private static PoseView Stepped(PoseView view, PoseStudioOptions settings) =>
        PoseProjection.Step(view, PoseRotationAxis.Yaw, 1, settings);

    /// <summary>Mean joint movement as a percentage of the figure's height, in the pose's own pixels.</summary>
    private static double MeanDisplacementPercentOfHeight(PosePerson before, PosePerson after)
    {
        var height = BodyHeight(before.Body);

        return MeanDisplacement(before, after) / height * 100.0;
    }

    private static double MeanDisplacement(PosePerson before, PosePerson after) =>
        before.Body
            .Select((point, index) => (From: point, To: after.Body[index]))
            .Where(pair => pair.From.Confidence > OpenPosePoseJson.VisibilityFloor)
            .Average(pair => Distance(pair.From, pair.To));

    private static double Displacement(IReadOnlyList<PoseKeypoint> before, IReadOnlyList<PoseKeypoint> after) =>
        before
            .Select((point, index) => (From: point, To: after[index]))
            .Where(pair => pair.From.Confidence > OpenPosePoseJson.VisibilityFloor)
            .Average(pair => Distance(pair.From, pair.To));

    private static double Distance(PoseKeypoint from, PoseKeypoint to) =>
        Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));

    /// <summary>
    /// A shape measure: the mean distance from a cluster's own centre. A rigid move preserves it, a reshape does not,
    /// which is what lets these tests tell "carried" from "redrawn".
    /// </summary>
    private static double Spread(IReadOnlyList<PoseKeypoint> cluster)
    {
        var cx = cluster.Average(point => point.X);
        var cy = cluster.Average(point => point.Y);

        return cluster.Average(point => Math.Sqrt(Math.Pow(point.X - cx, 2) + Math.Pow(point.Y - cy, 2)));
    }

    /// <summary>
    /// Guards a shape assertion against being true for the wrong reason: a cluster that did not move at all would
    /// preserve its spread too.
    /// </summary>
    private static double BodyHeight(IReadOnlyList<PoseKeypoint> body)
    {
        var ys = body
            .Where(point => point.Confidence > OpenPosePoseJson.VisibilityFloor)
            .Select(point => point.Y)
            .ToArray();

        return ys.Max() - ys.Min();
    }

    private static PosePerson Pack(string relativePath) => OpenPosePoseJson.Parse(
        File.ReadAllText(Path.Combine(
            RepositoryRoot(), "pose-packs", PoseLibraryIds.BundledPackFolder,
            relativePath.Replace('/', Path.DirectorySeparatorChar))),
        relativePath);

    private static PoseStudioOptions Settings() => new()
    {
        FocalLengthPx = 1600,
        CameraDistance = 4.5,
        Canvas = 1024,
        RotationStepDegrees = 5
    };

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "pose-packs"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"The repository root was not found above '{AppContext.BaseDirectory}'.");
    }
}
