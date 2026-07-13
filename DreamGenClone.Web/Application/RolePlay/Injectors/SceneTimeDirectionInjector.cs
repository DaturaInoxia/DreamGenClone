using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Merged Scene Time Direction (replaces old ScenePacingContract + PacingDirective).
/// Text varies by SceneDirection.Pacing and SceneDirection.TimeShift (6-case table).
/// Theme-controlled.
/// </summary>
public sealed class SceneTimeDirectionInjector : IPromptInjector
{
    public string Id => "scene-time-direction";
    public int Priority => 70;

    public bool ShouldFire(PromptInjectionContext context)
        => context.Intent != PromptIntent.Narrative;

    public string BuildText(PromptInjectionContext context)
    {
        var sb = new System.Text.StringBuilder();
        var pacing = context.SceneDirection.Pacing;
        var timeShift = context.SceneDirection.TimeShift;
        var hasTimeShift = timeShift != TimeShiftPolicy.None;

        sb.AppendLine();
        sb.AppendLine("Scene Time Direction:");

        if (!hasTimeShift)
        {
            switch (pacing)
            {
                case ScenePacing.Slow:
                    sb.AppendLine("- Cover one beat per response with detailed sensory and emotional depth.");
                    sb.AppendLine("- Advance to a new beat each response. Do not repeat or linger on a previous beat.");
                    break;
                case ScenePacing.Fast:
                    sb.AppendLine("- Compress multiple beats into one response. Cover more story ground per response.");
                    sb.AppendLine("- No time shift — all beats occur within the current timeframe.");
                    break;
                default: // Medium
                    sb.AppendLine("- Let the scene breathe without dragging.");
                    sb.AppendLine("- Cover one to two beats per response. No time shift — continue from the current moment.");
                    break;
            }
        }
        else
        {
            switch (pacing)
            {
                case ScenePacing.Slow:
                    sb.AppendLine("- Cover one beat per response, richly expanded with sensory and emotional depth.");
                    sb.AppendLine("- Move to a new beat each response. Let the scene unfold naturally but keep advancing.");
                    break;
                case ScenePacing.Fast:
                    sb.AppendLine("- Cover the full arc rapidly: initiate, escalate, conclude, react — all within this response and the next.");
                    sb.AppendLine("- Do not linger on any single beat. Compress the action into efficient, urgent prose.");
                    sb.AppendLine("- Do not jump to a different time or setting. Stay within the current scene but move through it quickly.");
                    break;
                default: // Medium
                    sb.AppendLine("- Cover one to two beats this response — dialogue, actions, reactions. Advance to new beats each response.");
                    sb.AppendLine("- Let transitions feel organic within the scene. Do not leap to a different time or setting.");
                    break;
            }
        }

        return sb.ToString();
    }
}
