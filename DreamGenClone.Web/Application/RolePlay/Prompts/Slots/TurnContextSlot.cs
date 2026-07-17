using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 3, Zone A — Turn number, response position, pacing-aware position guidance.
/// Replaces duplicate TurnContextInjector. Never trimmed.
/// FR-007.
/// </summary>
public sealed class TurnContextSlot : IPromptSlot
{
    private readonly ILogger<TurnContextSlot> _logger;

    public PromptSlotId Id => PromptSlotId.TurnContext;
    public PromptZone Zone => PromptZone.A;
    public int Order => 3;
    public bool IsTrimEligible => false;

    public TurnContextSlot(ILogger<TurnContextSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context)
        => context.TurnIndex.HasValue && context.TurnActorCount.HasValue;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var turnIndex = context.TurnIndex!.Value;
        var turnActorCount = context.TurnActorCount!.Value;
        var positionInTurn = context.PositionInTurn;
        var variant = context.Variant;

        var sb = new StringBuilder();

        if (variant == PromptVariant.Narrative || !positionInTurn.HasValue)
        {
            // Narrative variant or no position info.
            sb.AppendLine($"Turn Context: turn {turnIndex}, narrative close");
            sb.AppendLine($"- All {turnActorCount} character responses for this turn are complete.");
            sb.AppendLine("- Write an omniscient account: setting, character positions, sensations, atmosphere.");
            sb.AppendLine("- Synthesize character perspectives into a rich, unified picture.");
            sb.AppendLine("- Do NOT advance the scene beyond what the characters established this turn.");
        }
        else
        {
            var pos = positionInTurn.Value;
            sb.AppendLine($"Turn Context: turn {turnIndex}, response {pos} of {turnActorCount}");
            sb.AppendLine($"- {turnActorCount} character responses this turn, in sequence, then a narrative close.");

            if (pos == 1)
            {
                sb.AppendLine("- You are first this turn. Establish the scene beat — advance from where the previous turn left off.");
                if (turnActorCount > 1)
                    sb.AppendLine($"- The other {turnActorCount - 1} character(s) will describe this same moment from their perspectives after you.");
                sb.AppendLine("- Do not leave the beat unresolved — give it clear shape so others can react to it.");
            }
            else if (pos == turnActorCount)
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
        }

        var text = sb.ToString().TrimEnd();

        _logger.LogDebug(
            "TurnContextSlot: SessionId={SessionId} Turn={Turn} Pos={Pos}/{Total} Variant={Variant}",
            context.Session.Id, turnIndex, positionInTurn, turnActorCount, variant);

        return Task.FromResult(text);
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
