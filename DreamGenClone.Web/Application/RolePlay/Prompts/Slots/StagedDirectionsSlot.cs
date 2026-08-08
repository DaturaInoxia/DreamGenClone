using System.Text;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 9, Zone C — Transient batch scene directions (B-076).
/// Staged interaction rows (IsStagedDirection=true) are injected as a
/// one-shot block on the next … continuation, then graduated to normal
/// history rows. Renders after PinnedContext (Order 8) so persistent
/// constraints precede the one-shot staged plan.
/// </summary>
public sealed class StagedDirectionsSlot : IPromptSlot
{
    private readonly ILogger<StagedDirectionsSlot> _logger;

    public PromptSlotId Id => PromptSlotId.StagedDirections;
    public PromptZone Zone => PromptZone.C;
    public int Order => 9;
    public bool IsTrimEligible => false;

    public StagedDirectionsSlot(ILogger<StagedDirectionsSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context)
    {
        // Staged directions are operational, not variant-specific —
        // fire for both Character and Narrative variants when staged rows exist.
        return context.StagedInteractions is { Count: > 0 };
    }

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var staged = context.StagedInteractions;
        if (staged.Count == 0)
            return Task.FromResult(string.Empty);

        var sb = new StringBuilder();
        sb.AppendLine("[Staged Scene Directions — Execute This Turn]");

        foreach (var interaction in staged)
        {
            var content = interaction.Content?.Trim() ?? "";
            if (string.IsNullOrEmpty(content))
                continue;

            var isInstruction = interaction.InteractionType == InteractionType.System
                && string.Equals(interaction.ActorName, "Instruction", StringComparison.OrdinalIgnoreCase);

            var label = isInstruction
                ? "Instruction"
                : $"Character Message: {interaction.ActorName} —";

            sb.AppendLine($"{label} {content}");
        }

        _logger.LogDebug(
            "StagedDirectionsSlot: SessionId={SessionId} StagedCount={StagedCount}",
            context.Session.Id, staged.Count);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
