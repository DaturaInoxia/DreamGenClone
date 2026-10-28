using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Which face and body reference VIEW a character's recorded stance suggests for one scene (B-166 D8).
/// </summary>
/// <param name="FaceView">The face angle nearest the head angle the scene shows, or null when none is recorded.</param>
/// <param name="BodyView">The body angle the scene shows, or null when none is recorded.</param>
/// <param name="Basis">
/// The recorded fact the recommendation came from, in the operator's words, or the reason there is none. Shown beside
/// the view selector so the operator can judge the suggestion instead of trusting it.
/// </param>
public sealed record SceneViewRecommendation(
    SceneImageReferenceFaceView? FaceView,
    SceneImageReferenceBodyView? BodyView,
    string Basis)
{
    /// <summary>No angle recorded — the operator picks, and the step must not pre-select a view.</summary>
    public bool IsEmpty => FaceView is null && BodyView is null;
}

/// <summary>
/// Derives the reference VIEW a scene shows from the facts the brief already records.
/// </summary>
/// <remarks>
/// <para>
/// Binding a front face to a moment whose person is turned away is a real, repeated failure mode: the reference wins
/// over the prose and the render comes back facing the wrong way. The pack already knows which views it has, so the
/// missing piece is only "which one does THIS moment show" — and that is recorded in the character's stance and
/// sightline.
/// </para>
/// <para>
/// "Left"/"right" are read in FRAME terms (a left profile is "her nose pointing to the left of the frame" — see the
/// qwen-21-native-reference RUNBOOK). The two inputs mean different things and are read separately: a SIGHTLINE of
/// "gaze left" means the nose points left, so the camera sees the LEFT side; a POSITION of "to the left of Dean" places
/// the character left-of-frame, where they are turned toward the centre and the camera sees their RIGHT side.
/// </para>
/// <para>
/// This is GUIDANCE and nothing else. It never selects, substitutes or falls back to another view: a step with no
/// recorded angle says so, and a recommended view the pack has not approved fails by name where the caller binds it.
/// </para>
/// </remarks>
public static class SceneViewRecommender
{
    /// <summary>The recommendation for a character's recorded stance and sightline.</summary>
    /// <param name="sightline">The character's recorded sightline, or null.</param>
    /// <param name="stance">The character's recorded position/stance, or null.</param>
    public static SceneViewRecommendation Resolve(string? sightline, string? stance)
    {        var text = $"{sightline} {stance}".Trim();
        if (text.Length == 0)
        {
            return None("no angle is recorded on this brief");
        }

        // Back first: a turned-away body shows the back view and NO face, so recommending a face would be wrong.
        if (Contains(text, "away from the camera") || Contains(text, " back to ") || Contains(text, "back to the camera")
            || Contains(text, "from behind") || Contains(text, "behind her") || Contains(text, "behind him"))
        {
            return new SceneViewRecommendation(null, SceneImageReferenceBodyView.Back, "the character faces away from the camera");
        }

        var profile = Contains(text, "profile") || Contains(text, "side profile") || Contains(text, "in profile");

        // The SIGHTLINE is the gaze/facing DIRECTION, recorded in frame terms ("gaze left", "Directed right"). "Left"
        // means the nose points to the LEFT of the frame, so the camera sees the LEFT side. It is read FIRST because it
        // is the direct statement of which way the character faces.
        if (HasWord(sightline ?? string.Empty, "left"))
        {
            return Side(profile, leftSide: true,
                "the recorded sightline looks to the LEFT of the frame, so the camera sees their LEFT side");
        }

        if (HasWord(sightline ?? string.Empty, "right"))
        {
            return Side(profile, leftSide: false,
                "the recorded sightline looks to the RIGHT of the frame, so the camera sees their RIGHT side");
        }

        // The POSITION is where the character stands in the frame ("to the left of Dean"). A character on the LEFT is
        // turned toward the centre — to the right — so the camera sees their RIGHT side: the word maps to the OPPOSITE
        // reference. This frame-placement rule is the opposite of the gaze rule above, which is why the two fields are
        // read separately instead of concatenated.
        if (HasWord(stance ?? string.Empty, "left"))
        {
            return Side(profile, leftSide: false,
                "the recorded position places the character on the LEFT of the frame, so the camera sees their RIGHT side");
        }

        if (HasWord(stance ?? string.Empty, "right"))
        {
            return Side(profile, leftSide: true,
                "the recorded position places the character on the RIGHT of the frame, so the camera sees their LEFT side");
        }

        if (Contains(text, "toward the camera") || Contains(text, "facing the camera") || Contains(text, "forward")
            || Contains(text, "front"))
        {
            return new SceneViewRecommendation(
                SceneImageReferenceFaceView.Front,
                SceneImageReferenceBodyView.Front,
                "the recorded stance or sightline faces the camera");
        }

        return None("the recorded stance and sightline name no angle");
    }

    /// <summary>A left- or right-side view at the recorded depth (three-quarter vs full profile).</summary>
    private static SceneViewRecommendation Side(bool profile, bool leftSide, string basis) => leftSide
        ? new SceneViewRecommendation(
            profile ? SceneImageReferenceFaceView.ProfileLeft : SceneImageReferenceFaceView.ThreeQuarterLeft,
            profile ? SceneImageReferenceBodyView.ProfileLeft : SceneImageReferenceBodyView.ThreeQuarterLeft,
            basis)
        : new SceneViewRecommendation(
            profile ? SceneImageReferenceFaceView.ProfileRight : SceneImageReferenceFaceView.ThreeQuarterRight,
            profile ? SceneImageReferenceBodyView.ProfileRight : SceneImageReferenceBodyView.ThreeQuarterRight,
            basis);

    private static SceneViewRecommendation None(string basis) => new(null, null, basis);

    private static bool Contains(string text, string value)
        => text.Contains(value, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A WHOLE-WORD match. "upright" contains "right" and "leftover" contains "left", so a substring test would
    /// recommend a three-quarter view for an upright stance — the exact kind of confident nonsense this must not emit.
    /// </summary>
    private static bool HasWord(string text, string word)
        => System.Text.RegularExpressions.Regex.IsMatch(
            text,
            $@"(?<![\p{{L}}\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(word)}(?![\p{{L}}\p{{N}}])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
