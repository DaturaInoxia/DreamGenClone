namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// What the figure is DOING with its body. Persisted as the enum name, like every other stored enum in this
/// codebase, so a later addition cannot silently reinterpret a stored row.
///
/// <see cref="Unknown"/> is a first-class value rather than a placeholder: a pack whose category the app has no
/// declaration for gets <see cref="Unknown"/> and says so, which is the honest answer. A guessed "standing" would
/// reach the prompt as a stance clause and change the render.
/// </summary>
public enum PoseStance
{
    Unknown = 0,
    Standing = 1,
    Sitting = 2,
    Kneeling = 3,
    Lying = 4,
    AllFours = 5,
    Squatting = 6,
    Suspended = 7,
    SplitLeg = 8,
    Jumping = 9,
    Dancing = 10,
    Flexing = 11,
    TPose = 12
}

/// <summary>
/// Which way the figure faces relative to the camera. This is the axis the reference images are matched on, so it
/// is the field that decides whether a render is conditioned on the character's FRONT or its BACK.
///
/// The naming follows the app's existing convention (<see cref="SceneImageReferenceFaceView"/>): "ProfileRight"
/// means the subject's RIGHT side is toward the camera, which is the same angle the identity pack stores under that
/// name. Left and right here are the SUBJECT's, never the viewer's — a subtlety that matters because the two are
/// mirrored, and a mirrored reference is the one failure a viewer will not notice.
/// </summary>
public enum PoseFacingDirection
{
    Unknown = 0,
    Front = 1,
    ThreeQuarterLeft = 2,
    ThreeQuarterRight = 3,
    ProfileLeft = 4,
    ProfileRight = 5,

    /// <summary>
    /// The figure's back is toward the camera, so NO face is visible in the pose. Recorded because it is a different
    /// statement from "we could not tell": a back-facing pose legitimately needs no face reference, while an unknown
    /// one must not be conditioned on a face the pose contradicts.
    /// </summary>
    Back = 6
}

/// <summary>
/// Where the camera sits relative to the figure. Measured at the library level, because a flat keypoint set cannot
/// encode height above the subject: the same 2D skeleton is a standing figure seen from the front and a lying figure
/// seen from above, which is exactly the ambiguity that made the model stand a lying pose up.
/// </summary>
public enum PoseCameraAngle
{
    Unknown = 0,
    EyeLevel = 1,
    FromAbove = 2,
    FromBelow = 3
}

/// <summary>
/// Whether the pose depicts a clothed or an unclothed subject. Declared per pack and never inferred from pixels or
/// filenames, because it selects which body reference the test conditions on — and a clothed pose conditioned on an
/// unclothed reference renders unclothed.
/// </summary>
public enum PoseContentRating
{
    Unrated = 0,
    Sfw = 1,
    Nsfw = 2
}

/// <summary>
/// Everything the app knows about a pose beyond its keypoints: the wording it should be rendered with, the angle
/// references it needs, and a flag saying whether the two disagreed.
///
/// <see cref="Prompt"/> is stored rather than composed at read time so that what the operator reads on the card and
/// what the test render sends are the same string — a prompt quietly recomposed at render time would mean the
/// recorded wording and the sent wording could differ with nothing recording the change.
/// </summary>
/// <param name="NeedsReview">
/// True when a MEASUREMENT of the keypoints contradicts the DECLARED metadata (for example a category declared
/// front-facing whose shoulders are ordered like a back view). The pose stays usable — the declaration is still what
/// the render uses — but it is flagged so the disagreement is visible instead of being averaged away.
/// </param>
/// <param name="ReviewNote">The measured evidence behind <see cref="NeedsReview"/>, in words, for the operator.</param>
public sealed record PoseMetadata(
    PoseStance Stance,
    PoseFacingDirection Direction,
    PoseCameraAngle Camera,
    PoseContentRating Rating,
    string Prompt,
    bool NeedsReview = false,
    string ReviewNote = "");

/// <summary>
/// The reference images a pose requires, resolved from what the pose IS rather than chosen by hand: the direction
/// decides which angle of the character, and the rating decides clothed or unclothed.
///
/// Either view may be null, and each null means something specific. A null <see cref="BodyView"/> means the pose's
/// direction is unknown, so no body angle can be justified; a null <see cref="FaceView"/> with a DECLARED direction
/// means the pose shows no face (a back view) and no face reference is required. Callers must distinguish the two
/// and refuse in the first case rather than falling back to the front.
/// </summary>
public sealed record PoseReferencePlan(
    SceneImageReferenceFaceView? FaceView,
    SceneImageReferenceBodyView? BodyView,
    SceneImageReferenceBodyState? BodyState,
    string Rationale);
