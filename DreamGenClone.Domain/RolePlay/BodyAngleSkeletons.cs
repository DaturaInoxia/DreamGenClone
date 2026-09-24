namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// One canonical angle's OpenPose skeleton: the file the render is conditioned on, where it came from, and what was
/// measured about it.
/// </summary>
/// <param name="FileName">File name under the app's <c>pose-library</c> folder.</param>
/// <param name="VerifiedOn">The committed proof case this skeleton was annotated from (its own provenance).</param>
/// <param name="KnownLimitation">
/// What is known to be unusual about the skeleton, or null when nothing is. Stated rather than left for the operator
/// to discover in a render: these are annotations of a generated figure, not anatomical drawings.
/// </param>
public sealed record BodyAngleSkeleton(string FileName, string VerifiedOn, string? KnownLimitation = null);

/// <summary>
/// The canonical-angle → skeleton map for body views (B-122).
///
/// These are NOT rotations of the frontal skeleton: rotating a frontal figure in-plane only tilts it, rotating it in
/// world-3D shears it (measured: ~27 cm of shear across a 1.30 m body, from a 0.49 m ear-to-ankle depth gradient), and
/// rotating it as a flat cutout only narrows it. An angle skeleton therefore has to be ANNOTATED from a figure that
/// is actually in that view, which is how the OpenPose datasets were made: a plate is rendered in the target view and
/// DWPose extracts its skeleton. The whole proof — including the rejected rotation attempts, with numbers — is in
/// <c>specs/image-generator-tests/qwen-21-native-reference/RUNBOOK.md</c>.
///
/// The front view deliberately has NO entry: it is the base, generated from the body card alone.
/// </summary>
public static class BodyAngleSkeletons
{
    /// <summary>Where the skeletons live, relative to the app's web root — one place, so a move breaks loudly.</summary>
    public const string WebRootFolder = "pose-library";

    /// <summary>
    /// What every skeleton in this set shares: it was annotated from a locally generated plate, so its limb LAYOUT is
    /// what it carries. Measured 2026-09-23 — the approximate proportions did not harm any render, because the accepted
    /// body reference supplies the build and the skeleton supplies only the angle of the limbs.
    /// </summary>
    private const string AnnotatedFromPlate =
        "Annotated by DWPose from a figure rendered in this view, so its limb layout is what it carries and its "
        + "proportions are approximate (a long neck and legs, ~62% of figure height). It is an angle input, not an "
        + "anatomical reference, and it is never sent as a style or appearance reference.";

    private static readonly Dictionary<SceneImageReferenceBodyView, BodyAngleSkeleton> Skeletons = new()
    {
        [SceneImageReferenceBodyView.ThreeQuarterLeft] = new(
            "angle-34-left.png",
            "specs/image-generator-tests/qwen-21-native-reference/skeletons/angle-34-left.png "
            + "(case body-angle-34-left-front-plus-skeleton)",
            AnnotatedFromPlate),

        [SceneImageReferenceBodyView.ThreeQuarterRight] = new(
            "angle-34-right.png",
            "specs/image-generator-tests/qwen-21-native-reference/skeletons/angle-34-right.png "
            + "(case body-angle-34-right-front-plus-skeleton)",
            AnnotatedFromPlate),

        [SceneImageReferenceBodyView.ProfileLeft] = new(
            "angle-profile-left.png",
            "specs/image-generator-tests/qwen-21-native-reference/skeletons/angle-profile-left.png "
            + "(case body-profile-left-front-plus-skeleton)",
            AnnotatedFromPlate),

        [SceneImageReferenceBodyView.ProfileRight] = new(
            "angle-profile-right.png",
            "specs/image-generator-tests/qwen-21-native-reference/skeletons/angle-profile-right.png "
            + "(case body-profile-right-front-plus-skeleton)",
            AnnotatedFromPlate),

        [SceneImageReferenceBodyView.Back] = new(
            "back.png",
            "specs/image-generator-tests/qwen-21-native-reference/skeletons/angle-back.png "
            + "(DWPose on plateBack; case body-back-front-plus-skeleton)",
            AnnotatedFromPlate)
    };

    /// <summary>The skeleton for a view, or a refusal naming the view — nothing is substituted.</summary>
    public static BodyAngleSkeleton Require(SceneImageReferenceBodyView view)
        => Skeletons.TryGetValue(view, out var skeleton)
            ? skeleton
            : throw new InvalidOperationException(
                $"'{view}' has no committed angle skeleton, so it cannot condition a render. The committed set is: "
                + $"{string.Join(", ", Skeletons.Keys)}. The front is the base and is generated from the body card "
                + "instead; any other angle is an extended view, which is produced as an edit of an accepted view.");

    /// <summary>True when a view can be angle-conditioned — used to offer only what exists.</summary>
    public static bool Has(SceneImageReferenceBodyView view) => Skeletons.ContainsKey(view);

    /// <summary>Every view that can be angle-conditioned, in the enum's own order.</summary>
    public static IReadOnlyList<SceneImageReferenceBodyView> Available { get; } =
        Enum.GetValues<SceneImageReferenceBodyView>().Where(Has).ToList();
}
