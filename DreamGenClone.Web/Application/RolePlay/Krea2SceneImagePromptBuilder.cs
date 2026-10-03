using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Krea 2 (Krea-2 Turbo) scene-image prompt builder. Produces the Krea 2 photographic brief: plain natural-language
/// sentences describing the finished photograph, under the rules measured on the 59-cell proof matrix
/// (<c>helpers/local-comfyui-host/run-krea2-proof.ps1</c>).
///
/// <para>
/// The message assembly (story moment, scene context, character appearance, image settings) is shared with the SDXL
/// and Qwen builders through <see cref="NaturalLanguageSceneImagePromptBuilder"/>; this class only supplies the
/// Krea 2 compiler instruction text. It is a separate builder rather than a shared one because the SDXL text tells
/// the model to LEAD WITH FRAMING and allows a body-part vocabulary - the two instructions that measurably degrade
/// Krea 2 output.
/// </para>
/// </summary>
public sealed class Krea2SceneImagePromptBuilder : NaturalLanguageSceneImagePromptBuilder, IKrea2SceneImagePromptBuilder
{
    /// <summary>Krea 2 photographic-brief system prompt for the beat path (story prose in).</summary>
    protected override string BuildSystemPrompt() => SceneImageCompilerSystemPrompts.Krea2Beat;

    /// <summary>Krea 2 photographic-brief system prompt for the canonical-brief path (immutable Still brief in).</summary>
    protected override string BuildCanonicalSystemPrompt() => SceneImageCompilerSystemPrompts.Krea2Canonical;
}
