using System.Text;
using System.Text.Json;
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
public sealed class SceneImagePromptPreprocessor : ISceneImagePromptPreprocessor
{
    public const int InputExcerptMaxChars = 1200;
    public const int OutputPromptMaxChars = 2000;
    public const int OutputPromptTargetChars = 800;
    public const int CharacterAppearanceDescriptionMaxChars = 240;

    /// <summary>Deterministic SFW clamp appended to prompts sent to SFW-filtered providers.</summary>
    public const string SfwClampSuffix = "keep fully clothed / non-explicit";

    /// <summary>Pony-style quality tags prepended to adult-allowed dense prompts.</summary>
    public const string PonyQualityTags = "score_9, score_8_up, score_7_up, rating_explicit";

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
            BuildSystemPrompt(resolvedPolicy),
            BuildUserPrompt(session, interaction, scenarioState, settings, resolvedPolicy, excerptOverride, refineInstruction, characters, null, null));
    }

    /// <summary>
    /// Full-turn variant (CR-006 P2): builds the prompt from the whole turn so the Narrative
    /// (omniscient) interaction contributes setting/environment detail. The selected interaction
    /// remains the primary subject; the turn's other interactions are appended as context.
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
        var baseUser = BuildUserPrompt(session, selected, scenarioState, settings, resolvedPolicy, excerptOverride, refineInstruction, characters, selectedBeat, pov);

        // Append the full-turn context (sibling interactions + Narrative synthesis) so the
        // pre-processor has the complete scene, not just the selected actor's slice.
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

        return (BuildSystemPrompt(resolvedPolicy), baseUser);
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

        // Tolerate a JSON envelope { prompt, excerpt }; otherwise treat the whole output as the prompt.
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

    public string BuildDeterministicBeatPrompt(
        RolePlaySession session,
        SceneImageBeat beat,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? refineInstruction,
        IReadOnlyList<Character>? characters = null)
    {
        if (beat.SchemaVersion != SceneImageBeatAnalysisService.CurrentSchemaVersion)
            throw new InvalidOperationException("The selected beat uses an unsupported schema. Generate beats again.");
        if (string.IsNullOrWhiteSpace(pov))
            throw new InvalidOperationException("A POV is required to build a deterministic scene image prompt.");

        var isOmniscient = string.Equals(pov, SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase);
        var visibleCharacters = SceneImageRenderBriefBuilder.ResolveVisibleCharacters(beat, pov);
        var visibleNameSet = visibleCharacters
            .Select(character => character.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var profilesByName = (characters ?? [])
            .Where(character => !string.IsNullOrWhiteSpace(character.Name))
            .ToDictionary(character => character.Name!.Trim(), StringComparer.OrdinalIgnoreCase);
        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();

        var excludedNames = beat.Characters
            .Select(character => character.Name)
            .Where(name => !visibleNameSet.Contains(name))
            .ToList();

        // ── Dense, Pony-tag-friendly prompt ────────────────────────────────────────────────
        // Pony/ComfyUI CLIP reads dense comma-separated tags, not caption prose. Lead with quality
        // + explicitness tokens, then fold identity, wardrobe, pose/action, location, lighting,
        // mood, and POV framing into one dense line.
        var tags = new List<string>();

        var isExplicit = resolvedPolicy == ImageContentPolicy.AdultAllowed
            || resolvedPolicy == ImageContentPolicy.AdultAllowedConfigurable;
        if (isExplicit)
        {
            tags.Add(PonyQualityTags);
        }

        // Style + size/aspect + omniscient angle are INJECTED placeholders, not baked values.
        // They are substituted at render time with the current studio setting so changing options
        // does not require regenerating the prompt.
        tags.Add("{{style}}");
        tags.Add("{{size}}");

        // Primary scene: frozen beat + location + environment.
        // Only Omniscient narrates the complete event (incl. any remote observer). A participant POV
        // must NOT receive the full scene description — that leaks in characters/observers outside
        // the frame and muddles the act. Participant details come from labeled spatial facts below.
        if (isOmniscient)
        {
            AddTag(tags, beat.VisualDescription, excludedNames, cameraHolderName: null);
        }
        AddTag(tags, beat.Location, [], null);
        AddTag(tags, beat.Environment, [], null);
        AddTag(tags, beat.TimeOfDay, [], null);
        AddTag(tags, beat.Lighting, [], null);
        AddTag(tags, beat.Mood, [], null);

        // Visible cast: identity + wardrobe (forced nudity for explicit beats).
        foreach (var beatCharacter in visibleCharacters)
        {
            var identity = ResolveCanonicalIdentity(session, personaName, beatCharacter.Name, profilesByName);
            if (!string.Equals(identity, "identity not established", StringComparison.OrdinalIgnoreCase))
                tags.Add($"{beatCharacter.Name}: {identity}");

            var clothing = ResolveDeterministicClothing(session, personaName, beatCharacter.Name, beatCharacter.Clothing, profilesByName, isExplicit);
            if (!string.IsNullOrWhiteSpace(clothing))
                tags.Add($"{beatCharacter.Name}: {(isExplicit && EqualsNudity(clothing) ? "naked" : clothing)}");
        }

        // Spatial facts: labeled pose / action / position per visible character (projected for POV).
        foreach (var beatCharacter in visibleCharacters)
        {
            var spatialFacts = new List<string>();
            AddProjectedFact(spatialFacts, beatCharacter.Position, excludedNames, isOmniscient ? null : pov);
            AddProjectedFact(spatialFacts, beatCharacter.ActionOrObservation, excludedNames, isOmniscient ? null : pov);
            if (spatialFacts.Count == 0) continue;
            tags.Add($"{beatCharacter.Name}: {string.Join(", ", spatialFacts)}");
        }

        // Explicit anatomical detail: describe the act concretely from the beat description + the
        // POV character's own-body awareness.
        tags.Add(isOmniscient
            ? DescribeAct(beat, null)
            : DescribeAct(beat, pov));

        // POV framing line.
        // For Omniscient, the camera angle is an injected placeholder ({{angle}}) substituted at
        // render time with the current studio selection; participant POVs bake their framing.
        if (string.Equals(pov, SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("External third-person camera (fly-on-the-wall). {{angle}}");
        }
        else
        {
            tags.Add(SceneImagePovFramer.BuildFramingLine(beat, pov));
        }

        if (!string.IsNullOrWhiteSpace(refineInstruction))
            tags.Add(refineInstruction.Trim());

        var prompt = string.Join(", ", tags.Where(t => !string.IsNullOrWhiteSpace(t)));
        return prompt.Trim();
    }

    /// <summary>
    /// Builds the deterministic negative prompt for a frozen beat + POV. Suppresses common
    /// image-model artifacts plus any character that must not appear in the frame.
    /// </summary>
    public string BuildDeterministicBeatNegativePrompt(SceneImageBeat beat, string pov)
    {
        var artifacts = "extra or missing body parts, extra limbs, extra arms, extra legs, extra hands, extra fingers, malformed anatomy, merged bodies, duplicate or extra people, wrong number of people, cropped anatomy, bad hands, wrong hand ownership, text, watermark, frame, border, signature, logo";

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

    private static void AddTag(List<string> tags, string? value, IReadOnlyList<string> excludedNames, string? cameraHolderName)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var projected = ProjectFact(value, excludedNames, cameraHolderName);
        if (!string.IsNullOrWhiteSpace(projected))
            tags.Add(projected.Trim());
    }

    private static bool EqualsNudity(string clothing)
        => clothing.Contains("naked", StringComparison.OrdinalIgnoreCase)
           || clothing.Contains("nude", StringComparison.OrdinalIgnoreCase);

    private static string ResolveDeterministicClothing(
        RolePlaySession session,
        string personaName,
        string name,
        string beatClothing,
        IReadOnlyDictionary<string, Character> profilesByName,
        bool isExplicit)
    {
        // For explicit/adult beats on a comfy-style backend, nudity is the expected state unless
        // the beat explicitly states clothing. Prefer "naked" so wardrobe never wrongly reintroduces
        // casual clothing from a profile during an explicit scene.
        if (isExplicit)
            return "naked";

        var profileClothing = ResolveProfileClothing(session, personaName, name, profilesByName);
        return !string.IsNullOrWhiteSpace(beatClothing)
            && !string.Equals(beatClothing, "not established", StringComparison.OrdinalIgnoreCase)
                ? beatClothing.Trim()
                : profileClothing;
    }

    /// <summary>
    /// Produces an explicit, anatomy-literal clause for the act. The beat's natural language usually
    /// euphemizes ("head of his cock remains inside her"); Pony/ComfyUI needs concrete anatomical
    /// action. For a participant POV, the act is framed from that character's own eyes (their body is
    /// visible). Omniscient gets a full external statement.
    /// </summary>
    private static string DescribeAct(SceneImageBeat beat, string? pov)
    {
        var povClause = string.IsNullOrWhiteSpace(pov)
            ? "external view of the couple"
            : $"{pov}'s first-person view, {pov}'s own body in frame";

        var marker = BuildExplicitPenetrationClause(beat);
        if (!string.IsNullOrWhiteSpace(marker))
            return $"{marker}, {povClause}";

        // No explicit penetration could be derived — fall back to the beat's visual description so
        // the scene's action is still conveyed (without leaking remote observers). 
        var description = beat.VisualDescription.Trim();
        return string.IsNullOrWhiteSpace(description)
            ? string.Empty
            : $"{description}, {povClause}";
    }

    /// <summary>
    /// Builds a short, concrete penetration/anatomy phrase from the beat layout. Returns empty when
    /// the beat does not clearly imply sex, so the act clause is simply omitted.
    /// </summary>
    private static string BuildExplicitPenetrationClause(SceneImageBeat beat)
    {
        // Identify the male participant (kneeling/mounted) vs the female participant (on her back
        // or all fours). The beat usually has both nude with matching locations.
        SceneImageBeatCharacter? man = null;
        SceneImageBeatCharacter? woman = null;
        foreach (var character in beat.Characters)
        {
            if (IsManBehind(character))
                man = man is null ? character : man;
            else if (IsWomanReceiving(character))
                woman = woman is null ? character : woman;
        }
        if (man is not null && woman is not null)
        {
            return $"{man.Name}'s erect cock penetrating {woman.Name}, penis visible entering her vagina, spread ass and anus visible, realistic genital anatomy";
        }
        if (man is not null)
        {
            return $"{man.Name}'s erect cock penetrating her, penis visible entering vagina, realistic genital anatomy";
        }

        var desc = beat.VisualDescription.ToLowerInvariant();
        if (desc.Contains("cock", StringComparison.Ordinal) || desc.Contains("penetrat", StringComparison.Ordinal)
            || desc.Contains("enter", StringComparison.Ordinal) || desc.Contains("inside her", StringComparison.Ordinal))
        {
            return "erect penis penetrating her vagina, penis visible inside her, realistic genital anatomy";
        }
        return string.Empty;
    }

    private static bool IsWomanReceiving(SceneImageBeatCharacter c)
    {
        if (string.IsNullOrWhiteSpace(c.Name)) return false;
        var text = $"{c.Clothing} {c.Position} {c.ActionOrObservation}".ToLowerInvariant();
        // Receiving poses: on her back / all fours / arching / spread.
        return IsNaked(text)
            && (text.Contains("all fours", StringComparison.Ordinal)
                || text.Contains("on her back", StringComparison.Ordinal)
                || text.Contains("back arched", StringComparison.Ordinal)
                || text.Contains("on the bed", StringComparison.Ordinal)
                || text.Contains("spread", StringComparison.Ordinal));
    }

    private static bool IsManBehind(SceneImageBeatCharacter c)
    {
        if (string.IsNullOrWhiteSpace(c.Name)) return false;
        var text = $"{c.Clothing} {c.Position} {c.ActionOrObservation}".ToLowerInvariant();
        // Mounting/driving poses: kneeling/mounted/behind, gripping, thrusting, penetrating, entering.
        return text.Contains("naked", StringComparison.Ordinal)
            && (text.Contains("behind", StringComparison.Ordinal)
                || text.Contains("kneel", StringComparison.Ordinal)
                || text.Contains("on top", StringComparison.Ordinal)
                || text.Contains("gripp", StringComparison.Ordinal)
                || text.Contains("thrust", StringComparison.Ordinal)
                || text.Contains("penetrat", StringComparison.Ordinal)
                || text.Contains("enter", StringComparison.Ordinal)
                || text.Contains("pushing into", StringComparison.Ordinal));
    }

    private static bool IsNaked(string value)
        => value.Contains("naked", StringComparison.OrdinalIgnoreCase)
           || value.Contains("nude", StringComparison.OrdinalIgnoreCase);

    private static void AddProjectedFact(
        List<string> facts,
        string? value,
        IReadOnlyList<string> excludedNames,
        string? cameraHolderName)
    {
        var projected = ProjectFact(value, excludedNames, cameraHolderName);
        if (!string.IsNullOrWhiteSpace(projected))
            facts.Add(projected);
    }

    private static string? ProjectFact(
        string? value,
        IReadOnlyList<string> excludedNames,
        string? cameraHolderName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var projected = string.IsNullOrWhiteSpace(cameraHolderName)
            ? value.Trim()
            : value.Trim().Replace(cameraHolderName, "the unseen viewpoint", StringComparison.OrdinalIgnoreCase);
        var remainingExcludedNames = excludedNames
            .Where(name => !string.Equals(name, cameraHolderName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return ReferencesExcludedCharacter(projected, remainingExcludedNames) ? null : projected;
    }

    private static bool ReferencesExcludedCharacter(string? value, IReadOnlyList<string> excludedNames)
        => !string.IsNullOrWhiteSpace(value)
            && excludedNames.Any(name => value.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static string ResolveCanonicalIdentity(
        RolePlaySession session,
        string personaName,
        string name,
        IReadOnlyDictionary<string, Character> profilesByName)
    {
        if (profilesByName.TryGetValue(name, out var character))
        {
            var identity = PhysicalAttributesFormatter.FormatVisualBlock(character.PhysicalAttributes);
            if (!string.IsNullOrWhiteSpace(identity)) return identity;
            if (!string.IsNullOrWhiteSpace(character.Description))
                return "Description — " + Truncate(character.Description, CharacterAppearanceDescriptionMaxChars);
        }
        else if (string.Equals(name, personaName, StringComparison.OrdinalIgnoreCase))
        {
            var identity = PhysicalAttributesFormatter.FormatVisualBlock(session.PersonaPhysicalAttributes);
            if (!string.IsNullOrWhiteSpace(identity)) return identity;
            if (!string.IsNullOrWhiteSpace(session.PersonaDescription))
                return "Description — " + Truncate(session.PersonaDescription, CharacterAppearanceDescriptionMaxChars);
        }

        return "identity not established";
    }

    private static string ResolveProfileClothing(
        RolePlaySession session,
        string personaName,
        string name,
        IReadOnlyDictionary<string, Character> profilesByName)
    {
        if (profilesByName.TryGetValue(name, out var character))
            return PhysicalAttributesFormatter.FormatVisualClothing(character.PhysicalAttributes);
        return string.Equals(name, personaName, StringComparison.OrdinalIgnoreCase)
            ? PhysicalAttributesFormatter.FormatVisualClothing(session.PersonaPhysicalAttributes)
            : string.Empty;
    }

    private static string BuildSystemPrompt(ImageContentPolicy policy)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert image prompt engineer for a narrative roleplay app.");
        sb.AppendLine("You will be given a story moment, scene context, characters, and image settings.");
        sb.AppendLine("Produce ONE dense, image-model-ready prompt that captures the single most depictable beat of the moment.");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Pick the exact part of the moment to depict: the salient beat, pose, framing, and expression.");
        sb.AppendLine("- Merge setting, time of day, mood, characters, and intensity into vivid visual language.");
        sb.AppendLine("- Honor the requested style, size, and aspect.");
        sb.AppendLine($@"- Keep the prompt under {OutputPromptTargetChars} characters, dense and comma-separated.");
        sb.AppendLine("- CHARACTER LIKENESS: the scene context lists each character's fixed visual identity (hair, eyes, skin, body type, age, marks). Reproduce those EXACT descriptors in the image prompt for every depicted character — do not invent or change hair color, eye color, body type, or age. The same character must look like the same person in every image.");
        sb.AppendLine("- Return ONLY the final image prompt as plain text. Do not add commentary, quotes, or markdown around it.");

        if (policy == ImageContentPolicy.SfwFiltered)
        {
            sb.AppendLine("- CONTENT POLICY: the image provider filters adult content. Keep the image safe-for-work: fully clothed, non-explicit, no nudity, no sexual content.");
            sb.AppendLine($@"- Always end the prompt with the phrase ""{SfwClampSuffix}"".");
        }
        else
        {
            sb.AppendLine("- The provider allows adult content. Follow the user's explicitness setting exactly.");
        }

        return sb.ToString();
    }

    private static string BuildUserPrompt(
        RolePlaySession session,
        RolePlayInteraction interaction,
        AdaptiveScenarioState scenarioState,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
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

        // Edge case: interaction content is empty/too short to depict. Fall back to the scene
        // context so the pre-processor still has depictable material rather than a blank prompt.
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
        sb.AppendLine($"- Narrative phase: {scenarioState.CurrentPhase}");
        sb.AppendLine($"- Resolved intensity: {session.LastResolvedIntensityLabel ?? "unknown"}");

        // Characters present: resolved via the authoritative presence model (CR-006 P1) so the
        // line shows real character names, not role labels.
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
            ? BuildCharacterAppearanceBlock(session, interaction, scenarioState, characters)
            : BuildBeatCharacterAppearanceBlock(session, selectedBeat, pov, characters);
        if (!string.IsNullOrWhiteSpace(appearanceBlock))
        {
            sb.AppendLine(appearanceBlock);
            sb.AppendLine();
        }

        if (selectedBeat is null)
        {
            var clothingBlock = BuildCharacterClothingBlock(session, interaction, scenarioState, characters);
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

    internal static string BuildBeatCharacterAppearanceBlock(
        RolePlaySession session,
        SceneImageBeat beat,
        string? pov,
        IReadOnlyList<Character>? characters)
    {
        var depictedNames = SceneImageRenderBriefBuilder
            .ResolveVisibleCharacters(beat, pov ?? throw new InvalidOperationException("A POV is required to build beat character appearance."))
            .Select(character => character.Name);
        var orderedNames = depictedNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (orderedNames.Count == 0)
            return string.Empty;

        var charactersByName = (characters ?? [])
            .Where(character => !string.IsNullOrWhiteSpace(character.Name))
            .ToDictionary(character => character.Name!.Trim(), StringComparer.OrdinalIgnoreCase);
        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();
        var entries = new List<(string Name, string Appearance)>();
        foreach (var name in orderedNames)
        {
            string? appearance = null;
            if (charactersByName.TryGetValue(name, out var character))
            {
                appearance = PhysicalAttributesFormatter.FormatVisualBlock(character.PhysicalAttributes);
                if (string.IsNullOrWhiteSpace(appearance) && !string.IsNullOrWhiteSpace(character.Description))
                    appearance = "Description — " + Truncate(character.Description, CharacterAppearanceDescriptionMaxChars);
            }
            else if (string.Equals(name, personaName, StringComparison.OrdinalIgnoreCase))
            {
                appearance = PhysicalAttributesFormatter.FormatVisualBlock(session.PersonaPhysicalAttributes);
                if (string.IsNullOrWhiteSpace(appearance) && !string.IsNullOrWhiteSpace(session.PersonaDescription))
                    appearance = "Description — " + Truncate(session.PersonaDescription, CharacterAppearanceDescriptionMaxChars);
            }

            entries.Add((name, string.IsNullOrWhiteSpace(appearance) ? "not established" : appearance));
        }

        var block = new StringBuilder();
        block.AppendLine("DEPICTED CHARACTER APPEARANCE (AUTHORITATIVE FIXED IDENTITY — include every line below in the image prompt):");
        foreach (var (name, appearance) in entries)
            block.AppendLine($"- {name}: {appearance}");
        return block.ToString();
    }

    /// <summary>
    /// Builds the CHARACTER APPEARANCE (FIXED IDENTITY) block: one line per relevant character with
    /// their stable visual descriptors (hair, eyes, skin, body type, age, marks). The pre-processor
    /// is instructed to reproduce these verbatim so every image of a character shows the SAME person.
    ///
    /// Included characters are resolved by <see cref="SceneImageParticipantResolver"/> (CR-006 P1):
    /// the interaction's actor (always), characters present at the scene location, characters
    /// actively in the encounter, and characters named in the story moment text. The persona is only
    /// included when it actually participates — it is no longer always injected.
    ///
    /// Appearance source: structured <see cref="PhysicalAttributes"/> via
    /// <see cref="PhysicalAttributesFormatter.FormatVisualBlock"/> (visual-only — no measurements or
    /// intimate fields). Falls back to the character's free-text Description only when no structured
    /// attributes exist. Characters with no appearance data are omitted entirely.
    /// </summary>
    internal static string BuildCharacterAppearanceBlock(
        RolePlaySession session,
        RolePlayInteraction interaction,
        AdaptiveScenarioState scenarioState,
        IReadOnlyList<Character>? characters)
    {
        var charactersById = (characters ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var charactersByName = (characters ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .ToDictionary(c => c.Name!.Trim(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<(string Name, string Appearance)>();

        // Resolve participants via the authoritative presence model (CR-006 P1).
        var participants = SceneImageParticipantResolver.ResolveParticipants(session, interaction, scenarioState, characters);

        foreach (var participant in participants)
        {
            if (entries.Count >= 5) break;

            // Resolve the appearance for this participant (scenario character or persona).
            string? appearance = null;
            if (charactersByName.TryGetValue(participant.Name, out var character))
            {
                appearance = PhysicalAttributesFormatter.FormatVisualBlock(character.PhysicalAttributes);
                if (string.IsNullOrWhiteSpace(appearance) && !string.IsNullOrWhiteSpace(character.Description))
                {
                    appearance = "Description — " + Truncate(character.Description, CharacterAppearanceDescriptionMaxChars);
                }
            }
            else if (string.Equals(participant.Name, personaName(session), StringComparison.OrdinalIgnoreCase))
            {
                var personaCharacter = ResolvePersonaCharacter(session, charactersById, charactersByName);
                appearance = personaCharacter?.PhysicalAttributes is not null
                    ? PhysicalAttributesFormatter.FormatVisualBlock(personaCharacter.PhysicalAttributes)
                    : PhysicalAttributesFormatter.FormatVisualBlock(session.PersonaPhysicalAttributes);
                if (string.IsNullOrWhiteSpace(appearance) && !string.IsNullOrWhiteSpace(session.PersonaDescription))
                {
                    appearance = "Description — " + Truncate(session.PersonaDescription, CharacterAppearanceDescriptionMaxChars);
                }
            }

            if (!string.IsNullOrWhiteSpace(appearance))
            {
                entries.Add((participant.Name, appearance));
            }
        }

        if (entries.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("CHARACTER APPEARANCE (FIXED IDENTITY — reproduce these exact visual descriptors in the image prompt so the same character looks the same in every image):");
        foreach (var (name, appearance) in entries)
        {
            sb.AppendLine($"- {name}: {appearance}");
        }
        return sb.ToString();
    }

    private static string personaName(RolePlaySession session)
        => string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();

    /// <summary>
    /// Builds the CHARACTER CLOTHING block: one line per participant with their consistent clothing.
    /// Clothing source priority (CR-006 clothing consistency):
    ///   1. Turn data — clothing described in the story moment text (e.g. "she was wearing a red dress")
    ///   2. Character profile — <c>PhysicalAttributes.ClothingStyle</c>
    ///   3. Default clothing — <c>PhysicalAttributes.DefaultClothing</c>
    /// This ensures the same character wears the same clothes across images unless the turn
    /// explicitly describes otherwise.
    /// </summary>
    internal static string BuildCharacterClothingBlock(
        RolePlaySession session,
        RolePlayInteraction interaction,
        AdaptiveScenarioState scenarioState,
        IReadOnlyList<Character>? characters)
    {
        var charactersById = (characters ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var charactersByName = (characters ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .ToDictionary(c => c.Name!.Trim(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<(string Name, string Clothing)>();

        // Resolve participants via the authoritative presence model (CR-006 P1).
        var participants = SceneImageParticipantResolver.ResolveParticipants(session, interaction, scenarioState, characters);

        foreach (var participant in participants)
        {
            if (entries.Count >= 5) break;

            // 1. Turn data — clothing mentioned in the story moment text.
            var turnClothing = ExtractClothingFromText(interaction.Content, participant.Name);
            if (!string.IsNullOrWhiteSpace(turnClothing))
            {
                entries.Add((participant.Name, turnClothing));
                continue;
            }

            // 2/3. Character profile ClothingStyle → DefaultClothing.
            string? clothing = null;
            if (charactersByName.TryGetValue(participant.Name, out var character))
            {
                clothing = PhysicalAttributesFormatter.FormatVisualClothing(character.PhysicalAttributes);
            }
            else if (string.Equals(participant.Name, personaName(session), StringComparison.OrdinalIgnoreCase))
            {
                var personaCharacter = ResolvePersonaCharacter(session, charactersById, charactersByName);
                clothing = personaCharacter?.PhysicalAttributes is not null
                    ? PhysicalAttributesFormatter.FormatVisualClothing(personaCharacter.PhysicalAttributes)
                    : PhysicalAttributesFormatter.FormatVisualClothing(session.PersonaPhysicalAttributes);
            }

            if (!string.IsNullOrWhiteSpace(clothing))
            {
                entries.Add((participant.Name, clothing));
            }
        }

        if (entries.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("CHARACTER CLOTHING (consistent outfit — reproduce these exact clothing descriptors in the image prompt so the same character wears the same clothes in every image):");
        foreach (var (name, clothing) in entries)
        {
            sb.AppendLine($"- {name}: {clothing}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Attempts to extract clothing for a character from the story moment text. Looks for a
    /// clothing phrase near the character's name (e.g. "Becky was wearing a red dress"). Returns
    /// null when no clothing is described for that character.
    /// </summary>
    private static string? ExtractClothingFromText(string? content, string characterName)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(characterName))
        {
            return null;
        }

        var lower = content.ToLowerInvariant();
        var nameLower = characterName.ToLowerInvariant();

        // Look for clothing phrases: "wearing", "dressed in", "in a", "clad in", "outfit".
        var clothingMarkers = new[] { "wearing", "dressed in", "clad in", "outfit", "in a" };
        foreach (var marker in clothingMarkers)
        {
            var markerIndex = lower.IndexOf(marker);
            if (markerIndex < 0) continue;

            // The character must be mentioned near the clothing phrase (within ~60 chars before).
            var windowStart = Math.Max(0, markerIndex - 60);
            var window = lower.Substring(windowStart, markerIndex - windowStart);
            if (!window.Contains(nameLower)) continue;

            // Extract up to ~80 chars after the marker as the clothing description.
            var start = markerIndex + marker.Length;
            var end = Math.Min(content.Length, start + 80);
            var clothing = content.Substring(start, end - start).Trim();
            // Cut at a sentence/punctuation boundary.
            var cut = clothing.IndexOfAny(new[] { '.', ';', ',', '!' });
            if (cut > 0)
            {
                clothing = clothing[..cut];
            }
            clothing = clothing.Trim();
            if (clothing.Length > 2)
            {
                return clothing;
            }
        }

        return null;
    }

    private static Character? ResolvePersonaCharacter(
        RolePlaySession session,
        IReadOnlyDictionary<string, Character> charactersById,
        IReadOnlyDictionary<string, Character> charactersByName)
    {
        // Prefer an explicit persona character id, then the IsPersona flag, then the persona name.
        if (!string.IsNullOrWhiteSpace(session.PersonaCharacterId)
            && charactersById.TryGetValue(session.PersonaCharacterId, out var byId))
        {
            return byId;
        }

        var byFlag = charactersById.Values.FirstOrDefault(c => c.IsPersona);
        if (byFlag is not null) return byFlag;

        if (!string.IsNullOrWhiteSpace(session.PersonaName)
            && charactersByName.TryGetValue(session.PersonaName.Trim(), out var byName))
        {
            return byName;
        }

        return null;
    }

    private static void AddAppearanceEntry(
        List<(string Name, string Appearance)> entries,
        string name,
        DreamGenClone.Domain.Templates.PhysicalAttributes? attributes,
        string? description)
    {
        var appearance = PhysicalAttributesFormatter.FormatVisualBlock(attributes);
        if (string.IsNullOrWhiteSpace(appearance) && !string.IsNullOrWhiteSpace(description))
        {
            appearance = "Description — " + Truncate(description, CharacterAppearanceDescriptionMaxChars);
        }
        if (string.IsNullOrWhiteSpace(appearance)) return;
        if (entries.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))) return;
        entries.Add((name, appearance));
    }

    private static string Truncate(string value, int maxChars)
    {
        return value.Length <= maxChars ? value : value[..maxChars];
    }

    /// <summary>
    /// Builds the FULL TURN CONTEXT block (CR-006 P2): the sibling interactions of the selected
    /// interaction's turn, so the pre-processor sees the complete scene. The Narrative (omniscient)
    /// interaction is highlighted as the richest setting source. The selected interaction itself is
    /// excluded (it is already the STORY MOMENT).
    /// </summary>
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
}
