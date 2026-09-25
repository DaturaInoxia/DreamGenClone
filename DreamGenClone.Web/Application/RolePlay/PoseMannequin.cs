using System.Numerics;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// One joint of the authoring rig: a rest offset from its parent and, for the eighteen that a COCO body has,
/// the index it feeds.
///
/// The rig exists because <b>an angle cannot be produced by editing 2D keypoints</b>. Rotating a flat skeleton
/// in the image plane only tilts it, rotating it as a cutout only narrows it, and rotating it with a monocular
/// depth estimate shears it — that last one is measured, not theoretical: MediaPipe world landmarks put the ears
/// ~0.29 m behind and the ankles ~0.20 m in front, so a 45° yaw injected ~27 cm of horizontal shear across a
/// 1.30 m body (<c>specs/image-generator-tests/qwen-21-native-reference/RUNBOOK.md</c>). A rig has none of those
/// problems because its depths are synthetic and mutually consistent, which is why the angle comes from posing
/// this and projecting it.
/// </summary>
public sealed class MannequinJoint
{
    /// <summary>Readable name, used in refusals and in the editor.</summary>
    public required string Name { get; init; }

    /// <summary>Index of the parent joint, or -1 for the root (the pelvis).</summary>
    public required int Parent { get; init; }

    /// <summary>Rest offset from the parent, in metres. The bone length is this vector's magnitude.</summary>
    public required Vector3 RestOffset { get; init; }

    /// <summary>Which COCO-18 keypoint this joint becomes, or null for an internal joint such as the pelvis.</summary>
    public int? CocoIndex { get; init; }

    /// <summary>
    /// The direction this joint faces in the rest pose, or null when the joint is visible from every direction a
    /// camera can be placed. Only the face has a facing worth stating: a nose points forward and is genuinely
    /// hidden once the head has turned far enough away, while shoulders, hips and ears are on the body's outside
    /// and are visible from the front and the back alike. Null therefore means "always visible", not "unknown".
    /// </summary>
    public Vector3? Facing { get; init; }

    /// <summary>
    /// How far <see cref="Facing"/> may swing away from the camera before the joint stops being drawn, in degrees.
    /// The values are set per joint from how that part of a real body behaves — a nose survives almost to profile
    /// because it is the silhouette's forward point, while an eye disappears as soon as its side of the face turns
    /// away. Both are pinned by tests on the projected poses, not by this comment.
    /// </summary>
    public double FacingLimitDegrees { get; init; } = 90.0;

    /// <summary>The bone length: fixed by definition, so no pose can stretch it.</summary>
    public double BoneLength => RestOffset.Length();
}

/// <summary>
/// A rigid standing figure in the authoring rig: Y is up, X is to the viewer's right, Z is toward the viewer.
/// Proportions are stored here rather than read from a licensed body model — see the licence note in
/// <see cref="PoseMannequin.Standing"/>.
/// </summary>
public sealed class PoseMannequin
{
    private PoseMannequin(IReadOnlyList<MannequinJoint> joints)
    {
        Joints = joints;
        RootIndex = joints.ToList().FindIndex(joint => joint.Parent < 0);
        NeckIndex = joints.ToList().FindIndex(joint => joint.Name == "neck");
        HeadIndex = joints.ToList().FindIndex(joint => joint.Name == "head");
        CocoIndices = joints
            .Select((joint, index) => (joint.CocoIndex, index))
            .Where(entry => entry.CocoIndex is not null)
            .ToDictionary(entry => entry.CocoIndex!.Value, entry => entry.index);

        if (NeckIndex < 0)
        {
            throw new InvalidOperationException(
                "The authoring rig has no 'neck' joint, so the body cannot be posed correctly.");
        }

        if (HeadIndex < 0)
        {
            throw new InvalidOperationException(
                "The authoring rig has no 'head' joint, so the head cannot be posed independently of the shoulders.");
        }
    }

    public IReadOnlyList<MannequinJoint> Joints { get; }

    /// <summary>Index of the pelvis joint, the only joint without a parent.</summary>
    public int RootIndex { get; }

    /// <summary>
    /// Index of the neck joint. The shoulders hang from it, so this is a <b>body</b> joint — rotating it leans the
    /// upper body. The head is a separate joint (see <see cref="HeadIndex"/>).
    /// </summary>
    public int NeckIndex { get; }

    /// <summary>
    /// Index of the head pivot, the joint the head tool drives. Nose, eyes and ears all hang from it, and nothing
    /// else does — which is why turning it moves the head without swinging the arms.
    /// </summary>
    public int HeadIndex { get; }

    /// <summary>
    /// The COCO indices that make up the head, used to frame a close-up. A body-scale render gives the head a
    /// handful of pixels, so the head tool frames these instead.
    /// </summary>
    public static readonly int[] HeadCocoIndices = [0, 1, 14, 15, 16, 17];

    /// <summary>COCO-18 index to rig joint index.</summary>
    public IReadOnlyDictionary<int, int> CocoIndices { get; }

    /// <summary>How many COCO joints the rig can express. Must be all of them.</summary>
    public const int CocoJointCount = 18;

    /// <summary>
    /// A neutral standing figure. The arms hang close to the body: an arm held out at ~45° is not "standing with
    /// the hands at the sides", and it also put the hand out of its own reach for any pose that crosses the body.
    /// Hanging arms alone are NOT enough for a legible figure — see <see cref="StandingStance"/>, which is what the
    /// authoring tools actually pose, and which exists because a hanging arm and a straight leg project on top of
    /// the torso in a side view.
    ///
    /// Proportions are authored here rather than taken from SMPL / SMPL-X / MANO / FLAME: those body models are
    /// licensed for non-commercial research only, so coupling the renderer to one would put a licence question
    /// inside the geometry path. The numbers are ordinary adult proportions, and the rig is a pose input — never
    /// an appearance or proportion source, exactly like the skeletons it produces.
    /// </summary>
    public static PoseMannequin Standing()
    {
        var joints = new List<MannequinJoint>
        {
            // The root. COCO has no pelvis joint, so it is internal: hips and the spine both hang from it.
            new() { Name = "pelvis", Parent = -1, RestOffset = new Vector3(0f, 0.95f, 0f) },

            new() { Name = "neck", Parent = 0, RestOffset = new Vector3(0f, 0.52f, 0f), CocoIndex = 1 },

            // The head is its own joint ABOVE the neck, not the neck itself. The shoulders hang from the neck, so
            // turning the neck would swing the arms; turning this pivot moves the head and nothing else. Its
            // offset is chosen so the rest pose is numerically identical to a rig without it.
            new() { Name = "head", Parent = 1, RestOffset = new Vector3(0f, 0.10f, 0f) },

            // Nose, eyes and ears hang from the head pivot, so the head tool drives all of them together.
            //
            // The face carries the rig's only facing information, and it has to: a standing figure is close to
            // left-right symmetric, so without it a front view and a back view project to the same picture and a
            // renderer has nothing to choose from (measured 2026-09-24: the front skeleton differed from its own
            // mirror by 1.2% of pixels, and Qwen-2.1 rendered the front pose facing away). The nose keeps its
            // visibility almost to profile because a nose IS the profile's forward point; each eye is offset
            // 26.6° toward its own side, so a profile view hides the far eye and a turned-away head hides both.
            new()
            {
                Name = "nose", Parent = 2, RestOffset = new Vector3(0f, 0.02f, 0.09f), CocoIndex = 0,
                Facing = new Vector3(0f, 0f, 1f), FacingLimitDegrees = 120.0
            },
            new()
            {
                Name = "eye_r", Parent = 2, RestOffset = new Vector3(-0.035f, 0f, 0.075f), CocoIndex = 14,
                Facing = new Vector3(-0.5f, 0f, 1f), FacingLimitDegrees = 100.0
            },
            new()
            {
                Name = "eye_l", Parent = 2, RestOffset = new Vector3(0.035f, 0f, 0.075f), CocoIndex = 15,
                Facing = new Vector3(0.5f, 0f, 1f), FacingLimitDegrees = 100.0
            },
            // Ears are visible from the front, the side AND the back, so they carry no facing limit: a back view
            // keeping its ears and losing its nose and eyes is exactly how a real OpenPose back view reads.
            new() { Name = "ear_r", Parent = 4, RestOffset = new Vector3(-0.075f, 0f, -0.015f), CocoIndex = 16 },
            new() { Name = "ear_l", Parent = 5, RestOffset = new Vector3(0.075f, 0f, -0.015f), CocoIndex = 17 },

            // Right arm (the subject's right, image-left). Parented to the NECK, not the head.
            // Lengths are anthropometric for this figure (upper arm ~0.186 x stature, forearm ~0.146 x stature, so
            // shoulder-to-wrist ~0.33 x stature), and the rest pose HANGS: an arm held out horizontally is not
            // "standing with the hands at the sides", and it also put the hand out of its own reach for any pose
            // that crosses the body.
            new() { Name = "shoulder_r", Parent = 1, RestOffset = new Vector3(-0.18f, -0.03f, 0f), CocoIndex = 2 },
            new() { Name = "elbow_r", Parent = 8, RestOffset = new Vector3(-0.03f, -0.310f, 0f), CocoIndex = 3 },
            new() { Name = "wrist_r", Parent = 9, RestOffset = new Vector3(-0.02f, -0.250f, 0f), CocoIndex = 4 },

            // Left arm.
            new() { Name = "shoulder_l", Parent = 1, RestOffset = new Vector3(0.18f, -0.03f, 0f), CocoIndex = 5 },
            new() { Name = "elbow_l", Parent = 11, RestOffset = new Vector3(0.03f, -0.310f, 0f), CocoIndex = 6 },
            new() { Name = "wrist_l", Parent = 12, RestOffset = new Vector3(0.02f, -0.250f, 0f), CocoIndex = 7 },

            // Right leg.
            new() { Name = "hip_r", Parent = 0, RestOffset = new Vector3(-0.10f, -0.02f, 0f), CocoIndex = 8 },
            new() { Name = "knee_r", Parent = 14, RestOffset = new Vector3(0f, -0.46f, 0f), CocoIndex = 9 },
            new() { Name = "ankle_r", Parent = 15, RestOffset = new Vector3(0f, -0.44f, 0f), CocoIndex = 10 },

            // Left leg.
            new() { Name = "hip_l", Parent = 0, RestOffset = new Vector3(0.10f, -0.02f, 0f), CocoIndex = 11 },
            new() { Name = "knee_l", Parent = 17, RestOffset = new Vector3(0f, -0.46f, 0f), CocoIndex = 12 },
            new() { Name = "ankle_l", Parent = 18, RestOffset = new Vector3(0f, -0.44f, 0f), CocoIndex = 13 }
        };

        var mannequin = new PoseMannequin(joints);

        if (mannequin.CocoIndices.Count != CocoJointCount)
        {
            throw new InvalidOperationException(
                $"The authoring rig must express all {CocoJointCount} COCO joints but expresses "
                + $"{mannequin.CocoIndices.Count}.");
        }

        return mannequin;
    }

    /// <summary>The rig's rest local rotations: every joint unrotated.</summary>
    public Quaternion[] RestRotations() =>
        Enumerable.Repeat(Quaternion.Identity, Joints.Count).ToArray();

    /// <summary>
    /// The natural standing stance: the rest pose plus the small articulation a real standing body has.
    ///
    /// This is not decoration. With the arms hanging straight down and the legs straight and together, a side view
    /// projects the arms onto the torso and the two legs onto each other, so a profile render is a bare vertical
    /// line — the figure is present but unreadable, both to a person looking at the preview and to a model
    /// conditioning on the skeleton. Swinging the arms slightly FORWARD is what separates them from the torso in a
    /// side view (moving them sideways would not: a lateral offset is along the view axis and projects to nothing).
    /// A small fore/aft stagger does the same for the legs.
    ///
    /// The angles are deliberately small — this is still a neutral standing pose, not a walk.
    /// </summary>
    public Quaternion[] StandingStance()
    {
        var rotations = RestRotations();

        // Arms: a few degrees forward at the shoulder and a slight bend at the elbow. Negative about X swings a
        // downward bone toward +Z, which is forward (the figure faces the camera at +Z).
        rotations[IndexOfJoint("shoulder_r")] = FromX(-8.0);
        rotations[IndexOfJoint("elbow_r")] = FromX(-8.0);
        rotations[IndexOfJoint("shoulder_l")] = FromX(-8.0);
        rotations[IndexOfJoint("elbow_l")] = FromX(-8.0);

        // Legs: the right leg slightly forward, the left slightly back, so the two legs separate in a side view.
        rotations[IndexOfJoint("hip_r")] = FromX(-6.0);
        rotations[IndexOfJoint("hip_l")] = FromX(6.0);

        return rotations;
    }

    /// <summary>
    /// Index of a named joint. Public because the rig fit and the authoring tools address joints by name, and a
    /// second name-to-index table elsewhere would be one more thing to keep in step with the rig.
    /// </summary>
    public int JointIndex(string name) => IndexOfJoint(name);

    private int IndexOfJoint(string name)
    {
        for (var index = 0; index < Joints.Count; index++)
        {
            if (string.Equals(Joints[index].Name, name, StringComparison.Ordinal)) return index;
        }

        throw new InvalidOperationException(
            $"The authoring rig has no '{name}' joint, so the standing stance cannot be built. The rig and this "
            + "stance have to be changed together.");
    }

    private static Quaternion FromX(double degrees) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(degrees * Math.PI / 180.0));

    /// <summary>
    /// Forward kinematics: parents first, so a parent's rotation carries its children. Local rotations are applied
    /// about each joint's own origin, which is what keeps bone lengths fixed by construction.
    /// </summary>
    public Vector3[] ForwardKinematics(IReadOnlyList<Quaternion> localRotations) =>
        ForwardFrames(localRotations).Positions;

    /// <summary>
    /// Forward kinematics that also returns each joint's world rotation. Visibility needs the rotation, not just
    /// the position: whether a nose is drawn depends on which way the head is pointing, which no position in the
    /// array carries on its own.
    /// </summary>
    public (Vector3[] Positions, Quaternion[] Rotations) ForwardFrames(IReadOnlyList<Quaternion> localRotations)
    {
        ArgumentNullException.ThrowIfNull(localRotations);
        if (localRotations.Count != Joints.Count)
        {
            throw new InvalidOperationException(
                $"A pose needs {Joints.Count} joint rotations but {localRotations.Count} were given.");
        }

        var positions = new Vector3[Joints.Count];
        var rotations = new Quaternion[Joints.Count];

        for (var index = 0; index < Joints.Count; index++)
        {
            var joint = Joints[index];

            if (joint.Parent < 0)
            {
                positions[index] = joint.RestOffset;
                rotations[index] = localRotations[index];
                continue;
            }

            // Safe because a joint's parent is always declared before it.
            var parentPosition = positions[joint.Parent];
            var parentRotation = rotations[joint.Parent];

            var local = localRotations[index];
            rotations[index] = Quaternion.Normalize(parentRotation * local);
            positions[index] = parentPosition + Vector3.Transform(joint.RestOffset, rotations[index]);
        }

        return (positions, rotations);
    }
}
