using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 21, Zone C — renders the user's sticky scene-direction override for the dimensions
/// that have no other prompt consumer (Beat Style, Time Shift, Granularity, Scene Presence).
/// Pacing (Slot 17), Deepening (Slot 3) and Word Count (Slot 18) are rendered by their own
/// slots from the already-overridden resolved data, so they are not duplicated here.
/// Never trimmed.
/// </summary>
public sealed class ContinuationOverrideSlot : IPromptSlot
{
    private readonly ILogger<ContinuationOverrideSlot> _logger;

    public PromptSlotId Id => PromptSlotId.ContinuationOverride;
    public PromptZone Zone => PromptZone.C;
    public int Order => 19;
    public bool IsTrimEligible => false;

    public ContinuationOverrideSlot(ILogger<ContinuationOverrideSlot> logger)
    {
        _logger = logger;
    }

    // B-085: retired. Beat Style / Time Shift / Granularity / Scene Presence now render
    // from the resolved SceneDirection in FinalInstructionSlot (Slot 17).
    public bool ShouldWrite(PromptBuildContext context) => false;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        _logger.LogDebug("ContinuationOverrideSlot: retired (B-085) — rendering moved to FinalInstructionSlot (Slot 17). SessionId={SessionId}", context.Session.Id);
        return Task.FromResult(string.Empty);
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
