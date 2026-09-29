using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// One framing, applied to every version of the pose being edited.
///
/// This exists because the editor recomputed the framing from whichever pose was on screen, so the figure was re-scaled
/// whenever its bounding box changed — reported 2026-09-27 as "after doing any pose edit the image changes size" and
/// "rotating it not rotating the full skeleton": a turn that widened the box shrank the whole figure instead of turning
/// it. The two properties worth pinning are that a reused framing holds the figure's size, and that splitting the fit
/// into ComputeFraming + Apply changed nothing about what FitToCanvas produces.
/// </summary>
public sealed class PoseFramingTests
{
    /// <summary>
    /// The property the operator asked for. Two poses of the same figure, one with an arm swung out, are drawn at the
    /// SAME size when they share a framing — and at different sizes when each is fitted to the canvas on its own, which
    /// is the re-scale that was being reported as the image changing size.
    /// </summary>
    [Fact]
    public void AReusedFramingHoldsTheFigureSize_WhereRefittingRescalesIt()
    {
        var compact = Synthetic(span: 100);
        var wide = Synthetic(span: 600);

        var framing = PoseSkeletonRenderer.ComputeFraming(compact, 1024);

        var framedCompact = PoseSkeletonRenderer.Apply(compact, framing);
        var framedWide = PoseSkeletonRenderer.Apply(wide, framing);

        Assert.Equal(Height(framedCompact.Body), Height(framedWide.Body), 6);

        // The old path, for contrast. A wider bounding box makes the width the limiting dimension, so fitting this pose
        // to the canvas on its own shrinks it — the whole figure, not just the arm that moved.
        var refitWide = PoseSkeletonRenderer.FitToCanvas(wide, 1024);

        Assert.True(
            Height(refitWide.Body) < Height(framedWide.Body) - 1.0,
            $"re-fitting the wider pose gave it a height of {Height(refitWide.Body):0.#} against "
            + $"{Height(framedWide.Body):0.#} with one shared framing, so this test cannot tell the two paths apart");
    }

    /// <summary>
    /// The refactor guard. ComputeFraming + Apply must produce exactly what FitToCanvas produced, on all four channels,
    /// because every existing caller and every committed skeleton depends on that being the same picture.
    /// </summary>
    [Fact]
    public void ApplyOfComputeFraming_IsExactlyFitToCanvas()
    {
        var pose = Synthetic(span: 300);
        var withClusters = new PosePerson
        {
            Body = pose.Body,
            LeftHand = [new PoseKeypoint(10, 200, 1.0), new PoseKeypoint(14, 206, 0.9)],
            Face = [new PoseKeypoint(300, 0, 1.0), new PoseKeypoint(306, 4, 0.8)]
        };

        var fitted = PoseSkeletonRenderer.FitToCanvas(withClusters, 1024);
        var applied = PoseSkeletonRenderer.Apply(
            withClusters, PoseSkeletonRenderer.ComputeFraming(withClusters, 1024));

        AssertSame(fitted.Body, applied.Body, "body");
        AssertSame(fitted.LeftHand, applied.LeftHand, "left hand");
        AssertSame(fitted.RightHand, applied.RightHand, "right hand");
        AssertSame(fitted.Face, applied.Face, "face");
    }

    /// <summary>
    /// The hands and the face go through the SAME transform as the body, so a wrist cannot part from its hand and a face
    /// cannot be drawn beside its own skull. Asserted as a relationship rather than as a number: the hand's offset from
    /// its wrist must be scaled by exactly the framing's scale.
    /// </summary>
    [Fact]
    public void AFramingReachesTheHandsAndTheFace()
    {
        var body = Synthetic(span: 200).Body;
        var wrist = body[OpenPosePoseJson.LeftWristIndex];
        var pose = new PosePerson
        {
            Body = body,
            LeftHand = [new PoseKeypoint(wrist.X + 10, wrist.Y + 10, 1.0)]
        };

        var framing = PoseSkeletonRenderer.ComputeFraming(pose, 1024);
        var applied = PoseSkeletonRenderer.Apply(pose, framing);

        var handOffset = applied.LeftHand[0].X - applied.Body[OpenPosePoseJson.LeftWristIndex].X;

        Assert.Equal(10 * framing.Scale, handOffset, 9);
    }

    /// <summary>
    /// A pose with nothing visible is refused by name rather than framed on an empty box, which would put every joint at
    /// the canvas centre and produce a picture that looks like a pose.
    /// </summary>
    [Fact]
    public void APoseWithNothingVisible_IsRefusedByName()
    {
        var empty = new PosePerson
        {
            Body = Enumerable.Range(0, PosePerson.BodyJointCount)
                .Select(_ => new PoseKeypoint(0, 0, 0))
                .ToArray()
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => PoseSkeletonRenderer.ComputeFraming(empty, 1024));

        Assert.Contains("no visible body keypoint", error.Message, StringComparison.Ordinal);
    }

    private static void AssertSame(
        IReadOnlyList<PoseKeypoint> expected, IReadOnlyList<PoseKeypoint> actual, string what)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].X, actual[index].X);
            Assert.Equal(expected[index].Y, actual[index].Y);
            Assert.Equal(expected[index].Confidence, actual[index].Confidence);
        }
    }

    /// <summary>
    /// The head close-up must share ONE frame across head angles, computed from the head at rest. Re-fitting it per
    /// angle was reported as "Turn left / right and Tilt are doing the same things", because the frame is measured on
    /// the six head points and the refit then scales whatever box a turn leaves behind back up to fill the canvas — so
    /// the RESIZE dominated and two different axes read as the same gesture.
    ///
    /// What is asserted here is exactly the part that was fixed and can be measured: with a shared frame, a point that
    /// the head rotation does not move stays on the same pixel. The neck is that point — the head hangs from it, so a
    /// head angle never moves it, and any movement of it on screen can only be the FRAME moving.
    ///
    /// What is deliberately NOT asserted: that re-fitting stretches a pitch back. Measured 2026-09-27, it does not —
    /// re-fitting gave a head height of 20.4 against the shared frame's 20.7, essentially the same. So re-fitting does
    /// not explain the reported "smoosh", and a test claiming it would would be pinning a fiction.
    /// </summary>
    [Fact]
    public void OneHeadFraming_KeepsTheHeadStill_WhereRefittingMovesItBetweenAngles()
    {
        using var fixture = new PoseLibraryTestFixture();

        var rest = fixture.Service.ProjectAuthoredPose(new PoseView(), new PoseHeadRotation());
        var framing = PoseSkeletonRenderer.ComputeFraming(rest, 320, PoseMannequin.HeadCocoIndices);

        var neckAtRest = PoseSkeletonRenderer.Apply(rest, framing).Body[OpenPosePoseJson.NeckIndex];

        foreach (var head in new[]
        {
            new PoseHeadRotation(YawDegrees: 45),
            new PoseHeadRotation(RollDegrees: 45),
            new PoseHeadRotation(PitchDegrees: -45)
        })
        {
            var projected = fixture.Service.ProjectAuthoredPose(new PoseView(), head);

            // One frame: the head turns about a neck that does not move, so the neck lands on the same pixel.
            var shared = PoseSkeletonRenderer.Apply(projected, framing).Body[OpenPosePoseJson.NeckIndex];

            Assert.Equal(neckAtRest.X, shared.X, 6);
            Assert.Equal(neckAtRest.Y, shared.Y, 6);

            // Re-fitting instead re-centres and re-scales around each angle's own box, so the same unmoved joint lands
            // somewhere else on the canvas. That movement is the frame, not the head.
            var refitted = PoseSkeletonRenderer
                .FitToCanvas(projected, 320, PoseMannequin.HeadCocoIndices).Body[OpenPosePoseJson.NeckIndex];

            var drift = Math.Sqrt(
                Math.Pow(refitted.X - shared.X, 2) + Math.Pow(refitted.Y - shared.Y, 2));

            Assert.True(
                drift > 1.0,
                $"re-fitting put the unmoved neck {drift:0.###} px from where one shared frame puts it, so this test "
                + "cannot tell the two paths apart");
        }
    }

    /// <summary>A figure whose joints span <paramref name="span"/> horizontally and 200 pixels vertically.</summary>
    private static PosePerson Synthetic(double span)
    {
        var body = new PoseKeypoint[PosePerson.BodyJointCount];
        for (var index = 0; index < body.Length; index++)
        {
            var fraction = index / (double)(body.Length - 1);
            body[index] = new PoseKeypoint(span * fraction, 200 * fraction, 1.0);
        }

        return new PosePerson { Body = body };
    }

    private static double Height(IReadOnlyList<PoseKeypoint> body) =>
        body.Max(point => point.Y) - body.Min(point => point.Y);
}
