using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Turns pose metadata into the two things a render needs from it: the PROMPT that describes the pose, and the
/// REFERENCE IMAGES the pose requires.
///
/// One function each, and both are pure, because the wording and the reference angles have to agree with each other.
/// A prompt saying "lying on her back" beside a reference angle resolved as "back view" would be a contradiction the
/// operator would have to spot by hand.
///
/// The wording is deliberately short and clause-by-clause. The skeleton carries the body's geometry on the
/// reference-image route but NOT which way the figure faces (measured 2026-09-24: an identical front-facing skeleton
/// came back facing away), and a flat skeleton cannot distinguish lying from standing at all — so the words carry
/// exactly the two things the keypoints cannot: the direction and the camera.
/// </summary>
public static class PoseMetadataPrompt
{
    /// <summary>
    /// The pose's prompt, or an EMPTY string when the pack declares no rating.
    ///
    /// That refusal is the point: the subject clause is "a naked woman" or "a clothed woman", and choosing between
    /// them without a declared rating would be the app deciding what the pack contains. An unrated pose is rendered
    /// with the operator's own prompt, and the library says why on the card.
    /// </summary>
    public static string Compose(PoseMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.Rating == PoseContentRating.Unrated) return string.Empty;

        var clauses = new List<string>(3);
        if (StanceClause(metadata) is { Length: > 0 } stance) clauses.Add(stance);
        if (DirectionClause(metadata.Direction) is { Length: > 0 } direction) clauses.Add(direction);
        if (CameraClause(metadata.Camera) is { Length: > 0 } camera) clauses.Add(camera);

        var subject = metadata.Rating == PoseContentRating.Nsfw
            ? "a naked woman"
            : "a woman, fully clothed";

        // The framing and the tail are the proof's formulation, unchanged: it is what the pose proof ran against every
        // tested pose, so a render driven by this prompt is comparable to the recorded results rather than a new
        // experiment. Only the pose clause in the middle is new.
        var pose = clauses.Count > 0 ? " " + string.Join(", ", clauses) : string.Empty;

        return "A full-body photograph of " + subject + pose
            + ", natural skin texture, photorealistic, plain studio background, 85mm.";
    }

    /// <summary>
    /// The reference images this pose needs. Nulls are meaningful and the caller must not fill them in: a null body
    /// view means the direction is unknown (no angle can be justified), while a null FACE view beside a known
    /// direction means the pose shows no face and needs no face reference.
    /// </summary>
    public static PoseReferencePlan ReferencePlan(PoseMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.Direction == PoseFacingDirection.Unknown)
        {
            return new PoseReferencePlan(
                FaceView: null,
                BodyView: null,
                BodyState: null,
                Rationale: "The pose's direction is not declared, so which angle of the character it needs cannot be "
                    + "justified. Declare it in the pack's pack.json.");
        }

        var bodyView = metadata.Direction switch
        {
            PoseFacingDirection.Front => SceneImageReferenceBodyView.Front,
            PoseFacingDirection.ThreeQuarterLeft => SceneImageReferenceBodyView.ThreeQuarterLeft,
            PoseFacingDirection.ThreeQuarterRight => SceneImageReferenceBodyView.ThreeQuarterRight,
            PoseFacingDirection.ProfileLeft => SceneImageReferenceBodyView.ProfileLeft,
            PoseFacingDirection.ProfileRight => SceneImageReferenceBodyView.ProfileRight,
            PoseFacingDirection.Back => SceneImageReferenceBodyView.Back,
            _ => (SceneImageReferenceBodyView?)null
        };

        var faceView = metadata.Direction switch
        {
            PoseFacingDirection.Front => SceneImageReferenceFaceView.Front,
            PoseFacingDirection.ThreeQuarterLeft => SceneImageReferenceFaceView.ThreeQuarterLeft,
            PoseFacingDirection.ThreeQuarterRight => SceneImageReferenceFaceView.ThreeQuarterRight,
            PoseFacingDirection.ProfileLeft => SceneImageReferenceFaceView.ProfileLeft,
            PoseFacingDirection.ProfileRight => SceneImageReferenceFaceView.ProfileRight,
            // A back-facing pose shows no face, so conditioning it on a face reference would assert a face the pose
            // contradicts — and the render would then put one where the pose has none.
            _ => (SceneImageReferenceFaceView?)null
        };

        var bodyState = metadata.Rating switch
        {
            PoseContentRating.Nsfw => SceneImageReferenceBodyState.Unclothed,
            PoseContentRating.Sfw => SceneImageReferenceBodyState.Clothed,
            _ => (SceneImageReferenceBodyState?)null
        };

        if (bodyState is null)
        {
            return new PoseReferencePlan(
                FaceView: faceView,
                BodyView: bodyView,
                BodyState: null,
                Rationale: "The pose declares no rating, so the body reference's clothed/unclothed state is unknown. "
                    + "Declare the pack's rating in its pack.json.");
        }

        var faceText = faceView is null
            ? "no face reference (a back-facing pose shows no face)"
            : $"{PoseMetadataLabels.Direction(metadata.Direction)} face";
        var rationale =
            $"Sent as {faceText} and {View(metadata.Direction)} "
            + $"body, {(bodyState == SceneImageReferenceBodyState.Unclothed ? "unclothed" : "clothed")} "
            + $"because the pose is {PoseMetadataLabels.Rating(metadata.Rating)}.";

        return new PoseReferencePlan(faceView, bodyView, bodyState, rationale);
    }

    private static string View(PoseFacingDirection direction) =>
        direction == PoseFacingDirection.Back ? "back" : PoseMetadataLabels.Direction(direction);

    /// <summary>
    /// The stance in words. A lying pose states which way UP it lies, because "lying" alone leaves the model to
    /// choose, and the direction is the only field that knows which side of the body the camera sees.
    /// </summary>
    private static string StanceClause(PoseMetadata metadata) => metadata.Stance switch
    {
        PoseStance.Standing => "standing",
        PoseStance.Sitting => "sitting",
        PoseStance.Kneeling => "kneeling",
        PoseStance.Lying => metadata.Direction switch
        {
            PoseFacingDirection.Back => "lying face down",
            PoseFacingDirection.Front => "lying on her back",
            // A lying pose seen from the side does not say which way up it is, and inventing one would be a claim the
            // keypoints cannot support.
            _ => "lying down"
        },
        PoseStance.AllFours => "on all fours",
        PoseStance.Squatting => "squatting",
        PoseStance.Suspended => "suspended in the air",
        PoseStance.SplitLeg => "standing with her legs spread apart",
        PoseStance.Jumping => "jumping",
        PoseStance.Dancing => "dancing",
        PoseStance.Flexing => "flexing her muscles",
        PoseStance.TPose => "standing with her arms outstretched to the sides",
        _ => string.Empty
    };

    private static string DirectionClause(PoseFacingDirection direction) => direction switch
    {
        PoseFacingDirection.Front => "facing the camera",
        PoseFacingDirection.Back => "seen from behind",
        PoseFacingDirection.ThreeQuarterLeft => "three-quarter view, her left side toward the camera",
        PoseFacingDirection.ThreeQuarterRight => "three-quarter view, her right side toward the camera",
        PoseFacingDirection.ProfileLeft => "side view, her left side toward the camera",
        PoseFacingDirection.ProfileRight => "side view, her right side toward the camera",
        _ => string.Empty
    };

    /// <summary>
    /// The camera in words. An eye-level or undeclared camera gets NO clause: the pose proof's prompt had none and
    /// 16 of 24 renders landed inside tolerance, so a clause here would be a claim the metadata does not make.
    /// </summary>
    private static string CameraClause(PoseCameraAngle camera) => camera switch
    {
        PoseCameraAngle.FromAbove => "viewed from above",
        PoseCameraAngle.FromBelow => "viewed from below",
        _ => string.Empty
    };
}
