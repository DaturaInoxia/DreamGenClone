using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// What a pose NEEDS from a character, in the operator's own words — one owner for the wording, so the pose library's
/// test render and a pose suite run cannot explain the same missing angle two different ways.
///
/// <para>
/// Every message names the ANGLE and who is missing it, and every one of them offers the action. "No reference" is not
/// an explanation: the operator's next step is to approve that angle in Character Studio, or to pick a different
/// character, and the message has to say which angle so that step is possible.
/// </para>
/// </summary>
public static class PoseReferenceRequirement
{
    /// <summary>
    /// Why a pose cannot be conditioned on a character at all, or null when it can. Asked BEFORE the pack is consulted:
    /// a pose whose direction or rating was never declared cannot justify an angle, and falling back to the front would
    /// condition the render on an angle the pose contradicts.
    /// </summary>
    public static string? Unplannable(string poseName, PoseReferencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.BodyView is null || plan.BodyState is null)
        {
            return $"'{poseName}' cannot be conditioned on a character: {plan.Rationale}";
        }

        return null;
    }

    /// <summary>The approved FACE angle the pose needs is missing from the character, or null when it has one.</summary>
    public static string? MissingFace(IdentityPackOwner owner, string poseName, PoseFacingDirection direction)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return $"'{owner.DisplayName}' has no APPROVED {PoseMetadataLabels.Direction(direction)} face, which is the "
            + $"angle '{poseName}' is shot at. Approve one in Character Studio, or pick another character.";
    }

    /// <summary>The approved BODY angle and state the pose needs is missing from the character, or null when it has one.</summary>
    public static string? MissingBody(
        IdentityPackOwner owner,
        string poseName,
        PoseFacingDirection direction,
        PoseContentRating rating,
        SceneImageReferenceBodyState state)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var stateText = state == SceneImageReferenceBodyState.Unclothed ? "unclothed" : "clothed";
        return $"'{owner.DisplayName}' has no APPROVED {stateText} {PoseMetadataLabels.Direction(direction)} body, "
            + $"which is what '{poseName}' needs (the pose is {PoseMetadataLabels.Rating(rating)}). Approve one in "
            + "Character Studio, or pick another character.";
    }

    /// <summary>What a run will send for this pose: the positive statement, for a preview beside the pose.</summary>
    public static string Describe(PoseReferencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Rationale;
    }
}
