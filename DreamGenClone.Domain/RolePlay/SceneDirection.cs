namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Per-turn narrative pacing directive. Lowercased kebab-style values are used in
/// prompt injection so the model reads a stable token regardless of enum casing.
/// </summary>
public enum ScenePacing
{
    Slow = 0,
    Medium = 1,
    Fast = 2
}

/// <summary>
/// How long the current narrative beat should span (in turns). Drives the model's
/// decision to linger in a moment vs. advance after resolving it.
/// </summary>
public enum BeatScope
{
    /// <summary>Single turn — resolve the moment, then a time shift is permitted next turn.</summary>
    Single = 0,

    /// <summary>Two to three turns — build the moment across a few exchanges before shifting.</summary>
    Short = 1,

    /// <summary>Four or more turns — stay in the current moment/scene for several exchanges.</summary>
    Extended = 2
}

/// <summary>
/// Controls whether and how far the story may jump forward in time this turn.
/// </summary>
public enum TimeShiftPolicy
{
    /// <summary>No time shift. Continue from the exact moment the last response ended.</summary>
    None = 0,

    /// <summary>Small time shift allowed (minutes to a few hours) — e.g. skip to later that day.</summary>
    Small = 1,

    /// <summary>Medium time shift allowed (hours to half a day) — e.g. morning to evening.</summary>
    Medium = 2,

    /// <summary>Large time shift allowed (a day or more) — e.g. skip to the next day.</summary>
    Large = 3
}

/// <summary>
/// Controls the narrative density within a single response — how much story time and
/// action the model should cover in one generation. Orthogonal to time-shifting
/// (which controls when the next scene starts) and beat scope (which controls how
/// many turns to spend in a moment).
/// </summary>
public enum NarrativeGranularity
{
    /// <summary>One response = one moment. Deep sensory/emotional detail.
    /// Use for tense, pivotal, or intimate scenes where every heartbeat counts.</summary>
    Micro = 0,

    /// <summary>One response = one scene/beat. Natural scene-length narration.
    /// Covers a breakfast, a beach visit, an evening conversation. Default for most phases.</summary>
    Meso = 1,

    /// <summary>One response = a day or significant span. Morning to evening arc.
    /// Summarize routines; focus on the moments that matter. Use for transitions and aftermath.</summary>
    Macro = 2,

    /// <summary>One response = multiple days to weeks. Selected highlights across time.
    /// "Over the next few days..." "By the weekend..." Skip the ordinary, keep the meaningful.</summary>
    Montage = 3
}

/// <summary>
/// Controls whether subsequent actors in a turn should deepen existing beats from their
/// POV rather than advancing to new beats or positions.
/// </summary>
public enum DeepeningPolicy
{
    /// <summary>No deepening constraint — standard pacing rules apply to all actors.</summary>
    None = 0,

    /// <summary>Position 2+ deepens from POV only — never advances beat/position.
    /// Orthogonal to pacing. Overrides position 2+ advancement even under fast pacing.</summary>
    SubsequentActors = 1
}

/// <summary>
/// Climax phase subdivision. <see cref="None"/> is used for every non-Climax phase and for
/// themes that do not stage the Climax into sub-phases (no <c>[BeatStyle:episodic]</c> marker,
/// no beat cursor). Drives the phase-appropriate climax hard constraints.
/// </summary>
public enum ClimaxSubPhase
{
    None = 0,
    Early = 1,
    Mid = 2,
    Late = 3
}

/// <summary>
/// B-089 — user-facing narrative density control (replaces the Pacing + TimeShift +
/// Granularity trio). Each value is a coherent bundle of the three raw dimensions so they
/// can never contradict (the C3 problem). Derived from <see cref="SceneDirection"/> raw
/// fields by <see cref="SceneDirection.TempoFrom"/>; the continuation-settings override can
/// set it directly, and it maps back to the raw bundle via <see cref="SceneDirection.TempoBundle"/>.
/// </summary>
public enum SceneTempo
{
    /// <summary>Stay in the exact moment — sensory depth, no advance. (Slow + TimeShift=None + Granularity=Micro)</summary>
    Linger = 0,

    /// <summary>Advance one beat, small time shifts allowed. (Medium + TimeShift=Small + Granularity=Meso)</summary>
    Steady = 1,

    /// <summary>Compress 2–3 beats toward resolution. (Fast + TimeShift=Medium + Granularity=Meso)</summary>
    Push = 2,

    /// <summary>Advance a day or more — aftermath/montage. (Fast + TimeShift=Large + Granularity=Macro/Montage)</summary>
    Leap = 3
}

/// <summary>
/// B-089 — user-facing duration control for the current moment (replaces the Beat Style
/// label). Maps to the beat budget via <see cref="SceneDirection.SpanTurnBudget"/> and to a
/// <see cref="BeatScope"/> via <see cref="SceneDirection.SpanToBeatScope"/>.
/// </summary>
public enum SceneSpan
{
    /// <summary>Resolve the moment in one turn. (BeatScope.Single)</summary>
    Moment = 0,

    /// <summary>Build the moment across 3 turns. (BeatScope.Short)</summary>
    Scene = 1,

    /// <summary>Linger across 5 turns. (BeatScope.Extended)</summary>
    ExtendedArc = 2
}

/// <summary>
/// Resolved scene direction for a single continuation prompt — the coordinated per-turn
/// "Scene Direction" block values plus the free-text director note. Produced by
/// <c>SceneDirectionResolver</c> from narrative phase, theme phase-guidance markers,
/// climax sub-phase, and the optional profile-configured scene directive. Immutable.
/// </summary>
public sealed record SceneDirection
{
    public ScenePacing Pacing { get; init; } = ScenePacing.Medium;
    public BeatScope BeatScope { get; init; } = BeatScope.Short;
    public TimeShiftPolicy TimeShift { get; init; } = TimeShiftPolicy.Small;
    public ClimaxSubPhase ClimaxSubPhase { get; init; } = ClimaxSubPhase.None;
    public DeepeningPolicy Deepening { get; init; } = DeepeningPolicy.None;
    public NarrativeGranularity Granularity { get; init; } = NarrativeGranularity.Meso;

    // ── B-089: derived Tempo / Span (single source of truth = the raw fields) ──
    // The prompt renders these two, not the raw trio. Derived so the resolver and the
    // raw fields stay the source of truth and the prompt/engine never diverge.

    public SceneTempo Tempo => TempoFrom(Pacing, TimeShift, Granularity);
    public SceneSpan Span => SpanFrom(BeatScope);

    /// <summary>Derives a coherent <see cref="SceneTempo"/> from the raw trio. Pacing drives the density; a Large time-shift or Macro/Montage granularity upgrades Fast to Leap.</summary>
    public static SceneTempo TempoFrom(ScenePacing pacing, TimeShiftPolicy timeShift, NarrativeGranularity granularity)
    {
        if (pacing == ScenePacing.Slow)
            return SceneTempo.Linger;
        if (pacing == ScenePacing.Fast)
        {
            return timeShift == TimeShiftPolicy.Large
                   || granularity is NarrativeGranularity.Macro or NarrativeGranularity.Montage
                ? SceneTempo.Leap
                : SceneTempo.Push;
        }
        return SceneTempo.Steady;
    }

    /// <summary>Maps a <see cref="SceneTempo"/> to its coherent raw bundle.</summary>
    public static (ScenePacing Pacing, TimeShiftPolicy TimeShift, NarrativeGranularity Granularity) TempoBundle(SceneTempo tempo) => tempo switch
    {
        SceneTempo.Linger => (ScenePacing.Slow, TimeShiftPolicy.None, NarrativeGranularity.Micro),
        SceneTempo.Push => (ScenePacing.Fast, TimeShiftPolicy.Medium, NarrativeGranularity.Meso),
        SceneTempo.Leap => (ScenePacing.Fast, TimeShiftPolicy.Large, NarrativeGranularity.Macro),
        _ => (ScenePacing.Medium, TimeShiftPolicy.Small, NarrativeGranularity.Meso),
    };

    /// <summary>Derives a <see cref="SceneSpan"/> from a <see cref="BeatScope"/>.</summary>
    public static SceneSpan SpanFrom(BeatScope beatScope) => beatScope switch
    {
        BeatScope.Single => SceneSpan.Moment,
        BeatScope.Extended => SceneSpan.ExtendedArc,
        _ => SceneSpan.Scene,
    };

    /// <summary>Maps a <see cref="SceneSpan"/> to its <see cref="BeatScope"/>.</summary>
    public static BeatScope SpanToBeatScope(SceneSpan span) => span switch
    {
        SceneSpan.Moment => BeatScope.Single,
        SceneSpan.ExtendedArc => BeatScope.Extended,
        _ => BeatScope.Short,
    };

    /// <summary>Turn budget for a <see cref="SceneSpan"/> (Moment=1, Scene=3, ExtendedArc=5).</summary>
    public static int SpanTurnBudget(SceneSpan span) => span switch
    {
        SceneSpan.Moment => 1,
        SceneSpan.ExtendedArc => 5,
        _ => 3,
    };
}