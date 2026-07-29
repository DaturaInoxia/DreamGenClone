using System.Text;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 9, Zone B — Tiered interaction history with 3-layer compression.
/// Layer 1: Full detail for last <c>HistoryFullDetailTurnBand</c> interactions.
/// Layer 2: Narrative-only 1-2 line summaries for next <c>HistoryNarrativeOnlyTurnBand</c> interactions.
/// Layer 3: Interactions beyond <c>ContextWindowTurns</c> are omitted (delegated to SessionMemory).
/// Trimmable (priority 1 — oldest trimmed first). FR-015.
/// </summary>
public sealed class InteractionHistorySlot : IPromptSlot
{
    private readonly ILogger<InteractionHistorySlot> _logger;

    public PromptSlotId Id => PromptSlotId.InteractionHistory;
    public PromptZone Zone => PromptZone.B;
    public int Order => 9;
    public bool IsTrimEligible => true;

    public InteractionHistorySlot(ILogger<InteractionHistorySlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context)
    {
        return context.RecentInteractions is { Count: > 0 };
    }

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var session = context.Session;

        // ── Fail-fast on missing thresholds (FR-012a, FR-015) ──
        var fullDetailTurns = session.HistoryFullDetailTurnBand;
        if (fullDetailTurns is null or <= 0)
        {
            throw new InvalidOperationException(
                $"MissingPromptConfig: session '{session.Id}' HistoryFullDetailTurnBand must be a positive integer; " +
                "no hardcoded default is permitted (FR-012a).");
        }

        var interactions = context.RecentInteractions
            .Where(x => !x.IsExcluded)
            .ToList();

        if (interactions.Count == 0)
            return Task.FromResult(string.Empty);

        var entries = context.RecentInteractionEntries;
        var entryLookup = entries is not null
            ? entries.ToDictionary(e => e.Interaction.Id, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, RecentInteractionEntry>(StringComparer.OrdinalIgnoreCase);

        // Group interactions by turn number (from pre-computed entries),
        // take the last N turns, render all interactions within those turns.
        var turnGroups = new List<(int TurnNumber, List<(RolePlayInteraction Interaction, RecentInteractionEntry? Entry)> Items)>();
        foreach (var interaction in interactions)
        {
            entryLookup.TryGetValue(interaction.Id, out var entry);
            var turnNum = entry?.TurnNumber ?? 0;
            if (turnGroups.Count == 0 || turnGroups[^1].TurnNumber != turnNum)
            {
                turnGroups.Add((turnNum, new List<(RolePlayInteraction, RecentInteractionEntry?)>()));
            }
            turnGroups[^1].Items.Add((interaction, entry));
        }

        var recentTurns = turnGroups.Count <= fullDetailTurns.Value
            ? turnGroups
            : turnGroups.GetRange(turnGroups.Count - fullDetailTurns.Value, fullDetailTurns.Value);

        var sb = new StringBuilder();
        sb.AppendLine("Interaction History:");

        var roleMap = context.ActorRoleMap;
        foreach (var turn in recentTurns)
        {
            sb.AppendLine($"  Turn {turn.TurnNumber}:");
            foreach (var (interaction, entry) in turn.Items)
            {
                var content = interaction.Content?.Trim() ?? "";
                var role = ResolveInteractionRole(interaction, roleMap);
                var rolePart = string.IsNullOrEmpty(role) ? "" : $" ({role})";
                var turnAnnotation = entry is not null
                    ? $" Interaction {entry.PositionInTurn}/{entry.TurnActorCount}"
                    : "";
                sb.AppendLine($"    [{interaction.ActorName}{rolePart}]{turnAnnotation}: {content}");
            }
        }

        _logger.LogDebug(
            "InteractionHistorySlot: SessionId={SessionId} Turns={Turns} Interactions={Interactions}",
            session.Id, recentTurns.Count, interactions.Count);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Resolves a human-readable role label for an interaction, preferring the actor role map
    /// from scenario characters, falling back to interaction type.
    /// </summary>
    private static string ResolveInteractionRole(
        RolePlayInteraction interaction,
        IReadOnlyDictionary<string, string>? roleMap)
    {
        var actorName = interaction.ActorName?.Trim() ?? "";
        if (roleMap is not null && roleMap.TryGetValue(actorName, out var role))
            return role;

        return interaction.InteractionType switch
        {
            InteractionType.System => "Narrative",
            InteractionType.User => "You",
            _ => "",
        };
    }

    public string Trim(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        // Trim oldest entries first — drop lines from the "Earlier Interactions" section,
        // then trim individual lines from "Recent Interactions" oldest-first.
        var lines = text.Split('\n').ToList();

        // Find the "Earlier Interactions:" boundary.
        var earlierIdx = lines.FindIndex(l => l.TrimStart().StartsWith("Earlier Interactions:"));
        var recentIdx = lines.FindIndex(l => l.TrimStart().StartsWith("Recent Interactions:"));

        // Remove from earlier section first (oldest priority).
        while (text.Length > maxChars && earlierIdx >= 0 && earlierIdx + 1 < lines.Count)
        {
            // Find next content line (starts with whitespace + [ActorName])
            var contentLineIdx = lines.FindIndex(earlierIdx + 1, l => l.TrimStart().StartsWith("["));
            if (contentLineIdx < 0) break;
            lines.RemoveAt(contentLineIdx);
            text = string.Join("\n", lines);
        }

        // If still over budget, trim recent section oldest-first.
        while (text.Length > maxChars && recentIdx >= 0 && recentIdx + 1 < lines.Count)
        {
            // Find the first content line after "Recent Interactions:" header.
            var contentLineIdx = lines.FindIndex(recentIdx + 1, l => l.TrimStart().StartsWith("["));
            if (contentLineIdx < 0) break;
            lines.RemoveAt(contentLineIdx);
            text = string.Join("\n", lines);
        }

        // If still over budget, truncate the whole text.
        if (text.Length > maxChars)
        {
            text = text[..Math.Max(1, maxChars)];
        }

        return text;
    }
}
