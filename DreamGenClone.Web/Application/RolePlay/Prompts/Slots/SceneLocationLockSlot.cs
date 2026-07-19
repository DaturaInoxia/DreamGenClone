using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 4, Zone A — Last known location as factual context.
/// No lock, no directive — just where things were last detected.
/// Never trimmed. FR-008.
/// </summary>
public sealed class SceneLocationLockSlot : IPromptSlot
{
    private readonly ILogger<SceneLocationLockSlot> _logger;

    public PromptSlotId Id => PromptSlotId.SceneLocationLock;
    public PromptZone Zone => PromptZone.A;
    public int Order => 4;
    public bool IsTrimEligible => false;

    public SceneLocationLockSlot(ILogger<SceneLocationLockSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        // SKIPPED: Location assertion ("Last known location: X") removed from prompt injection.
        // Reason: Creates a self-reinforcing lock — prompt says "scene is at X" → AI writes
        // all characters at X → location detection confirms X → prompt says "scene is at X".
        // The model should infer location from interaction history, like the legacy system did.
        // Location data is still tracked in AdaptiveState for engine use; just not injected here.
        // To restore: uncomment the block below.
        /*
        var location = context.Session.AdaptiveState.CurrentSceneLocation;
        var resolvedLocation = !string.IsNullOrWhiteSpace(location) ? location
            : context.Scenario.DefaultStartingLocationName;

        string text;
        if (!string.IsNullOrWhiteSpace(resolvedLocation))
        {
            text = $"Last known location: {resolvedLocation}";
        }
        else
        {
            text = "Last known location: unknown";
        }

        _logger.LogDebug(
            "SceneLocationLockSlot: SessionId={SessionId} Location={Location}",
            context.Session.Id, location ?? "(none)");

        return Task.FromResult(text);
        */

        return Task.FromResult(string.Empty);
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
