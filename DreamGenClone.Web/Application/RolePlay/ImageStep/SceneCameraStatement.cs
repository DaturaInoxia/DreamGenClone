using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// The ONE owner of the trailing camera statement — the sentence that tells the image model how the frame is taken —
/// worded per MODEL FAMILY.
/// </summary>
/// <remarks>
/// <para>
/// The frame (<see cref="SceneBlocking"/>) is family-independent data: shot size, angle, height and who stands where.
/// What a family can DO with that data is not: SDXL/Flux/Qwen document shot-size and angle terms of art and obey a
/// stated camera, while Krea 2 was measured to do the opposite — an explicit framing demand is its worst offender and
/// makes it crop the figure or drop the head (59-cell proof matrix, 2026-10-01, recorded on
/// <c>SceneImageCompilerSystemPrompts.Krea2Beat</c>).
/// </para>
///
/// <para>
/// That is why the demand form could not stay in one shared line: the Krea 2 compiler prompt forbids framing demands in
/// rule 4, and the assembler then appended one to every render. The operator still chooses from the SAME controls —
/// height, shot size — and this translates the choice into the wording that family actually reads: a demand for the
/// families that document terms, a description of the viewpoint and what fills the frame for Krea 2.
/// </para>
///
/// <para>
/// The <see cref="FinalImagePromptPlanBuilder.FramingMarker"/> label is kept on BOTH forms on purpose: it is what
/// <see cref="FinalImagePromptPlanBuilder"/> uses to guarantee a prompt carries exactly ONE camera statement, and that
/// invariant has nothing to do with wording. A second camera line was measured overriding the first on 2026-10-21.
/// </para>
/// </remarks>
public static class SceneCameraStatement
{
    /// <summary>
    /// The camera statement for this family, or empty when the brief carries no frame to state.
    /// </summary>
    /// <param name="family">The family the render will run on — it decides the wording, never whether a camera is stated.</param>
    /// <param name="blocking">The brief's frame-space blocking.</param>
    /// <param name="inclusion">
    /// The inclusion sentence to state, or null for <see cref="FrameInclusion.Sentence"/>. A participant POV whose camera
    /// holder's parts are in the picture passes <see cref="FrameInclusion.ParticipantSentence"/>: "every person whole"
    /// is true of an external frame and impossible for the camera.
    /// </param>
    public static string For(SceneImageModelFamily family, SceneBlocking? blocking, string? inclusion = null)
    {
        if (blocking?.Camera is not { } camera || string.IsNullOrWhiteSpace(camera.ShotSize))
        {
            // No frame was derived, so there is no camera to state. Claiming one would frame a render the brief never
            // described.
            return string.Empty;
        }

        return family == SceneImageModelFamily.Krea2
            ? ForKrea2(camera, inclusion ?? FrameInclusion.Sentence)
            : SceneFramingClause.LineFor(blocking, inclusion);
    }

    /// <summary>
    /// Krea 2's form: describe the viewpoint the photograph was taken from and what fills the frame — no shot-size term
    /// and no instruction about how much of the subject to include.
    /// </summary>
    /// <remarks>
    /// The measured rule is that Krea 2 renders what it is TOLD about the scene and reacts badly to being told how to
    /// frame it. So the operator's dropdown choice is TRANSLATED rather than removed: "above" becomes where the camera
    /// sits and which way it looks, and the shot size becomes what fills the picture ("the subject fills most of the
    /// frame"), which is a fact about the image rather than an instruction to the model.
    /// </remarks>
    private static string ForKrea2(SceneBlockingCamera camera, string inclusion)
    {
        var viewpoint = ViewpointOf(camera.Height) is { Length: > 0 } described
            ? $"the picture is taken {described}."
            : string.Empty;
        var coverage = CoverageOf(camera.ShotSize, inclusion);

        var parts = new[] { viewpoint, coverage }.Where(part => part.Length > 0).ToArray();
        if (parts.Length == 0)
        {
            // Neither a height nor a recognised shot size: there is no viewpoint to describe, and inventing one would
            // frame a render the operator did not choose.
            return string.Empty;
        }

        return $"{FinalImagePromptPlanBuilder.FramingMarker}: {string.Join(" ", parts)}";
    }

    /// <summary>Where the camera sits and which way it looks, in description rather than in demand.</summary>
    private static string ViewpointOf(string? height) => height?.Trim().ToLowerInvariant() switch
    {
        CameraHeights.Floor => "from floor level, looking up",
        CameraHeights.Waist => "from waist height, level with the subject",
        CameraHeights.Eye => "from eye level",
        CameraHeights.Above => "from above the subject's eye line, looking down",
        CameraHeights.Overhead => "from directly overhead, looking straight down",
        _ => string.Empty
    };

    /// <summary>What fills the frame, in the operator's own shot vocabulary translated to what the picture shows.</summary>
    /// <remarks>
    /// Every arm states the frame's INCLUSION first. A shot size describes how much of the picture the subject
    /// occupies — it never decides which parts of them exist in it, and a caption that reads "close-up shot, subject
    /// fills the frame" alone will do what a caption writer did on 2026-10-25 (SceneImage 8bb09d13): invent a crop
    /// ("her head is not visible … the image cuts off at about waist height") and hand the image model a crop to obey.
    /// The inclusion sentence is emitted for every shot size, before any fullness claim, so there is never a licence to
    /// cut a person at the frame edge.
    /// </remarks>
    private static string CoverageOf(string shotSize, string inclusionSentence)
    {
        var shot = shotSize.Trim();
        var inclusion = inclusionSentence;

        if (shot.Length == 0)
        {
            return inclusion;
        }

        if (Contains(shot, "tight") || Contains(shot, "close"))
        {
            return $"{inclusion} The subject fills most of the frame.";
        }

        if (Contains(shot, "wide") || Contains(shot, "establishing") || Contains(shot, "full") || Contains(shot, "long"))
        {
            // The scale sentence is the same fact SDXL is given as an instruction, here stated as what the picture
            // shows: a wide frame is mostly setting.
            return $"{inclusion} The setting fills the frame, and everyone in it is scaled by how far they are from the "
                + "camera: a person further away is smaller in the picture and is never enlarged to stay readable.";
        }

        if (Contains(shot, "medium") || Contains(shot, "mid"))
        {
            return $"{inclusion} The subject fills the centre of the frame, with the setting visible around them.";
        }

        return inclusion;
    }

    private static bool Contains(string text, string term)
        => text.Contains(term, StringComparison.OrdinalIgnoreCase);
}
