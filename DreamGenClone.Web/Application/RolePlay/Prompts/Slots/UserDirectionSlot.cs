using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 16, Zone C — User direction (FR-022). Conditional: fires only when
/// the user has provided real direction content. Generic defaults like
/// "Continue naturally" are omitted. Never trimmed when present.
/// </summary>
public sealed class UserDirectionSlot : IPromptSlot
{
    private readonly ILogger<UserDirectionSlot> _logger;

    // Phrases that indicate generic/default direction (should be suppressed).
    private static readonly HashSet<string> GenericDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        "continue naturally",
        "continue naturally.",
        "continue",
        "continue.",
        "",
    };

    public PromptSlotId Id => PromptSlotId.UserDirection;
    public PromptZone Zone => PromptZone.C;
    public int Order => 16;
    public bool IsTrimEligible => false;

    public UserDirectionSlot(ILogger<UserDirectionSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context)
    {
        if (string.IsNullOrWhiteSpace(context.PromptText))
            return false;

        if (GenericDefaults.Contains(context.PromptText.Trim()))
            return false;

        return true;
    }

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        _logger.LogDebug(
            "UserDirectionSlot: SessionId={SessionId} DirectionLength={Length}",
            context.Session.Id, context.PromptText.Length);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("User Direction:");

        // Prepend pacing directive before the per-turn prompt
        if (context.Intensity.SceneDirection is not null)
        {
            var pacingText = context.Intensity.SceneDirection.Pacing switch
            {
                ScenePacing.Slow => "Slow pace — advance the scene deliberately. Let each moment land before moving to the next.",
                ScenePacing.Fast => "Fast pace — advance the scene briskly. Keep the momentum going.",
                _ => "Medium pace — advance the story forward without rushing or stalling."
            };
            sb.AppendLine($"  {pacingText}");
        }

        sb.AppendLine(context.PromptText.Trim());

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        // Never trimmed when present, but implement contractually.
        return text[..Math.Max(1, maxChars)];
    }
}
