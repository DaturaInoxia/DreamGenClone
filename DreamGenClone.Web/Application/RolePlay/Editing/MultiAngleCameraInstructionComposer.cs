using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Turns a picked multi-angle pose into the exact text the model is sent (the fal multi-angle LoRA's
/// <c>&lt;sks&gt; azimuth elevation distance</c> grammar). This is deterministic by design — like a preset, the
/// precision that makes an orbit read is the exact trained token, so running it through the vision compiler would
/// paraphrase away the contract the LoRA was trained on and cost a model call per edit.
///
/// <para>
/// The composition is one spelling owned here, used by both the service that queues a run and the writer that
/// re-derives it: two spellings would disagree and every multi-angle run would refuse itself.
/// </para>
/// </summary>
public static class MultiAngleCameraInstructionComposer
{
    /// <summary>
    /// The instruction for a picked pose. Fails fast on an undefined enum value rather than sending an instruction
    /// that names a pose the LoRA does not know.
    /// </summary>
    public static string Compose(
        MultiAngleAzimuth azimuth,
        MultiAngleElevation elevation,
        MultiAngleDistance distance)
    {
        if (!Enum.IsDefined(azimuth))
            throw new InvalidOperationException($"Azimuth '{(int)azimuth}' is not a known multi-angle azimuth.");
        if (!Enum.IsDefined(elevation))
            throw new InvalidOperationException($"Elevation '{(int)elevation}' is not a known multi-angle elevation.");
        if (!Enum.IsDefined(distance))
            throw new InvalidOperationException($"Distance '{(int)distance}' is not a known multi-angle distance.");

        return MultiAngleCameraTokens.Instruction(azimuth, elevation, distance);
    }
}
