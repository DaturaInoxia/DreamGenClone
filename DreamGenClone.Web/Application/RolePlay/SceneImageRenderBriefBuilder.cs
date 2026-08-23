using System.Text;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public static class SceneImageRenderBriefBuilder
{
    public static string Build(
        SceneImageBeat beat,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy contentPolicy)
    {
        if (beat.SchemaVersion != SceneImageBeatAnalysisService.CurrentSchemaVersion)
            throw new InvalidOperationException("The selected beat uses an unsupported schema. Generate beats again.");
        if (string.IsNullOrWhiteSpace(pov))
            throw new InvalidOperationException("A POV is required to build the scene image render brief.");

        var visibleCharacters = ResolveVisibleCharacters(beat, pov);
        var remoteObservers = ResolveRemoteObservers(beat, pov);
        var framing = SceneImagePovFramer.BuildFramingLine(beat, pov, settings.OmniscientAngle);
        var brief = new StringBuilder();
        brief.AppendLine("AUTHORITATIVE RENDER BRIEF");
        brief.AppendLine("Create exactly one still image from this resolved visual event. Do not reinterpret the turn or invent omitted details.");
        brief.AppendLine($"Beat: {beat.Label}");
        brief.AppendLine($"Frozen moment: {beat.VisualDescription}");
        brief.AppendLine($"Primary location: {beat.Location}");
        brief.AppendLine($"Time of day: {beat.TimeOfDay}");
        brief.AppendLine($"Lighting: {beat.Lighting}");
        brief.AppendLine($"Environment: {beat.Environment}");
        brief.AppendLine($"Mood: {beat.Mood}");
        brief.AppendLine("VISIBLE CAST:");
        if (visibleCharacters.Count == 0)
            brief.AppendLine("- No people visible in frame.");
        foreach (var character in visibleCharacters)
        {
            brief.AppendLine($"- {character.Name} [{character.Involvement}]");
            brief.AppendLine($"  Position: {character.Position}");
            brief.AppendLine($"  Action or observation: {character.ActionOrObservation}");
            brief.AppendLine($"  Clothing: {character.Clothing}");
            brief.AppendLine($"  Sightline: {character.Sightline}");
        }
        if (remoteObservers.Count > 0)
        {
            brief.AppendLine("REMOTE OBSERVER CUES:");
            foreach (var observer in remoteObservers)
                brief.AppendLine($"- {BuildRemoteObserverCue(observer)}");
        }
        brief.AppendLine("SELECTED POV:");
        brief.AppendLine($"- Option: {pov}");
        brief.AppendLine($"- Camera and visibility: {framing}");
        brief.AppendLine("- Do not depict any character outside VISIBLE CAST.");
        brief.AppendLine("- Do not imply reciprocal awareness that the brief does not state.");
        brief.AppendLine("IMAGE SETTINGS:");
        brief.AppendLine($"- Style: {settings.Style}");
        brief.AppendLine($"- Size: {settings.ImageSize}");
        brief.AppendLine($"- Aspect ratio: {(string.IsNullOrWhiteSpace(settings.AspectRatio) ? "not specified" : settings.AspectRatio)}");
        brief.AppendLine($"- Content policy: {contentPolicy}");
        brief.AppendLine("Transform this complete brief into one direct, specific image-generation prompt. Preserve all authoritative facts.");
        return brief.ToString();
    }

    internal static IReadOnlyList<SceneImageBeatCharacter> ResolveVisibleCharacters(SceneImageBeat beat, string pov)
    {
        if (string.IsNullOrWhiteSpace(pov))
            throw new InvalidOperationException("A POV is required to resolve the scene image visible cast.");
        if (string.Equals(pov, SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase))
            return beat.Characters.Where(character => !IsRemoteObserver(beat, character)).ToList();

        var cameraHolder = beat.Characters.FirstOrDefault(character => string.Equals(character.Name, pov, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"POV character '{pov}' is not associated with beat '{beat.BeatId}'.");
        var visibleNames = cameraHolder.VisibleCharacterNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // The POV character IS visible in their own frame when they are a physical participant in the
        // scene (first-person body awareness: own hands, own limbs, own body). An external remote
        // observer (e.g. watching through a window) stays off-camera — they are not part of the act.
        if (!IsRemoteObserver(beat, cameraHolder))
        {
            visibleNames.Add(cameraHolder.Name);
        }

        var charactersByName = beat.Characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Name))
            .ToDictionary(character => character.Name, StringComparer.OrdinalIgnoreCase);
        var visibleCharacters = new List<SceneImageBeatCharacter>();
        foreach (var name in visibleNames)
        {
            if (!charactersByName.TryGetValue(name, out var visibleCharacter))
                throw new InvalidOperationException($"Beat '{beat.BeatId}' marks unknown character '{name}' as visible from POV '{pov}'.");
            visibleCharacters.Add(visibleCharacter);
        }

        return visibleCharacters;
    }

    internal static IReadOnlyList<SceneImageBeatCharacter> ResolveRemoteObservers(SceneImageBeat beat, string pov)
        => string.Equals(pov, SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase)
            ? beat.Characters.Where(character => IsRemoteObserver(beat, character)).ToList()
            : [];

    internal static string BuildRemoteObserverCue(SceneImageBeatCharacter observer)
        => $"An anonymous, indistinct, small, distant human silhouette at {observer.PhysicalLocation}, {observer.Position}; {observer.ActionOrObservation}. The silhouette is heavily occluded by the scene boundary and surrounding darkness.";

    private static bool IsRemoteObserver(SceneImageBeat beat, SceneImageBeatCharacter character)
        => string.Equals(character.Involvement, "observer", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(character.PhysicalLocation, beat.Location, StringComparison.OrdinalIgnoreCase);
}