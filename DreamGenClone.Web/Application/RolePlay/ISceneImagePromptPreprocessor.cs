using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Builds and parses the pre-processor LLM call that turns an interaction + scene context + image
/// settings into an editable image prompt. The pre-processor is a text model (separate function
/// from the image renderer).
/// </summary>
public interface ISceneImagePromptPreprocessor
{
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

    /// <summary>Parse the pre-processor output into the editable prompt (+ pulled excerpt).
    /// Tolerates a JSON envelope {{prompt, excerpt}} or plain text. Fails fast on empty/overlong.</summary>
    SceneImagePreprocessorResult ParseOutput(string rawOutput);
}

/// <summary>Parsed pre-processor output.</summary>
public sealed record SceneImagePreprocessorResult(string Prompt, string Excerpt);
