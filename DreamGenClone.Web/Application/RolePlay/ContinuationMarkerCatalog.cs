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
        BeatScope.Short => "Build the moment across 2–3 turns.",
        BeatScope.Extended => "Linger in this moment for 4+ turns (episodic).",
        _ => "Build the moment across 2–3 turns.",
    };

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
