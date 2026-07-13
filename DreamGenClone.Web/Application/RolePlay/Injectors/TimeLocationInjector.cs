using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects turn structure time/location directives. Engine-owned structural inject with
/// theme-controlled overrides via markers.
/// 
/// Position 1: Time Span Reminder — may establish/shift time and location.
/// Position > 1: Location Continuity — must maintain the anchor set by position 1.
/// </summary>
public sealed class TimeLocationInjector : IPromptInjector
{
    public string Id => "time-location";
    public int Priority => 10;

    public bool ShouldFire(PromptInjectionContext context)
        => context.Intent != PromptIntent.Narrative;

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
            // Position > 1: tri-state location continuity based on IsActorInScene.
            if (context.IsActorInScene == true)
            {
                // Actor is confirmed in-scene: HARD CONSTRAINT to maintain the setting.
                sb.AppendLine();
                sb.AppendLine("Location Continuity (HARD CONSTRAINT):");
                sb.AppendLine("- The scene is now at the time and location established by the first response this turn.");
                sb.AppendLine("- Maintain this physical setting. Do not silently relocate any character.");
                sb.AppendLine("- If a character moves, write the transition explicitly.");
            }
            else if (context.IsActorInScene == false)
            {
                // Actor is confirmed out-of-scene: soft directive to continue from own location.
                sb.AppendLine();
                sb.AppendLine("Location Continuity:");
                sb.AppendLine("- You are NOT at the scene established by the first response this turn. Your character is elsewhere.");
                sb.AppendLine("- Continue from your own location and perspective. Do not insert yourself into the scene just described.");
                sb.AppendLine("- Only reference what your character can perceive from where they are.");
                sb.AppendLine("- If your character later joins the scene, write the transition explicitly.");
            }
            else
            {
                // Unknown (location services off, scene location absent, or actor's truth state not tracked):
                // soft directive — restores pre-refactor behavior where the LLM has latitude.
                sb.AppendLine();
                sb.AppendLine("Location Continuity:");
                sb.AppendLine("- Continue from your character's perspective at their current location.");
                sb.AppendLine("- If your character is not at the scene just described, write them at their own location");
                sb.AppendLine("  and perspective. Do not assume they are present at the scene.");
            }

        }

        return sb.ToString();
    }
}
