using System.Numerics;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Turns a HEAD pose — a face channel with no usable body — the way the rig turns a body pose: a rigid rotation in 3D,
/// projected through the same camera.
///
/// Why it is needed: the body path needs a body to fit, and a face-framed crop makes DWPose report joints that are not
/// in the picture. Measured 2026-09-27 on the two authored head poses: 5 and 7 "visible" body joints spanning
/// x = 0..940 on a crop of a face. Fitting the standing rig onto that is what mangled the pose. The FACE is the pose —
/// 70 of 70 points visible — so the face is what has to turn.
///
/// Where the depths come from, which is the entire question. A face is flat in this app, and a flat point set cannot be
/// turned: the repo measured that in-plane rotation only tilts a figure, and lifting it to world-3D from monocular
/// landmarks shears ~27 cm across a 1.30 m body. So the face BORROWS a head shape from the rig, exactly as a loaded
/// body pose borrows its depths from the fit — the nose is the most forward point, the eyes are recessed, the ears sit
/// back at the sides. A point's depth is interpolated from where it sits ACROSS the face, between those anchors.
///
/// That is a fixed, synthetic, mutually consistent piece of geometry — the same class as the mannequin, and NOT an
/// estimate read off a photograph. A rotation of it therefore cannot shear, which is the property the body path is
/// built on and the reason this is allowed where the monocular lift was not.
///
/// The proxy carries ANGLE, not identity, exactly as the mannequin carries angle rather than build: a generic head at
/// 35°, not this person's face at 35°. Identity comes from the face reference image, as build comes from the accepted
/// body reference.
/// </summary>
public static class PoseFaceProxy
{
    /// <summary>COCO index of the nose — the head's most forward point, and the centre of the depth profile.</summary>
    private const int NoseCoco = 0;

    /// <summary>COCO index of the right eye (OpenPose's own right, i.e. image-left).</summary>
    private const int RightEyeCoco = 14;

    private const int LeftEyeCoco = 15;

    private const int RightEarCoco = 16;

    private const int LeftEarCoco = 17;

    /// <summary>
    /// How few visible body joints a pose must have before the rig is no longer worth fitting onto it. A face-framed
    /// crop reports a handful of joints that are not in the picture — measured 2026-09-27 on the two authored head
    /// poses: 5 and 7, spanning x = 0..940 across a crop of a face — so the COUNT is what decides this, not whether a
    /// face channel is present.
    /// </summary>
    private const int MinBodyJointsForTheRig = 12;

    /// <summary>How many visible face points a pose needs before the head path can turn it at all.</summary>
    private const int MinFacePoints = 3;

    /// <summary>
    /// The head's OWN points inside the BODY channel: nose, both eyes and both ears. The renderer links exactly these in
    /// blue from the neck up — (1,0), (0,14), (14,16), (0,15), (15,17) in <see cref="PoseSkeletonRenderer.BodyLinks"/> —
    /// so this list IS the head's blue frame, and the part of the body channel that has to turn with the face.
    ///
    /// The neck is deliberately ABSENT. It is the body's anchor and the pivot the head turns about, not part of the head,
    /// and it sits below the chin (measured on '34 Right Head': y = 1218 against a face box ending at y = 1094).
    /// </summary>
    private static readonly int[] HeadBodyIndices =
        [NoseCoco, RightEyeCoco, LeftEyeCoco, RightEarCoco, LeftEarCoco];

    /// <summary>
    /// True when this pose has to be turned by the HEAD path: enough face to turn, and no body worth fitting.
    ///
    /// The joint COUNT is the test on purpose. The guard this replaces refused on the FACE CHANNEL alone, so it rejected
    /// poses that carried a face and a perfectly good body, and its refusal claimed "no body" without ever counting one.
    /// </summary>
    public static bool IsHeadPose(PosePerson pose)
    {
        ArgumentNullException.ThrowIfNull(pose);

        var body = pose.Body.Count(point => point.Confidence > OpenPosePoseJson.VisibilityFloor);
        var face = pose.Face.Count(point => point.Confidence > OpenPosePoseJson.VisibilityFloor);

        return face >= MinFacePoints && body < MinBodyJointsForTheRig;
    }

    /// <summary>
    /// A named BODY view expressed as a HEAD rotation, so ONE set of controls can drive either.
    ///
    /// The names keep their meaning rather than being reinterpreted: "Front" is straight ahead and a profile is 90°
    /// off it, which is exactly what the same buttons already mean for the body. That is the whole reason a head pose
    /// needs no second control scheme — the rig's view cannot turn a face, because <see cref="Project"/> reads the head
    /// and takes no view at all, so the angle those controls produce has to land on the head instead of on the camera.
    /// </summary>
    public static PoseHeadRotation AsHeadRotation(PoseView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new PoseHeadRotation(view.YawDegrees, view.PitchDegrees, view.RollDegrees);
    }

    /// <summary>
    /// The face turned to <paramref name="head"/>.
    ///
    /// A NEUTRAL head returns the SAME instance, so "leave the head alone" is exact rather than a round trip through
    /// the projection that would land a hair away from where it started.
    /// </summary>
    public static PosePerson Project(PosePerson pose, PoseHeadRotation head, PoseStudioOptions settings)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(settings);

        if (head.IsNeutral) return pose;

        var visible = pose.Face
            .Select((point, index) => (point, index))
            .Where(entry => entry.point.Confidence > OpenPosePoseJson.VisibilityFloor)
            .ToArray();

        if (visible.Length < 3)
        {
            throw new InvalidOperationException(
                $"A face pose needs at least 3 visible face points to be turned, but this one has {visible.Length}.");
        }

        var points = visible.Select(entry => entry.point).ToArray();

        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);

        var layoutWidth = maxX - minX;
        var layoutHeight = maxY - minY;

        if (layoutWidth <= 0 || layoutHeight <= 0)
        {
            throw new InvalidOperationException(
                $"The visible face points have no extent ({layoutWidth:0.##} x {layoutHeight:0.##}), so there is no "
                + "face shape to turn.");
        }

        var centreX = (minX + maxX) / 2.0;
        var centreY = (minY + maxY) / 2.0;

        // The rig's head supplies the depth profile. Read at the neutral view because it is a SHAPE being borrowed, not
        // a pose: the head shape does not depend on which way the rig happens to be facing.
        var mannequin = PoseMannequin.Standing();
        var world = PoseProjection.WorldPositions(mannequin, mannequin.StandingStance(), new PoseView());

        var nose = world[mannequin.CocoIndices[NoseCoco]];
        var eyeRight = world[mannequin.CocoIndices[RightEyeCoco]];
        var eyeLeft = world[mannequin.CocoIndices[LeftEyeCoco]];
        var earRight = world[mannequin.CocoIndices[RightEarCoco]];
        var earLeft = world[mannequin.CocoIndices[LeftEarCoco]];

        var headHalfWidth = Math.Abs(earLeft.X - earRight.X) / 2.0;
        if (headHalfWidth <= 0)
        {
            throw new InvalidOperationException(
                "The rig's head has no width, so a face cannot borrow a head shape from it.");
        }

        // Where the eyes sit, as a fraction of the way from the centre line out to the ears. That is what places the
        // second depth anchor between the nose and the ears.
        var eyeLateral = Math.Abs(eyeLeft.X - eyeRight.X) / 2.0 / headHalfWidth;

        if (eyeLateral <= 0 || eyeLateral >= 1)
        {
            throw new InvalidOperationException(
                $"The rig's eyes sit at {eyeLateral:0.###} of the way from the centre line to the ears, which is not "
                + "between them, so the face's depth profile cannot be built from the head's own joints.");
        }

        var zNose = nose.Z;
        var zEye = (eyeLeft.Z + eyeRight.Z) / 2.0;
        var zEar = (earLeft.Z + earRight.Z) / 2.0;

        // Depth across the face: furthest forward at the centre line, then out through the eyes to the ears. A face's
        // depth is very nearly a function of how far a point sits from its centre line, which is why one axis is enough.
        double Depth(double lateral) => lateral <= eyeLateral
            ? zNose + ((zEye - zNose) * (lateral / eyeLateral))
            : zEye + ((zEar - zEye) * ((lateral - eyeLateral) / (1.0 - eyeLateral)));

        // The borrowed depths are expressed in units of the head's OWN width and then applied to the FACE's own width.
        // That is what puts the profile at the right scale for the face it is applied to without a camera in the
        // middle: the SHAPE is a ratio taken from the rig, and the pixels come from the pose.
        var depthSpan = headHalfWidth * 2.0;

        // Depths are measured from the head's AXIS OF ROTATION, not from the nose. A head turns about the neck, which
        // sits under the ears, so the ears are the axis. This is not cosmetic: anchoring on the nose puts the nose
        // exactly ON the axis, where a yaw leaves it perfectly still in the centre of the face and the turn reads as
        // nothing happening. Measured 2026-09-27 — with the nose as the anchor, a 5° step moved it 0.000 px.
        var noseDepth = (zNose - zEar) / depthSpan;
        var eyeDepth = (zEye - zEar) / depthSpan;

        double DepthRatio(double lateral) => lateral <= eyeLateral
            ? noseDepth + ((eyeDepth - noseDepth) * (lateral / eyeLateral))
            // Out to the ears, which END at the axis: their depth is zero by construction.
            : eyeDepth * (1.0 - ((lateral - eyeLateral) / (1.0 - eyeLateral)));

        // The same rotation core the body uses — a head turn is a rotation about the same axes, so it needs no second
        // projection of its own. The studio options are not consulted here: an orthographic projection has no camera.
        var turn = PoseProjection.RotationBetween(
            new PoseView(), new PoseView(head.YawDegrees, head.PitchDegrees, head.RollDegrees));

        // ONE mapping for the whole head. The white face dots and the blue head frame are the SAME object: both are
        // DWPose's output for the same picture, in the same source pixels, so they must share one centre, one depth
        // scale and one depth profile. Turning them separately is what left the face rotating under a skull that stayed
        // put — reported by the operator 2026-09-28 as "it is not rotating the main blue line 5 dot connection so the
        // face is all skewed, it needs to be kept in sync with the other dots".
        PoseKeypoint Turn(double x, double y, double confidence)
        {
            // CLAMPED, and that is the geometry rather than a guard. The profile runs from the nose out to the ears,
            // which sit ON the axis, so lateral 1 IS the back of the head; a point outside the face's own width is an
            // ear projecting slightly past the face box (measured: lateral 1.07 on '34 Right Head') and there is no
            // further back for it to go. Unclamped, the eye-to-ear segment extrapolates PAST the ears and swings the
            // point forward of the nose — the shear this whole approach exists to avoid.
            var lateral = Math.Min(1.0, Math.Abs(x - centreX) / (layoutWidth / 2.0));

            var turned = Vector3.Transform(
                new Vector3(
                    (float)(x - centreX),
                    // Image y grows down while the rotation's y grows up, which is the projection's own convention.
                    (float)(-(y - centreY)),
                    (float)(DepthRatio(lateral) * layoutWidth)),
                turn);

            // ORTHOGRAPHIC: the screen point is the rotated point, with no perspective division. That is a deliberate
            // choice rather than a shortcut. It makes a NEUTRAL head an exact identity on all three axes, and the
            // rig's perspective camera is measurably the thing that exaggerates a profile — the span probe records the
            // rig keeping 7.30% of figure height at profile against a real render's ~2%.
            return new PoseKeypoint(centreX + turned.X, centreY - turned.Y, confidence);
        }

        var face = pose.Face.ToArray();

        foreach (var (point, index) in visible)
        {
            face[index] = Turn(point.X, point.Y, point.Confidence);
        }

        // The head's blue frame, rotated by the SAME mapping. Leaving it behind is what skewed the face: the dots turned
        // and the skull they belong to did not.
        var body = pose.Body.ToArray();

        foreach (var index in HeadBodyIndices)
        {
            var point = body[index];

            // Only a point the estimator actually placed. An invisible one keeps the value it was given rather than
            // being moved to a position nothing measured: on '34 Right Head' the left ear is a (0,0) artifact, and
            // rotating it would drag it in from the corner of the canvas.
            if (point.Confidence <= OpenPosePoseJson.VisibilityFloor) continue;

            body[index] = Turn(point.X, point.Y, point.Confidence);
        }

        return new PosePerson
        {
            // The LIMBS are carried through untouched, and deliberately so. A head pose has no usable body — that is
            // what makes it a head pose — and the handful of "visible" limb joints a face crop reports are detector
            // artifacts (measured 2026-09-27: 5 and 7 of them, spanning x = 0..940 on a crop of a face). Rotating those
            // would be inventing motion for joints that are not in the picture. Only the head's own frame moves.
            Body = body,
            LeftHand = pose.LeftHand,
            RightHand = pose.RightHand,
            Face = face
        };
    }
}
