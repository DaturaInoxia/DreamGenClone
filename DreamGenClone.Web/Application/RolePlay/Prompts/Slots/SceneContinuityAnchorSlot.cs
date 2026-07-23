using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 11, Zone B — Scene continuity anchor: cross-perceptions only, drop self-perceptions.
/// Low trim priority (6). Absorbs ScenePresenceInjector. FR-017.
/// </summary>
public sealed class SceneContinuityAnchorSlot : IPromptSlot
{
    private readonly ILogger<SceneContinuityAnchorSlot> _logger;

    public PromptSlotId Id => PromptSlotId.SceneContinuityAnchor;
    public PromptZone Zone => PromptZone.B;
    public int Order => 11;
    public bool IsTrimEligible => true;

    public SceneContinuityAnchorSlot(ILogger<SceneContinuityAnchorSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var profile = context.ActorProfile;
        var session = context.Session;
        var currentScene = session.AdaptiveState?.CurrentSceneLocation;

        var sb = new StringBuilder();
        sb.AppendLine("Scene Context:");

        // ── Time of day grounding ──
        var timeOfDay = session.AdaptiveState?.CurrentTimeOfDay;
        if (timeOfDay.HasValue)
        {
            sb.AppendLine($"  Time: {timeOfDay.Value.ToString().ToLowerInvariant()}");
        }

        // ── Character location grounding ──
        // SKIPPED: Per-character location assertions ("Dean is at: X") removed from prompt injection.
        // Reason: Creates a self-reinforcing lock — prompt says "Dean is at Trailer" → AI writes
        // Dean at Trailer → location detection confirms Dean at Trailer → prompt says "Dean is at Trailer".
        // The model should infer character presence from interaction history, like the legacy system did.
        // Character location data is still tracked in AdaptiveState for engine use; just not injected here.
        // To restore: uncomment the block below.
        /*
        var characterLocations = session.AdaptiveState?.CharacterLocations;
        if (characterLocations is { Count: > 0 })
        {
            foreach (var cl in characterLocations)
            {
                if (!string.IsNullOrWhiteSpace(cl.TrueLocation) && !cl.IsHidden)
                {
                    sb.AppendLine($"  {cl.CharacterId} is at: {cl.TrueLocation.Trim()}");
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(currentScene))
        {
            // Fallback: just note the current scene
            sb.AppendLine($"  Current scene: {currentScene.Trim()}");
        }
        */

        // ── Last beat anchor ──
        if (context.RecentInteractions is { Count: > 0 })
        {
            var lastInteraction = context.RecentInteractions[^1];
            if (!string.IsNullOrWhiteSpace(lastInteraction.Content))
            {
                var snippet = lastInteraction.Content.Trim();
                if (snippet.Length > 120)
                {
                    snippet = snippet[..120] + "...";
                }
                sb.AppendLine($"  Last beat: [{lastInteraction.ActorName}] {snippet}");
            }
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        // Keep the header + scene location line. Drop cross-perception guidance first, then last beat.
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        var remaining = maxChars;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("  Focus on") || trimmed.StartsWith("  Describe what each"))
                continue; // Drop cross-perception guidance.

            if (trimmed.StartsWith("  Last beat:"))
                continue; // Drop last beat snippet.

            if (remaining <= 0)
                break;

            if (trimmed.Length + Environment.NewLine.Length <= remaining)
            {
                sb.AppendLine(trimmed);
                remaining -= trimmed.Length + Environment.NewLine.Length;
            }
        }

        var result = sb.ToString().TrimEnd();
        return string.IsNullOrEmpty(result) ? text[..Math.Min(maxChars, text.Length)] : result;
    }
}
