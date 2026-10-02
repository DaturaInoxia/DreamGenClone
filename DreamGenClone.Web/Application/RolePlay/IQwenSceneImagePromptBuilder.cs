using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Qwen-Image-2.1 scene-image prompt builder contract. Produces the Qwen long-form natural-language prompt
/// (route 1 of B135-008: the vendor's prompt-shape rules adopted into our own prose, cited to
/// <c>research/qwen-2-1-prompt-enhancer.md</c>). Fully separate from the SDXL builder — no SDXL style tail
/// ("35mm", "natural skin texture"), no quality boosters, no Pony tag vocabulary.
///
/// <para>
/// The render-stage negative prompt is deliberately NOT part of this contract (B-135 D10): a negative is
/// declared on the checkpoint's <c>ImageCompilerProfile</c> and read from there by the render path, so a
/// builder cannot introduce one.
/// </para>
/// </summary>
public interface IQwenSceneImagePromptBuilder : ISceneImageLLMPromptBuilder
{
}
