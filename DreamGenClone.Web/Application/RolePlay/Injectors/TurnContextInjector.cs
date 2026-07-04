namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects turn/position context: "This is response X of N in this turn."
/// Engine-owned structural inject — fires whenever position-in-turn is known.
/// </summary>
public sealed class TurnContextInjector : IPromptInjector
{
    public string Id => "turn-context";
    public int Priority => 5;

    public bool ShouldFire(PromptInjectionContext context)
        => context.PositionInTurn.HasValue && context.TurnActorCount.HasValue;

    public string BuildText(PromptInjectionContext context)
    {
        var pos = context.PositionInTurn!.Value;
        var total = context.TurnActorCount!.Value;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"Turn Context: response {pos} of {total}");
        sb.AppendLine($"- {total} character responses this turn, in sequence, then a narrative close.");

        if (pos == 1)
        {
            sb.AppendLine("- You are first this turn. Establish the scene beat — advance from where the previous turn left off.");
            if (total > 1)
                sb.AppendLine($"- The other {total - 1} character(s) will describe this same moment from their perspectives after you.");
            sb.AppendLine("- Do not leave the beat unresolved — give it clear shape so others can react to it.");
        }
        else if (pos == total)
        {
            sb.AppendLine("- Continue from your character's perspective — what you observe, feel, or what occupies your attention in this moment.");
            sb.AppendLine("- The narrative closes the turn after your response.");
        }
        else
        {
            sb.AppendLine("- Describe the same scene beat established this turn, from your character's perspective.");
            sb.AppendLine("- Give your sensations, reactions, dialogue, and internal experience of this exact moment.");
            sb.AppendLine("- Do NOT advance to a new act, position, or story beat.");
        }

        return sb.ToString();
    }
}
