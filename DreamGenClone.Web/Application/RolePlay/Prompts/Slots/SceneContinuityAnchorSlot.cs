using System.Text;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
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
        var session = context.Session;
        var sb = new StringBuilder();
        sb.AppendLine("Scene Context:");

        // ── Time of day grounding ──
        var timeOfDay = session.AdaptiveState?.CurrentTimeOfDay;
        if (timeOfDay.HasValue)
        {
            sb.AppendLine($"  Time: {timeOfDay.Value.ToString().ToLowerInvariant()}");
        }

        // ── Last Narrative (full synthesized omniscient close from previous turn) ──
        var lastNarrative = session.Interactions
            .LastOrDefault(i => i.InteractionType == InteractionType.System
                && string.Equals(i.ActorName, "Narrative", StringComparison.OrdinalIgnoreCase)
                && !i.IsExcluded);
        if (lastNarrative is not null && !string.IsNullOrWhiteSpace(lastNarrative.Content))
        {
            sb.AppendLine("  Last Narrative:");
            sb.AppendLine($"    {lastNarrative.Content.Trim()}");
            sb.AppendLine();
        }

        // ── Current turn interactions ──
        // Take the last N-1 interactions for character variant, all for Narrative.
        var positionInTurn = context.PositionInTurn;
        var totalInTurn = context.TurnActorCount;
        if (context.RecentInteractions is { Count: > 0 })
        {
            var priorCount = positionInTurn.HasValue
                ? positionInTurn.Value - 1
                : (totalInTurn ?? 0);

            _logger.LogDebug(
                "SceneContinuityAnchor: SessionId={SessionId} Pos={Pos} TotalInTurn={TotalInTurn} RecentCount={RecentCount} PriorCount={PriorCount}",
                session.Id, positionInTurn, totalInTurn, context.RecentInteractions.Count, priorCount);

            if (priorCount > 0)
            {
                var priorInteractions = context.RecentInteractions
                    .TakeLast(priorCount)
                    .ToList();

                if (priorInteractions.Count > 0)
                {
                    sb.AppendLine("  Current Turn:");
                    for (int i = 0; i < priorInteractions.Count; i++)
                    {
                        var interaction = priorInteractions[i];
                        var content = interaction.Content?.Trim() ?? "";
                        var pos = i + 1;
                        var annotation = totalInTurn.HasValue
                            ? $" Interaction {pos}/{totalInTurn.Value}"
                            : "";
                        sb.AppendLine($"    [{interaction.ActorName}]{annotation}: {content}");
                    }
                    sb.AppendLine();
                }
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
