using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Qwen-Image-2.1 scene-image prompt builder. Produces the Qwen long-form natural-language prompt — a full,
/// present-tense observer description of the finished picture (~20 sentences / 400-500 words), NOT the short
/// SDXL-family photography brief and NOT Pony tag vocabulary. Route 1 of B135-008: the vendor's prompt-shape
/// rules are adopted into our own prose, cited to
/// <c>specs/Planning/B-135-image-playground/research/qwen-2-1-prompt-enhancer.md</c> §4 and §8.
///
/// <para>
/// The message assembly (story moment, scene context, character appearance, image settings) is shared with
/// <see cref="SdxlSceneImagePromptBuilder"/> through <see cref="NaturalLanguageSceneImagePromptBuilder"/>; this class
/// only supplies the Qwen-2.1 compiler instruction text.
/// </para>
/// </summary>
public sealed class QwenSceneImagePromptBuilder : NaturalLanguageSceneImagePromptBuilder, IQwenSceneImagePromptBuilder
{
    /// <summary>
    /// Qwen-2.1 long-form system prompt for the beat path. No identity fluff: the model's behaviour comes from
    /// (a) facts about the Qwen-2.1 checkpoint, (b) explicit rules, and the vendor's observer-description procedure.
    /// Research + rationale in <c>research/qwen-2-1-prompt-enhancer.md</c> §4/§8.
    /// </summary>
    protected override string BuildSystemPrompt() => SceneImageCompilerSystemPrompts.Qwen21Beat;

    /// <summary>
    /// Qwen-2.1 long-form system prompt for the canonical-brief path (B-104/B-103 part B). Same route-1 source as
    /// the beat path, adapted to the immutable canonical Still brief.
    /// </summary>
    protected override string BuildCanonicalSystemPrompt() => SceneImageCompilerSystemPrompts.Qwen21Canonical;
}
