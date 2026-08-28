using System.Text;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 8, Zone C — Pinned interactions injected at deterministic position
/// before InteractionHistory. Pinned items appear in every continuation prompt
/// regardless of context-window position. FR-024.
/// </summary>
public sealed class PinnedContextSlot : IPromptSlot
{
    private readonly ILogger<PinnedContextSlot> _logger;

    public PromptSlotId Id => PromptSlotId.PinnedContext;
    public PromptZone Zone => PromptZone.C;
    public int Order => 8;
    public bool IsTrimEligible => false;

    public PinnedContextSlot(ILogger<PinnedContextSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context)
    {
        if (context.Variant == PromptVariant.Narrative)
            return false;

        return context.PinnedInteractions is { Count: > 0 };
    }

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var pinned = context.PinnedInteractions;
        if (pinned.Count == 0)
            return Task.FromResult(string.Empty);

        var sb = new StringBuilder();
        sb.AppendLine("[Pinned Context]");

        foreach (var interaction in pinned)
        {
            var content = interaction.Content?.Trim() ?? "";
            if (string.IsNullOrEmpty(content))
                continue;

            var label = interaction.InteractionType switch
            {
                InteractionType.System => "Instruction",
                _ => $"Character Message — {interaction.ActorName}"
            };

            sb.AppendLine($"{label}: {content}");
        }

        _logger.LogDebug(
            "PinnedContextSlot: SessionId={SessionId} PinnedCount={PinnedCount}",
            context.Session.Id, pinned.Count);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
