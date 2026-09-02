using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// LLM-driven scene-image prompt builder (Seedream/OpenAI-protocol era). Composes system + user
/// messages for a text pre-processor model that drafts the image prompt from an interaction + scene
/// context + settings, then parses the model's output. Distinct from
/// <see cref="IPonySceneImagePromptBuilder"/>, which projects beats deterministically for Pony/ComfyUI.
/// </summary>
public interface ISceneImageLLMPromptBuilder
{
    (string SystemPrompt, string UserPrompt) BuildMessages(
        CompiledMediaBrief brief,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? refineInstruction);

    /// <summary>
    /// Canonical composition-path variant that also receives the scenario characters so the builder
    /// can inject each depicted character's fixed physical appearance. The default implementation
    /// delegates to the character-less overload (Pony/API builders ignore characters); the SDXL
    /// builder overrides this to append the authoritative appearance block.
    /// </summary>
    (string SystemPrompt, string UserPrompt) BuildMessages(
        CompiledMediaBrief brief,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? refineInstruction,
        IReadOnlyList<Character>? characters)
        => BuildMessages(brief, pov, settings, resolvedPolicy, refineInstruction);

    /// <summary>Compose the system + user messages for the pre-processor model.</summary>
    (string SystemPrompt, string UserPrompt) BuildMessages(
        RolePlaySession session,
        RolePlayInteraction interaction,
        AdaptiveScenarioState scenarioState,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? excerptOverride,
        string? refineInstruction,
        IReadOnlyList<Character>? characters = null);

    /// <summary>
    /// Compose the system + user messages from a full-turn context (CR-006 P2). The turn's
    /// interactions (including the Narrative omniscient synthesis) contribute setting detail.
    /// </summary>
    (string SystemPrompt, string UserPrompt) BuildMessages(
        RolePlaySession session,
        FullTurnContext fullTurn,
        AdaptiveScenarioState scenarioState,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? excerptOverride,
        string? refineInstruction,
        IReadOnlyList<Character>? characters = null,
        SceneImageBeat? selectedBeat = null,
        string? pov = null);

    /// <summary>Parse the pre-processor output into the editable prompt (+ pulled excerpt).
    /// Tolerates a JSON envelope {{prompt, excerpt}} or plain text. Fails fast on empty/overlong.</summary>
    SceneImagePreprocessorResult ParseOutput(string rawOutput);
}