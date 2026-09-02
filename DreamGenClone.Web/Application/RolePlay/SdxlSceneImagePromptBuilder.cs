using System.Text;
using System.Text.Json;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// SDXL / Juggernaut scene-image prompt builder. Produces NATURAL-LANGUAGE, photorealistic image
/// prompts for SDXL-family checkpoints (SDXL base 1.0, Juggernaut XL, RealVisXL, ...). Fully
/// separate from <see cref="PonySceneImagePromptBuilder"/>: no Pony tag vocabulary (no score_*,
/// no rating_*, no count tags, no CLIP-skip conventions). Explicitness is driven by the narrative
/// phase (theme intensity) expressed in prose, exactly like the Pony rating mapping.
/// </summary>
public sealed class SdxlSceneImagePromptBuilder : ISdxlSceneImagePromptBuilder
{
    public const int InputExcerptMaxChars = 1200;
    public const int OutputPromptMaxChars = 2000;
    public const int OutputPromptTargetChars = 800;

    private const int CharacterAppearanceDescriptionMaxChars = 240;

    private static readonly JsonSerializerOptions FrozenStateJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Deterministic SFW clamp appended to prompts sent to SFW-filtered providers.</summary>
    public const string DefaultSfwClampSuffix = "fully clothed, wholesome, non-explicit";

    /// <summary>
    /// Default SDXL/Juggernaut negative guard set. Exposed as the studio's editable negative
    /// default and reused by the deterministic beat negative builder.
    /// </summary>
    public const string DefaultNegativePrompt =
        "deformed, bad anatomy, extra limbs, extra legs, four legs, fused legs, extra fingers, extra arms, " +
        "missing limbs, malformed hands, malformed feet, misplaced genitals, penis on arm, penis on hand, " +
        "detached penis, extra penis, penis from mouth, mouth to mouth, wrong attachment, double mouth, " +
        "blurry genitals, featureless genitals, censored, cartoon, anime, illustration, painting, sketch, " +
        "watermark, text, low quality, oversaturated, plastic skin";

    /// <inheritdoc/>
    public string SfwClampSuffix => DefaultSfwClampSuffix;

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
        IReadOnlyList<Character>? characters)
    {
        CompiledMediaContractValidator.ValidateBrief(brief);
        if (brief.MediaKind != MediaProductionKind.StillImage || brief.Status != MediaCompilerStatus.Complete)
            throw new InvalidOperationException("Canonical scene-image prompt generation requires a complete compiled Still brief.");
        if (string.IsNullOrWhiteSpace(pov))
            throw new InvalidOperationException("Canonical scene-image prompt generation requires the production group POV.");

        var systemPrompt = BuildCanonicalSystemPrompt(resolvedPolicy);
        var userPrompt = BuildCanonicalUserPrompt(brief, pov, settings, resolvedPolicy, refineInstruction, characters);
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
            BuildSystemPrompt(resolvedPolicy, scenarioState.CurrentPhase),
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

        return (BuildSystemPrompt(resolvedPolicy, scenarioState.CurrentPhase), baseUser);
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

    /// <inheritdoc/>
    public string BuildDeterministicBeatNegativePrompt(SceneImageBeat beat, string pov)
    {
        // SDXL-family models need a heavier guard set than Pony: limb/leg artifacts, censored or
        // featureless genitals, and non-photoreal styles. Validated on pod 2026-08-23.
        var artifacts = DefaultNegativePrompt;

        var excludedNames = SceneImageRenderBriefBuilder.ResolveVisibleCharacters(beat, pov)
            .Select(character => character.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var absent = beat.Characters
            .Select(character => character.Name)
            .Where(name => !excludedNames.Contains(name))
            .ToList();

        var negative = new List<string> { artifacts };
        foreach (var name in absent)
        {
            negative.Add(name);
            negative.Add($"{name} absent from frame");
        }
        return string.Join(", ", negative).Trim();
    }

    /// <summary>
    /// SDXL expert system prompt: natural-language photography brief, no tag vocabulary, with the
    /// phase-driven explicitness level expressed in prose (the SDXL analogue of the Pony rating tag).
    /// </summary>
    private static string BuildSystemPrompt(ImageContentPolicy policy, NarrativePhase phase)
    {
        var explicitnessProse = ResolveExplicitnessProse(phase, policy);
        var sb = new StringBuilder();
        sb.AppendLine("Convert story prose into a short, NATURAL-LANGUAGE image prompt for an SDXL-based photorealistic model (SDXL base 1.0, Juggernaut XL, Big Lust) — not comma-tag soup, not danbooru tags, not attribute metadata blocks.");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Write 2-4 short natural sentences or phrases that describe the scene like a photography brief. The result must look realistic and photographic.");
        sb.AppendLine("- Describe each character's appearance (hair, eyes, body type, age) so the same character is recognizable every time. State each person's gender explicitly (e.g. 'a middle-aged man and a middle-aged woman') so the model never merges or miscounts people.");
        sb.AppendLine("- Fold in location, time of day, lighting, and mood in plain words. Do not repeat the same fact twice.");
        sb.AppendLine("- Include photographic style cues such as photorealistic, 35mm, natural skin texture, sharp focus.");
        sb.AppendLine("- Honor beat-stated clothing exactly; only use nudity when the beat implies it AND the explicitness level allows it.");
        sb.AppendLine("- Never use Pony-style vocabulary: no score_9, no rating_explicit/questionable/safe, no 1girl/1boy/2people count tags, no danbooru tokens.");
        sb.AppendLine($@"- Explicitness level: {explicitnessProse}. Match it exactly; never exceed it. For explicit levels use concrete anatomical language (penis, vagina, correct anatomy); for safe/questionable levels imply rather than spell out.");
        sb.AppendLine($@"- Keep the ENTIRE prompt under {OutputPromptTargetChars} characters. Short and concrete beats verbose.");
        sb.AppendLine("- Return ONLY the final image prompt as plain text. No commentary, quotes, or markdown.");

        if (policy == ImageContentPolicy.SfwFiltered)
        {
            sb.AppendLine("- CONTENT POLICY: the image provider filters adult content. Keep the image safe-for-work: fully clothed, non-explicit, no nudity, no sexual content.");
            sb.AppendLine($@"- Always end the prompt with the phrase ""{DefaultSfwClampSuffix}"".");
        }
        else
        {
            sb.AppendLine("- The provider allows adult content. Follow the explicitness level exactly; do not add explicitness beyond it.");
        }

        return sb.ToString();
    }

    private static string BuildCanonicalSystemPrompt(ImageContentPolicy policy)
    {
        // Canonical composition-path prompt (B-104 / B-103 part B). No identity fluff: the model's
        // behavior comes from (a) facts about the target SDXL-family checkpoint, (b) explicit
        // rules, and (c) a concrete example showing the target shape. Research + rationale in
        // .github/instructions/scene-image-prompt-compiler-standards.instructions.md and the
        // B-103 failure analysis (Becky dropped from a wide, distant, shadowed shot).
        var sb = new StringBuilder();
        sb.AppendLine("Convert the canonical Still brief below into ONE photographic text-to-image prompt for an SDXL-family photorealistic checkpoint (SDXL 1.0 / Juggernaut XL / Big Lust).");
        sb.AppendLine("Facts about these models that shape the prompt:");
        sb.AppendLine("- They read natural-language photographic descriptions and map only what is visually renderable: visible people, clothing, hair, pose, location, objects, lighting, camera.");
        sb.AppendLine("- They do not know character names, relationships, or ownership (\"Dean\", \"Becky\", \"Ken's shirt\" carry no visual meaning). Describe people by appearance only.");
        sb.AppendLine("- They cannot render faces at a distance; a far or shadowed subject is dropped — never rely on a distant figure the model will drop. Keep every required person near, clearly lit, and in focus.");
        sb.AppendLine("- They are trained on NSFW data; describing each person's clothing anchors the output and prevents accidental nudity.");
        sb.AppendLine("- They merge or miscount people, and confuse which attribute belongs to whom (hair, clothing), unless gender and number are stated and each person is described as one self-contained clause.");
        sb.AppendLine("Rules:");
        sb.AppendLine("1. POV FRAMING: Render the scene strictly from the PRODUCTION POV character's viewpoint — show only what that character sees and NEVER include the POV character in the frame. If the POV is Omniscient, show the full scene with all characters visible.");
        sb.AppendLine("2. NO NAMES / RELATIONS / OWNERSHIP: appearance only (build, hair style/color, skin tone, clothing type+color, visible pose).");
        sb.AppendLine("3. RENDERABLE-ONLY: include only the frozen, visually present instant. Omit narrative distance, intent, metaphor, and off-screen facts.");
        sb.AppendLine("4. GENDER AND COUNT, NO ATTRIBUTE BLEED: state each person's gender and number, and describe each person as ONE self-contained clause (appearance + clothing together) — never interleave attributes across people.");
        sb.AppendLine("5. CLOTHING IS A SAFETY ANCHOR: always describe each person's clothing unless the brief explicitly implies nudity.");
        sb.AppendLine("6. TIGHT PHOTO CAPTION: lead with framing and the main subject in the first sentence; keep the ENTIRE caption under 800 characters; use photorealistic cues (35mm, natural skin texture, shallow depth of field).");
        sb.AppendLine("Examples — target shape only, do not reuse their content:");
        sb.AppendLine("- General caption shape (SDXL/Juggernaut prompting guides): \"Young woman reading a book in a cozy coffee shop, brunette hair cascading over her shoulders, style candid photography, mood relaxed, lighting natural light streaming through a nearby window, perspective over-the-shoulder, texture soft wool sweater and glossy wooden table.\"");
        sb.AppendLine("- POV-scene shape (project reference, follows the same anatomy): \"a photorealistic view from twenty feet away across the grass at night: a woman with dark hair in a loose bun stands at the wooden deck railing of a silver trailer, wearing an unbuttoned pale-blue camp shirt, bare-legged, one hand resting beside a glass on the rail, her face turned toward the dark pines. She is lit only by thin strips of blue television light leaking through warped blinds. 35mm, shallow depth of field, natural skin texture.\" — the POV character is never in frame and people are described by appearance only.");
        sb.AppendLine("Never emit Pony vocabulary, score tags, rating tags, danbooru tokens, or count tags.");
        sb.AppendLine("Return ONLY the final image prompt as plain text. No commentary, quotes, or markdown.");
        if (policy == ImageContentPolicy.SfwFiltered)
            sb.AppendLine($"- CONTENT POLICY: keep every person fully clothed and the result non-explicit; end verbatim with: {DefaultSfwClampSuffix}");
        return sb.ToString();
    }

    private static string BuildCanonicalUserPrompt(
        CompiledMediaBrief brief,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy policy,
        string? refineInstruction,
        IReadOnlyList<Character>? characters)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CANONICAL STILL BRIEF (immutable; this is the complete semantic source):");
        sb.AppendLine(brief.SemanticInputSnapshotJson);
        sb.AppendLine("CANONICAL PROVIDER REQUEST SNAPSHOT (immutable):");
        sb.AppendLine(brief.ProviderRequestSnapshotJson);
        var appearanceBlock = BuildCanonicalCharacterAppearanceBlock(brief, pov, characters);
        if (!string.IsNullOrWhiteSpace(appearanceBlock))
        {
            sb.AppendLine(appearanceBlock);
            sb.AppendLine();
        }
        sb.AppendLine($"PRODUCTION POV: {pov}");
        sb.AppendLine($"IMAGE SETTINGS: style={settings.Style}; size={settings.ImageSize}; aspect={settings.AspectRatio}; policy={policy}");
        if (!string.IsNullOrWhiteSpace(refineInstruction))
            sb.AppendLine($"REFINE INSTRUCTION: {refineInstruction.Trim()}");
        return sb.ToString();
    }

    /// <summary>
    /// Builds the AUTHORITATIVE FIXED IDENTITY appearance block for the canonical composition path.
    /// Maps the compiled brief's frozen-state characters (keyed by CharacterId, then Name) to their
    /// scenario <see cref="Character.PhysicalAttributes"/> and formats each via
    /// <see cref="PhysicalAttributesFormatter.FormatVisualBlock"/> (visual-only — no measurements or
    /// intimate fields). The POV character is excluded for a named observer POV (rule 1: never in
    /// frame); an Omniscient POV includes every frozen character. Characters with no appearance data
    /// are omitted entirely. This closes the B-100 design gap where the canonical compiler was told
    /// to "describe by appearance" but the brief carried no appearance to describe ("one woman").
    /// </summary>
    private static string BuildCanonicalCharacterAppearanceBlock(
        CompiledMediaBrief brief,
        string pov,
        IReadOnlyList<Character>? characters)
    {
        if (characters is null || characters.Count == 0)
            return string.Empty;

        var frozen = ReadFrozenCharacters(brief);
        if (frozen.Count == 0)
            return string.Empty;

        var isOmniscient = string.Equals(pov, SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase);
        var depicted = frozen
            .Where(character => isOmniscient
                || (!string.Equals(character.CharacterId, pov, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(character.Name, pov, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (depicted.Count == 0)
            return string.Empty;

        var charactersById = characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Id))
            .ToDictionary(character => character.Id!, StringComparer.OrdinalIgnoreCase);
        var charactersByName = characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Name))
            .GroupBy(character => character.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("DEPICTED CHARACTER APPEARANCE (AUTHORITATIVE FIXED IDENTITY — describe each person by these traits, never by name or relationship):");
        var emittedAny = false;
        foreach (var frozenCharacter in depicted)
        {
            var character = ResolveCharacter(frozenCharacter, charactersById, charactersByName);
            var appearance = character is null
                ? string.Empty
                : PhysicalAttributesFormatter.FormatVisualBlock(character.PhysicalAttributes);
            if (string.IsNullOrWhiteSpace(appearance) && character is not null && !string.IsNullOrWhiteSpace(character.Description))
                appearance = "Description — " + Truncate(character.Description, CharacterAppearanceDescriptionMaxChars);
            if (string.IsNullOrWhiteSpace(appearance))
                continue;

            var label = string.IsNullOrWhiteSpace(frozenCharacter.Name) ? frozenCharacter.CharacterId : frozenCharacter.Name;
            sb.AppendLine($"- {label}: {appearance}");
            emittedAny = true;
        }

        return emittedAny ? sb.ToString() : string.Empty;
    }

    private static Character? ResolveCharacter(
        FrozenCharacterRef frozen,
        IReadOnlyDictionary<string, Character> charactersById,
        IReadOnlyDictionary<string, Character> charactersByName)
    {
        if (!string.IsNullOrWhiteSpace(frozen.CharacterId)
            && charactersById.TryGetValue(frozen.CharacterId, out var byId))
            return byId;
        if (!string.IsNullOrWhiteSpace(frozen.Name)
            && charactersByName.TryGetValue(frozen.Name.Trim(), out var byName))
            return byName;
        return null;
    }

    private static IReadOnlyList<FrozenCharacterRef> ReadFrozenCharacters(CompiledMediaBrief brief)
    {
        try
        {
            using var document = JsonDocument.Parse(brief.SemanticInputSnapshotJson);
            if (!document.RootElement.TryGetProperty("frozenState", out var frozenState)
                || !frozenState.TryGetProperty("characters", out var characters)
                || characters.ValueKind != JsonValueKind.Array)
                return [];
            return characters.Deserialize<List<FrozenCharacterRef>>(FrozenStateJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record FrozenCharacterRef(string? CharacterId, string? Name);

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
        sb.AppendLine($"- Explicitness level: {ResolveExplicitnessProse(phase, resolvedPolicy)}");

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

        var explicitAllowed = resolvedPolicy != ImageContentPolicy.SfwFiltered && settings.AllowExplicitImage;
        sb.AppendLine($"- Explicitness: {(explicitAllowed ? "explicit content allowed" : "non-explicit / implied only")}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(refineInstruction))
        {
            sb.AppendLine($"REFINE INSTRUCTION (apply to the existing prompt direction): {refineInstruction.Trim()}");
            sb.AppendLine("Adjust the prompt accordingly; keep the same subject matter unless instructed otherwise.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Resolves the explicitness level from the narrative phase (theme intensity), the SDXL prose
    /// analogue of the Pony rating mapping: BuildUp → safe, Committed/Approaching → questionable,
    /// Climax → explicit, Reset → questionable, anything else (Opening) → safe. A SFW-filtered
    /// provider policy is a hard clamp to safe regardless of phase.
    /// </summary>
    private static string ResolveExplicitnessProse(NarrativePhase phase, ImageContentPolicy policy)
    {
        if (policy == ImageContentPolicy.SfwFiltered)
        {
            return "safe: fully clothed, wholesome, non-explicit";
        }
        return phase switch
        {
            NarrativePhase.Climax => "explicit: nude bodies, explicit sexual activity, correct genital anatomy",
            NarrativePhase.Committed or NarrativePhase.Approaching or NarrativePhase.Reset => "questionable: partially undressed, suggestive, implied intimacy",
            _ => "safe: fully clothed, wholesome"
        };
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
