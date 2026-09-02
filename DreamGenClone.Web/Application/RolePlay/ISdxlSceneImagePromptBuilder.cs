using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// SDXL / Juggernaut scene-image prompt builder contract. Produces natural-language, photorealistic
/// prompts (no Pony tag vocabulary) plus the render-stage SDXL negative prompt. Fully separate from
/// the Pony builder — the Pony code path is unchanged.
/// </summary>
public interface ISdxlSceneImagePromptBuilder : ISceneImageLLMPromptBuilder
{
    /// <summary>
    /// Deterministic SFW clamp appended to prompts sent to SFW-filtered providers. SDXL prose form
    /// (distinct from the Pony clamp suffix).
    /// </summary>
    string SfwClampSuffix { get; }

    /// <summary>
    /// Builds the deterministic negative prompt for a frozen beat + POV using SDXL-family negatives
    /// (SDXL needs a heavier guard set than Pony: limb/leg artifacts, censored or featureless
    /// genitals, and non-photoreal styles). Also suppresses any character that must not appear.
    /// </summary>
    string BuildDeterministicBeatNegativePrompt(SceneImageBeat beat, string pov);
}
