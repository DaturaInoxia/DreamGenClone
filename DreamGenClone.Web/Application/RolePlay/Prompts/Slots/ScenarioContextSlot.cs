using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 6, Zone B — Scenario context: world setting, plot description, goals, conflicts, rules.
/// Progressive compression using <c>ScenarioCompressionTurnThreshold</c>.
/// Trimmable (priority 3). FR-012.
/// </summary>
public sealed class ScenarioContextSlot : IPromptSlot
{
    private readonly ILogger<ScenarioContextSlot> _logger;

    public PromptSlotId Id => PromptSlotId.ScenarioContext;
    public PromptZone Zone => PromptZone.B;
    public int Order => 6;
    public bool IsTrimEligible => true;

    public ScenarioContextSlot(ILogger<ScenarioContextSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context)
    {
        var s = context.Scenario;
        return !string.IsNullOrWhiteSpace(s.Name)
            || !string.IsNullOrWhiteSpace(s.Description)
            || !string.IsNullOrWhiteSpace(s.PlotDescription)
            || !string.IsNullOrWhiteSpace(s.WorldDescription);
    }

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var scenario = context.Scenario;
        var session = context.Session;
        var turnIndex = context.TurnIndex ?? 0;

        // Fail-fast if compression threshold is missing (FR-012a).
        var compressionThreshold = session.ScenarioCompressionTurnThreshold;
        if (compressionThreshold is null or <= 0)
        {
            throw new InvalidOperationException(
                $"MissingPromptConfig: session '{session.Id}' ScenarioCompressionTurnThreshold must be a positive integer; " +
                "no hardcoded default is permitted (FR-012a).");
        }

        var sb = new StringBuilder();
        sb.AppendLine("Scenario:");

        // Title + plot description (always included).
        if (!string.IsNullOrWhiteSpace(scenario.Name))
        {
            sb.AppendLine($"  Title: {scenario.Name.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(scenario.PlotDescription))
        {
            sb.AppendLine($"  Plot: {scenario.PlotDescription.Trim()}");
        }

        // Progressive compression: full detail for early turns, compressed summary after threshold.
        var isCompressed = turnIndex >= compressionThreshold.Value;

        if (isCompressed)
        {
            // Compressed: 2-3 line world context summary.
            sb.AppendLine("  World Context (summary):");
            if (!string.IsNullOrWhiteSpace(scenario.WorldDescription))
            {
                // Take first ~300 chars of world description as summary.
                var summary = scenario.WorldDescription.Trim();
                if (summary.Length > 300)
                {
                    var cutPoint = summary.IndexOf('.', 250);
                    if (cutPoint > 0 && cutPoint < 350)
                        summary = summary[..(cutPoint + 1)];
                    else
                        summary = summary[..300] + "...";
                }
                sb.AppendLine($"    {summary}");
            }

            // Include time frame if present.
            if (!string.IsNullOrWhiteSpace(scenario.TimeFrame))
            {
                sb.AppendLine($"    Time: {scenario.TimeFrame.Trim()}");
                sb.AppendLine("    Time Span Reminder: This story takes place within this time frame. Scenes may skip forward in time.");
            }
        }
        else
        {
            // Full detail for early turns.
            if (!string.IsNullOrWhiteSpace(scenario.WorldDescription))
            {
                sb.AppendLine("  World:");
                sb.AppendLine($"    {scenario.WorldDescription.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(scenario.TimeFrame))
            {
                sb.AppendLine($"  Time Frame: {scenario.TimeFrame.Trim()}");
                sb.AppendLine("  Time Span Reminder: This entire story takes place within the time frame above. Scenes may skip forward in time; a new response does not have to be the immediate continuation of the last moment.");
            }

            if (scenario.Goals.Count > 0)
            {
                sb.AppendLine("  Goals:");
                foreach (var goal in scenario.Goals)
                {
                    if (!string.IsNullOrWhiteSpace(goal))
                        sb.AppendLine($"    - {goal.Trim()}");
                }
            }

            if (scenario.Conflicts.Count > 0)
            {
                sb.AppendLine("  Conflicts:");
                foreach (var conflict in scenario.Conflicts)
                {
                    if (!string.IsNullOrWhiteSpace(conflict))
                        sb.AppendLine($"    - {conflict.Trim()}");
                }
            }

            if (scenario.WorldRules.Count > 0)
            {
                sb.AppendLine("  World Rules:");
                foreach (var rule in scenario.WorldRules)
                {
                    if (!string.IsNullOrWhiteSpace(rule))
                        sb.AppendLine($"    - {rule.Trim()}");
                }
            }

            if (scenario.EnvironmentalDetails.Count > 0)
            {
                sb.AppendLine("  Environment:");
                foreach (var detail in scenario.EnvironmentalDetails)
                {
                    if (!string.IsNullOrWhiteSpace(detail))
                        sb.AppendLine($"    - {detail.Trim()}");
                }
            }
        }

        // Narrative guidelines always included (compact).
        if (scenario.NarrativeGuidelines.Count > 0)
        {
            sb.AppendLine("  Narrative Guidelines:");
            foreach (var guide in scenario.NarrativeGuidelines)
            {
                if (!string.IsNullOrWhiteSpace(guide))
                    sb.AppendLine($"    - {guide.Trim()}");
            }
        }

        // Locations list.
        if (scenario.Locations.Count > 0)
        {
            sb.Append("  Locations: ");
            sb.AppendLine(string.Join(", ", scenario.Locations
                .Where(l => !string.IsNullOrWhiteSpace(l.Name))
                .Select(l => l.Name!.Trim())));
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        // Keep the title line + compress to summary.
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        var remaining = maxChars;

        // Always include first line (title).
        if (lines.Length > 0)
        {
            var firstLine = lines[0].TrimEnd('\r');
            sb.AppendLine(firstLine);
            remaining -= firstLine.Length + Environment.NewLine.Length;
        }

        // Try to include a summary line.
        if (remaining > 50 && lines.Length > 1)
        {
            var summary = "  (Context compressed to fit budget)";
            if (summary.Length <= remaining)
            {
                sb.Append(summary);
            }
        }

        var result = sb.ToString().TrimEnd();
        return string.IsNullOrEmpty(result) ? text[..Math.Min(maxChars, text.Length)] : result;
    }
}
