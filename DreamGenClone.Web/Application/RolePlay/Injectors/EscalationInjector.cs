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
        => context.Intent != PromptIntent.Instruction
        && context.Intent != PromptIntent.Narrative;

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
                    sb.AppendLine("- Cover exactly one beat this response — richly detailed, deeply explored.");
                    sb.AppendLine("- Fill the response with sensory, emotional, and physical detail specific to this beat.");
                    sb.AppendLine("- Advance to a new beat next response. Do not repeat or re-describe the same beat.");
                    break;
                case ScenePacing.Fast:
                    sb.AppendLine("- This is a fast-paced encounter. Move through the full arc rapidly — initiation, act, climax, conclusion — within this and the next response.");
                    sb.AppendLine("- Do not linger on individual beats. Cover the essential actions efficiently and keep moving forward.");
                    sb.AppendLine("- Prioritize forward momentum over detailed description. This is meant to be brief and urgent.");
                    break;
                default: // Medium
                    sb.AppendLine("- Advance the scene with forward momentum within the current scene.");
                    sb.AppendLine("- Cover one to two beats this response. Each response should advance to new beats — do not repeat previous beats.");
                    break;
            }
        }

        return sb.ToString();
    }
}
