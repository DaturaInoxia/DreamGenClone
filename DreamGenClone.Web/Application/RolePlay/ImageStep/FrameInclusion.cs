namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// What the frame must CONTAIN, stated so no shot-size word can be read as a licence to crop a person.
/// </summary>
/// <remarks>
/// <para>
/// A shot size describes how much of the picture the subject occupies. It says nothing about which parts of them exist
/// in the picture, and treating it as if it did failed live: SceneImage 8bb09d13 was rendered from a caption that read
/// "A close-up shot captured from above … Her head is not visible due to being positioned below the frame line where the
/// image cuts off at about waist height." Nothing about the frame required that — a close view from above of someone
/// bent over a bench frames her whole body — the CAPTION invented the crop and the image model obeyed it.
/// </para>
/// <para>
/// The default extent is therefore the whole person, for every shot size, and it is stated in the prompt rather than
/// left implicit. The operator may narrow it (this becomes a Prompt Plan field, P1 of
/// <c>specs/Planning/B-167-scene-data-facts-and-prompt-compiler/holistic-prompt-pipeline.md</c>); until then every frame
/// states the default, because "state the whole person" cannot produce a missing head and "assume the shot implies it"
/// demonstrably can.
/// </para>
/// </remarks>
public static class FrameInclusion
{
    /// <summary>The extent every frame states unless the operator narrows it.</summary>
    public const string WholeBody = "crown to feet";

    /// <summary>
    /// The sentence a realised prompt carries: what is IN the frame, phrased as a fact about the picture.
    /// </summary>
    public const string Sentence =
        "Every person in frame is in frame from crown to feet, whole and uncropped by the edge of the picture.";

    /// <summary>True when the text already states the inclusion, so a caller does not say it twice.</summary>
    public static bool IsStated(string? text)
        => !string.IsNullOrWhiteSpace(text)
           && text.Contains(WholeBody, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The inclusion sentence for a FIRST-PERSON frame whose camera holder's body parts are in the picture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Sentence"/> says "every person in frame is in frame from crown to feet". A participant POV cannot
    /// honour that about its own camera: the holder is cropped by the frame BY DEFINITION, and telling the image model
    /// that every person is whole is what made it try to build a body for the only parts of the holder the caption
    /// named. Measured 2026-10-27 (SceneImage 516cc462, Dean POV with Camera Holder parts on): the caption described his
    /// hands and hips, the prompt also said every person is whole "from crown to feet", and the render produced two
    /// disembodied hands growing out of the subject's hips.
    /// </para>
    /// <para>
    /// The SUBJECT is still whole and uncropped — that half of the rule exists so a shot-size word is never read as a
    /// licence to crop, and it still applies. Only the camera holder is exempt, because they are the camera.
    /// </para>
    /// </remarks>
    /// <param name="parts">The holder's visible parts, already phrased for prose ("hands", "hands and hips").</param>
    public static string ParticipantSentence(string? parts)
    {
        var named = string.IsNullOrWhiteSpace(parts) ? "the parts the request names" : parts.Trim();
        return "FIRST-PERSON POV: the subject is in frame from crown to feet, whole and uncropped by the edge of the "
            + "picture. The camera holder is the camera, not a second person: only their " + named + " are in the "
            + "picture, entering from the bottom edge of the frame in the near foreground, and the rest of them is "
            + "behind the camera.";
    }
}
