using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects the final writing directive based on the prompt intent.
/// Engine-owned structural inject — always fires.
/// Also reinforces pacing directive at end-of-prompt for maximum authority.
/// </summary>
public sealed class FinalDirectiveInjector : IPromptInjector
{
    public string Id => "final-directive";
    public int Priority => 100;

    public bool ShouldFire(PromptInjectionContext context)
        => true;

    public string BuildText(PromptInjectionContext context)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();

        switch (context.Intent)
        {
            case PromptIntent.Message:
                sb.AppendLine("Continue from your character's perspective.");
                break;
            case PromptIntent.Narrative:
                sb.AppendLine("- Write an omniscient account: setting, character positions, sensations, atmosphere.");
                sb.AppendLine("- Synthesize character perspectives into a rich, unified picture.");
                sb.AppendLine("- Do NOT advance the scene beyond what the characters established this turn.");
                break;
            case PromptIntent.Instruction:
                sb.AppendLine("Respond to the instruction. Do not write narrative or advance the scene.");
                break;
            default:
                sb.AppendLine("Continue from your character's perspective.");
                break;
        }

        // End-of-prompt pacing reinforcement — highest authority position
        if (context.Intent == PromptIntent.Message && context.SceneDirection.Pacing == ScenePacing.Fast)
        {
            sb.AppendLine();
            sb.AppendLine("HARD CONSTRAINT — Fast Pacing Directive:");
            sb.AppendLine("- This is a fast-paced scene. Cover more story ground per response — compress multiple beats into one.");
            sb.AppendLine("- Do not fixate on a single beat. Advance through the full arc of the current moment toward its natural resolution.");
            sb.AppendLine("- If the previous response already described a sexual act, advance to a new act, position, or time. Do not repeat.");
        }

        return sb.ToString();
    }
}
