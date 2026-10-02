using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// SDXL / Juggernaut scene-image prompt builder. Produces NATURAL-LANGUAGE, photorealistic image
/// prompts for SDXL-family checkpoints (SDXL base 1.0, Juggernaut XL, RealVisXL, ...). Fully
/// separate from <see cref="PonySceneImagePromptBuilder"/>: no Pony tag vocabulary (no score_*,
/// no rating_*, no count tags, no CLIP-skip conventions). Explicitness is chosen from depicted
/// scene content and expressed in natural-language prose.
///
/// <para>
/// The message assembly (story moment, scene context, character appearance, image settings) is shared with
/// <see cref="QwenSceneImagePromptBuilder"/> through <see cref="NaturalLanguageSceneImagePromptBuilder"/>; this class
/// only supplies the SDXL-family compiler instruction text.
/// </para>
/// </summary>
public sealed class SdxlSceneImagePromptBuilder : NaturalLanguageSceneImagePromptBuilder, ISdxlSceneImagePromptBuilder
{
    /// <summary>
    /// SDXL expert system prompt: natural-language photography brief, no tag vocabulary, with
    /// depicted-content explicitness expressed in prose.
    ///
    /// <para>
    /// B-135: the text itself now lives in <see cref="SceneImageCompilerSystemPrompts"/> so the per-checkpoint profile
    /// rows can be seeded from the same source this builder compiles with. It is the same text, moved - the provenance
    /// note (which clauses were adopted from the operator-supplied SDXL rewriter prompt and which were REJECTED) moved
    /// with it.
    /// </para>
    /// </summary>
    protected override string BuildSystemPrompt() => SceneImageCompilerSystemPrompts.NaturalLanguageBeat;

    /// <summary>
    /// Canonical composition-path system prompt (B-104 / B-103 part B). No identity fluff: the model's
    /// behavior comes from (a) facts about the target SDXL-family checkpoint, (b) explicit
    /// rules, and (c) a concrete example showing the target shape. Research + rationale in
    /// <c>.github/instructions/scene-image-prompt-compiler-standards.instructions.md</c> and the B-103 failure analysis
    /// (Becky dropped from a wide, distant, shadowed shot).
    ///
    /// <para>
    /// B-135: the text now lives in <see cref="SceneImageCompilerSystemPrompts"/>. Rules 7-10 were adopted from the
    /// operator-supplied SDXL rewriter prompt (assessment record in the B-135 research folder); its tag-list grammar,
    /// backtick wrapper, numeric-age clamp and race-&gt;appearance inference were REJECTED - rule 8 is the corrected
    /// form of that inference clause, not a copy of it.
    /// </para>
    /// </summary>
    protected override string BuildCanonicalSystemPrompt() => SceneImageCompilerSystemPrompts.NaturalLanguageCanonical;
}
