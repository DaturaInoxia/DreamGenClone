using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Generates the POV framing line for a scene image (CR-006 P5). POV is modeled as a *framing
/// dimension*, not narrative perspective: identity anchors + beat description stay constant, and
/// only the framing/composition line varies per POV. Image models respond to concrete camera/framing
/// language, not abstract "first-person POV".
/// </summary>
public static class SceneImagePovFramer
{
    /// <summary>The omniscient/wide POV key.</summary>
    public const string Omniscient = "Omniscient";

    /// <summary>
    /// Returns the available POV options for a beat: <see cref="Omniscient"/> plus each participant
    /// present in the beat (per CR-006 P3/P5 — POVs are derived from who is in the beat).
    /// </summary>
    public static IReadOnlyList<string> GetPovOptions(IReadOnlyList<string> subjectCharacterNames)
    {
        var options = new List<string> { Omniscient };
        foreach (var name in subjectCharacterNames)
        {
            if (!string.IsNullOrWhiteSpace(name)
                && !options.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                options.Add(name);
            }
        }
        return options;
    }

    /// <summary>
    /// Builds the framing line for the given POV. Returns null when the POV is not recognized
    /// (caller should fall back to omniscient).
    /// </summary>
    public static string BuildFramingLine(SceneImageBeat beat, string pov)
    {
        if (string.IsNullOrWhiteSpace(pov))
            throw new InvalidOperationException("A POV is required for scene image framing.");

        if (string.Equals(pov, Omniscient, StringComparison.OrdinalIgnoreCase))
        {
            return "External third-person camera. Frame the complete visible event in one coherent composition.";
        }

        var character = beat.Characters.FirstOrDefault(x => string.Equals(x.Name, pov, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"POV character '{pov}' is not associated with beat '{beat.BeatId}'.");
        var visibleCount = character.VisibleCharacterNames.Count;
        var visibleSubjects = visibleCount == 0
            ? "an empty scene with no visible people"
            : $"exactly {visibleCount} visible {(visibleCount == 1 ? "person" : "people")}: {string.Join(", ", character.VisibleCharacterNames)}";
        var range = string.Equals(character.PhysicalLocation, beat.Location, StringComparison.OrdinalIgnoreCase)
            ? "Close-range"
            : "Distant";
        return $"Strict first-person camera at the viewpoint character's eye position in {character.PhysicalLocation}, with the camera origin at {character.Position}, looking {character.Sightline}. {range} view. Frame {visibleSubjects}. The viewpoint remains fully off-camera.";
    }
}