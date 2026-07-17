using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 1, Zone A — Location + phase one-liner replacing "You are continuing..." header.
/// Grounds the model in WHERE and WHEN immediately. Never trimmed.
/// FR-005.
/// </summary>
public sealed class SceneAnchorSlot : IPromptSlot
{
    private readonly ILogger<SceneAnchorSlot> _logger;

    public PromptSlotId Id => PromptSlotId.SceneAnchor;
    public PromptZone Zone => PromptZone.A;
    public int Order => 1;
    public bool IsTrimEligible => false;

    public SceneAnchorSlot(ILogger<SceneAnchorSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var location = context.Session.AdaptiveState.CurrentSceneLocation;
        var locationLabel = !string.IsNullOrWhiteSpace(location) ? location
            : context.Scenario.DefaultStartingLocationName ?? "Unknown location";
        var phase = context.Phase;

        _logger.LogDebug(
            "SceneAnchorSlot: SessionId={SessionId} Location={Location} Phase={Phase}",
            context.Session.Id, locationLabel, phase);

        var text = $"Current scene: {locationLabel} — {phase} phase.";
        return Task.FromResult(text);
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        // Never trimmed, but implement contractually.
        return text[..Math.Max(1, maxChars)];
    }
}
