using System.Text;
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

    public bool ShouldWrite(PromptBuildContext context)
        => context.Override is not null && context.Override.HasUnconsumedDimensionOverride;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var ov = context.Override;
        if (ov is null || !ov.HasUnconsumedDimensionOverride)
        {
            return Task.FromResult(string.Empty);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Scene Direction Override (user-selected for this session):");

        if (ov.BeatScope.HasValue)
            sb.AppendLine($"  HARD CONSTRAINT — Beat Style: {ov.BeatScope.Value} — {ContinuationMarkerCatalog.DescribeBeatScope(ov.BeatScope.Value)}");
        if (ov.TimeShift.HasValue)
            sb.AppendLine($"  HARD CONSTRAINT — Time Shift: {ov.TimeShift.Value} — {ContinuationMarkerCatalog.DescribeTimeShift(ov.TimeShift.Value)}");
        if (ov.Granularity.HasValue)
            sb.AppendLine($"  HARD CONSTRAINT — Granularity: {ov.Granularity.Value} — {ContinuationMarkerCatalog.DescribeGranularity(ov.Granularity.Value)}");
        if (ov.RequireScenePresence.HasValue)
            sb.AppendLine($"  HARD CONSTRAINT — Scene Presence: {(ov.RequireScenePresence.Value ? "on" : "off")} — {ContinuationMarkerCatalog.DescribeScenePresence(ov.RequireScenePresence.Value)}");

        _logger.LogDebug("ContinuationOverrideSlot: SessionId={SessionId} emitted override block", context.Session.Id);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
