using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using System.Text;

namespace DreamGenClone.Web.Application.RolePlay;

public static class RolePlayAssistantPrompts
{
    public static IReadOnlyList<string> GetThemePhaseGuidanceLines(
        RPTheme? activeTheme,
        string phase,
        int maxLines = 3)
    {
        if (activeTheme is null || activeTheme.PhaseGuidance.Count == 0)
        {
            return [];
        }

        var clampedMax = Math.Clamp(maxLines, 1, 8);
        return activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.IsNullOrWhiteSpace(x.GuidanceText))
            .Select(x => x.GuidanceText.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(clampedMax)
            .ToList();
    }

    public static IReadOnlyList<RPThemeAIGuidanceNote> GetPhaseRelevantThemeAIGuidanceNotes(
        RPTheme? activeTheme,
        string phase,
        int maxNotes,
        bool includeFormulaNotes = false)
    {
        if (activeTheme is null || activeTheme.AIGenerationNotes.Count == 0)
        {
            return [];
        }

        var clampedMax = Math.Clamp(maxNotes, 1, 12);
        var phaseWeights = BuildSectionWeightsForPhase(phase);

        return activeTheme.AIGenerationNotes
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Where(x => includeFormulaNotes || x.Section != RPThemeAIGuidanceSection.FitFormula)
            .Select(x => new
            {
                Note = x,
                SectionWeight = phaseWeights.TryGetValue(x.Section, out var w) ? w : 999
            })
            .OrderBy(x => x.SectionWeight)
            .ThenBy(x => x.Note.SortOrder)
            .Select(x => x.Note)
            .DistinctBy(x => x.Text.Trim(), StringComparer.OrdinalIgnoreCase)
            .Take(clampedMax)
            .ToList();
    }

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

        if (phase == "BuildUp")
        {
            guards.Add("This is the BuildUp phase — tension and anticipation only. Do not write explicit sexual acts, physical consummation, or explicit physical contact of a sexual nature.");
            guards.Add("Characters may flirt, exchange glances, build emotional tension, and suggestively interact, but all explicit escalation must be withheld until the scene advances past this phase.");
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

    public static void AppendThemeAIGuidance(
        StringBuilder promptBuilder,
        RPTheme? activeTheme,
        string phase,
        int influencePercent,
        int maxNotes)
    {
        ArgumentNullException.ThrowIfNull(promptBuilder);

        if (activeTheme is null || activeTheme.AIGenerationNotes.Count == 0)
        {
            return;
        }

        var clampedInfluence = Math.Clamp(influencePercent, 0, 100);
        if (clampedInfluence == 0)
        {
            return;
        }

        var clampedMax = Math.Clamp(maxNotes, 1, 12);
        var weightedMax = Math.Clamp((int)Math.Round(clampedMax * (clampedInfluence / 100.0), MidpointRounding.AwayFromZero), 1, clampedMax);
        var includeFormula = clampedInfluence >= 60;
        var selectedNotes = GetPhaseRelevantThemeAIGuidanceNotes(activeTheme, phase, weightedMax, includeFormula);

        if (selectedNotes.Count == 0)
        {
            return;
        }

        promptBuilder.AppendLine($"Theme AI Guidance (soft hints, influence={clampedInfluence}%):");
        foreach (var note in selectedNotes)
        {
            promptBuilder.AppendLine($"- {note.Text.Trim()}");
        }

        promptBuilder.AppendLine("Apply these as soft guidance only; avoid repetitive restatement and do not force them if they conflict with immediate user direction or safety constraints.");
    }

    private static IReadOnlyDictionary<RPThemeAIGuidanceSection, int> BuildSectionWeightsForPhase(string phase)
    {
        var defaultWeights = new Dictionary<RPThemeAIGuidanceSection, int>
        {
            [RPThemeAIGuidanceSection.KeyScenarioElement] = 1,
            [RPThemeAIGuidanceSection.InteractionDynamics] = 2,
            [RPThemeAIGuidanceSection.Avoidance] = 3,
            [RPThemeAIGuidanceSection.ScenarioDistinction] = 4,
            [RPThemeAIGuidanceSection.Variation] = 5,
            [RPThemeAIGuidanceSection.FitPattern] = 6,
            [RPThemeAIGuidanceSection.FitNote] = 7,
            [RPThemeAIGuidanceSection.FitFormula] = 8
        };

        if (string.Equals(phase, "BuildUp", StringComparison.OrdinalIgnoreCase))
        {
            defaultWeights[RPThemeAIGuidanceSection.KeyScenarioElement] = 1;
            defaultWeights[RPThemeAIGuidanceSection.Variation] = 2;
            defaultWeights[RPThemeAIGuidanceSection.ScenarioDistinction] = 3;
        }
        else if (string.Equals(phase, "Committed", StringComparison.OrdinalIgnoreCase))
        {
            defaultWeights[RPThemeAIGuidanceSection.InteractionDynamics] = 1;
            defaultWeights[RPThemeAIGuidanceSection.KeyScenarioElement] = 2;
            defaultWeights[RPThemeAIGuidanceSection.Avoidance] = 3;
            defaultWeights[RPThemeAIGuidanceSection.FitPattern] = 4;
        }
        else if (string.Equals(phase, "Approaching", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(phase, "Climax", StringComparison.OrdinalIgnoreCase))
        {
            defaultWeights[RPThemeAIGuidanceSection.InteractionDynamics] = 1;
            defaultWeights[RPThemeAIGuidanceSection.Avoidance] = 2;
            defaultWeights[RPThemeAIGuidanceSection.FitPattern] = 3;
            defaultWeights[RPThemeAIGuidanceSection.KeyScenarioElement] = 4;
        }
        else if (string.Equals(phase, "Reset", StringComparison.OrdinalIgnoreCase))
        {
            defaultWeights[RPThemeAIGuidanceSection.ScenarioDistinction] = 1;
            defaultWeights[RPThemeAIGuidanceSection.Variation] = 2;
            defaultWeights[RPThemeAIGuidanceSection.Avoidance] = 3;
        }

        return defaultWeights;
    }
}
