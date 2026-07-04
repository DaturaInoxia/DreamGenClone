using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects the Scene Presence Contract when the theme declares [ScenePresence] in its
/// phase guidance. Fires only when SceneDirection.RequireScenePresence is true.
/// Theme-controlled — opt-in via marker.
/// </summary>
public sealed class ScenePresenceInjector : IPromptInjector
{
    public string Id => "scene-presence";
    public int Priority => 75;

    public bool ShouldFire(PromptInjectionContext context)
        => context.SceneDirection.RequireScenePresence
        && context.Intent != PromptIntent.Instruction
        && context.Intent != PromptIntent.Narrative;

    public string BuildText(PromptInjectionContext context)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Scene Presence Contract:");
        sb.AppendLine("- Any intimate physical encounter — kissing, touching, caressing, or sexual activity — occurring in the current moment must be described in full in this response. Do not fade to black. Do not summarize what happened with a single sentence.");
        sb.AppendLine("- Do not write time-skip transitions that bypass an intimate scene in progress: e.g. 'the door closed behind her', 'an hour later', 'when it was over'. Stay present inside the encounter.");
        sb.AppendLine("- ONE RESPONSE = ONE SCENE MOMENT. Do not write the intimate encounter AND the return-to-public-space (e.g. returning to the husband, re-entering the room, the couple scene after) within the same response. Write through the encounter and stop. The return belongs in a subsequent turn.");
        sb.AppendLine("- The Resolved Intensity controls HOW explicitly you write the encounter (vocabulary, anatomical detail), not WHETHER you write it.");
        sb.AppendLine("- At lower intensity levels: use evocative, sensory, emotionally resonant language — describe physical contact, sensation, and reactions without graphic anatomy.");
        return sb.ToString();
    }
}
