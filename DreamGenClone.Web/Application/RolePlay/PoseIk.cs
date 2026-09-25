namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Which chain a drag moves. The root of each chain never moves; the effector follows the pointer.</summary>
public enum PoseDragChain
{
    /// <summary>Right arm: shoulder → elbow → wrist.</summary>
    RightArm,

    /// <summary>Left arm: shoulder → elbow → wrist.</summary>
    LeftArm,

    /// <summary>Right leg: hip → knee → ankle.</summary>
    RightLeg,

    /// <summary>Left leg: hip → knee → ankle.</summary>
    LeftLeg
}

/// <summary>
/// Inverse kinematics for the drag editor, as FABRIK (forward and backward reaching) over a chain of fixed-length
/// bones.
///
/// FABRIK rather than a pencil-and-paper triangle solve because a handshake is not a two-bone problem: the wrist
/// target has to be reached while the elbow keeps its exact distance from BOTH the shoulder and the wrist, and
/// FABRIK gets that by construction — it only ever moves a joint onto the ray between its neighbours, so no bone
/// can stretch. That invariant is the whole point: a pose whose limbs lengthen when dragged is not a pose the
/// model can render.
///
/// This operates in 2D on the projected skeleton, which is what the editor drags. It deliberately does not try to
/// guess a depth: a 2D screen drag has no third dimension to read, and inventing one would put a fabricated
/// number inside the geometry path.
/// </summary>
public static class PoseIk
{
    /// <summary>Reach iterations. FABRIK converges in a handful; the cap keeps a drag bounded and deterministic.</summary>
    public const int DefaultIterations = 12;

    /// <summary>Stop once the effector is within this many units of the target.</summary>
    public const double DefaultTolerance = 0.5;

    /// <summary>The COCO joints each chain is solved over, root first, effector last.</summary>
    private static readonly Dictionary<PoseDragChain, int[]> Chains = new()
    {
        [PoseDragChain.RightArm] = [2, 3, 4],
        [PoseDragChain.LeftArm] = [5, 6, 7],
        [PoseDragChain.RightLeg] = [8, 9, 10],
        [PoseDragChain.LeftLeg] = [11, 12, 13]
    };

    /// <summary>The COCO index each chain's effector ends on — the joint a drag actually moves.</summary>
    private static readonly Dictionary<int, PoseDragChain> Effectors = new()
    {
        [4] = PoseDragChain.RightArm,
        [7] = PoseDragChain.LeftArm,
        [10] = PoseDragChain.RightLeg,
        [13] = PoseDragChain.LeftLeg
    };

    /// <summary>True when dragging this joint is something the editor offers.</summary>
    public static bool CanDrag(int cocoIndex) => Effectors.ContainsKey(cocoIndex);

    /// <summary>The joints a chain moves, root first. Exposed so the editor can show what will follow a drag.</summary>
    public static IReadOnlyList<int> JointsOf(PoseDragChain chain) => Chains[chain];

    /// <summary>
    /// Moves the chain that ends at <paramref name="effectorIndex"/> so its effector reaches
    /// <paramref name="target"/>, leaving every other joint untouched and every bone at its original length.
    /// </summary>
    public static PosePerson Drag(
        PosePerson person,
        int effectorIndex,
        PoseKeypoint target,
        int iterations = DefaultIterations,
        double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(person);

        if (!Effectors.TryGetValue(effectorIndex, out var chain))
        {
            throw new InvalidOperationException(
                $"Joint {effectorIndex} is not draggable. The editor drags the endpoints of the four limbs "
                + $"(wrists {string.Join(", ", Chains[PoseDragChain.RightArm][2], Chains[PoseDragChain.LeftArm][2])} "
                + $"and ankles {string.Join(", ", Chains[PoseDragChain.RightLeg][2], Chains[PoseDragChain.LeftLeg][2])}); "
                + "moving a shoulder or a hip would drag the whole body.");
        }

        var indices = Chains[chain];
        var chainPoints = indices.Select(index => person.Body[index]).ToArray();
        var solved = Solve(chainPoints, target, iterations, tolerance);

        var body = person.Body.ToArray();
        for (var step = 0; step < indices.Length; step++)
        {
            body[indices[step]] = solved[step];
        }

        return new PosePerson
        {
            Body = body,
            LeftHand = person.LeftHand,
            RightHand = person.RightHand
        };
    }

    /// <summary>
    /// FABRIK over one chain. Bone lengths are taken from the chain as it stands, so a drag can never stretch one;
    /// a target beyond the chain's reach extends the chain straight at it and stops, rather than snapping or
    /// silently shortening a bone.
    /// </summary>
    public static IReadOnlyList<PoseKeypoint> Solve(
        IReadOnlyList<PoseKeypoint> chain,
        PoseKeypoint target,
        int iterations = DefaultIterations,
        double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count < 2)
        {
            throw new InvalidOperationException("An IK chain needs at least two joints.");
        }

        var lengths = new double[chain.Count - 1];
        for (var index = 0; index < lengths.Length; index++)
        {
            lengths[index] = Distance(chain[index], chain[index + 1]);
            if (lengths[index] <= 0)
            {
                throw new InvalidOperationException(
                    $"The chain has a zero-length bone between joints {index} and {index + 1}, so its direction is "
                    + "undefined and it cannot be solved.");
            }
        }

        var root = chain[0];
        var points = chain.Select(point => (X: point.X, Y: point.Y)).ToArray();

        var totalLength = lengths.Sum();
        var rootToTarget = Distance(root.X, root.Y, target.X, target.Y);

        if (rootToTarget > totalLength)
        {
            // Out of reach: lay the chain out straight towards the target. Every bone keeps its length and the
            // effector simply cannot arrive — the honest outcome, not a stretch.
            var scale = totalLength / rootToTarget;
            points[0] = (root.X, root.Y);
            for (var index = 0; index < lengths.Length; index++)
            {
                var forwardX = (target.X - root.X) * scale;
                var forwardY = (target.Y - root.Y) * scale;
                points[index + 1] = (points[index].X + (forwardX * lengths[index] / totalLength),
                    points[index].Y + (forwardY * lengths[index] / totalLength));
            }

            return ToKeypoints(points, chain);
        }

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            if (Distance(points[^1].X, points[^1].Y, target.X, target.Y) <= tolerance) break;

            // Backward: pull the effector onto the target, then walk to the root.
            points[^1] = (target.X, target.Y);
            for (var index = points.Length - 2; index >= 0; index--)
            {
                points[index] = Reach(points[index], points[index + 1], lengths[index]);
            }

            // Forward: pin the root again and walk back out to the effector.
            points[0] = (root.X, root.Y);
            for (var index = 1; index < points.Length; index++)
            {
                points[index] = Reach(points[index], points[index - 1], lengths[index - 1]);
            }
        }

        return ToKeypoints(points, chain);
    }

    /// <summary>Places <paramref name="from"/> at exactly <paramref name="length"/> away from <paramref name="anchor"/>.</summary>
    private static (double X, double Y) Reach((double X, double Y) from, (double X, double Y) anchor, double length)
    {
        var dx = from.X - anchor.X;
        var dy = from.Y - anchor.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        if (distance <= double.Epsilon)
        {
            // Coincident joints have no direction to keep; nudge along +X so the chain stays solvable.
            return (anchor.X + length, anchor.Y);
        }

        var ratio = length / distance;
        return (anchor.X + (dx * ratio), anchor.Y + (dy * ratio));
    }

    /// <summary>
    /// Rebuilds the chain as keypoints. Positions change; confidence is carried over so an invisible joint cannot
    /// be brought to life by being dragged past.
    /// </summary>
    private static IReadOnlyList<PoseKeypoint> ToKeypoints(
        (double X, double Y)[] points, IReadOnlyList<PoseKeypoint> source)
    {
        var result = new PoseKeypoint[points.Length];
        for (var index = 0; index < points.Length; index++)
        {
            result[index] = new PoseKeypoint(points[index].X, points[index].Y, source[index].Confidence);
        }

        return result;
    }

    private static double Distance(PoseKeypoint a, PoseKeypoint b) => Distance(a.X, a.Y, b.X, b.Y);

    private static double Distance(double ax, double ay, double bx, double by) =>
        Math.Sqrt(Math.Pow(ax - bx, 2) + Math.Pow(ay - by, 2));
}
