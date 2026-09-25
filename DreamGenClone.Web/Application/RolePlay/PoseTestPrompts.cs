namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The wording a pose test starts from — ONE copy, and ONE prompt for every pose.
///
/// This is the proof's formulation, minus the two things the proof only had because it fixed them by hand. The
/// proof rendered 12 different poses with a single unchanged prompt, so a test that needed wording per pose would
/// be testing the words rather than the pose:
///
///   * the SUBJECT is generic ("a fully clothed person") rather than "a woman in a grey sweater and dark trousers";
///   * there is NO stance clause and NO view clause. The skeleton carries the stance, and the app does not know
///     which way a library pose faces (a pack file carries keypoints, not a declared view), so asserting
///     "front view, facing the camera" would state a facing the reference may contradict. It is also unnecessary:
///     the proof's prompt had no view clause and 16 of 24 all-fours renders landed within 6 % mean joint error.
///
/// It is offered, never enforced — every surface that shows it lets the operator edit it, which is where a
/// specific view or clothing belongs when a render needs one.
/// </summary>
public static class PoseTestPrompts
{
    public const string Default =
        "A full-body photograph of a fully clothed person, natural skin texture, photorealistic, "
        + "plain studio background, 85mm.";
}
