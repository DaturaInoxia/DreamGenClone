using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects escalation guidance varying by SceneDirection.Pacing. If Deepening=SubsequentActors
/// and position > 1, emits deepening-from-POV guidance instead.
/// Theme-controlled.
/// </summary>
public sealed class EscalationInjector : IPromptInjector
{
    public string Id => "escalation";
    public int Priority => 60;

    public bool ShouldFire(PromptInjectionContext context)
        => context.Intent != PromptIntent.Instruction;

    public string BuildText(PromptInjectionContext context)
    {
        var sb = new System.Text.StringBuilder();
        var isPosition2Plus = context.PositionInTurn.HasValue && context.PositionInTurn.Value > 1;
        var shouldDeepen = context.SceneDirection.Deepening == DeepeningPolicy.SubsequentActors
            && isPosition2Plus;

        if (shouldDeepen)
        {
            sb.AppendLine();
            sb.AppendLine("Scene Deepening (Subsequent Actor):");
            sb.AppendLine("- Deepen the current scene beat from your character's POV only.");
            sb.AppendLine("- Do NOT advance to a new beat or position.");
            sb.AppendLine("- Explore internal reactions, sensory details, and emotional responses to this moment.");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("Escalation Guidance:");
            switch (context.SceneDirection.Pacing)
            {
                case ScenePacing.Slow:
                    sb.AppendLine("- Advance within the same beat — deepen, do not leap.");
                    sb.AppendLine("- Fill the response with sensory, emotional, and physical detail specific to this moment.");
                    sb.AppendLine("- Do not describe a new beat or position.");
                    break;
                case ScenePacing.Fast:
                    sb.AppendLine("- This is a fast-paced scene. Cover more story ground per response — advance through the full arc of this moment.");
                    sb.AppendLine("- Compress multiple beats into each response. Do not write only one beat when multiple beats fit naturally.");
                    sb.AppendLine("- If an encounter reaches its natural conclusion (orgasm, resolution, or scene end), advance to a new time or setting afterwards.");
                    break;
                default: // Medium
                    sb.AppendLine("- Advance the scene with forward momentum.");
                    sb.AppendLine("- Cover one to two beats this response. Avoid repeating only hesitant or reset beats.");
                    break;
            }
        }

        return sb.ToString();
    }
}
