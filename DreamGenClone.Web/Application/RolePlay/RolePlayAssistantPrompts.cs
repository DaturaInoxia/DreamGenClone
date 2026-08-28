using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using System.Text;
using System.Text.RegularExpressions;

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
            .Select(x => StripPhaseGuidanceMarkers(x.GuidanceText.Trim()))
            .Where(x => !string.IsNullOrWhiteSpace(x))
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
            .Select(x => StripPhaseGuidanceMarkers(x.DirectiveText.Trim()))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Removes theme phase-guidance markers (<c>[BeatStyle:*]</c>, <c>[Pacing:*]</c>,
    /// <c>[TimeShift:*]</c>, <c>[Granularity:*]</c>, <c>[ClimaxMode:*]</c>,
    /// <c>[Aftermath:*]</c>, <c>[Deepening:*]</c>, <c>[ScenePresence]</c>) from guidance
    /// prose before it is rendered to the model or UI. Markers are engine-side control
    /// signals parsed from <see cref="RPTheme.PhaseGuidance"/> by the scene-direction
    /// resolver — they must not leak as literal text into prompts. Marker parsing is
    /// unaffected: resolution reads the raw guidance text, not these rendered lines.
    /// </summary>
    public static string StripPhaseGuidanceMarkers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return PhaseGuidanceMarkerRegex.Replace(text, string.Empty).Trim();
    }

    private static readonly Regex PhaseGuidanceMarkerRegex = new(
        @"\[(BeatStyle|Pacing|TimeShift|Granularity|ClimaxMode|Aftermath|Deepening|ScenePresence)(?::[A-Za-z0-9-]+)?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsEpisodicBeatStyle(RPTheme? activeTheme, string phase)
    {
        if (activeTheme is null) return false;
        return activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Any(x => x.GuidanceText.Contains("[BeatStyle:episodic]", StringComparison.OrdinalIgnoreCase));
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
    /// Detects the [Aftermath:husband-contrast] phase-guidance marker (B-056).
    /// When true, encounter boundaries in the phase trigger an AftermathCoupleInteraction
    /// closure turn: the wife gets dressed, returns to the normal setting, interacts with
    /// her husband, and acts normal — the contrast between secret reality and ordinary
    /// performance is the narrative point. Works in any non-Reset phase; Reset is explicitly
    /// excluded (out of scope per spec).
    /// </summary>
    public static bool IsAftermathHusbandContrast(RPTheme? activeTheme, string phase)
    {
        if (activeTheme is null) return false;
        if (string.Equals(phase, "Reset", StringComparison.OrdinalIgnoreCase)) return false;
        return activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Any(x => x.GuidanceText.Contains("[Aftermath:husband-contrast]", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the pacing mode declared by [Pacing:slow], [Pacing:medium], or [Pacing:fast]
    /// in the theme's phase guidance for the given phase. Returns null if no marker is present.
    /// When no marker is present, no scene writing directive is injected.
    /// </summary>
    public static string? GetPacingMode(RPTheme? activeTheme, string phase)
    {
        if (activeTheme is null || activeTheme.PhaseGuidance.Count == 0)
            return null;

        var guidanceTexts = activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.GuidanceText)
            .Where(x => !string.IsNullOrWhiteSpace(x));

        foreach (var text in guidanceTexts)
        {
            if (text.Contains("[Pacing:fast]", StringComparison.OrdinalIgnoreCase))
                return "fast";
            if (text.Contains("[Pacing:medium]", StringComparison.OrdinalIgnoreCase))
                return "medium";
            if (text.Contains("[Pacing:slow]", StringComparison.OrdinalIgnoreCase))
                return "slow";
        }

        return null;
    }

    /// <summary>
    /// Returns the word target marker declared by [targetwords:small], [targetwords:medium],
    /// or [targetwords:large] in the theme's phase guidance for the given phase.
    /// Returns null if no marker is present — the caller applies the default [small].
    /// </summary>
    public static string? GetWordTargetMarker(RPTheme? activeTheme, string phase)
    {
        if (activeTheme is null || activeTheme.PhaseGuidance.Count == 0)
            return null;

        var guidanceTexts = activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.GuidanceText)
            .Where(x => !string.IsNullOrWhiteSpace(x));

        foreach (var text in guidanceTexts)
        {
            if (text.Contains("[targetwords:large]", StringComparison.OrdinalIgnoreCase))
                return "large";
            if (text.Contains("[targetwords:medium]", StringComparison.OrdinalIgnoreCase))
                return "medium";
            if (text.Contains("[targetwords:small]", StringComparison.OrdinalIgnoreCase))
                return "small";
        }

        return null;
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
            promptBuilder.AppendLine($"CHARACTER TENDENCY — {label} behavioral frame (yields to theme contract): {frameText}");
            if (guidance.CharacterStatStateTexts.TryGetValue(label, out var statStateText))
            {
                promptBuilder.AppendLine($"CHARACTER TENDENCY — {label} current state (yields to theme contract): {statStateText}");
            }
        }

        foreach (var (label, statStateText) in guidance.CharacterStatStateTexts)
        {
            if (!guidance.CharacterBehavioralFrames.ContainsKey(label))
            {
                promptBuilder.AppendLine($"CHARACTER TENDENCY — {label} current state (yields to theme contract): {statStateText}");
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
            promptBuilder.AppendLine($"- Cooldown turns in current state: {snapshot.TurnsInCurrentState}");
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
