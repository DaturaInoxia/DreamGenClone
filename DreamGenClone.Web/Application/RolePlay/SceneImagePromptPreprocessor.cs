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
            BuildUserPrompt(session, interaction, scenarioState, settings, resolvedPolicy, excerptOverride, refineInstruction, characters));
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
        IReadOnlyList<Character>? characters)
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

        var roles = (scenarioState.CharacterRoles ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            .Values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (roles.Count > 0)
        {
            sb.AppendLine($"- Characters present: {string.Join(", ", roles.Take(4))}");
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

        var appearanceBlock = BuildCharacterAppearanceBlock(session, interaction, scenarioState, characters);
        if (!string.IsNullOrWhiteSpace(appearanceBlock))
        {
            sb.AppendLine(appearanceBlock);
            sb.AppendLine();
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
    /// Builds the CHARACTER APPEARANCE (FIXED IDENTITY) block: one line per relevant character with
    /// their stable visual descriptors (hair, eyes, skin, body type, age, marks). The pre-processor
    /// is instructed to reproduce these verbatim so every image of a character shows the SAME person.
    ///
    /// Included characters: the interaction's actor (always, when resolvable), the persona (when it
    /// differs from the actor and has appearance data), and any other scenario character whose name
    /// appears in the story moment text (they are likely depicted in the frame).
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

        // 1. The actor (the subject of the moment).
        if (!string.IsNullOrWhiteSpace(interaction.ActorName)
            && charactersByName.TryGetValue(interaction.ActorName.Trim(), out var actorCharacter))
        {
            AddAppearanceEntry(entries, actorCharacter.Name ?? interaction.ActorName, actorCharacter.PhysicalAttributes, actorCharacter.Description);
        }

        // 2. The persona (only when it is a different person from the actor).
        var personaCharacter = ResolvePersonaCharacter(session, charactersById, charactersByName);
        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();
        var actorName = interaction.ActorName?.Trim();
        if (personaCharacter is not null || session.PersonaPhysicalAttributes is not null || !string.IsNullOrWhiteSpace(session.PersonaDescription))
        {
            if (string.IsNullOrEmpty(actorName)
                || !string.Equals(personaName, actorName, StringComparison.OrdinalIgnoreCase))
            {
                var appearance = personaCharacter?.PhysicalAttributes is not null
                    ? PhysicalAttributesFormatter.FormatVisualBlock(personaCharacter.PhysicalAttributes)
                    : PhysicalAttributesFormatter.FormatVisualBlock(session.PersonaPhysicalAttributes);
                if (string.IsNullOrWhiteSpace(appearance) && !string.IsNullOrWhiteSpace(session.PersonaDescription))
                {
                    appearance = "Description — " + Truncate(session.PersonaDescription, CharacterAppearanceDescriptionMaxChars);
                }
                if (!string.IsNullOrWhiteSpace(appearance))
                {
                    entries.Add((personaName, appearance));
                }
            }
        }

        // 3. Other characters whose name appears in the story moment (likely depicted).
        if (!string.IsNullOrWhiteSpace(interaction.Content))
        {
            foreach (var character in charactersByName.Values)
            {
                var name = character.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (string.Equals(name, actorName, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, personaName, StringComparison.OrdinalIgnoreCase)) continue;
                if (interaction.Content.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    AddAppearanceEntry(entries, name, character.PhysicalAttributes, character.Description);
                    if (entries.Count >= 5) break;
                }
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
}
