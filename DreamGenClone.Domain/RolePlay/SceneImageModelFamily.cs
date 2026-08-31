namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The family of diffusion model a scene-image checkpoint belongs to. Determines which prompt
/// builder (Pony tags vs SDXL natural language) and which ComfyUI workflow (CLIP skip vs none)
/// are used. This is the single decision path for scene-image model routing — the resolved
/// checkpoint identifier is the only discriminator.
/// </summary>
public enum SceneImageModelFamily
{
    Unknown = 0,
    Pony = 1,
    Sdxl = 2
}

/// <summary>
/// Classifies a ComfyUI checkpoint identifier into its scene-image model family. Central
/// single-source used by the prompt generation handler, the render handler, and the ComfyUI client
/// so there is exactly one routing decision for Pony-vs-SDXL. Unknown identifiers must fail fast at
/// the call sites (never fall back to a default model).
/// </summary>
public static class SceneImageModelFamilyResolver
{
    /// <summary>
    /// Classify a checkpoint filename into a model family. Matching is case-insensitive on the
    /// identifier the user registers in Model Manager (which ComfyUI uses verbatim as the
    /// checkpoint name). Returns <see cref="SceneImageModelFamily.Unknown"/> for anything that is
    /// not an explicitly supported family.
    /// </summary>
    public static SceneImageModelFamily Classify(string? checkpointName)
    {
        if (string.IsNullOrWhiteSpace(checkpointName))
        {
            return SceneImageModelFamily.Unknown;
        }

        var id = checkpointName.Trim();

        // Pony family: the Pony V6 XL checkpoint (danbooru tag vocabulary, CLIP skip 2).
        if (id.Contains("pony", StringComparison.OrdinalIgnoreCase))
        {
            return SceneImageModelFamily.Pony;
        }

        // SDXL / Juggernaut family: natural-language, photorealistic SDXL-family checkpoints
        // (Juggernaut XL, SDXL base/refiner, RealVisXL, Lustify, ...). No CLIP skip.
        if (id.Contains("juggernaut", StringComparison.OrdinalIgnoreCase)
            || id.Contains("jugg", StringComparison.OrdinalIgnoreCase)
            || id.Contains("sd_xl", StringComparison.OrdinalIgnoreCase)
            || id.Contains("sdxl", StringComparison.OrdinalIgnoreCase)
            || id.Contains("realvis", StringComparison.OrdinalIgnoreCase)
            || id.Contains("realistic vision", StringComparison.OrdinalIgnoreCase)
            || id.Contains("lustify", StringComparison.OrdinalIgnoreCase)
            || id.Contains("biglust", StringComparison.OrdinalIgnoreCase))
        {
            return SceneImageModelFamily.Sdxl;
        }

        return SceneImageModelFamily.Unknown;
    }
}
