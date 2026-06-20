using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using System.Text;

namespace DreamGenClone.Web.Application.RolePlay;

public static class RolePlayAssistantPrompts
{
    public static IReadOnlyList<string> GetThemePhaseGuidanceLines(
        RPTheme? activeTheme,
        string phase)
    {
        if (activeTheme is null || activeTheme.PhaseGuidance.Count == 0)
        {
            return [];
        }

        return activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.IsNullOrWhiteSpace(x.GuidanceText))
            .Select(x => x.GuidanceText.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> GetThemePhaseDirectiveLines(
        RPTheme? activeTheme,
        string phase)
    {
        if (activeTheme is null || activeTheme.PhaseGuidance.Count == 0)
        {
            return [];
        }

        return activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.IsNullOrWhiteSpace(x.DirectiveText))
            .Select(x => x.DirectiveText.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsEpisodicBeatStyle(RPTheme? activeTheme, string phase)
    {
        if (activeTheme is null) return false;
        return activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Any(x => x.GuidanceText.Contains("[BeatStyle:episodic]", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsQuickFinishClimaxMode(RPTheme? activeTheme, string phase)
    {
        if (activeTheme is null) return false;
        return activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Any(x => x.GuidanceText.Contains("[ClimaxMode:quick-finish]", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Detects the [ClimaxMode:multi-encounter] phase-guidance marker. When true, the Climax
    /// phase is paced as multiple discrete encounters whose boundaries are detected by the
    /// sync encounter-completed semantic inference call. Theme-scoped — dormant for themes
    /// without the marker.
    /// </summary>
    public static bool IsMultiEncounterClimax(RPTheme? activeTheme, string phase)
    {
        if (activeTheme is null) return false;
        return activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Any(x => x.GuidanceText.Contains("[ClimaxMode:multi-encounter]", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Validates that a theme does not declare both [ClimaxMode:multi-encounter] and
    /// [ClimaxMode:quick-finish] in the same phase — they are mutually exclusive pacing modes.
    /// Throws InvalidOperationException with an explicit diagnostic if both are present.
    /// </summary>
    public static void EnsureClimaxModeMutualExclusion(RPTheme? activeTheme, string phase)
    {
        if (activeTheme is null) return;
        var phaseGuidance = activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var hasMulti = phaseGuidance.Any(x => x.GuidanceText.Contains("[ClimaxMode:multi-encounter]", StringComparison.OrdinalIgnoreCase));
        var hasQuick = phaseGuidance.Any(x => x.GuidanceText.Contains("[ClimaxMode:quick-finish]", StringComparison.OrdinalIgnoreCase));
        if (hasMulti && hasQuick)
        {
            throw new InvalidOperationException(
                $"ClimaxModeConflict: theme '{activeTheme.Id}' phase '{phase}' declares both [ClimaxMode:multi-encounter] and [ClimaxMode:quick-finish]. These are mutually exclusive pacing modes — remove one.");
        }
    }

    public static bool AllowsWithinTimeframeTimeShift(RPTheme? activeTheme, string phase)
    {
        if (activeTheme is null) return false;
        return activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Any(x =>
                x.GuidanceText.Contains("[TimeShift:within-timeframe]", StringComparison.OrdinalIgnoreCase)
                || x.GuidanceText.Contains("Responses may skip forward within the time frame", StringComparison.OrdinalIgnoreCase)
                || x.GuidanceText.Contains("a new response does not have to be the immediate next moment", StringComparison.OrdinalIgnoreCase));
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

        var clampedMax = Math.Clamp(maxNotes, 1, 30);
        var phaseWeights = BuildSectionWeightsForPhase(phase);

        return activeTheme.AIGenerationNotes
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Where(x => x.Section != RPThemeAIGuidanceSection.HardConstraint)
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

    public static IReadOnlyList<string> GetThemeHardConstraintLines(
        RPTheme? activeTheme,
        int maxConstraints)
    {
        if (activeTheme is null || activeTheme.AIGenerationNotes.Count == 0)
        {
            return [];
        }

        var clampedMax = Math.Clamp(maxConstraints, 1, 20);

        return activeTheme.AIGenerationNotes
            .Where(x => x.Section == RPThemeAIGuidanceSection.HardConstraint)
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Text.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
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

        foreach (var (label, frameText) in guidance.CharacterBehavioralFrames)
        {
            promptBuilder.AppendLine($"HARD CONSTRAINT — {label} behavioral frame (authoritative character state): {frameText}");
            if (guidance.CharacterStatStateTexts.TryGetValue(label, out var statStateText))
            {
                promptBuilder.AppendLine($"HARD CONSTRAINT — {label} current state (authoritative): {statStateText}");
            }
        }

        foreach (var (label, statStateText) in guidance.CharacterStatStateTexts)
        {
            if (!guidance.CharacterBehavioralFrames.ContainsKey(label))
            {
                promptBuilder.AppendLine($"HARD CONSTRAINT — {label} current state (authoritative): {statStateText}");
            }
        }

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
        => BuildFramingGuards(phase, activeScenarioId, activeTheme: null);

    public static IReadOnlyList<string> BuildFramingGuards(string phase, string? activeScenarioId, RPTheme? activeTheme)
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
            var isQuickFinishClimax = IsQuickFinishClimaxMode(activeTheme, phase);

            guards.Add("Deliver high-intensity culmination consistent with established relational dynamics.");
            guards.Add("Write with explicit positional and sensory detail; name body parts and movements specifically.");
            guards.Add("Narrative urgency (time pressure, risk of interruption) must increase writing intensity, not truncate scene length.");

            if (isQuickFinishClimax)
            {
                guards.Add("QUICK-FINISH CLIMAX MODE: Resolve the finale as urgent, frantic, and quick-release focused without reducing explicit detail.");
                guards.Add("Urgency is conveyed through tone, pacing pressure, and interruption risk — not by shortening the scene into vague or minimal content.");
                guards.Add("The encounter may span multiple turns/interactions with rich explicit detail before completion, but keep one primary position/focal act rather than running a full beat-sheet tour.");
                guards.Add("All explicit contact must remain plausibly hidden from the husband and nearby guests in the moment; maintain believable concealment and deniability.");
                guards.Add("Do not write overtly visible acts in open view (for example obvious neck-kissing, openly exposed groping, or other unmistakable public signals) when husband/bystanders are in direct line of sight.");
                guards.Add("Choose one completion position (oral or penetrative sex) for the encounter, sustain it with explicit sensory detail, then return immediately to social composure.");
                guards.Add("If close-proximity oral/penetrative completion is plausible, keep it nearby. Otherwise, a brief sneak-off to a secluded spot is allowed for rapid release, followed by immediate return.");
                guards.Add("Do not force beat/sub-beat progression or multi-position variation in this finale.");
            }
            else
            {
                guards.Add("Every turn must advance the scene to a new beat. Do not repeat the same physical act, position, or sensation that was the focus of the immediately preceding turn.");
                guards.Add("Within each stage of physical intimacy, vary position, tempo, who is the focus, and specific sensations each turn. Same stage is fine — same description is forbidden.");
            }
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

        var clampedMax = Math.Clamp(maxNotes, 1, 30);
        var includeFormula = clampedInfluence >= 60;
        var selectedNotes = GetPhaseRelevantThemeAIGuidanceNotes(activeTheme, phase, clampedMax, includeFormula);

        if (selectedNotes.Count == 0)
        {
            return;
        }

        var strengthLabel = clampedInfluence >= 80 ? "authoritative directives" : clampedInfluence >= 50 ? "strong guidance" : "soft hints";
        promptBuilder.AppendLine($"Theme AI Guidance ({strengthLabel}, influence={clampedInfluence}%):");
        foreach (var note in selectedNotes)
        {
            promptBuilder.AppendLine($"- {note.Text.Trim()}");
        }

        var closingNote = clampedInfluence >= 80
            ? "Apply these as authoritative directives; follow them unless the user explicitly overrides."
            : "Apply these as soft guidance only; avoid repetitive restatement and do not force them if they conflict with immediate user direction or safety constraints.";
        promptBuilder.AppendLine(closingNote);
    }

    public static void AppendThemeHardConstraints(
        StringBuilder promptBuilder,
        RPTheme? activeTheme,
        int maxConstraints)
    {
        ArgumentNullException.ThrowIfNull(promptBuilder);

        var constraints = GetThemeHardConstraintLines(activeTheme, maxConstraints);
        if (constraints.Count == 0)
        {
            return;
        }

        promptBuilder.AppendLine("Theme Hard Constraints (authoritative):");
        foreach (var constraint in constraints)
        {
            promptBuilder.AppendLine($"- HARD CONSTRAINT: {constraint}");
        }
    }

    public static void AppendThemeMachineGuidance(
        StringBuilder promptBuilder,
        ThemeMachineSessionSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(promptBuilder);

        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.CurrentStateCode))
        {
            return;
        }

        promptBuilder.AppendLine("Theme Machine Continuity:");
        promptBuilder.AppendLine($"- Machine Key: {snapshot.MachineKey}");
        promptBuilder.AppendLine($"- Definition: {snapshot.DefinitionId} v{snapshot.DefinitionVersion}");
        promptBuilder.AppendLine($"- Current State: {snapshot.CurrentStateCode}");

        if (string.Equals(snapshot.CurrentStateCode, "ReturnBeatRequired", StringComparison.OrdinalIgnoreCase))
        {
            promptBuilder.AppendLine("- HARD CONSTRAINT: Return beat is required before any new disappearance beat can be introduced.");
            promptBuilder.AppendLine("- HARD CONSTRAINT: Keep narrative focus on return/repair continuity and avoid initiating a fresh disappearance arc.");
        }
        else if (string.Equals(snapshot.CurrentStateCode, "ReintegrationCooldown", StringComparison.OrdinalIgnoreCase))
        {
            promptBuilder.AppendLine("- HARD CONSTRAINT: Reintegration cooldown is active; keep disappearance beats blocked until cooldown obligations are met.");
            promptBuilder.AppendLine($"- Cooldown interactions in current state: {snapshot.TurnsInCurrentState}");
            promptBuilder.AppendLine($"- Return beat completed: {(snapshot.ReturnBeatCompleted ? "yes" : "no")}");
        }
        else if (string.Equals(snapshot.CurrentStateCode, "NextDisappearanceEligible", StringComparison.OrdinalIgnoreCase))
        {
            promptBuilder.AppendLine("- Continuity note: Next disappearance eligibility has been reached; maintain consistency with established machine progression.");
        }
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
