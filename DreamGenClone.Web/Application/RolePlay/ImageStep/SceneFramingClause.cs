using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// States the frame's CAMERA to the image model, as the last thing it reads (B-161).
///
/// <para>
/// The camera already existed: the staging profile declares a shot size, the resolver reads it out of the brief's
/// rationale, and it lands in the snapshot as <c>blocking.camera</c>. What was missing is that nothing ever TOLD the
/// model. The framing only reached it as a JSON field the prompt-writing model might echo into the prose — which is
/// exactly what happened with "The camera sits at eye level in a medium shot" — so a subject placed "in the
/// background" had no scale instruction at all and rendered at half height in a frame that was supposed to show a
/// whole yard (reported 2026-10-13, measured again on the next render).
/// </para>
///
/// <para>
/// So the framing is stated where the frame is already authoritative — beside the reference-role block, at the end of
/// the prompt — and it also says what the framing MEANS for scale, because "wide establishing shot" on its own is a
/// term of art the model may satisfy by zooming out slightly while keeping both people large. The two things this
/// asks for are the two the operator kept having to fix by hand: everyone is scaled by their distance from the camera,
/// and a distant subject is not enlarged to stay readable.
/// </para>
///
/// <para>
/// One owner, one line, appended by <c>FinalImagePromptPlanBuilder</c> — the single place a final prompt is
/// assembled — so every render path that has a frame states it, and none states it differently.
/// </para>
/// </summary>
public static class SceneFramingClause
{
    /// <param name="inclusion">
    /// The inclusion sentence this frame states. Defaults to <see cref="FrameInclusion.Sentence"/> — every person whole —
    /// which is right for an external frame. A participant POV whose camera holder's parts are in the picture passes
    /// <see cref="FrameInclusion.ParticipantSentence"/> instead, because that holder is the camera and is cropped by the
    /// frame by definition.
    /// </param>
    public static string LineFor(SceneBlocking? blocking, string? inclusion = null)
    {
        if (blocking?.Camera is not { } camera || string.IsNullOrWhiteSpace(camera.ShotSize))
        {
            // No frame was derived, so there is no camera to state. Claiming one would frame a render the brief never
            // described.
            return string.Empty;
        }

        var shot = camera.ShotSize.Trim();
        var angle = string.IsNullOrWhiteSpace(camera.Angle) ? string.Empty : camera.Angle.Trim();
        // The height is the operator's/compile-time decision and is stated in the ONE camera clause, as term + relation
        // ("low angle from floor level, looking up"). Two sentences would read as two subjects, and the research is
        // explicit that a bare term alone under-delivers at the extremes.
        var height = CameraHeights.IsDefined(camera.Height)
            ? $", {CameraHeights.Wording(CameraHeights.Require(camera.Height, "The compiled camera"))}"
            : string.Empty;
        // The angle and the height are derived from the SAME pitch when the operator chose a height and no angle, so
        // stating both printed it twice: "at high angle, viewed from above, high angle, camera just above eye level,
        // looking down" (reported live 2026-10-25, SceneImage 57fcc461). When the height wording already carries the
        // angle's own term, the term is said once — an operator-supplied angle ("three-quarter angle") is kept.
        if (angle.Length > 0 && height.Length > 0 && height.Contains(AngleTerm(angle), StringComparison.OrdinalIgnoreCase))
        {
            angle = string.Empty;
        }

        var angleClause = angle.Length > 0 ? $" at {angle}" : string.Empty;
        var head = $"CAMERA — AUTHORITATIVE: this frame is a {shot}{angleClause}{height}."
            + $" {inclusion ?? FrameInclusion.Sentence}";
        // The SCALE instruction only applies to a shot that contains distance. It was measured on a WIDE establishing
        // shot (B-161, 2026-10-13): a subject told to be "far in the background" still rendered at half frame height
        // until the camera distance was stated in the prompt. On a close-up there is no distance to scale, so the same
        // paragraph is noise that competes with the subject — which is what it was doing on a "close shot".
        if (!IsDistanceShot(shot))
        {
            return head + " The subject fills the frame.";
        }

        return head + " Every person in it is scaled by how far they are from that camera: someone further away is "
            + "smaller in the picture, and a person at a distance is not enlarged to stay readable — a wide frame is "
            + "mostly setting. Reproduce that scale rather than framing everyone at close range.";
    }

    /// <summary>
    /// The angle's leading TERM — "high angle, viewed from above" → "high angle". This is the part a height wording can
    /// already say, which is what makes a duplicated camera phrase detectable without a second table of synonyms.
    /// </summary>
    private static string AngleTerm(string angle)
    {
        var comma = angle.IndexOf(',');
        return (comma > 0 ? angle[..comma] : angle).Trim();
    }

    /// <summary>
    /// Whether this shot size contains enough distance for the scale rule to mean anything. A close shot has one
    /// subject at arm's length; "someone further away is smaller" is not a fact about it.
    /// </summary>
    private static bool IsDistanceShot(string shotSize)
        => shotSize.Contains("wide", StringComparison.OrdinalIgnoreCase)
           || shotSize.Contains("establishing", StringComparison.OrdinalIgnoreCase)
           || shotSize.Contains("full", StringComparison.OrdinalIgnoreCase)
           || shotSize.Contains("long shot", StringComparison.OrdinalIgnoreCase);
}
