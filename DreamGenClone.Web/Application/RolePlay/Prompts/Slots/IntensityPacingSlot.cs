using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 15, Zone C — Merged intensity escalation, scene-time-direction,
/// and available positions (FR-021). Never trimmed.
/// Absorbs IntensityContractInjector + EscalationInjector +
/// SceneTimeDirectionInjector + PositionListInjector.
/// </summary>
public sealed class IntensityPacingSlot : IPromptSlot
{
    private readonly ILogger<IntensityPacingSlot> _logger;

    public PromptSlotId Id => PromptSlotId.IntensityPacing;
    public PromptZone Zone => PromptZone.C;
    public int Order => 15;
    public bool IsTrimEligible => false;

    public IntensityPacingSlot(ILogger<IntensityPacingSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var intensity = context.Intensity;

        _logger.LogDebug(
            "IntensityPacingSlot: SessionId={SessionId} Label={Label}",
            context.Session.Id, intensity.ResolvedLabel ?? "none");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Intensity & Pacing:");

        // Resolved intensity label + description
        if (!string.IsNullOrWhiteSpace(intensity.ResolvedLabel))
        {
            sb.AppendLine($"- Intensity level: {intensity.ResolvedLabel}");
        }

        if (!string.IsNullOrWhiteSpace(intensity.Description))
        {
            sb.AppendLine($"- Intensity contract: {intensity.Description}");
        }

        // Floor/ceiling overrides if present
        if (!string.IsNullOrWhiteSpace(intensity.FloorOverride))
        {
            sb.AppendLine($"- Intensity floor: {intensity.FloorOverride}");
        }

        if (!string.IsNullOrWhiteSpace(intensity.CeilingOverride))
        {
            sb.AppendLine($"- Intensity ceiling: {intensity.CeilingOverride}");
        }

        // Scene direction: pacing, time-shift, deepening
        if (intensity.SceneDirection is not null)
        {
            var sd = intensity.SceneDirection;
            
            // Pacing: describe what the enum means for the LLM
            var pacingText = sd.Pacing switch
            {
                ScenePacing.Slow => "Slow pace — linger on sensory detail, internal reflection, and atmosphere. Let moments stretch.",
                ScenePacing.Fast => "Fast pace — drive toward the next beat. Keep actions crisp and dialogue forward-moving.",
                _ => "Medium pace — advance the scene naturally, not rushed, not stalled. Let moments breathe without dragging."
            };
            sb.AppendLine($"- Scene pacing: {pacingText}");

            // Time shift: describe what time jumps are permitted
            var timeShiftText = sd.TimeShift switch
            {
                TimeShiftPolicy.None => "No time shift — continue from the exact moment the last response ended.",
                TimeShiftPolicy.Medium => "Medium time shifts allowed (hours to half a day) — use transitions like 'by evening', 'the next morning'.",
                TimeShiftPolicy.Large => "Large time shifts allowed (a day or more) — use transitions like 'the next day', 'by the weekend'.",
                _ => "Small time shifts allowed (minutes to hours) — use transitions like 'later that afternoon', 'after supper', 'a while later'. Never jump without a transition."
            };
            sb.AppendLine($"- Time direction: {timeShiftText}");

            // Deepening: position 2+ constraint
            if (sd.Deepening == DeepeningPolicy.SubsequentActors)
            {
                sb.AppendLine("- Position 2+ actors: deepen from your POV only. Do NOT advance to a new beat, position, or location. React to and enrich what was already established.");
            }

            // Beat scope: how long to stay in this moment
            var beatText = sd.BeatScope switch
            {
                BeatScope.Single => "Single turn — resolve this moment, then time may advance next turn.",
                BeatScope.Extended => "Extended — stay in this moment for 4+ exchanges. Build depth across multiple turns.",
                _ => "Short beat — stay in this moment for 2-3 exchanges before advancing. Deepen sensory and emotional texture rather than jumping to a new beat."
            };
            sb.AppendLine($"- Beat scope: {beatText}");
        }

        // Available positions
        if (intensity.AvailablePositions.Count > 0)
        {
            sb.AppendLine("- Available positions:");
            foreach (var pos in intensity.AvailablePositions)
            {
                sb.AppendLine($"  • {pos}");
            }
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        // Never trimmed, but implement contractually.
        return text[..Math.Max(1, maxChars)];
    }
}
