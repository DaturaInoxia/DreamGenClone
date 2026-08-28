using System.Text;
using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Builds and validates the structured model request that discovers depictable moments in a turn.</summary>
public sealed class SceneImageBeatAnalysisService
{
    public const int CurrentSchemaVersion = 3;
    public const int MaxBeats = 12;

    public (string SystemPrompt, string UserPrompt) BuildMessages(
        FullTurnContext fullTurn,
        RolePlaySession session,
        IReadOnlyList<Character>? characters)
    {
        var narrative = fullTurn.NarrativeInteraction
            ?? throw new InvalidOperationException("Scene image beat analysis requires the turn's Narrative synthesis interaction.");

        var system = new StringBuilder();
        system.AppendLine("You are a narrative-to-image scene analyst.");
        system.AppendLine("Analyze the complete story turn and identify 1 to 12 distinct visual moments that would make strong individual still images.");
        system.AppendLine("Treat every interaction as evidence about one shared timeline. The Narrative interaction is the authoritative synthesis for chronology, shared action, spatial relationships, and scene progression.");
        system.AppendLine("Use character interactions only to enrich the Narrative with concurrent actions, reactions, perceptions, knowledge, clothing, positions, and sightlines.");
        system.AppendLine("Never create a new beat merely because the source interaction, speaker, or viewpoint changes. Merge parallel accounts of the same moment into one beat.");
        system.AppendLine("First derive the chronological sequence of material state changes from the Narrative alone. Then attach supporting character evidence to those existing moments; do not derive an independent beat sequence from each character interaction.");
        system.AppendLine("Start a new beat only when the visually depictable state materially changes: action, physical arrangement, location, clothing state, lighting, or time.");
        system.AppendLine("A camera viewpoint, a character looking or waiting, or an observer's remote location is not by itself a material state change. Attach that observer, position, sightline, and visibility to the simultaneous event beat instead of emitting an observer-only establishing beat.");
        system.AppendLine("Classify a character as active only when they physically cause or undergo the material state change depicted by that beat. A character who only watches, waits, notices, or reacts without changing the visual state is an observer.");
        system.AppendLine("Every beat must include at least one active character. A beat whose characters are all observers — a lone figure in a separate location who is not physically causing or undergoing the state change — is invalid; never emit establishing, transition, or contrast shots with zero active characters. Attach such observers to the simultaneous active-event beat or omit them.");
        system.AppendLine("Before returning JSON, merge candidate beats that depict the same action at the same narrative time, even when different characters witness it from different locations.");
        system.AppendLine("Each beat has exactly one primary active-event location. Write that one physical space in beat.location. Every active character's physicalLocation must exactly equal beat.location. Remote observers retain their own different physicalLocation.");
        system.AppendLine("Beat.environment describes only the primary active-event location. Do not include remote observer rooms, buildings, exterior areas, or objects in environment.");
        system.AppendLine("Do not use a preset taxonomy and do not infer beats from keywords. Read the narrative meaning.");
        system.AppendLine("A beat may have additional active characters and zero or many observers. Include every character actively involved in or observing that moment. Every beat still requires at least one active character.");
        system.AppendLine("For each character, preserve physical position, depictable action or observation, and directional sightline. Visibility does not imply reciprocal awareness.");
        system.AppendLine("Use only details supported by the turn or the supplied character profiles. A named person without a profile is valid.");
        system.AppendLine("For clothing, use explicit turn evidence first; otherwise use supplied profile clothing; otherwise write 'not established'.");
        system.AppendLine("Do not invent garments, body positions, actions, locations, visibility, lighting, or awareness.");
        system.AppendLine("Write visualDescription as a complete multi-sentence visual brief for one frozen moment, including compositionally relevant spatial arrangement and visible action.");
        system.AppendLine("Every beat must cite the Narrative interaction id plus every character interaction that materially supports that beat.");
        system.AppendLine("Return strictly valid JSON: keep every string on one line, escape all quotes and control characters, and never place a literal newline or tab inside a JSON string value.");
        system.AppendLine("Return only JSON in this exact shape:");
        system.AppendLine("{\"beats\":[{\"schemaVersion\":3,\"beatId\":\"b1\",\"order\":1,\"label\":\"short title\",\"visualDescription\":\"complete multi-sentence visual brief\",\"interactionIds\":[\"narrative id\",\"supporting id\"],\"characters\":[{\"name\":\"name\",\"profileId\":null,\"involvement\":\"active or observer\",\"physicalLocation\":\"one physical space\",\"position\":\"position within physicalLocation\",\"actionOrObservation\":\"depictable action, reaction, or observation\",\"sightline\":\"view geometry or not applicable\",\"visibleCharacterNames\":[\"name\"],\"clothing\":\"supported clothing or not established\"}],\"location\":\"one primary active-event physical space\",\"timeOfDay\":\"time\",\"lighting\":\"visible lighting\",\"environment\":\"only the primary location's spatial and environmental context\",\"mood\":\"visually depictable atmosphere\"}]} ");

        var user = new StringBuilder();
        user.AppendLine("AUTHORITATIVE NARRATIVE SYNTHESIS:");
        user.AppendLine($"[{narrative.Id}] Narrative ({narrative.CreatedAt:O}):");
        user.AppendLine(narrative.Content);
        user.AppendLine("SUPPORTING CHARACTER INTERACTIONS:");
        foreach (var interaction in fullTurn.Interactions.Where(x => !string.Equals(x.Id, narrative.Id, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.CreatedAt))
        {
            user.AppendLine($"[{interaction.Id}] {interaction.ActorName} ({interaction.InteractionType}, {interaction.CreatedAt:O}):");
            user.AppendLine(interaction.Content);
        }
        user.AppendLine("KNOWN CHARACTER PROFILES:");
        foreach (var character in characters ?? [])
        {
            if (string.IsNullOrWhiteSpace(character.Name)) continue;
            var clothing = PhysicalAttributesFormatter.FormatVisualClothing(character.PhysicalAttributes);
            user.AppendLine($"- {character.Name} | profileId={character.Id} | clothing={(string.IsNullOrWhiteSpace(clothing) ? "not established" : clothing)} | description={character.Description ?? "not established"}");
        }
        if (!string.IsNullOrWhiteSpace(session.PersonaName))
        {
            var personaClothing = PhysicalAttributesFormatter.FormatVisualClothing(session.PersonaPhysicalAttributes);
            user.AppendLine($"- {session.PersonaName} | profileId=null | clothing={(string.IsNullOrWhiteSpace(personaClothing) ? "not established" : personaClothing)} | description={session.PersonaDescription ?? "not established"}");
        }
        return (system.ToString(), user.ToString());
    }

    public IReadOnlyList<SceneImageBeat> ParseOutput(string rawOutput, IReadOnlyList<RolePlayInteraction> turnInteractions)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            throw new InvalidOperationException("Scene image beat analysis returned empty output.");

        var start = rawOutput.IndexOf('{');
        var end = rawOutput.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("Scene image beat analysis did not return a JSON object.");

        var json = rawOutput[start..(end + 1)];
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("beats", out var beatsElement) || beatsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Scene image beat analysis response is missing the beats array.");

        var beats = new List<SceneImageBeat>();
        var validInteractionIds = turnInteractions.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var narrativeInteraction = turnInteractions.FirstOrDefault(x => string.Equals(x.ActorName, "Narrative", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Scene image beat analysis requires the turn's Narrative synthesis interaction.");
        foreach (var element in beatsElement.EnumerateArray())
        {
            var schemaVersion = RequiredInt(element, "schemaVersion");
            if (schemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException($"Scene image beat analysis returned unsupported schemaVersion {schemaVersion}; expected {CurrentSchemaVersion}.");
            var beatId = RequiredString(element, "beatId");
            var order = RequiredInt(element, "order");
            var label = RequiredString(element, "label");
            var visualDescription = RequiredString(element, "visualDescription");
            var location = RequiredString(element, "location");
            ValidateAtomicLocation(location, beatId);
            var timeOfDay = RequiredString(element, "timeOfDay");
            var lighting = RequiredString(element, "lighting");
            var environment = RequiredString(element, "environment");
            var mood = RequiredString(element, "mood");
            var interactionIds = RequiredStringArray(element, "interactionIds");
            if (interactionIds.Count == 0 || interactionIds.Any(id => !validInteractionIds.Contains(id)))
                throw new InvalidOperationException($"Scene image beat '{beatId}' references an interaction outside the analyzed turn.");
            if (!interactionIds.Contains(narrativeInteraction.Id, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Scene image beat '{beatId}' does not cite the authoritative Narrative interaction.");
            var characters = RequiredCharacters(element);
            if (characters.Count == 0)
                throw new InvalidOperationException($"Scene image beat '{beatId}' must include at least one character.");
            if (!characters.Any(x => string.Equals(x.Involvement, "active", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Scene image beat '{beatId}' must include at least one active character.");
            if (characters.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != characters.Count)
                throw new InvalidOperationException($"Scene image beat '{beatId}' contains duplicate characters.");
            var characterNames = characters.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (characters.SelectMany(x => x.VisibleCharacterNames).Any(name => !characterNames.Contains(name)))
                throw new InvalidOperationException($"Scene image beat '{beatId}' references a visible character who is not associated with the beat.");
            var activeOutsidePrimaryLocation = characters.FirstOrDefault(character =>
                string.Equals(character.Involvement, "active", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(character.PhysicalLocation, location, StringComparison.OrdinalIgnoreCase));
            if (activeOutsidePrimaryLocation is not null)
            {
                throw new InvalidOperationException(
                    $"Scene image beat '{beatId}' active character '{activeOutsidePrimaryLocation.Name}' has physicalLocation '{activeOutsidePrimaryLocation.PhysicalLocation}', which does not match primary location '{location}'.");
            }
            beats.Add(new SceneImageBeat
            {
                SchemaVersion = schemaVersion,
                BeatId = beatId,
                Order = order,
                Label = label,
                VisualDescription = visualDescription,
                InteractionIds = interactionIds,
                Characters = characters,
                SubjectCharacterNames = characters.Select(x => x.Name).ToList(),
                Location = location,
                TimeOfDay = timeOfDay,
                Lighting = lighting,
                Environment = environment,
                Mood = mood,
                Excerpt = visualDescription
            });
        }

        if (beats.Count is < 1 or > MaxBeats)
            throw new InvalidOperationException($"Scene image beat analysis must return between 1 and {MaxBeats} beats.");
        if (beats.Select(x => x.BeatId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != beats.Count)
            throw new InvalidOperationException("Scene image beat analysis returned duplicate beat ids.");
        return beats.OrderBy(x => x.Order).ToList();
    }

    private static void ValidateAtomicLocation(string location, string beatId)
    {
        string[] compoundSeparators = [" and ", " & ", " / ", ";", "|"];
        if (compoundSeparators.Any(separator => location.Contains(separator, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Scene image beat '{beatId}' primary location '{location}' combines multiple physical spaces; exactly one active-event location is required.");
        }
    }

    private static string RequiredString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!.Trim()
            : throw new InvalidOperationException($"Scene image beat analysis response is missing {name}.");

    private static int RequiredInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"Scene image beat analysis response has an invalid {name}.");

    private static IReadOnlyList<string> RequiredStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Scene image beat analysis response is missing {name}.");
        return property.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
    }

    private static IReadOnlyList<SceneImageBeatCharacter> RequiredCharacters(JsonElement element)
    {
        if (!element.TryGetProperty("characters", out var property) || property.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Scene image beat analysis response is missing characters.");
        return property.EnumerateArray().Select(character =>
        {
            var involvement = RequiredString(character, "involvement").ToLowerInvariant();
            if (involvement is not ("active" or "observer"))
                throw new InvalidOperationException("Scene image beat character involvement must be 'active' or 'observer'.");
            return new SceneImageBeatCharacter
            {
                Name = RequiredString(character, "name"),
                ProfileId = character.TryGetProperty("profileId", out var profile) && profile.ValueKind == JsonValueKind.String ? profile.GetString() : null,
                Involvement = involvement,
                PhysicalLocation = RequiredString(character, "physicalLocation"),
                Position = RequiredString(character, "position"),
                ActionOrObservation = RequiredString(character, "actionOrObservation"),
                Sightline = RequiredString(character, "sightline"),
                VisibleCharacterNames = RequiredStringArray(character, "visibleCharacterNames"),
                Clothing = RequiredString(character, "clothing")
            };
        }).ToList();
    }
}