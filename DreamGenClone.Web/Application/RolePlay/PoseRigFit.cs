using System.Numerics;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// What a fit recovered: the view the pose was observed from, a rig pose that reproduces it, and how closely it
/// does — reported as the same measure the acceptance probe uses, so a fit and a render are comparable numbers.
/// </summary>
/// <param name="View">The camera angle the observed pose is most consistent with.</param>
/// <param name="Rotations">Rig local rotations. Feed these to the projection and the authoring tools work on the pose.</param>
/// <param name="Reprojection">The rig re-projected at <paramref name="View"/> — what the user is about to edit.</param>
/// <param name="MeanErrorPercentOfHeight">Mean joint displacement, as a percentage of figure height.</param>
/// <param name="WorstJoint">The joint that fit worst, so a bad fit names itself.</param>
/// <param name="JointsMatched">How many joints both sides had visible. A low count means a weak fit.</param>
/// <param name="OrientationAmbiguous">
/// True when more than one view fitted within the tolerance. The shape is matched either way, but the TURN is not
/// determined by the observation — so a caller that needs to know which way the figure faces (to say so in a
/// prompt, for instance) must not read this fit's yaw as that answer.
/// </param>
public sealed record PoseRigFitResult(
    PoseView View,
    Quaternion[] Rotations,
    PosePerson Reprojection,
    double MeanErrorPercentOfHeight,
    string WorstJoint,
    int JointsMatched)
{
    /// <summary>Set when several views fitted equally well. See the type summary.</summary>
    public bool OrientationAmbiguous { get; init; }
}

/// <summary>
/// Fits the authoring rig onto an observed 2D pose, so a pose from the library can be opened in the editor and
/// used with every tool — the 5° arrows, the head controls and the drag editor all drive the RIG, and a library
/// pose is only keypoints.
///
/// Why this is needed at all: a library pose (or any DWPose extraction) is a 2D projection with no depth, and the
/// rig needs a complete 3D pose to rotate. Editing the 2D keypoints directly is the approach this repository
/// already measured and rejected — rotating them in the image plane only tilts the figure, and injecting a
/// monocular depth estimate sheared a 1.30 m body by ~27 cm. Fitting is the third option and the one that works:
/// the rig's depths are synthetic and mutually consistent, so a fitted rig can be rotated exactly.
///
/// How it fits: the rig's own projection is the forward model, and the parameters are the camera yaw plus one
/// rotation per bendable joint. Both sides are normalised to a unit-height, centre-on-bounding-box frame first,
/// because the observed pose is in some source image's pixels and the rig's is on its own canvas — what can be
/// compared is the SHAPE, not the placement. Coordinate descent then minimises the mean joint displacement, which
/// keeps the solver deterministic: no random restarts, same input, same answer.
///
/// What it honestly cannot do: a single 2D view does not determine depth. A limb aimed at the camera, or crossed
/// arms, can be lifted wrongly, and a front-facing and a back-facing hypothesis fit a near-symmetric figure almost
/// equally well. The result therefore reports its own residual rather than claiming exactness, and the caller
/// decides whether that residual is good enough to edit from.
/// </summary>
public static class PoseRigFit
{
    /// <summary>Joints the fit may rotate. Terminal joints are excluded: rotating a wrist moves no joint.</summary>
    private static readonly string[] BendableJoints =
    [
        "neck", "head",
        "shoulder_r", "elbow_r", "shoulder_l", "elbow_l",
        "hip_r", "knee_r", "hip_l", "knee_l"
    ];

    /// <summary>Yaw seeds. The objective has local minima in yaw, so the descent starts from several views.
    /// Searched from the smallest turn outward: a symmetric standing figure is explained almost equally well by
    /// several views, and the first one to reach the tolerance wins the tie.</summary>
    private static readonly double[] YawSeeds = [0, 45, -45, 90, -90, 135, -135, 180];

    /// <summary>
    /// How much worse a less-turned view may fit and still be preferred, in percentage points of figure height.
    ///
    /// A single 2D view does not determine the turn: measured 2026-09-25, a frontal standing pack pose fitted to
    /// <b>1.49% with the rig turned 126.8°</b>, which looks identical but claims a turn neither the pose nor the
    /// operator asked for — and would send the 5° arrows the wrong way. A residual difference below a percentage
    /// point is smaller than the spread this pipeline measures on renders (2.1–4.3% on the same angles), so it is
    /// not a difference anyone can see. So among fits within this margin the least-turned wins.
    /// </summary>
    private const double MeaningfulErrorMarginPercentOfHeight = 1.0;

    /// <summary>Coarsest rotation step, in degrees. Halved until <see cref="MinimumStepDegrees"/>.</summary>
    private const double InitialStepDegrees = 24.0;

    private const double MinimumStepDegrees = 0.75;

    /// <summary>
    /// How many times the whole step schedule is run. One pass stalls easily: a joint that needs a large move is
    /// stranded once the shared step has shrunk, and a bent limb is exactly that case — measured 2026-09-25, a
    /// squatting pack pose stalled with its knee as the worst joint at 8.40% while a standing pose fitted to
    /// 2.09%. Re-running the schedule from the coarse step again lets those joints move.
    /// </summary>
    private const int RefinementRounds = 4;

    /// <summary>A fit that lands worse than this over more than a handful of joints is a warning, not a number.</summary>
    public const double UsableMeanErrorPercentOfHeight = 8.0;

    /// <summary>Fits <paramref name="observed"/> onto the rig and reports how well it landed.</summary>
    public static PoseRigFitResult Fit(
        PosePerson observed,
        PoseStudioOptions settings,
        PoseView? startingView = null)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(settings);

        var mannequin = PoseMannequin.Standing();
        var target = Normalise(observed, "the observed pose");
        var jointIndices = BendableJoints.Select(mannequin.JointIndex).ToArray();

        var seeds = startingView is null ? YawSeeds : [startingView.YawDegrees];

        var candidates = seeds
            .Select(yaw => Descend(
                mannequin,
                target,
                jointIndices,
                settings,
                new PoseView(startingView?.YawDegrees ?? yaw, startingView?.PitchDegrees ?? 0, startingView?.RollDegrees ?? 0)))
            .ToArray();

        // Seeds run from the smallest turn outward, so the first candidate inside the margin is the least turned
        // one. A genuinely turned pose is not affected: its frontal explanation is far worse than the margin.
        var bestError = candidates.Min(candidate => candidate.MeanErrorPercentOfHeight);
        var withinMargin = candidates
            .Where(candidate => candidate.MeanErrorPercentOfHeight <= bestError + MeaningfulErrorMarginPercentOfHeight)
            .ToArray();

        return withinMargin[0] with
        {
            OrientationAmbiguous = withinMargin
                .Select(candidate => Math.Round(candidate.View.YawDegrees, 1))
                .Distinct()
                .Count() > 1
        };
    }

    /// <summary>
    /// Coordinate descent over the view and the joint rotations: for each parameter, try a step either way and keep
    /// a step only when it improves the fit, halving the step when nothing helps.
    /// </summary>
    private static PoseRigFitResult Descend(
        PoseMannequin mannequin,
        IReadOnlyDictionary<int, (double X, double Y)> target,
        int[] jointIndices,
        PoseStudioOptions settings,
        PoseView view)
    {
        var rotations = mannequin.StandingStance();
        var parameters = new double[(jointIndices.Length * 3) + 3];
        parameters[0] = view.YawDegrees;
        parameters[1] = view.PitchDegrees;
        parameters[2] = view.RollDegrees;

        double Measure() => Evaluate(mannequin, rotations, parameters, jointIndices, target, settings).Mean;

        var current = Measure();
        for (var round = 0; round < RefinementRounds; round++)
        {
            for (var step = InitialStepDegrees; step >= MinimumStepDegrees; step /= 2.0)
            {
                for (var index = 0; index < parameters.Length; index++)
                {
                    var original = parameters[index];

                    parameters[index] = ClampParameter(index, original + step);
                    var up = Measure();

                    parameters[index] = ClampParameter(index, original - step);
                    var down = Measure();

                    if (up < current && up <= down) parameters[index] = ClampParameter(index, original + step);
                    else if (down < current) parameters[index] = ClampParameter(index, original - step);
                    else parameters[index] = original;

                    current = Measure();
                }
            }
        }

        var evaluation = Evaluate(mannequin, rotations, parameters, jointIndices, target, settings);
        return new PoseRigFitResult(
            new PoseView(parameters[0], parameters[1], parameters[2]),
            rotations,
            evaluation.Reprojection,
            Math.Round(evaluation.Mean * 100.0, 3),
            evaluation.WorstJoint,
            evaluation.Matched);
    }

    /// <summary>
    /// The projection is the forward model, so a candidate is scored by projecting it and comparing the normalised
    /// shape. Joint rotations are written straight onto the rig's rotation array, which is what makes every other
    /// tool usable on the result.
    /// </summary>
    private static (double Mean, string WorstJoint, int Matched, PosePerson Reprojection) Evaluate(
        PoseMannequin mannequin,
        Quaternion[] rotations,
        double[] parameters,
        int[] jointIndices,
        IReadOnlyDictionary<int, (double X, double Y)> target,
        PoseStudioOptions settings)
    {
        for (var index = 0; index < jointIndices.Length; index++)
        {
            var x = parameters[3 + (index * 3)];
            var y = parameters[4 + (index * 3)];
            var z = parameters[5 + (index * 3)];

            rotations[jointIndices[index]] = Quaternion.Normalize(
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Radians(z))
                * Quaternion.CreateFromAxisAngle(Vector3.UnitX, Radians(x))
                * Quaternion.CreateFromAxisAngle(Vector3.UnitY, Radians(y)));
        }

        var view = new PoseView(parameters[0], parameters[1], parameters[2]);
        var reprojection = PoseProjection.Project(mannequin, rotations, view, settings);
        var projected = Normalise(reprojection, "the rig's projection");

        var total = 0.0;
        var worst = 0.0;
        var worstJoint = "none";
        var matched = 0;

        foreach (var (cocoIndex, (tx, ty)) in target)
        {
            if (!projected.TryGetValue(cocoIndex, out var point)) continue;

            var distance = Math.Sqrt(((point.X - tx) * (point.X - tx)) + ((point.Y - ty) * (point.Y - ty)));
            total += distance;
            matched++;

            if (distance > worst)
            {
                worst = distance;
                worstJoint = CocoJointNames[cocoIndex];
            }
        }

        return (matched == 0 ? double.MaxValue : total / matched, worstJoint, matched, reprojection);
    }

    /// <summary>
    /// Puts a pose into a unit-height frame centred on its own visible bounding box, so two poses from different
    /// images are comparable. The same transform the acceptance probe uses, deliberately: a fit and a render
    /// measurement have to be the same kind of number to be worth reading together.
    /// </summary>
    private static Dictionary<int, (double X, double Y)> Normalise(PosePerson person, string what)
    {
        var visible = person.Body
            .Select((point, index) => (point, index))
            .Where(entry => entry.point.Confidence > OpenPosePoseJson.VisibilityFloor)
            .ToArray();

        if (visible.Length < 4)
        {
            throw new InvalidOperationException(
                $"'{what}' has only {visible.Length} visible body joints, which is too few to fit a rig to.");
        }

        var minX = visible.Min(entry => entry.point.X);
        var maxX = visible.Max(entry => entry.point.X);
        var minY = visible.Min(entry => entry.point.Y);
        var maxY = visible.Max(entry => entry.point.Y);

        var height = maxY - minY;
        if (height <= 0)
        {
            throw new InvalidOperationException($"'{what}' has no height, so it cannot be compared with the rig.");
        }

        var centreX = (minX + maxX) / 2.0;
        var centreY = (minY + maxY) / 2.0;

        return visible.ToDictionary(
            entry => entry.index,
            entry => ((entry.point.X - centreX) / height, (entry.point.Y - centreY) / height));
    }

    private static double ClampParameter(int index, double value) => index switch
    {
        1 => Math.Clamp(value, -PoseProjection.PitchLimitDegrees, PoseProjection.PitchLimitDegrees),
        2 => Math.Clamp(value, -PoseProjection.PitchLimitDegrees, PoseProjection.PitchLimitDegrees),
        _ => value
    };

    private static float Radians(double degrees) => (float)(degrees * Math.PI / 180.0);

    private static readonly string[] CocoJointNames =
    [
        "nose", "neck", "r_shoulder", "r_elbow", "r_wrist", "l_shoulder", "l_elbow", "l_wrist",
        "r_hip", "r_knee", "r_ankle", "l_hip", "l_knee", "l_ankle", "r_eye", "l_eye", "r_ear", "l_ear"
    ];
}
