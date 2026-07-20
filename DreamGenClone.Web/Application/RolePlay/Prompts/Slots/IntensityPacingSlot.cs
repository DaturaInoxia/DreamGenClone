using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 15, Zone C — After consolidation (001-final-writing-instruction),
/// this slot emits only available positions. Heat Level, contract, and pacing
/// have moved to FinalInstructionSlot (Slot 17). Never trimmed.
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
            "IntensityPacingSlot: SessionId={SessionId} — heat/pacing consolidated to Slot 17; emitting positions only",
            context.Session.Id);

        // Only emit available positions (structural data, not writing direction)
        if (intensity.AvailablePositions.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Available positions:");
            foreach (var pos in intensity.AvailablePositions)
            {
                sb.AppendLine($"  • {pos}");
            }
            return Task.FromResult(sb.ToString().TrimEnd());
        }

        return Task.FromResult(string.Empty);
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
