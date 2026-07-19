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
    public bool RequireScenePresence { get; init; } = false;
    public NarrativeGranularity Granularity { get; init; } = NarrativeGranularity.Meso;
}