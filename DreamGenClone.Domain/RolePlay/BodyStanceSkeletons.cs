namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// One stance's rendered OpenPose skeleton: the file the ControlNet is conditioned on, and what was actually verified
/// about it.
/// </summary>
/// <param name="FileName">File name under the app's <c>pose-library</c> folder.</param>
/// <param name="VerifiedOn">The verified-holding source pose this skeleton was rendered from (its own provenance).</param>
/// <param name="KnownLimitation">
/// What is known to be wrong or unusual about the pose, or null when nothing is. Stated rather than left for the
/// operator to discover in a render: a reference sheet is only comparable if the operator knows what the pose does.
/// </param>
public sealed record BodyStanceSkeleton(string FileName, string VerifiedOn, string? KnownLimitation = null);

/// <summary>
/// The verified stance → skeleton map for body references (B-122).
///
/// The set is deliberately THREE stances, and it is the same set <see cref="BodyReferenceStance"/> offers, because
/// these are the only poses measured to HOLD under OpenPoseXL2 on this stack — the model re-poses lying, all-fours
/// and feet-tucked kneeling. A body reference whose pose silently drifts is not comparable to the next one, and
/// comparability is the entire reason for generating several.
///
/// The skeletons are rendered from the pose package's verified source JSONs by
/// <c>helpers/runpod/render-single-pose.py</c> and committed under <c>wwwroot/pose-library</c>, so the conditioning
/// input is reproducible from the repository rather than from someone's machine.
/// </summary>
public static class BodyStanceSkeletons
{
    /// <summary>Where the skeletons live, relative to the app's web root — one place, so a move breaks loudly.</summary>
    public const string WebRootFolder = "pose-library";

    private static readonly Dictionary<BodyReferenceStance, BodyStanceSkeleton> Skeletons = new()
    {
        [BodyReferenceStance.Standing] = new(
            "standing.png",
            "NSFW_standing/512768/NSFW_standing028.json"),

        [BodyReferenceStance.Squatting] = new(
            "squatting.png",
            "NSFW_Squatting/512512/NSFW_Squatting029.json"),

        [BodyReferenceStance.Kneeling] = new(
            "kneeling.png",
            "NSFW_Kneeling/512768/NSFW_Kneeling017.json",
            "The arms are raised overhead — that is the verified-holding kneeling pose, not a neutral reference "
            + "stance. A neutral arms-down kneeling skeleton exists in the pose package but has not been verified to "
            + "hold, so it is not offered yet.")
    };

    /// <summary>The skeleton for a stance, or a refusal naming the stance — nothing is substituted.</summary>
    public static BodyStanceSkeleton Require(BodyReferenceStance stance)
        => Skeletons.TryGetValue(stance, out var skeleton)
            ? skeleton
            : throw new InvalidOperationException(
                $"Stance '{stance}' has no verified OpenPose skeleton, so it cannot condition a render. The verified "
                + $"set is: {string.Join(", ", Skeletons.Keys)}.");

    /// <summary>True when a stance can be pose-conditioned — used by the UI to offer only what exists.</summary>
    public static bool Has(BodyReferenceStance stance) => Skeletons.ContainsKey(stance);

    /// <summary>Every stance that can be pose-conditioned, in the enum's own order.</summary>
    public static IReadOnlyList<BodyReferenceStance> Available { get; } =
        Enum.GetValues<BodyReferenceStance>().Where(Has).ToList();
}
