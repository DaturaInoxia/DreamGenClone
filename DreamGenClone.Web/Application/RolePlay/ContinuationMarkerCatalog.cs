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
    public const string ClimaxModeLabel = "Climax Mode";
    public const string AftermathLabel = "Aftermath";
    public const string WordCountLabel = "Word Count";

    // ── B-089: Tempo / Span primary labels ──
    public const string TempoLabel = "Tempo";
    public const string SpanLabel = "Span";

    /// <summary>
    /// B-089 — full Tempo directive for a lead actor (position 1). Returned WITHOUT the
    /// "HARD CONSTRAINT — " prefix (the slot prepends it, matching the other HCs). Each
    /// value carries an imperative verb + explicit ceiling, replacing the old Pacing +
    /// TimeShift + Granularity trio.
    /// B-095 — Linger is turn-aware via <paramref name="isFinalBeatTurn"/>. On the final beat
    /// turn it must yield to Span's "conclude now" (its emphatic stay-in-moment wording would
    /// otherwise override the final-turn Span and the moment would loop instead of concluding).
    /// Non-Linger tempos ignore the flag. The popup preview passes no flag → base wording.
    /// </summary>
    public static string DescribeTempo(SceneTempo v, bool isFinalBeatTurn = false) => v switch
    {
        SceneTempo.Linger when isFinalBeatTurn =>
            "Tempo: Linger. This is the final turn of this moment — conclude it now within the exact present: resolve the physical or emotional beat that has been building, then let the moment settle. Do not skip time or leap to a new scene; close the moment here rather than jumping past it.",
        SceneTempo.Linger =>
            "Tempo: Linger. Stay in this exact moment — do not advance time, do not leap to a new beat, position, or location. Deepen what is happening right now and move it forward within the moment: escalate the sensory detail, the internal reaction, the mounting tension — each response advances the moment one step closer to its resolution without leaving it or advancing time. One response covers one moment, not a scene.",
        SceneTempo.Steady => "Tempo: Steady. Advance the scene by one beat, then stop. You may shift time forward by minutes to a few hours — no more than half a day. One response covers one scene or beat. Do not skip ahead by a day or more, and do not leap to a new location without a transition.",
        SceneTempo.Push => "Tempo: Push. Advance through two to three beats this response — compress toward the natural resolution of this moment. You may shift time by hours to half a day. Do not compress an entire arc (start to climax) into one response unless this is the final beat of the arc. One response covers one scene or beat of compressed action.",
        _ => "Tempo: Leap. Advance time by a day or more — skip routine time and land on the next meaningful moment. Summarize what passed in a sentence or two; focus the response on the new day, the new circumstance. One response covers a day, a significant span, or multiple days to weeks. Do not stay in the previous moment.",
    };

    /// <summary>
    /// B-089 / B-094 (Design Decision D-1) — subsequent-actor (position 2+) directive. ONLY the
    /// first actor sets the pace; position 2+ get one tempo-INDEPENDENT line that CONTINUES the
    /// beat at that pace (never speeding it up, skipping ahead, or restarting it). Reworded
    /// 2026-08-19: the original "do not advance time / introduce a new beat" froze the whole
    /// turn, which — combined with Linger — trapped the model in a repeated scene (observed live
    /// in session f1d424cc). The first actor still owns the pace; subsequent actors just move it
    /// forward within it. Returned WITHOUT the "HARD CONSTRAINT — " prefix.
    /// </summary>
    public static string DescribeSubsequentPace() =>
        "Subsequent actor: The first actor has set the pace — continue the beat at that pace from your character's perspective. Move it forward without speeding it up, skipping ahead, or restarting it.";

    /// <summary>
    /// B-089 — full Span directive (duration) for the lead actor, turn-position-aware.
    /// Returned WITHOUT the "HARD CONSTRAINT — " prefix. Replaces the Beat Style stage line.
    /// </summary>
    public static string DescribeSpan(SceneSpan v, int turnsInBeat, int budget, int? currentAbsoluteTurn = null)
        => $"Span: {v}. {DescribeBeatStage(turnsInBeat, budget, currentAbsoluteTurn)}";

    /// <summary>Turn budget for a <see cref="SceneSpan"/> (Moment=1, Scene=3, ExtendedArc=5). Delegates to <see cref="SceneDirection.SpanTurnBudget"/>.</summary>
    public static int GetSpanTurnBudget(SceneSpan v) => SceneDirection.SpanTurnBudget(v);

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
    public static string DescribeBeatStage(int turnsInBeat, int budget, int? currentAbsoluteTurn = null)
    {
        if (budget <= 1)
            return "This moment lasts a single turn — resolve it now.";

        var position = turnsInBeat + 1; // 1-based position of the turn about to be written
        // Absolute Interaction-History turn anchor (B-090): when the absolute turn about to
        // be written is known, name the beat's starting turn so the model can map the beat
        // back onto the numbered turns in Interaction History (e.g. Turn 35 → 36 → 37).
        // Callers without a current turn (popup preview, unit tests) pass null → relative-only.
        var beatStartTurn = currentAbsoluteTurn.HasValue ? currentAbsoluteTurn.Value - turnsInBeat : 0;

        if (position >= budget)
        {
            var begin = currentAbsoluteTurn.HasValue ? $", which began at Turn {beatStartTurn}" : "";
            return $"This moment ends this turn (turn {position} of {budget}{begin}) — bring it to its climax or conclusion now and move on.";
        }
        if (position == 1)
        {
            var begin = currentAbsoluteTurn.HasValue ? $", beginning this turn (Turn {currentAbsoluteTurn.Value})" : "";
            return $"This moment spans {budget} turns{begin}. You are on turn 1 of {budget} — establish it only. Do NOT bring the moment to its climax or conclusion this turn. End your response mid-action, before the resolution.";
        }
        var beginMid = currentAbsoluteTurn.HasValue ? $", which began at Turn {beatStartTurn}" : "";
        return $"This moment spans {budget} turns{beginMid}. You are on turn {position} of {budget} — develop it further. Do NOT bring the moment to its climax or conclusion this turn. End your response mid-action, before the resolution.";
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
