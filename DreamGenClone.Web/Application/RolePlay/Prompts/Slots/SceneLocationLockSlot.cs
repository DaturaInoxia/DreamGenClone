using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 4, Zone A — Hard constraint: current location + continuity rule.
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
        var location = context.Session.AdaptiveState.CurrentSceneLocation;
        var resolvedLocation = !string.IsNullOrWhiteSpace(location) ? location
            : context.Scenario.DefaultStartingLocationName;

        string text;
        if (!string.IsNullOrWhiteSpace(resolvedLocation))
        {
            text = $"HARD CONSTRAINT — Scene Location: The current scene is at \"{resolvedLocation}\". " +
                   "Do not move any character to a different location without writing an explicit transition " +
                   "in the narration. Do not jump to a new place between responses.";
        }
        else
        {
            text = "HARD CONSTRAINT — Location Continuity: The physical setting established in the previous " +
                   "response must be maintained in this response. Do not silently relocate any character to a " +
                   "different place. If a character moves, write the transition explicitly in the narration.";
        }

        _logger.LogDebug(
            "SceneLocationLockSlot: SessionId={SessionId} Location={Location}",
            context.Session.Id, location ?? "(none)");

        return Task.FromResult(text);
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
