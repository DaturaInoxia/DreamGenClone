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

        // Scene pacing: the single pacing directive (FR-021)
        if (intensity.SceneDirection is not null)
        {
            var sd = intensity.SceneDirection;

            var pacingText = sd.Pacing switch
            {
                ScenePacing.Slow => "Slow pace — linger on sensory detail, internal reflection, and atmosphere. Let moments stretch.",
                ScenePacing.Fast => "Fast pace — drive toward the next beat. Keep actions crisp and dialogue forward-moving.",
                _ => "Medium pace — advance the scene naturally, not rushed, not stalled. Let moments breathe without dragging."
            };
            sb.AppendLine($"- Scene pacing: {pacingText}");

            if (sd.Deepening == DeepeningPolicy.SubsequentActors)
            {
                sb.AppendLine("- Position 2+ actors: deepen from your POV — react to and enrich what was already established rather than introducing a new beat.");
            }
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
