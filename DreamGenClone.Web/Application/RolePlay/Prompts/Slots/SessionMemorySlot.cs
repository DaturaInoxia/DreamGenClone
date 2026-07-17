using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 10, Zone B — Session memory with 3 tiers from enriched encounter summaries.
/// Tier 1 (Long-term backstory): Encounter summaries from before
///   <c>SessionMemoryLongTermTurnThreshold</c> — persistent character self-knowledge.
/// Tier 2 (Medium-term encounters): Recent encounter summaries within threshold.
/// Tier 3 (Short-term milestones): Recent LLM-enhanced milestone summaries.
/// Trimmable (priority 4). FR-016.
/// </summary>
public sealed class SessionMemorySlot : IPromptSlot
{
    private readonly ILogger<SessionMemorySlot> _logger;

    public PromptSlotId Id => PromptSlotId.SessionMemory;
    public PromptZone Zone => PromptZone.B;
    public int Order => 10;
    public bool IsTrimEligible => true;

    public SessionMemorySlot(ILogger<SessionMemorySlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context)
    {
        return context.EncounterSummaries is { Count: > 0 };
    }

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var session = context.Session;

        // ── Fail-fast on missing threshold (FR-012a, FR-016) ──
        var longTermThreshold = session.SessionMemoryLongTermTurnThreshold;
        if (longTermThreshold is null or <= 0)
        {
            throw new InvalidOperationException(
                $"MissingPromptConfig: session '{session.Id}' SessionMemoryLongTermTurnThreshold must be a positive integer; " +
                "no hardcoded default is permitted (FR-012a).");
        }

        var summaries = context.EncounterSummaries
            .Where(s => s.LlmSummary is not null)
            .OrderByDescending(s => s.OccurredUtc)
            .ToList();

        if (summaries.Count == 0)
            return Task.FromResult(string.Empty);

        var actorName = context.ActorProfile.ActorName;

        // Partition summaries by age relative to the long-term threshold.
        // Long-term: encounter summaries from before the threshold (by encounter number).
        var thresholdEncounterNumber = Math.Max(1, summaries.Max(s => s.EncounterNumber) - longTermThreshold.Value + 1);

        var longTermSummaries = summaries
            .Where(s => s.EncounterNumber < thresholdEncounterNumber)
            .OrderBy(s => s.EncounterNumber)
            .ToList();

        var mediumTermSummaries = summaries
            .Where(s => s.EncounterNumber >= thresholdEncounterNumber
                     && s.SummaryType == EncounterSummaryType.EncounterCompletion)
            .OrderBy(s => s.EncounterNumber)
            .ToList();

        var shortTermMilestones = summaries
            .Where(s => s.SummaryType == EncounterSummaryType.PhaseMilestone
                     || s.SummaryType == EncounterSummaryType.ArcCompletion)
            .OrderByDescending(s => s.OccurredUtc)
            .Take(3) // Keep most recent milestones only
            .ToList();

        if (longTermSummaries.Count == 0 && mediumTermSummaries.Count == 0 && shortTermMilestones.Count == 0)
            return Task.FromResult(string.Empty);

        var sb = new StringBuilder();
        sb.AppendLine("Session Memory:");

        // ── Tier 1: Long-term backstory (persistent character self-knowledge) ──
        if (longTermSummaries.Count > 0)
        {
            sb.AppendLine("  Character Memories:");
            foreach (var summary in longTermSummaries)
            {
                var summaryText = summary.LlmSummary?.Trim() ?? summary.TemplateSummary.Trim();
                if (string.IsNullOrWhiteSpace(summaryText)) continue;

                // Prefer actor's own memories, but include others for context.
                var label = string.Equals(summary.CharacterId, actorName, StringComparison.OrdinalIgnoreCase)
                    ? summary.CharacterId
                    : $"{summary.CharacterId} (other perspective)";
                sb.AppendLine($"    [{label}] {summaryText}");
            }
        }

        // ── Tier 2: Medium-term encounters (recent encounter summaries) ──
        if (mediumTermSummaries.Count > 0)
        {
            sb.AppendLine("  Recent Encounter Memories:");
            foreach (var summary in mediumTermSummaries)
            {
                var summaryText = summary.LlmSummary?.Trim() ?? summary.TemplateSummary.Trim();
                if (string.IsNullOrWhiteSpace(summaryText)) continue;

                sb.AppendLine($"    Encounter {summary.EncounterNumber} ({summary.CharacterId}): {summaryText}");
            }
        }

        // ── Tier 3: Short-term milestones ──
        if (shortTermMilestones.Count > 0)
        {
            sb.AppendLine("  Recent Milestones:");
            foreach (var milestone in shortTermMilestones)
            {
                var summaryText = milestone.LlmSummary?.Trim() ?? milestone.TemplateSummary.Trim();
                if (string.IsNullOrWhiteSpace(summaryText)) continue;

                var phaseLabel = $"{milestone.FromPhase}→{milestone.ToPhase}";
                sb.AppendLine($"    [{phaseLabel}] {milestone.CharacterId}: {summaryText}");
            }
        }

        _logger.LogDebug(
            "SessionMemorySlot: SessionId={SessionId} LongTerm={LongTerm} MediumTerm={MediumTerm} ShortTerm={ShortTerm}",
            session.Id, longTermSummaries.Count, mediumTermSummaries.Count, shortTermMilestones.Count);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        // Trim oldest memories first (tier 1 long-term trimmed first, then medium-term, then milestones).
        var lines = text.Split('\n').ToList();

        // Find section boundaries.
        var charMemIdx = lines.FindIndex(l => l.TrimStart().StartsWith("Character Memories:"));
        var recentEncIdx = lines.FindIndex(l => l.TrimStart().StartsWith("Recent Encounter Memories:"));
        var milestonesIdx = lines.FindIndex(l => l.TrimStart().StartsWith("Recent Milestones:"));

        // Trim from Character Memories (long-term, lowest priority).
        while (text.Length > maxChars && charMemIdx >= 0 && charMemIdx + 1 < lines.Count)
        {
            var contentLineIdx = lines.FindIndex(charMemIdx + 1, l => l.TrimStart().StartsWith("["));
            if (contentLineIdx < 0 || (recentEncIdx >= 0 && contentLineIdx >= recentEncIdx)) break;
            lines.RemoveAt(contentLineIdx);
            text = string.Join("\n", lines);
            // Recalculate indices after removal.
            recentEncIdx = lines.FindIndex(l => l.TrimStart().StartsWith("Recent Encounter Memories:"));
            milestonesIdx = lines.FindIndex(l => l.TrimStart().StartsWith("Recent Milestones:"));
        }

        // Trim from Recent Encounter Memories.
        while (text.Length > maxChars && recentEncIdx >= 0 && recentEncIdx + 1 < lines.Count)
        {
            var contentLineIdx = lines.FindIndex(recentEncIdx + 1, l => l.TrimStart().StartsWith("Encounter "));
            if (contentLineIdx < 0 || (milestonesIdx >= 0 && contentLineIdx >= milestonesIdx)) break;
            lines.RemoveAt(contentLineIdx);
            text = string.Join("\n", lines);
            milestonesIdx = lines.FindIndex(l => l.TrimStart().StartsWith("Recent Milestones:"));
        }

        // Trim from Recent Milestones (last resort).
        while (text.Length > maxChars && milestonesIdx >= 0 && milestonesIdx + 1 < lines.Count)
        {
            var contentLineIdx = lines.FindIndex(milestonesIdx + 1, l => l.TrimStart().StartsWith("["));
            if (contentLineIdx < 0) break;
            lines.RemoveAt(contentLineIdx);
            text = string.Join("\n", lines);
        }

        if (text.Length > maxChars)
        {
            text = text[..Math.Max(1, maxChars)];
        }

        return text;
    }
}
