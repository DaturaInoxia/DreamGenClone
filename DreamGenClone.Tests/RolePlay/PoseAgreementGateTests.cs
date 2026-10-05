using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Evaluation.Gates;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B135-018 (P3): the pose-agreement gate must reproduce <c>tools/pose-angle-probe/probe_pose_angle.py</c>
/// joint-geometry scoring (normalise by figure height, centre on the bounding box, error as % of height,
/// caller-declared bar). These pin the port on a hand-computable T-pose.
/// </summary>
public sealed class PoseAgreementGateTests
{
    [Fact]
    public void IdenticalPoses_HaveZeroMeanError_AndPass()
    {
        var result = PoseAgreementGate.Measure(TBody(), TBody(), meanErrorBarPercent: 6.0);

        Assert.Equal(18, result.JointsCompared);
        Assert.Equal(0.0, result.MeanErrorPctOfHeight);
        Assert.Equal(0.0, result.MaxErrorPctOfHeight);
        Assert.True(result.Pass);
    }

    [Fact]
    public void TheTpose_ShoulderSpan_IsFortyPercentOfHeight()
    {
        // Visible bbox is x 0.1..0.9, y 0..1 → height 1.0, centre (0.5, 0.5). Shoulders at x 0.3/0.7 → span 0.4.
        var result = PoseAgreementGate.Measure(TBody(), TBody(), meanErrorBarPercent: 6.0);

        Assert.Equal(40.0, result.CandidateShoulderSpanPctOfHeight);
        Assert.Equal(40.0, result.ReferenceShoulderSpanPctOfHeight);
    }

    [Fact]
    public void TheDeclaredBar_IsTheOnlyVerdictThreshold()
    {
        // The canonical tool never decides the bar: the caller does. An identical pose fails a negative bar.
        Assert.False(PoseAgreementGate.Measure(TBody(), TBody(), meanErrorBarPercent: -0.1).Pass);
        Assert.True(PoseAgreementGate.Measure(TBody(), TBody(), meanErrorBarPercent: 0.1).Pass);
    }

    [Fact]
    public void ASingleJointShift_RaisesTheMeanAndMaxError()
    {
        var shifted = WithJoint(TBody(), index: 4, x: 0.0, y: 0.5, confidence: 1.0); // r_wrist moved left

        var identical = PoseAgreementGate.Measure(TBody(), TBody(), meanErrorBarPercent: 6.0);
        var result = PoseAgreementGate.Measure(TBody(), shifted, meanErrorBarPercent: 6.0);

        Assert.True(result.MeanErrorPctOfHeight > identical.MeanErrorPctOfHeight);
        Assert.True(result.MaxErrorPctOfHeight > identical.MaxErrorPctOfHeight);
    }

    [Fact]
    public void AJointBelowTheVisibilityFloor_IsExcludedFromTheComparison()
    {
        var candidate = WithJoint(TBody(), index: 13, x: 0.6, y: 1.0, confidence: 0.05); // l_ankle hidden

        var result = PoseAgreementGate.Measure(candidate, TBody(), meanErrorBarPercent: 6.0);

        Assert.Equal(17, result.JointsCompared);
    }

    private static IReadOnlyList<PoseKeypoint> TBody() =>
    [
        new(0.5, 0.0, 1.0),    // nose
        new(0.5, 0.1, 1.0),    // neck
        new(0.3, 0.1, 1.0),    // r_shoulder
        new(0.15, 0.3, 1.0),   // r_elbow
        new(0.1, 0.5, 1.0),    // r_wrist
        new(0.7, 0.1, 1.0),    // l_shoulder
        new(0.85, 0.3, 1.0),   // l_elbow
        new(0.9, 0.5, 1.0),    // l_wrist
        new(0.4, 0.5, 1.0),    // r_hip
        new(0.4, 0.75, 1.0),   // r_knee
        new(0.4, 1.0, 1.0),    // r_ankle
        new(0.6, 0.5, 1.0),    // l_hip
        new(0.6, 0.75, 1.0),   // l_knee
        new(0.6, 1.0, 1.0),    // l_ankle
        new(0.48, 0.02, 1.0),  // r_eye
        new(0.52, 0.02, 1.0),  // l_eye
        new(0.44, 0.03, 1.0),  // r_ear
        new(0.56, 0.03, 1.0),  // l_ear
    ];

    private static IReadOnlyList<PoseKeypoint> WithJoint(
        IReadOnlyList<PoseKeypoint> body, int index, double x, double y, double confidence)
    {
        var copy = body.ToArray();
        copy[index] = new PoseKeypoint(x, y, confidence);
        return copy;
    }
}
