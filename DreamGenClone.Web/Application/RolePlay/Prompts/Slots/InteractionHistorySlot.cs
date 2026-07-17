using System.Text;
using DreamGenClone.Domain.RolePlay;
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
        var fullDetailBand = session.HistoryFullDetailTurnBand;
        if (fullDetailBand is null or <= 0)
        {
            throw new InvalidOperationException(
                $"MissingPromptConfig: session '{session.Id}' HistoryFullDetailTurnBand must be a positive integer; " +
                "no hardcoded default is permitted (FR-012a).");
        }

        var narrativeOnlyBand = session.HistoryNarrativeOnlyTurnBand;
        if (narrativeOnlyBand is null or <= 0)
        {
            throw new InvalidOperationException(
                $"MissingPromptConfig: session '{session.Id}' HistoryNarrativeOnlyTurnBand must be a positive integer; " +
                "no hardcoded default is permitted (FR-012a).");
        }

        var contextWindowTurns = session.ContextWindowTurns;
        if (contextWindowTurns is null or <= 0)
        {
            throw new InvalidOperationException(
                $"MissingPromptConfig: session '{session.Id}' ContextWindowTurns must be a positive integer; " +
                "no hardcoded default is permitted (FR-012a).");
        }

        var interactions = context.RecentInteractions
            .Where(x => !x.IsExcluded)
            .ToList();

        if (interactions.Count == 0)
            return Task.FromResult(string.Empty);

        var sb = new StringBuilder();
        sb.AppendLine("Interaction History:");

        // Reverse so we process newest first, then reverse output for chronological order.
        var reversed = Enumerable.Reverse(interactions).ToList();

        var fullDetailCount = Math.Min(fullDetailBand.Value, reversed.Count);
        var narrativeOnlyStart = fullDetailCount;
        var narrativeOnlyEnd = Math.Min(fullDetailCount + narrativeOnlyBand.Value, reversed.Count);
        // total window cap handled by ContextWindowTurns — anything beyond is omitted.

        // ── Layer 1: Full detail (most recent interactions) ──
        var fullDetailItems = reversed.Take(fullDetailCount).Reverse().ToList();
        if (fullDetailItems.Count > 0)
        {
            sb.AppendLine("  Recent Interactions:");
            foreach (var interaction in fullDetailItems)
            {
                var content = interaction.Content?.Trim() ?? "";
                sb.AppendLine($"    [{interaction.ActorName}]: {content}");
            }
        }

        // ── Layer 2: Narrative-only summaries (1-2 lines each) ──
        var narrativeItems = reversed.Skip(narrativeOnlyStart).Take(narrativeOnlyEnd - narrativeOnlyStart).Reverse().ToList();
        if (narrativeItems.Count > 0)
        {
            sb.AppendLine("  Earlier Interactions:");
            foreach (var interaction in narrativeItems)
            {
                var content = interaction.Content?.Trim() ?? "";
                // Compress to ~80 chars max for narrative-only summary.
                var summary = content.Length > 80
                    ? content[..80] + "..."
                    : content;
                sb.AppendLine($"    [{interaction.ActorName}]: {summary}");
            }
        }

        _logger.LogDebug(
            "InteractionHistorySlot: SessionId={SessionId} FullDetail={FullDetail} NarrativeOnly={NarrativeOnly} Total={Total}",
            session.Id, fullDetailItems.Count, narrativeItems.Count, interactions.Count);

        return Task.FromResult(sb.ToString().TrimEnd());
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
