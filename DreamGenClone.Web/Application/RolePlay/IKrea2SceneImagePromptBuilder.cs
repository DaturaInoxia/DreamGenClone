using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Krea 2 scene-image prompt builder contract. Produces the Krea 2 photographic brief - plain natural-language
/// sentences shaped like a photographer's shot description, with the measured rules from the 59-cell proof matrix
/// encoded in its system prompt (no framing demands, no body noun-lists, no face-facing clause on an act, both
/// actors of an act named).
///
/// <para>
/// Separate from the SDXL builder even though both write natural language: the SDXL text instructs the model to
/// lead with framing and permits an SDXL-family body vocabulary, and both of those are measured DEFECTS on Krea 2
/// rather than shared behaviour.
/// </para>
///
/// <para>
/// The render-stage negative prompt is deliberately NOT part of this contract (B-135 D10, and D4 for Krea 2): Krea 2
/// takes no negative text at all - its graph zeroes the positive - so a builder that could emit one would be
/// emitting a value the graph has no input for.
/// </para>
/// </summary>
public interface IKrea2SceneImagePromptBuilder : ISceneImageLLMPromptBuilder
{
}
