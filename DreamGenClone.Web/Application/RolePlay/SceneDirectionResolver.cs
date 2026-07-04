using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Resolves a <see cref="SceneDirection"/> for a single continuation prompt from the narrative
/// phase, the active theme's phase-guidance markers, and the climax sub-phase.
/// Pure and deterministic — no IO, safe to unit-test.
///
/// Resolution precedence:
///   1. Theme phase-guidance markers (<c>[Pacing:*]</c>, <c>[TimeShift:within-timeframe]</c>,
///      <c>[ClimaxMode:quick-finish|multi-encounter]</c>, <c>[BeatStyle:episodic]</c>) override
///      the phase defaults for the dimensions they declare.
///   2. Phase-based defaults (see <see cref="PhaseDefaultPacing"/> / <see cref="PhaseDefaultBeatScope"/>
///      / <see cref="PhaseDefaultTimeShift"/>), following the table in the design spec.
/// </summary>
public static class SceneDirectionResolver
{
    /// <summary>
    /// Resolves the scene direction. All inputs are read-only; the returned object is immutable.
    /// </summary>
    /// <param name="phase">Current narrative phase name (case-insensitive): Opening, BuildUp, Committed, Approaching, Climax, Reset.</param>
    /// <param name="activeTheme">Active RP theme (may be null). Markers are read from its phase guidance.</param>
    /// <param name="climaxSubPhase">Climax subdivision; ignored for non-Climax phases.</param>
    /// <param name="intent">Prompt intent; Instruction prompts always resolve a minimal direction.</param>
    public static SceneDirection Resolve(
        string phase,
        RPTheme? activeTheme,
        ClimaxSubPhase climaxSubPhase,
        PromptIntent intent)
    {
        var normalizedPhase = NormalizePhase(phase);

        var pacing = ResolvePacing(normalizedPhase, activeTheme, climaxSubPhase);
        var beatScope = ResolveBeatScope(normalizedPhase, activeTheme, climaxSubPhase);
        var timeShift = ResolveTimeShift(normalizedPhase, activeTheme, climaxSubPhase);
        var deepening = ResolveDeepening(normalizedPhase, activeTheme);
        var requireScenePresence = ResolveScenePresence(normalizedPhase, activeTheme);

        return new SceneDirection
        {
            Pacing = pacing,
            BeatScope = beatScope,
            TimeShift = timeShift,
            Deepening = deepening,
            RequireScenePresence = requireScenePresence,
            ClimaxSubPhase = normalizedPhase == NarrativePhase.Climax ? climaxSubPhase : ClimaxSubPhase.None
        };
    }

    private static NarrativePhase? NormalizePhase(string? phase)
        => phase?.ToLowerInvariant() switch
        {
            "opening" => NarrativePhase.Opening,
            "buildup" => NarrativePhase.BuildUp,
            "committed" => NarrativePhase.Committed,
            "approaching" => NarrativePhase.Approaching,
            "climax" => NarrativePhase.Climax,
            "reset" => NarrativePhase.Reset,
            _ => null
        };

    private static ScenePacing ResolvePacing(
        NarrativePhase? normalizedPhase, RPTheme? activeTheme, ClimaxSubPhase climaxSubPhase)
    {
        // Tier 1: Profile directive takes the resolved default (handled by caller via DirectorNote).
        // Tier 2: Theme phase-guidance marker.
        var markerPacing = GetPacingMarker(activeTheme, normalizedPhase?.ToString() ?? "");
        if (markerPacing.HasValue)
            return markerPacing.Value;

        // Tier 3: Phase-based defaults.
        return PhaseDefaultPacing(normalizedPhase);
    }

    private static BeatScope ResolveBeatScope(
        NarrativePhase? normalizedPhase, RPTheme? activeTheme, ClimaxSubPhase climaxSubPhase)
    {
        // Tier 2: Theme markers [BeatStyle:episodic], [BeatStyle:short], [BeatStyle:single]
        // checked in all phases — not restricted to Climax only.
        if (activeTheme is not null && normalizedPhase.HasValue)
        {
            var phase = normalizedPhase.Value.ToString();
            if (HasMarker(activeTheme, phase, "BeatStyle:episodic"))
                return BeatScope.Extended;
            if (HasMarker(activeTheme, phase, "BeatStyle:short"))
                return BeatScope.Short;
            if (HasMarker(activeTheme, phase, "BeatStyle:single"))
                return BeatScope.Single;
        }

        // Tier 3: Phase-based defaults.
        return PhaseDefaultBeatScope(normalizedPhase);
    }

    private static TimeShiftPolicy ResolveTimeShift(
        NarrativePhase? normalizedPhase, RPTheme? activeTheme, ClimaxSubPhase climaxSubPhase)
    {
        // Tier 2: Theme marker [TimeShift:within-timeframe] → TimeShiftPolicy.Small.
        if (activeTheme is not null && normalizedPhase.HasValue)
        {
            var phase = normalizedPhase?.ToString() ?? "";
            if (HasMarker(activeTheme, phase, "TimeShift:within-timeframe"))
                return TimeShiftPolicy.Small;
        }

        // Tier 3: Phase-based defaults.
        return PhaseDefaultTimeShift(normalizedPhase);
    }

    private static DeepeningPolicy ResolveDeepening(
        NarrativePhase? normalizedPhase, RPTheme? activeTheme)
    {
        if (activeTheme is null || !normalizedPhase.HasValue)
            return DeepeningPolicy.None;

        var phase = normalizedPhase?.ToString() ?? "";
        if (HasMarker(activeTheme, phase, "Deepening:subsequent-actors"))
            return DeepeningPolicy.SubsequentActors;

        return DeepeningPolicy.None;
    }

    // ── Helper: check if a marker exists in the theme's phase guidance ──
    private static bool ResolveScenePresence(NarrativePhase? normalizedPhase, RPTheme? activeTheme)
    {
        if (activeTheme is not null && normalizedPhase.HasValue
            && HasMarker(activeTheme, normalizedPhase.Value.ToString(), "ScenePresence"))
            return true;
        return false;
    }

    private static bool HasMarker(RPTheme theme, string phase, string marker)
    {
        return theme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Any(x => x.GuidanceText.Contains($"[{marker}]", StringComparison.OrdinalIgnoreCase));
    }

    // ── Pacing marker resolution ──
    private static ScenePacing? GetPacingMarker(RPTheme? activeTheme, string phase)
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
                return ScenePacing.Fast;
            if (text.Contains("[Pacing:medium]", StringComparison.OrdinalIgnoreCase))
                return ScenePacing.Medium;
            if (text.Contains("[Pacing:slow]", StringComparison.OrdinalIgnoreCase))
                return ScenePacing.Slow;
        }

        return null;
    }

    // ── Phase defaults (tier 3) ──
    private static readonly Dictionary<NarrativePhase, ScenePacing> PhaseDefaultPacingMap = new()
    {
        [NarrativePhase.Opening] = ScenePacing.Medium,
        [NarrativePhase.BuildUp] = ScenePacing.Medium,
        [NarrativePhase.Committed] = ScenePacing.Medium,
        [NarrativePhase.Approaching] = ScenePacing.Medium,
        [NarrativePhase.Climax] = ScenePacing.Fast,
        [NarrativePhase.Reset] = ScenePacing.Slow
    };

    private static ScenePacing PhaseDefaultPacing(NarrativePhase? normalizedPhase)
        => normalizedPhase.HasValue && PhaseDefaultPacingMap.TryGetValue(normalizedPhase.Value, out var p)
            ? p : ScenePacing.Medium;

    private static readonly Dictionary<NarrativePhase, BeatScope> PhaseDefaultBeatScopeMap = new()
    {
        [NarrativePhase.Opening] = BeatScope.Short,
        [NarrativePhase.BuildUp] = BeatScope.Short,
        [NarrativePhase.Committed] = BeatScope.Short,
        [NarrativePhase.Approaching] = BeatScope.Short,
        [NarrativePhase.Climax] = BeatScope.Short,
        [NarrativePhase.Reset] = BeatScope.Single
    };

    private static BeatScope PhaseDefaultBeatScope(NarrativePhase? normalizedPhase)
        => normalizedPhase.HasValue && PhaseDefaultBeatScopeMap.TryGetValue(normalizedPhase.Value, out var b)
            ? b : BeatScope.Short;

    private static readonly Dictionary<NarrativePhase, TimeShiftPolicy> PhaseDefaultTimeShiftMap = new()
    {
        [NarrativePhase.Opening] = TimeShiftPolicy.Small,
        [NarrativePhase.BuildUp] = TimeShiftPolicy.Small,
        [NarrativePhase.Committed] = TimeShiftPolicy.Small,
        [NarrativePhase.Approaching] = TimeShiftPolicy.Small,
        [NarrativePhase.Climax] = TimeShiftPolicy.Medium,
        [NarrativePhase.Reset] = TimeShiftPolicy.None
    };

    private static TimeShiftPolicy PhaseDefaultTimeShift(NarrativePhase? normalizedPhase)
        => normalizedPhase.HasValue && PhaseDefaultTimeShiftMap.TryGetValue(normalizedPhase.Value, out var t)
            ? t : TimeShiftPolicy.Small;
}