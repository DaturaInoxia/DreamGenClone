using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// The pose a step's bindings ask for, as the two fields a generation request reads.
/// </summary>
/// <param name="PresetId">
/// The pose library preset, which the render reads the skeleton BY — so a stale path in a payload can never make it
/// read a file other than the pose it named.
/// </param>
/// <param name="SkeletonRelativePath">The preset's own artifact path, recorded alongside the id.</param>
public sealed record ImageStepPoseSelection(string PresetId, string? SkeletonRelativePath);

/// <summary>
/// Turn a BOUND pose slot into a generation option — the one translation, so every host that offers a Pose tab
/// carries the pose without knowing how it travels.
/// </summary>
/// <remarks>
/// Why this exists (2026-09-27): the render path could already carry a pose skeleton BESIDE an identity face and a body
/// build — <c>SceneAssetGenerationJobHandler.RenderIdentityConditionedAsync</c> adds the skeleton as one more native
/// reference, and a host proof landed all three together — but no code in the app ever set the generation option, so a
/// bound pose was dropped and the Pose control was decorative everywhere. The gap was never a capability, it was this
/// joiner. Keeping it in one place is the whole point of the shared step: a capability added here reaches every host
/// instead of one page out of seven.
/// </remarks>
public static class ImageStepPoseBinding
{
    /// <summary>
    /// The pose this binding set names, or null when it names none.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A pose binding with no preset id. The render reads a preset's skeleton BY ID, so a binding that names only a
    /// path cannot be honoured — and dropping it silently is exactly the defect this class was written to remove.
    /// </exception>
    public static ImageStepPoseSelection? Resolve(IReadOnlyList<ReferenceApplicationSelection> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var pose = bindings.FirstOrDefault(binding =>
            string.Equals(binding.Kind, ImageStepSlotKind.Pose.ToString(), StringComparison.OrdinalIgnoreCase)
            && binding.SuppliesImage);

        if (pose is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(pose.PosePresetId))
        {
            throw new InvalidOperationException(
                "A pose reference is bound to this step but names no pose library preset, so the render has no skeleton "
                + "to condition on. Clear the step's pose slot or pick the pose again from the pose library — a preset "
                + "id is required because the skeleton is read by id, never by a path a stored payload could redirect.");
        }

        return new ImageStepPoseSelection(pose.PosePresetId.Trim(), pose.SkeletonRelativePath);
    }
}
