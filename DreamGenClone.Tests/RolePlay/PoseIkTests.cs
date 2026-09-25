using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The IK is the part of the drag editor that can be wrong quietly: a solver that stretches a bone still looks
/// like a pose on screen. These pin the invariant that makes the result renderable.
/// </summary>
public sealed class PoseIkTests
{
    private const int RightWrist = 4;
    private const int LeftWrist = 7;
    private const int RightShoulder = 2;
    private const int LeftShoulder = 5;
    private const int LeftElbow = 6;
    private const int RightAnkle = 10;

    [Fact]
    public void Solve_KeepsEveryBoneLength()
    {
        var chain = Chain((100, 100), (160, 140), (210, 190));
        var lengths = Lengths(chain);

        var solved = PoseIk.Solve(chain, new PoseKeypoint(150, 60, 1.0));

        AssertLengths(lengths, solved);
    }

    [Fact]
    public void Solve_ReachesAReachableTarget()
    {
        var chain = Chain((100, 100), (160, 140), (210, 190));
        var target = new PoseKeypoint(150, 60, 1.0);

        var solved = PoseIk.Solve(chain, target);

        // The contract is DefaultTolerance, so that is what is asserted — not a precision that happens to be
        // tighter than the solver promises, which would make the test flaky rather than meaningful.
        Assert.True(Distance(solved[^1], target) <= PoseIk.DefaultTolerance,
            $"the effector should arrive within {PoseIk.DefaultTolerance} but is {Distance(solved[^1], target):0.###} away");
    }

    [Fact]
    public void Solve_PinsTheRoot()
    {
        var chain = Chain((100, 100), (160, 140), (210, 190));

        var solved = PoseIk.Solve(chain, new PoseKeypoint(150, 60, 1.0));

        Assert.Equal(100, solved[0].X, 6);
        Assert.Equal(100, solved[0].Y, 6);
    }

    [Fact]
    public void Solve_WithAnUnreachableTarget_LaysTheChainOutStraightInsteadOfStretchingIt()
    {
        var chain = Chain((100, 100), (160, 140), (210, 190));
        var lengths = Lengths(chain);
        var total = lengths.Sum();

        var solved = PoseIk.Solve(chain, new PoseKeypoint(2000, 2000, 1.0));

        // Every bone keeps its length, and the chain simply cannot arrive.
        AssertLengths(lengths, solved);
        var distanceFromRoot = Distance(solved[0], solved[^1]);
        Assert.Equal(total, distanceFromRoot, 1);
        Assert.True(Distance(solved[^1], new PoseKeypoint(2000, 2000, 1.0)) > 1);
    }

    [Fact]
    public void Solve_CarriesVisibilityOverRatherThanInventingIt()
    {
        var chain = new[]
        {
            new PoseKeypoint(100, 100, 1.0),
            new PoseKeypoint(160, 140, 1.0),
            new PoseKeypoint(210, 190, 0.0)
        };

        var solved = PoseIk.Solve(chain, new PoseKeypoint(150, 60, 1.0));

        // The invisible effector stays invisible: a drag must not bring a joint to life.
        Assert.Equal(0.0, solved[^1].Confidence);
    }

    [Fact]
    public void Solve_IsDeterministic()
    {
        var chain = Chain((100, 100), (160, 140), (210, 190));

        var first = PoseIk.Solve(chain, new PoseKeypoint(150, 60, 1.0));
        var second = PoseIk.Solve(chain, new PoseKeypoint(150, 60, 1.0));

        for (var index = 0; index < first.Count; index++)
        {
            Assert.Equal(first[index].X, second[index].X, 9);
            Assert.Equal(first[index].Y, second[index].Y, 9);
        }
    }

    [Fact]
    public void Drag_MovesTheChainAndLeavesEveryOtherJointAlone()
    {
        var person = Standing();
        var before = person.Body.ToArray();

        var dragged = PoseIk.Drag(person, LeftWrist, new PoseKeypoint(560, 430, 1.0));

        // The chain moved...
        Assert.NotEqual(before[LeftWrist].X, dragged.Body[LeftWrist].X, 3);

        // ...the root did not, so the figure is not sliding around the canvas...
        Assert.Equal(before[LeftShoulder].X, dragged.Body[LeftShoulder].X, 6);
        Assert.Equal(before[LeftShoulder].Y, dragged.Body[LeftShoulder].Y, 6);

        // ...and nothing outside the chain changed at all.
        var chain = PoseIk.JointsOf(PoseDragChain.LeftArm);
        for (var index = 0; index < before.Length; index++)
        {
            if (chain.Contains(index)) continue;
            Assert.Equal(before[index].X, dragged.Body[index].X, 6);
            Assert.Equal(before[index].Y, dragged.Body[index].Y, 6);
        }
    }

    [Fact]
    public void Drag_KeepsTheArmBonesAtTheirLength()
    {
        var person = Standing();
        var chain = PoseIk.JointsOf(PoseDragChain.LeftArm);
        var lengths = Lengths(chain.Select(index => person.Body[index]).ToArray());

        var dragged = PoseIk.Drag(person, LeftWrist, new PoseKeypoint(600, 300, 1.0));

        AssertLengths(lengths, chain.Select(index => dragged.Body[index]).ToArray());
    }

    [Fact]
    public void Drag_BothHandsToASharedPoint_MakesAHandshake()
    {
        // The stated case, done the way it is actually reachable: a handshake is both hands meeting at a point in
        // front of the chest, not one hand dragged onto the other hand's rest position — the arms rest near their
        // own limit, so that first move is out of reach (pinned by the test below).
        var person = Standing();
        var leftChain = PoseIk.JointsOf(PoseDragChain.LeftArm);
        var rightChain = PoseIk.JointsOf(PoseDragChain.RightArm);
        var leftLengths = Lengths(leftChain.Select(index => person.Body[index]).ToArray());
        var rightLengths = Lengths(rightChain.Select(index => person.Body[index]).ToArray());

        // Ruler taken from the pose itself: the shoulders are 0.36 m apart in the rig, so their pixel separation
        // calibrates the projection without the test needing to know the camera settings.
        var shoulderSpan = Distance(person.Body[LeftShoulder], person.Body[RightShoulder]);
        var pixelsPerMetre = shoulderSpan / 0.36;
        var chestY = person.Body[LeftShoulder].Y + (0.25 * pixelsPerMetre);
        var midlineX = (person.Body[LeftShoulder].X + person.Body[RightShoulder].X) / 2.0;
        var meetingPoint = new PoseKeypoint(midlineX, chestY, 1.0);

        var shook = PoseIk.Drag(person, LeftWrist, meetingPoint);
        shook = PoseIk.Drag(shook, RightWrist, meetingPoint);

        Assert.True(Distance(shook.Body[LeftWrist], meetingPoint) <= PoseIk.DefaultTolerance,
            $"the left hand should arrive but is {Distance(shook.Body[LeftWrist], meetingPoint):0.###} away");
        Assert.True(Distance(shook.Body[RightWrist], meetingPoint) <= PoseIk.DefaultTolerance,
            $"the right hand should arrive but is {Distance(shook.Body[RightWrist], meetingPoint):0.###} away");

        // And the hands are together, which is the gesture.
        Assert.True(Distance(shook.Body[LeftWrist], shook.Body[RightWrist]) <= 2 * PoseIk.DefaultTolerance);

        // Neither arm was lengthened to get there.
        AssertLengths(leftLengths, leftChain.Select(index => shook.Body[index]).ToArray());
        AssertLengths(rightLengths, rightChain.Select(index => shook.Body[index]).ToArray());
    }

    [Fact]
    public void Drag_OneHandOntoTheOtherHandsRestPosition_IsOutOfReach()
    {
        // A property of the rig, not a defect: standing with the arms at the sides, the hands are already close to
        // the arms' own limit, so the far hand cannot be reached in one move. The editor says so rather than
        // stretching the arm, and the handshake is a two-drag gesture toward a shared point.
        var person = Standing();
        var armLength = Lengths(PoseIk.JointsOf(PoseDragChain.LeftArm)
            .Select(index => person.Body[index]).ToArray()).Sum();
        var shoulderToOtherHand = Distance(person.Body[LeftShoulder], person.Body[RightWrist]);

        Assert.True(shoulderToOtherHand > armLength,
            $"the far hand should be out of reach ({shoulderToOtherHand:0.###} vs an arm of {armLength:0.###})");

        var reached = PoseIk.Drag(person, LeftWrist, person.Body[RightWrist]);
        Assert.True(Distance(reached.Body[LeftWrist], person.Body[RightWrist]) > PoseIk.DefaultTolerance,
            "an unreachable target must not be reported as reached");
    }

    [Fact]
    public void Drag_ALegKeepsItsBonesToo()
    {
        var person = Standing();
        var chain = PoseIk.JointsOf(PoseDragChain.RightLeg);
        var lengths = Lengths(chain.Select(index => person.Body[index]).ToArray());

        var dragged = PoseIk.Drag(person, RightAnkle, new PoseKeypoint(430, 900, 1.0));

        AssertLengths(lengths, chain.Select(index => dragged.Body[index]).ToArray());
    }

    [Fact]
    public void Drag_AJointThatIsNotALimbEnd_IsRefusedByName()
    {
        var person = Standing();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PoseIk.Drag(person, 2, new PoseKeypoint(400, 300, 1.0)));

        Assert.Contains("not draggable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Solve_AChainWithAZeroLengthBone_IsRefused()
    {
        var chain = Chain((100, 100), (160, 140), (160, 140));

        var error = Assert.Throws<InvalidOperationException>(() =>
            PoseIk.Solve(chain, new PoseKeypoint(150, 60, 1.0)));

        Assert.Contains("zero-length", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAuthoredPose_RecordsTheDragsAndCallsItselfDragged()
    {
        using var fixture = new PoseLibraryTestFixture();
        await fixture.Service.EnsureAuthoredLibraryAsync();

        var start = fixture.Service.ProjectAuthoredPose(new PoseView());
        var dragged = PoseIk.Drag(start, LeftWrist, start.Body[RightWrist]);

        var preset = await fixture.Service.SaveAuthoredPoseAsync(new AuthoredPoseRequest(
            "Handshake", "standing", "reach", new PoseView(), PoseLibraryIds.Authored,
            Head: null,
            Keypoints: dragged,
            Drags: ["left hand → (500,400)"]));

        Assert.NotNull(preset.ProvenanceJson);
        Assert.Contains("\"kind\":\"dragged\"", preset.ProvenanceJson!, StringComparison.Ordinal);
        Assert.Contains("drags", preset.ProvenanceJson!, StringComparison.Ordinal);
        Assert.Contains("authored dragged", preset.Keywords, StringComparison.Ordinal);

        // The stored keypoints are the dragged ones, not a re-projection of the view.
        var stored = OpenPosePoseJson.Parse(preset.KeypointsJson, "saved dragged pose");
        Assert.Equal(dragged.Body[LeftWrist].X, stored.Body[LeftWrist].X, 3);
    }

    private static PosePerson Standing()
    {
        using var fixture = new PoseLibraryTestFixture();
        return fixture.Service.ProjectAuthoredPose(new PoseView());
    }

    private static PoseKeypoint[] Chain(params (double X, double Y)[] points) =>
        points.Select(point => new PoseKeypoint(point.X, point.Y, 1.0)).ToArray();

    private static double[] Lengths(IReadOnlyList<PoseKeypoint> chain)
    {
        var lengths = new double[chain.Count - 1];
        for (var index = 0; index < lengths.Length; index++)
        {
            lengths[index] = Distance(chain[index], chain[index + 1]);
        }

        return lengths;
    }

    private static void AssertLengths(IReadOnlyList<double> expected, IReadOnlyList<PoseKeypoint> chain)
    {
        var actual = Lengths(chain);
        Assert.Equal(expected.Count, actual.Length);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index], actual[index], 2);
        }
    }

    private static double Distance(PoseKeypoint a, PoseKeypoint b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
