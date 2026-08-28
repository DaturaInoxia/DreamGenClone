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
    /// Builds the framing line for the given POV. The POV encodes *from whose eyes* the scene is
    /// seen. For a participant (Dean/Becky), that character's body is naturally visible in their own
    /// frame (own hands, own legs, own cock) — first-person body awareness. For Omniscient, it is an
    /// external "fly-on-the-wall" camera whose angle can be freely chosen.
    /// </summary>
    public static string BuildFramingLine(SceneImageBeat beat, string pov, string? omniscientAngle = null)
    {
        if (string.IsNullOrWhiteSpace(pov))
            throw new InvalidOperationException("A POV is required for scene image framing.");

        if (string.Equals(pov, Omniscient, StringComparison.OrdinalIgnoreCase))
        {
            var angle = string.IsNullOrWhiteSpace(omniscientAngle)
                ? "frame the complete visible event in one coherent composition"
                : omniscientAngle.Trim();
            return $"External third-person camera (fly-on-the-wall). {angle}.";
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

        // A physical participant (same location as the act) sees their own body in frame; an
        // external remote observer (watching from elsewhere, e.g. through a window) does not.
        var inAct = string.Equals(character.PhysicalLocation, beat.Location, StringComparison.OrdinalIgnoreCase);
        var bodyClause = inAct
            ? $" {pov}'s own body is visible in the frame (own hands/limbs/body as natural in this vantage)."
            : $" {pov}'s own body is not visible; the viewpoint is off-camera.";
        return $"First-person camera at {pov}'s eye position, looking {character.Sightline} from {character.Position} in {character.PhysicalLocation}.{bodyClause} {range} view of {visibleSubjects}.";
    }
}