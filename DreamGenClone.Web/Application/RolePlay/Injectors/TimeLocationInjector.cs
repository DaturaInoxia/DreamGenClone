using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects turn structure time/location directives. Engine-owned structural inject with
/// theme-controlled overrides via markers.
/// 
/// Position 1: Time Span Reminder — may establish/shift time and location.
/// Position > 1: Location Continuity HC — must maintain the anchor set by position 1.
/// Position > 1 + [Pacing:fast] or [TimeShift:*] markers: modified time-shift permission.
/// </summary>
public sealed class TimeLocationInjector : IPromptInjector
{
    public string Id => "time-location";
    public int Priority => 10;

    public bool ShouldFire(PromptInjectionContext context)
        => true;

    public string BuildText(PromptInjectionContext context)
    {
        var sb = new System.Text.StringBuilder();
        var isPosition1 = context.PositionInTurn is null or 1;

        if (isPosition1)
        {
            // Position 1: Time Span Reminder — may establish/shift time and location.
            sb.AppendLine();
            sb.AppendLine("Time Span Reminder:");
            sb.AppendLine("- You are the first response this turn. You may establish or shift the time and location for this turn.");
            sb.AppendLine("- Scenes may skip forward in time; a new response does not have to be the immediate continuation of the last moment.");
        }
        else
        {
            // Position > 1: Location Continuity HC — enhanced with anchor.
            sb.AppendLine();
            sb.AppendLine("Location Continuity (HARD CONSTRAINT):");
            sb.AppendLine("- The scene is now at the time and location established by the first response this turn.");
            sb.AppendLine("- Maintain this physical setting. Do not silently relocate any character.");
            sb.AppendLine("- If a character moves, write the transition explicitly.");

            // Position > 1 + marker overrides: modified time-shift permission.
            bool hasOverride = context.SceneDirection.Pacing == ScenePacing.Fast
                || context.SceneDirection.TimeShift != TimeShiftPolicy.None;
            if (hasOverride)
            {
                sb.AppendLine();
                sb.AppendLine("Time Shift Permission:");
                sb.AppendLine("- You may also shift time or location, following the pacing and time shift rules.");
            }
        }

        return sb.ToString();
    }
}
