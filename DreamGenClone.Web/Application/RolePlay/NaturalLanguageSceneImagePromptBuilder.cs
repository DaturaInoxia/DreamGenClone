using System.Text;
using System.Text.Json;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Shared message assembly for the NATURAL-LANGUAGE scene-image prompt builders (SDXL-family and Qwen-Image-2.1).
/// Both produce natural-language, photorealistic image prompts from the same story-moment / canonical-brief inputs;
/// the ONLY thing that differs is the researched compiler instruction text each family feeds the pre-processor model,
/// supplied by the subclasses through <see cref="BuildSystemPrompt"/> and <see cref="BuildCanonicalSystemPrompt"/>.
///
/// <para>
/// This class exists so the SDXL and Qwen builders share one user-prompt assembly (story moment, scene context,
/// character appearance, image settings) without duplicating it, while each keeps its own dialect's system prompt.
/// It is deliberately NOT registered in DI: only the concrete family builders are, because two builders implement
/// <see cref="ISceneImageLLMPromptBuilder"/> and a single interface binding can only ever be right for one dialect.
/// </para>
/// </summary>
public abstract class NaturalLanguageSceneImagePromptBuilder : ISceneImageLLMPromptBuilder
{
    public const int InputExcerptMaxChars = 1200;
    public const int OutputPromptMaxChars = 2000;

    /// <summary>Kept as the builder's name for the shared target so no caller has to know where it lives (B-135).</summary>
    public const int OutputPromptTargetChars = SceneImageCompilerSystemPrompts.OutputTargetChars;

    /// <summary>
    /// The standing rules appended to every natural-language family's canonical system prompt.
    ///
    /// <para>
    /// They live here, not in each family's text, because they are facts about how the COMPILER is driven rather than
    /// about any checkpoint family — and because four copies of a rule is four chances for one of them to be lost in
    /// an edit. This rule states what the accompanying USER REMOVALS notice means; without it the notice is a claim
    /// the model has not been told to honour.
    /// </para>
    /// </summary>
    internal static readonly string CanonicalAuthorityRules = """

        USER REMOVALS ARE AUTHORITATIVE: when this request carries a USER REMOVALS notice, those elements were removed
        deliberately. Never render, describe, mention, infer or re-derive a removed element, and never substitute an
        equivalent taken from another element.
        """;

    /// <summary>The beat-path system prompt for this builder's family (story prose in).</summary>
    protected abstract string BuildSystemPrompt();

    /// <summary>The canonical-brief-path system prompt for this builder's family (immutable Still brief in).</summary>
    protected abstract string BuildCanonicalSystemPrompt();

    public (string SystemPrompt, string UserPrompt) BuildMessages(
        CompiledMediaBrief brief,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? refineInstruction)
        => BuildMessages(brief, pov, settings, resolvedPolicy, refineInstruction, null);

    public (string SystemPrompt, string UserPrompt) BuildMessages(
        CompiledMediaBrief brief,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? refineInstruction,
        IReadOnlyList<Character>? characters,
        IReadOnlyDictionary<string, string>? appearanceOverrides = null,
        IReadOnlyDictionary<string, string>? canonicalAppearance = null,
        ScenePromptOverrides? effectiveOverrides = null,
        IReadOnlyList<ReferenceApplicationSelection>? referenceBindings = null)
    {
        CompiledMediaContractValidator.ValidateBrief(brief);
        if (brief.MediaKind != MediaProductionKind.StillImage || brief.Status != MediaCompilerStatus.Complete)
            throw new InvalidOperationException("Canonical scene-image prompt generation requires a complete compiled Still brief.");
        if (string.IsNullOrWhiteSpace(pov))
            throw new InvalidOperationException("Canonical scene-image prompt generation requires the production group POV.");

        var systemPrompt = BuildCanonicalSystemPrompt() + CanonicalAuthorityRules;
        var userPrompt = BuildCanonicalUserPrompt(brief, pov, settings, resolvedPolicy, refineInstruction, characters, appearanceOverrides, canonicalAppearance, effectiveOverrides, referenceBindings);
        return (systemPrompt, userPrompt);
    }

    public (string SystemPrompt, string UserPrompt) BuildMessages(
        RolePlaySession session,
        RolePlayInteraction interaction,
        AdaptiveScenarioState scenarioState,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? excerptOverride,
        string? refineInstruction,
        IReadOnlyList<Character>? characters = null)
    {
        return (
            BuildSystemPrompt(),
            BuildUserPrompt(session, interaction, scenarioState, settings, resolvedPolicy, scenarioState.CurrentPhase, excerptOverride, refineInstruction, characters, null, null));
    }

    /// <summary>
    /// Full-turn variant: builds the prompt from the whole turn so the Narrative (omniscient)
    /// interaction contributes setting/environment detail. The selected interaction remains the
    /// primary subject; the turn's other interactions are appended as context.
    /// </summary>
    public (string SystemPrompt, string UserPrompt) BuildMessages(
        RolePlaySession session,
        FullTurnContext fullTurn,
        AdaptiveScenarioState scenarioState,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? excerptOverride,
        string? refineInstruction,
        IReadOnlyList<Character>? characters = null,
        SceneImageBeat? selectedBeat = null,
        string? pov = null)
    {
        var selected = fullTurn.SelectedInteraction;
        var baseUser = BuildUserPrompt(session, selected, scenarioState, settings, resolvedPolicy, scenarioState.CurrentPhase, excerptOverride, refineInstruction, characters, selectedBeat, pov);

        var turnContext = BuildFullTurnContextBlock(fullTurn, selected);
        if (!string.IsNullOrWhiteSpace(turnContext))
        {
            baseUser += "\n" + turnContext;
        }

        if (selectedBeat is not null)
        {
            var renderBrief = SceneImageRenderBriefBuilder.Build(
                selectedBeat,
                pov ?? throw new InvalidOperationException("A POV is required with a selected beat."),
                settings,
                resolvedPolicy);
            baseUser += "\n" + renderBrief;
        }

        return (BuildSystemPrompt(), baseUser);
    }

    public SceneImagePreprocessorResult ParseOutput(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            throw new InvalidOperationException("Scene image pre-processor returned empty output.");
        }

        var trimmed = rawOutput.Trim();
        string prompt;
        string excerpt = string.Empty;

        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("prompt", out var promptElement)
                    && promptElement.ValueKind == JsonValueKind.String)
                {
                    prompt = promptElement.GetString()?.Trim() ?? string.Empty;
                    if (doc.RootElement.TryGetProperty("excerpt", out var excerptElement)
                        && excerptElement.ValueKind == JsonValueKind.String)
                    {
                        excerpt = excerptElement.GetString()?.Trim() ?? string.Empty;
                    }
                }
                else
                {
                    prompt = trimmed;
                }
            }
            catch (JsonException)
            {
                prompt = trimmed;
            }
        }
        else
        {
            prompt = trimmed;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException("Scene image pre-processor returned a prompt that is empty after parsing.");
        }

        if (prompt.Length > OutputPromptMaxChars)
        {
            throw new InvalidOperationException(
                $"Scene image pre-processor returned an overlong prompt ({prompt.Length} chars); cap is {OutputPromptMaxChars}.");
        }

        return new SceneImagePreprocessorResult(prompt, excerpt);
    }

    private static string BuildCanonicalUserPrompt(
        CompiledMediaBrief brief,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy policy,
        string? refineInstruction,
        IReadOnlyList<Character>? characters,
        IReadOnlyDictionary<string, string>? appearanceOverrides,
        IReadOnlyDictionary<string, string>? canonicalAppearance = null,
        ScenePromptOverrides? effectiveOverrides = null,
        IReadOnlyList<ReferenceApplicationSelection>? referenceBindings = null)
    {
        var sb = new StringBuilder();
        // The brief is the SINGLE semantic source. The provider-request snapshot restates it and used to hand back
        // every element an override had removed, which is why it is not sent (debug 052); the DB record keeps it for
        // provenance. Only the brief reaches the pre-processor.
        sb.AppendLine("CANONICAL STILL BRIEF (the complete semantic source for this request):");
        sb.AppendLine(brief.SemanticInputSnapshotJson);
        var appearanceBlock = BuildCanonicalCharacterAppearanceBlock(brief, pov, characters, appearanceOverrides, canonicalAppearance);
        if (!string.IsNullOrWhiteSpace(appearanceBlock))
        {
            sb.AppendLine(appearanceBlock);
            sb.AppendLine();
        }
        sb.AppendLine($"PRODUCTION POV: {pov}");
        sb.AppendLine($"IMAGE SETTINGS: style={settings.Style}; size={settings.ImageSize}; aspect={settings.AspectRatio}; policy={policy}");
        if (!string.IsNullOrWhiteSpace(refineInstruction))
            sb.AppendLine($"REFINE INSTRUCTION: {refineInstruction.Trim()}");

        // Prompt adaptation is TWO halves and both have to reach the pre-processor. The removal notice stops it
        // re-deriving an element an override deliberately took out (the brief's neighbours often restate the same
        // instant). The role clause says what each reference IMAGE is, which is the only thing that binds a
        // description to one image rather than to another.
        var removalNotice = ScenePromptRemovalNotice.Build(effectiveOverrides ?? settings.PromptOverrides, characters);
        if (!string.IsNullOrWhiteSpace(removalNotice))
        {
            sb.AppendLine();
            sb.AppendLine(removalNotice);
        }

        var roleClause = ReferenceRoleClauses.ForPreprocessor(referenceBindings);
        if (!string.IsNullOrWhiteSpace(roleClause))
        {
            sb.AppendLine();
            sb.AppendLine(roleClause);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the AUTHORITATIVE FIXED IDENTITY appearance block for the canonical composition path.
    /// Delegates to <see cref="CanonicalCharacterAppearance.BuildBlock"/> (shared with the Pony
    /// builder) so per-character physical appearance reaches the pre-processor. The POV character
    /// is excluded for a named observer POV (rule 1: never in frame); an Omniscient POV includes
    /// every frozen character. Characters with no appearance data are omitted entirely.
    /// </summary>
    private static string BuildCanonicalCharacterAppearanceBlock(
        CompiledMediaBrief brief,
        string pov,
        IReadOnlyList<Character>? characters,
        IReadOnlyDictionary<string, string>? appearanceOverrides,
        IReadOnlyDictionary<string, string>? canonicalAppearance = null)
        => CanonicalCharacterAppearance.BuildBlock(brief, pov, characters, appearanceOverrides, canonicalAppearance);

    private static string BuildUserPrompt(
        RolePlaySession session,
        RolePlayInteraction interaction,
        AdaptiveScenarioState scenarioState,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        NarrativePhase phase,
        string? excerptOverride,
        string? refineInstruction,
        IReadOnlyList<Character>? characters,
        SceneImageBeat? selectedBeat,
        string? pov)
    {
        var sb = new StringBuilder();

        var moment = string.IsNullOrWhiteSpace(excerptOverride) ? interaction.Content : excerptOverride;
        if (moment.Length > InputExcerptMaxChars)
        {
            moment = moment[..InputExcerptMaxChars];
        }

        sb.AppendLine("STORY MOMENT:");
        if (string.IsNullOrWhiteSpace(moment) || moment.Trim().Length < 2)
        {
            sb.AppendLine("(The story moment text is empty. Depict the current scene using the context below.)");
            var setting = scenarioState.CurrentSceneLocation ?? "the current scene";
            var timeOfDay = scenarioState.CurrentTimeOfDay.ToString();
            sb.AppendLine($"- Setting: {setting}, {timeOfDay}");
            if (!string.IsNullOrWhiteSpace(interaction.ActorName))
            {
                sb.AppendLine($"- Character: {interaction.ActorName}");
            }
        }
        else
        {
            sb.AppendLine(moment);
        }
        sb.AppendLine();

        sb.AppendLine("SCENE CONTEXT:");
        sb.AppendLine($"- Actor: {interaction.ActorName}");
        sb.AppendLine($"- Setting: {scenarioState.CurrentSceneLocation ?? "unknown"}");
        sb.AppendLine($"- Time of day: {scenarioState.CurrentTimeOfDay}");
        sb.AppendLine($"- Narrative phase: {phase}");
        sb.AppendLine($"- Resolved intensity: {session.LastResolvedIntensityLabel ?? "unknown"}");

        var participants = SceneImageParticipantResolver.ResolveParticipants(session, interaction, scenarioState, characters);
        var presentNames = participants
            .Where(p => p.Presence != SceneImageParticipantResolver.Presence.Observer)
            .Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        if (presentNames.Count > 0)
        {
            sb.AppendLine($"- Characters present: {string.Join(", ", presentNames)}");
        }
        if (!string.IsNullOrWhiteSpace(session.PersonaDescription))
        {
            sb.AppendLine($"- Persona: {Truncate(session.PersonaDescription, 200)}");
        }

        if (interaction.WasInSexScene == true)
        {
            sb.AppendLine("- In-encounter: yes (explicit story beat)");
        }
        sb.AppendLine();

        var appearanceBlock = selectedBeat is null
            ? PonySceneImagePromptBuilder.BuildCharacterAppearanceBlock(session, interaction, scenarioState, characters)
            : PonySceneImagePromptBuilder.BuildBeatCharacterAppearanceBlock(session, selectedBeat, pov, characters);
        if (!string.IsNullOrWhiteSpace(appearanceBlock))
        {
            sb.AppendLine(appearanceBlock);
            sb.AppendLine();
        }

        if (selectedBeat is null)
        {
            var clothingBlock = PonySceneImagePromptBuilder.BuildCharacterClothingBlock(session, interaction, scenarioState, characters);
            if (!string.IsNullOrWhiteSpace(clothingBlock))
            {
                sb.AppendLine(clothingBlock);
                sb.AppendLine();
            }
        }

        sb.AppendLine("IMAGE SETTINGS:");
        sb.AppendLine($"- Style: {settings.Style}");
        sb.AppendLine($"- Size/Aspect: {settings.ImageSize}{(string.IsNullOrWhiteSpace(settings.AspectRatio) ? "" : $" / {settings.AspectRatio}")}");

        sb.AppendLine("- Explicitness: describe the content shown in the scene accurately, including explicit sexual acts or visible genitals when depicted.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(refineInstruction))
        {
            sb.AppendLine($"REFINE INSTRUCTION (apply to the existing prompt direction): {refineInstruction.Trim()}");
            sb.AppendLine("Adjust the prompt accordingly; keep the same subject matter unless instructed otherwise.");
        }

        return sb.ToString();
    }

    private static string BuildFullTurnContextBlock(FullTurnContext fullTurn, RolePlayInteraction selected)
    {
        var siblings = fullTurn.Interactions
            .Where(x => !string.Equals(x.Id, selected.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.CreatedAt)
            .Take(6)
            .ToList();

        if (siblings.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("FULL TURN CONTEXT (the other interactions from this same turn — use for setting, environment, and who is present):");
        foreach (var interaction in siblings)
        {
            var actor = string.IsNullOrWhiteSpace(interaction.ActorName) ? "Unknown" : interaction.ActorName;
            var content = Truncate(interaction.Content, 400);
            sb.AppendLine($"- [{actor}]: {content}");
        }
        return sb.ToString();
    }

    private static string Truncate(string value, int maxChars)
    {
        return value.Length <= maxChars ? value : value[..maxChars];
    }
}
