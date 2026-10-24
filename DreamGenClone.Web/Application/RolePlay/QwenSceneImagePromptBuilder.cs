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

    /// <summary>
    /// A User Intent draft that BINDS reference images is compiled under the ACTION-ONLY contract, not the long-form
    /// one: the long-form contract requires a full description of the picture, which is exactly what the bound images
    /// supply, and the compiler resolves that conflict by inventing the room, the outfit and the objects.
    /// </summary>
    /// <remarks>
    /// The contract is composed from what the bound images actually supply. Selecting it for ANY bound reference while
    /// asserting all four element classes as supplied is what made a clothing change unwritable — SceneImage a597ba82
    /// (2026-10-18) bound identity + location only, and every compiled prompt was forbidden from stating the clothing
    /// state the operator asked for. With no clothing reference bound, the contract now REQUIRES that state instead.
    /// See <see cref="SceneImageCompilerSystemPrompts.Qwen21CanonicalActionOnly(IntentReferenceCoverage)"/> and the
    /// measured findings behind it (SceneImage f74058d3, 87454bef).
    /// </remarks>
    protected override string BuildIntentSystemPrompt(IntentReferenceCoverage coverage) => coverage.Any
        ? SceneImageCompilerSystemPrompts.Qwen21CanonicalActionOnly(coverage)
        : base.BuildIntentSystemPrompt(coverage);
}
