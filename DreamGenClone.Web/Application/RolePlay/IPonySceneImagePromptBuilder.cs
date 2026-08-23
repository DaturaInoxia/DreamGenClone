using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Pony/ComfyUI scene-image prompt builder. Emits the dense, comma-separated, tag-friendly image
/// prompt that PonyV6 (ComfyUI) reads natively, using the deterministic beat projection path.
/// </summary>
public interface IPonySceneImagePromptBuilder
{
    /// <summary>
    /// Projects a frozen beat directly into an image-model prompt. Identity, wardrobe, visible
    /// cast, spatial facts, and camera geometry are deterministic and are never LLM-paraphrased.
    /// </summary>
    string BuildDeterministicBeatPrompt(
        RolePlaySession session,
        SceneImageBeat beat,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? refineInstruction,
        IReadOnlyList<Character>? characters = null);

    /// <summary>
    /// Builds the deterministic negative prompt for a frozen beat + POV. Suppresses common
    /// image-model artifacts (extra limbs, malformed anatomy, merged bodies) and any character that
    /// must not be shown in the frame. Consumed by backends that support a separate negative prompt.
    /// </summary>
    string BuildDeterministicBeatNegativePrompt(
        SceneImageBeat beat,
        string pov);

    /// <summary>Parse the pre-processor output into the editable prompt (+ pulled excerpt).
    /// Tolerates a JSON envelope {{prompt, excerpt}} or plain text. Fails fast on empty/overlong.</summary>
    SceneImagePreprocessorResult ParseOutput(string rawOutput);
}

/// <summary>Parsed pre-processor output.</summary>
public sealed record SceneImagePreprocessorResult(string Prompt, string Excerpt);
