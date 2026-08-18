using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Single source of human-readable labels/descriptions for the continuation settings
/// popup and the <c>ContinuationOverrideSlot</c> rendering. Mirrors the enum doc
/// comments in <see cref="SceneDirection"/> so UI prose and injected prose stay consistent.
/// </summary>
public static class ContinuationMarkerCatalog
{
    public const string PacingLabel = "Pacing";
    public const string BeatStyleLabel = "Beat Style";
    public const string TimeShiftLabel = "Time Shift";
    public const string GranularityLabel = "Granularity";
    public const string DeepeningLabel = "Deepening";
    public const string ScenePresenceLabel = "Scene Presence";
    public const string ClimaxModeLabel = "Climax Mode";
    public const string AftermathLabel = "Aftermath";
    public const string WordCountLabel = "Word Count";

    public static string DescribePacing(ScenePacing v) => v switch
    {
        ScenePacing.Slow => "Stay within the current beat — deepen, do not leap.",
        ScenePacing.Fast => "Compress multiple beats into one response — push the story forward rapidly.",
        _ => "Advance one beat with forward momentum.",
    };

    public static string DescribeBeatScope(BeatScope v) => v switch
    {
        BeatScope.Single => "Resolve this moment in one turn.",
        BeatScope.Short => "Build the moment across 3 turns.",
        BeatScope.Extended => "Linger in this moment across 5 turns.",
        _ => "Build the moment across 3 turns.",
    };

    /// <summary>Turn budget (duration in turns) for a moment under the given Beat Style (Single=1, Short=3, Extended=5).</summary>
    public static int GetBeatStyleTurnBudget(BeatScope v) => v switch
    {
        BeatScope.Single => 1,
        BeatScope.Short => 3,
        BeatScope.Extended => 5,
        _ => 3
    };

    /// <summary>
    /// Per-response duration directive. Carries an explicit turn position and a hard
    /// negative on non-final turns so the model holds the moment instead of resolving it
    /// in a single response.
    /// </summary>
    public static string DescribeBeatStage(int turnsInBeat, int budget)
    {
        if (budget <= 1)
            return "This moment lasts a single turn — resolve it now.";
        var position = turnsInBeat + 1; // 1-based position of the turn about to be written
        if (position >= budget)
            return $"This moment ends this turn (turn {position} of {budget}) — bring it to its climax or conclusion now and move on.";
        if (position == 1)
            return $"This moment spans {budget} turns. You are on turn 1 of {budget} — establish it only. Do NOT bring the moment to its climax or conclusion this turn. End your response mid-action, before the resolution.";
        return $"This moment spans {budget} turns. You are on turn {position} of {budget} — develop it further. Do NOT bring the moment to its climax or conclusion this turn. End your response mid-action, before the resolution.";
    }

    public static string DescribeTimeShift(TimeShiftPolicy v) => v switch
    {
        TimeShiftPolicy.None => "No time skip — continue from the exact moment.",
        TimeShiftPolicy.Small => "Minutes to a few hours.",
        TimeShiftPolicy.Medium => "Hours to half a day.",
        TimeShiftPolicy.Large => "A day or more.",
        _ => "Hours to half a day.",
    };

    public static string DescribeGranularity(NarrativeGranularity v) => v switch
    {
        NarrativeGranularity.Micro => "One response = one moment.",
        NarrativeGranularity.Meso => "One response = one scene/beat.",
        NarrativeGranularity.Macro => "One response = a day or significant span.",
        NarrativeGranularity.Montage => "One response = multiple days to weeks.",
        _ => "One response = one scene/beat.",
    };

    public static string DescribeDeepening(DeepeningPolicy v) => v switch
    {
        DeepeningPolicy.SubsequentActors => "Positions 2+ deepen the current beat from their POV — never advance.",
        _ => "Standard pacing applies to all actors.",
    };

    public static string DescribeScenePresence(bool v)
        => v ? "Stay present — no time skip." : "No stay-present contract.";

    public static string DescribeMultiEncounterClimax(bool v)
        => v ? "Split the Climax into several discrete encounters with time-skips." : "Single continuous Climax.";

    public static string DescribeAftermathHusbandContrast(bool v)
        => v ? "After an encounter, act normal to your husband — the secret-vs-ordinary contrast is the point." : "No aftermath closure turn.";

    // ── Word-count presets (mirrors the [targetwords:*] mapping) ──
    public static readonly IReadOnlyList<(string Key, int Min, int Max)> WordCountPresets =
        new (string, int, int)[]
        {
            ("small", 200, 400),
            ("medium", 300, 700),
            ("large", 500, 1000),
        };
}
