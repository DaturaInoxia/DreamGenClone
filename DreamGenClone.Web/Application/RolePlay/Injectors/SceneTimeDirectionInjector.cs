using DreamGenClone.Domain.RolePlay;

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
        => true;

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
                    sb.AppendLine("- Stay in the current moment. Do not skip forward.");
                    sb.AppendLine("- Savor the moment with detailed sensory and emotional depth. One beat per response.");
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
                    sb.AppendLine("- Focus on one beat per response.");
                    sb.AppendLine("- Let time advance naturally to the next moment. Use organic transitions.");
                    break;
                case ScenePacing.Fast:
                    sb.AppendLine("- Compress multiple beats. Time must advance significantly — cover more story ground.");
                    sb.AppendLine("- Use clear transitions. Do not remain in the same time frame across consecutive responses.");
                    sb.AppendLine("- Do not skip through multiple separate times within a single response.");
                    break;
                default: // Medium
                    sb.AppendLine("- Cover one to two beats per response.");
                    sb.AppendLine("- Let time advance naturally — let transitions feel organic.");
                    break;
            }
        }

        return sb.ToString();
    }
}
