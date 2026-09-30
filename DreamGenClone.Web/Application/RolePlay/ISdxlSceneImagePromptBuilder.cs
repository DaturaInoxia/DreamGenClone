using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// SDXL / Juggernaut scene-image prompt builder contract. Produces natural-language, photorealistic
/// prompts (no Pony tag vocabulary). Fully separate from the Pony builder — the Pony code path is
/// unchanged.
///
/// <para>
/// The render-stage negative prompt is deliberately NOT part of this contract (B-135 D10): a negative is
/// declared on the checkpoint's <c>ImageCompilerProfile</c> and read from there by the render path, so a
/// builder cannot introduce one.
/// </para>
/// </summary>
public interface ISdxlSceneImagePromptBuilder : ISceneImageLLMPromptBuilder
{
}
