namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// The trailing statement that fixes WHERE the camera holder's visible body parts are, in a first-person frame.
/// </summary>
/// <remarks>
/// <para>
/// The caption can describe the parts correctly and still be rendered as a mutation. Measured 2026-10-27 on SceneImage
/// <b>516cc462</b> (Qwen-Image-2.1, Dean POV, Camera Holder parts on): the compiled caption said "The camera holder's
/// hands are clenching hard against her thighs while their hips press tightly into hers … they stand directly behind
/// Becky near the viewer", which is an accurate third-person description — and the render came back with two disembodied
/// hands growing out of the subject's hips and no holder at all. Nothing in the prompt told the image model that the
/// described person IS the camera: to the image model, "someone behind her whose hands and hips are visible" is a
/// partially drawn person, and the inclusion line ("every person in frame is … whole and uncropped") then asked it to
/// complete that person from the only parts it had been given.
/// </para>
/// <para>
/// So the geometry is stated as DATA, at the end of the prompt, next to the staging block and for the same reason those
/// exist (see <c>SceneStagingStatement</c>, §0.1.27 of the B-167 plan): a 7B compiler model cannot be relied on to carry a
/// fact from instruction to caption, and the caption in turn cannot be relied on to tell an image model whose body it is.
/// The statement is derived from the SAME recorded contacts the projection's note came from — never from the caption —
/// and it is emitted only for a participant POV whose holder's parts are in frame, so an Omniscient prompt is untouched.
/// </para>
/// </remarks>
public static class CameraHolderBodyStatement
{
    /// <summary>The marker that identifies the block and makes it findable in a recorded prompt.</summary>
    public const string Marker = "CAMERA HOLDER — AUTHORITATIVE";

    /// <summary>
    /// The block for this projection, or empty when there is nothing of the holder in frame to place — a null or
    /// Omniscient projection, or a participant POV whose contacts put none of their body in the picture.
    /// </summary>
    public static string For(SceneImagePovProjection.Projection? projection)
    {
        if (projection is null || projection.IsOmniscient)
        {
            return string.Empty;
        }

        var parts = CameraHolderPartsFromContacts.PartNamesOf(projection.CameraHolderParts);
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        var phrase = Join(parts);
        return $"""
            {Marker} (first-person POV: the camera is the holder's own eyes):
            - This is a FIRST-PERSON POV photograph: the frame is what the camera holder sees from where they stand, at their own eye height, looking straight ahead at the subject.
            - The only parts of the camera holder inside the picture are: {phrase}. They are in the NEAR FOREGROUND, closest to the lens, entering from the BOTTOM EDGE of the frame: the hands reach forward from below the picture with the forearms foreshortened and receding back toward the camera, and the top of the hips shows at the very bottom of the frame.
            - They are the CAMERA's body, not the subject's: the hands come from OUTSIDE the picture toward the subject, never out of the subject's own arms, and nothing is attached to the subject's body where they hold.
            - Everything else of the camera holder is behind the camera and never appears: no head, no face, no hair, no shoulders, no chest, no further torso, no legs. There is no second person behind the subject — the body the contacts name IS this camera.
            """;
    }

    /// <summary>
    /// The holder's visible parts phrased for prose ("hands", "hands and hips"), or the generic fallback when the note
    /// names none. Shared with the inclusion sentence so both statements list the same parts.
    /// </summary>
    public static string PartsPhrase(string? note)
    {
        var parts = CameraHolderPartsFromContacts.PartNamesOf(note);
        return parts.Count == 0 ? "the parts the request names" : Join(parts);
    }

    /// <summary>
    /// "hands, hips and feet" — a list a caption can carry, with no Oxford comma and no trailing "and" for one item.
    /// </summary>
    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => string.Empty,
        1 => parts[0],
        2 => $"{parts[0]} and {parts[1]}",
        _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}"
    };
}
