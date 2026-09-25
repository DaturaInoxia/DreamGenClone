using System.Numerics;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Where the camera looks from and how the figure is turned. Yaw turns the figure about the vertical axis
/// (what the left/right arrows do), pitch tips it forward or back, roll tilts it.
/// </summary>
public sealed record PoseView(double YawDegrees = 0, double PitchDegrees = 0, double RollDegrees = 0);

/// <summary>Which axis a rotation applies to.</summary>
public enum PoseRotationAxis
{
    /// <summary>About the vertical axis: the figure turns to face 3⁄4 and then profile.</summary>
    Yaw,

    /// <summary>About the lateral axis: the figure tips forward or back, and a head looks down or up.</summary>
    Pitch,

    /// <summary>About the view axis: the figure tilts without turning.</summary>
    Roll
}

/// <summary>
/// Turns a posed rig into a 2D skeleton by projecting it — the one core the body and the head both use.
///
/// This is a real camera projection of one coherent rigid figure, which is what makes a requested angle exact
/// rather than approximate: the rotation is a rotation, and the perspective term supplies the foreshortening and
/// narrowing that a 2D edit cannot. It is deliberately NOT a monocular depth estimate (see
/// <see cref="PoseMannequin"/> for the measured reason).
/// </summary>
public static class PoseProjection
{
    /// <summary>
    /// How much clear space the camera must have past the figure's own depth, as a multiple of that depth. Below
    /// this the projection divides by a vanishing denominator and the figure inverts, so it is refused by name
    /// instead of producing a mirrored skeleton nobody can explain.
    /// </summary>
    private const double DepthClearanceFactor = 1.25;

    /// <summary>Projects the rig, rotated by <paramref name="view"/>, into COCO-18 keypoints.</summary>
    public static PosePerson Project(
        PoseMannequin mannequin,
        IReadOnlyList<Quaternion> localRotations,
        PoseView view,
        PoseStudioOptions settings)
    {
        ArgumentNullException.ThrowIfNull(mannequin);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(settings);

        var focalLength = settings.RequireFocalLengthPx();
        var cameraDistance = settings.RequireCameraDistance();
        var canvas = settings.RequireCanvas();

        var rest = mannequin.ForwardFrames(localRotations);
        var viewRotation = BuildRotation(view);
        var rotated = ApplyView(mannequin, rest.Positions, viewRotation);

        // The figure's own depth extent, which the camera has to clear. A turning figure grows deeper: at profile
        // the arm span points at the camera, so the check is made after the rotation, not before it.
        var depthExtent = rotated.Max(position => Math.Abs(position.Z));
        var requiredDistance = depthExtent * DepthClearanceFactor;
        var nearestPlaneDistance = cameraDistance - rotated.Max(position => position.Z);

        if (nearestPlaneDistance <= 0 || cameraDistance <= requiredDistance)
        {
            throw new InvalidOperationException(
                $"The camera at distance {cameraDistance:0.###} is too close to the figure, whose depth extent is "
                + $"{depthExtent:0.###}. Raise '{PoseStudioOptions.SectionName}:"
                + $"{nameof(PoseStudioOptions.CameraDistance)}' above {requiredDistance:0.###} — closer than that "
                + "the projection inverts instead of foreshortening.");
        }

        var centre = canvas / 2.0;
        var body = new PoseKeypoint[PoseMannequin.CocoJointCount];

        for (var cocoIndex = 0; cocoIndex < PoseMannequin.CocoJointCount; cocoIndex++)
        {
            var jointIndex = mannequin.CocoIndices[cocoIndex];
            var position = rotated[jointIndex];
            var denominator = cameraDistance - position.Z;

            var screenX = focalLength * (position.X / denominator);
            // Image y grows down while the rig's y grows up, hence the negation.
            var screenY = -focalLength * (position.Y / denominator);

            body[cocoIndex] = new PoseKeypoint(
                centre + screenX,
                centre + screenY,
                Visibility(mannequin.Joints[jointIndex], rest.Rotations[jointIndex], viewRotation));
        }

        return new PosePerson { Body = body };
    }

    /// <summary>
    /// Whether a joint is visible from this view: 1 when it faces the camera within its own limit, 0 when it has
    /// turned away.
    ///
    /// This is the channel OpenPose uses for visibility, and holding it at 1 for every joint was a real defect with
    /// a visible cost: the renderer draws a joint only above the visibility floor, so a constant 1 drew a complete
    /// face — a nose and both eyes — in every single view. A front view and a view turned away therefore produced
    /// the same picture, and a model handed it had nothing to read the figure's facing from (measured 2026-09-24:
    /// the front skeleton differed from its own mirror by 1.2% of pixels, and Qwen-2.1 rendered the front pose
    /// facing away from the camera). A joint with no facing — a shoulder, an ear — stays visible, because those are
    /// visible from the front and the back alike.
    /// </summary>
    private static double Visibility(MannequinJoint joint, Quaternion jointRotation, Quaternion viewRotation)
    {
        if (joint.Facing is not Vector3 facing) return 1.0;

        // The joint's own world rotation comes first, then the view's, so a head turn and a body turn both count.
        var total = Quaternion.Normalize(viewRotation * jointRotation);
        var towardsCamera = Vector3.Transform(Vector3.Normalize(facing), total).Z;
        var limit = Math.Cos(joint.FacingLimitDegrees * Math.PI / 180.0);

        return towardsCamera > limit ? 1.0 : 0.0;
    }

    /// <summary>Rotates the whole figure about its own root, so it turns on the spot rather than orbiting away.</summary>
    private static Vector3[] ApplyView(
        PoseMannequin mannequin, Vector3[] positions, Quaternion viewRotation)
    {
        var root = positions[mannequin.RootIndex];

        var rotated = new Vector3[positions.Length];
        for (var index = 0; index < positions.Length; index++)
        {
            rotated[index] = root + Vector3.Transform(positions[index] - root, viewRotation);
        }

        return rotated;
    }

    private static Quaternion BuildRotation(PoseView view) =>
        Quaternion.Normalize(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Degrees(view.RollDegrees))
            // Negated so a POSITIVE pitch means the figure tips back / looks up. Rotating about +X by a positive
            // angle carries the figure's front (+Z) downward, which would make the arrows read backwards.
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, Degrees(-view.PitchDegrees))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(view.YawDegrees)));

    /// <summary>Rotates the figure by one step of the configured size, in the given direction.</summary>
    public static PoseView Step(PoseView view, PoseRotationAxis axis, int steps, PoseStudioOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (steps == 0) return view;

        return Rotate(view, axis, settings.RequireRotationStepDegrees() * steps);
    }

    /// <summary>
    /// Adds a rotation to a view. Yaw and roll accumulate without wrapping; pitch is clamped short of the poles
    /// because at exactly ±90° the figure's own up vector is parallel to the view axis and the projection has no
    /// stable orientation to lay out.
    /// </summary>
    public static PoseView Rotate(PoseView view, PoseRotationAxis axis, double degrees)
    {
        ArgumentNullException.ThrowIfNull(view);

        return axis switch
        {
            PoseRotationAxis.Yaw => view with { YawDegrees = view.YawDegrees + degrees },
            PoseRotationAxis.Roll => view with { RollDegrees = view.RollDegrees + degrees },
            PoseRotationAxis.Pitch => view with { PitchDegrees = ClampPitch(view.PitchDegrees + degrees) },
            _ => throw new InvalidOperationException($"Unknown rotation axis '{axis}'.")
        };
    }

    /// <summary>The pitch limit, in degrees: the same 90° pole, pulled back far enough to stay stable.</summary>
    public const double PitchLimitDegrees = 89.0;

    private static double ClampPitch(double degrees) =>
        Math.Clamp(degrees, -PitchLimitDegrees, PitchLimitDegrees);

    private static float Degrees(double value) => (float)(value * Math.PI / 180.0);
}

/// <summary>
/// How the head is turned relative to the body, as a rotation of the rig's neck joint. Yaw turns the face left or
/// right (front → three-quarter → profile), pitch nods it (looking straight down → level → tipped back and up),
/// and roll tilts it sideways.
///
/// Two honest limits travel with this, and the UI states them rather than leaving them to be discovered in a
/// render: a COCO-18 body carries only five head points (nose, both eyes, both ears), so the head is a small
/// shape rather than a face; and OpenPoseXL2 does not reliably honour face keypoints at all, so a rotated head
/// pays off on a model that reads a skeleton as a reference (Qwen-2.1) or on a framed head shot — not on an
/// OpenPose ControlNet render.
/// </summary>
public sealed record PoseHeadRotation(double YawDegrees = 0, double PitchDegrees = 0, double RollDegrees = 0)
{
    /// <summary>
    /// Pitch is clamped short of 90° for the same reason the body's is: at the pole the head's own up vector is
    /// parallel to the view axis, so the projected head has no stable orientation. "Looking straight down" is
    /// therefore 89°, which reads as straight down.
    /// </summary>
    public const double PitchLimitDegrees = 89.0;

    public bool IsNeutral =>
        YawDegrees == 0 && PitchDegrees == 0 && RollDegrees == 0;

    /// <summary>
    /// The neck's local rotation. Yaw is about the vertical axis so the face leads the turn, and pitch is negated
    /// for the same reason the body's is: a POSITIVE pitch means the head tips back and looks up.
    /// </summary>
    public Quaternion ToLocalRotation() => Quaternion.Normalize(
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(RollDegrees * Math.PI / 180.0))
        * Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(-PitchDegrees * Math.PI / 180.0))
        * Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(YawDegrees * Math.PI / 180.0)));

    /// <summary>Adds a rotation on one axis, clamping pitch at the pole.</summary>
    public static PoseHeadRotation Rotate(PoseHeadRotation head, PoseRotationAxis axis, double degrees)
    {
        ArgumentNullException.ThrowIfNull(head);

        return axis switch
        {
            PoseRotationAxis.Yaw => head with { YawDegrees = head.YawDegrees + degrees },
            PoseRotationAxis.Roll => head with { RollDegrees = head.RollDegrees + degrees },
            PoseRotationAxis.Pitch => head with
            {
                PitchDegrees = Math.Clamp(
                    head.PitchDegrees + degrees, -PitchLimitDegrees, PitchLimitDegrees)
            },
            _ => throw new InvalidOperationException($"Unknown rotation axis '{axis}'.")
        };
    }

    /// <summary>One press of an arrow: the configured step, on this axis.</summary>
    public static PoseHeadRotation Step(
        PoseHeadRotation head, PoseRotationAxis axis, int steps, PoseStudioOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (steps == 0) return head;

        return Rotate(head, axis, settings.RequireRotationStepDegrees() * steps);
    }
}
