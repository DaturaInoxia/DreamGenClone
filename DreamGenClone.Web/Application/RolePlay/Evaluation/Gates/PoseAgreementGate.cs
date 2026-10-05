namespace DreamGenClone.Web.Application.RolePlay.Evaluation.Gates;

/// <summary>
/// B135-018 (P3): the pose-agreement gate — a C# port of the joint-geometry comparison in
/// <c>tools/pose-angle-probe/probe_pose_angle.py</c>.
///
/// <para>
/// It compares two single-person OpenPose bodies — the candidate the app projected and the reference
/// DWPose read back from a render — as JOINT GEOMETRY, never raster overlap (raster IoU was already
/// proved invalid for this purpose). Both sides are normalised by figure height and centred on their
/// own bounding box, so the comparison is scale- and position-invariant; every error is a percentage
/// of height. The pass bar is declared by the caller, exactly as the canonical tool does — it never
/// decides the bar itself.
/// </para>
///
/// <para>
/// Per the operator's "nothing blocks" rule, this records a verdict; it never refuses a render or
/// changes behaviour.
/// </para>
/// </summary>
public static class PoseAgreementGate
{
    /// <summary>A keypoint counts as visible only above this score (matches <c>OpenPosePoseJson.VisibilityFloor</c>).</summary>
    public const double VisibilityFloor = 0.1;

    /// <summary>OpenPose COCO-18 body joint count.</summary>
    public const int BodyJointCount = 18;

    /// <summary>Fewer visible joints than this cannot be normalised or compared (matches the canonical tool).</summary>
    public const int MinVisibleJoints = 4;

    public const int RightShoulderIndex = 2;
    public const int LeftShoulderIndex = 5;

    public static PoseAgreementResult Measure(
        IReadOnlyList<PoseKeypoint> candidateBody,
        IReadOnlyList<PoseKeypoint> referenceBody,
        double meanErrorBarPercent)
    {
        var candidate = Normalise(candidateBody);
        var reference = Normalise(referenceBody);

        var shared = new List<int>();
        for (var i = 0; i < BodyJointCount; i++)
        {
            if (candidate.ContainsKey(i) && reference.ContainsKey(i))
                shared.Add(i);
        }

        if (shared.Count == 0)
            throw new InvalidOperationException("The two poses share no visible joints, so there is nothing to compare.");

        var perJoint = new List<(int Joint, double ErrorPct)>();
        foreach (var joint in shared)
        {
            var (cx, cy) = candidate[joint];
            var (rx, ry) = reference[joint];
            var dx = cx - rx;
            var dy = cy - ry;
            var errorPct = Math.Round(Math.Sqrt(dx * dx + dy * dy) * 100.0, 3);
            perJoint.Add((Joint: joint, ErrorPct: errorPct));
        }

        var errors = perJoint.Select(item => item.ErrorPct).OrderBy(value => value).ToList();
        var count = errors.Count;
        var mean = errors.Sum() / count;
        var p95Index = Math.Min(count - 1, (int)Math.Round(0.95 * (count - 1)));

        return new PoseAgreementResult(
            JointsCompared: count,
            MeanErrorPctOfHeight: Math.Round(mean, 3),
            P95ErrorPctOfHeight: Math.Round(errors[p95Index], 3),
            MaxErrorPctOfHeight: Math.Round(errors[^1], 3),
            WorstJointIndex: perJoint.OrderByDescending(item => item.ErrorPct).First().Joint,
            CandidateShoulderSpanPctOfHeight: ShoulderSpanPct(candidate),
            ReferenceShoulderSpanPctOfHeight: ShoulderSpanPct(reference),
            Pass: mean < meanErrorBarPercent);
    }

    private static Dictionary<int, (double X, double Y)> Normalise(IReadOnlyList<PoseKeypoint> body)
    {
        var visible = new List<(int Index, double X, double Y)>();
        for (var i = 0; i < body.Count && i < BodyJointCount; i++)
        {
            if (body[i].Confidence > VisibilityFloor)
                visible.Add((i, body[i].X, body[i].Y));
        }

        if (visible.Count < MinVisibleJoints)
            throw new InvalidOperationException(
                $"The pose has only {visible.Count} visible body joints; {MinVisibleJoints} are needed to normalise or compare.");

        var minX = visible.Min(item => item.X);
        var maxX = visible.Max(item => item.X);
        var minY = visible.Min(item => item.Y);
        var maxY = visible.Max(item => item.Y);
        var height = maxY - minY;
        if (height <= 0.0)
            throw new InvalidOperationException("The pose has zero height, so it cannot be normalised.");

        var centreX = (minX + maxX) / 2.0;
        var centreY = (minY + maxY) / 2.0;

        var normalised = new Dictionary<int, (double X, double Y)>();
        foreach (var item in visible)
            normalised[item.Index] = ((item.X - centreX) / height, (item.Y - centreY) / height);
        return normalised;
    }

    private static double? ShoulderSpanPct(Dictionary<int, (double X, double Y)> normalised)
    {
        if (!normalised.TryGetValue(RightShoulderIndex, out var right)
            || !normalised.TryGetValue(LeftShoulderIndex, out var left))
            return null;

        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Round(Math.Sqrt(dx * dx + dy * dy) * 100.0, 3);
    }
}

/// <summary>The pose-agreement report, mirroring the canonical tool's return shape.</summary>
public sealed record PoseAgreementResult(
    int JointsCompared,
    double MeanErrorPctOfHeight,
    double P95ErrorPctOfHeight,
    double MaxErrorPctOfHeight,
    int WorstJointIndex,
    double? CandidateShoulderSpanPctOfHeight,
    double? ReferenceShoulderSpanPctOfHeight,
    bool Pass);
