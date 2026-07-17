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
        sb.AppendLine("Scene Continuity:");

        // Always include current scene location.
        if (!string.IsNullOrWhiteSpace(currentScene))
        {
            sb.AppendLine($"  Current scene: {currentScene.Trim()}");
        }

        // Cross-perceptions: what other characters perceive (not self-perceptions).
        // This slot focuses on scene-level continuity — what happened from others' perspective.
        var actorName = profile.ActorName;

        if (profile.Kind == ActorProfileKind.Player)
        {
            // Player: note other characters' perspective on the scene.
            sb.AppendLine($"  Focus on what other characters perceive of {actorName}, not {actorName}'s own thoughts.");
        }
        else if (profile.Kind == ActorProfileKind.NpcPresent || profile.Kind == ActorProfileKind.NpcNonPresent)
        {
            // NPC: note cross-perceptions from other characters.
            sb.AppendLine($"  Focus on what others perceive of {actorName}, not {actorName}'s self-reflection.");
        }
        else if (profile.Kind == ActorProfileKind.Narrative)
        {
            // Narrative: omniscient cross-perceptions.
            sb.AppendLine("  Describe what each character perceives of the others in the scene.");
        }

        // Continuity rule: anchor to the last interaction.
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
