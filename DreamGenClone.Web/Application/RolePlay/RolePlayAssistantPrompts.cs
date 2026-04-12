using DreamGenClone.Application.StoryAnalysis.Models;
using System.Text;

namespace DreamGenClone.Web.Application.RolePlay;

public static class RolePlayAssistantPrompts
{
    public static void AppendScenarioGuidance(
        StringBuilder promptBuilder,
        ScenarioGuidanceContext guidance,
        IReadOnlyList<string> framingGuards)
    {
        ArgumentNullException.ThrowIfNull(promptBuilder);
        ArgumentNullException.ThrowIfNull(guidance);

        promptBuilder.AppendLine("Scenario Guidance:");
        promptBuilder.AppendLine($"- Narrative Phase: {guidance.Phase}");

        if (!string.IsNullOrWhiteSpace(guidance.ActiveScenarioId))
        {
            promptBuilder.AppendLine($"- Active Scenario: {guidance.ActiveScenarioId}");
        }

        promptBuilder.AppendLine($"- Guidance: {guidance.GuidanceText}");

        if (guidance.ExcludedScenarioIds.Count > 0)
        {
            promptBuilder.AppendLine($"- Exclude contradictory framing for: {string.Join(", ", guidance.ExcludedScenarioIds)}");
        }

        foreach (var guard in framingGuards)
        {
            promptBuilder.AppendLine($"- Guard: {guard}");
        }
    }

    public static IReadOnlyList<string> BuildFramingGuards(string phase, string? activeScenarioId)
    {
        var guards = new List<string>();

        if (string.IsNullOrWhiteSpace(activeScenarioId))
        {
            return guards;
        }

        if (phase is "Committed" or "Approaching" or "Climax")
        {
            guards.Add($"Keep all major beats aligned to '{activeScenarioId}'.");
            guards.Add("Do not pivot to a competing scenario unless the user explicitly overrides.");
        }

        if (phase == "Climax")
        {
            guards.Add("Deliver high-intensity culmination consistent with established relational dynamics.");
        }

        return guards;
    }
}
