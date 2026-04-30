namespace DreamGenClone.Domain.RolePlay;

public sealed class ClimaxBeatEntry
{
    /// <summary>
    /// Unique code identifying this sub-beat, e.g. "1c", "8g".
    /// </summary>
    public string BeatCode { get; set; } = string.Empty;

    /// <summary>
    /// Physical escalation stage number (1–8).
    /// </summary>
    public byte StageNumber { get; set; }

    /// <summary>
    /// Human-readable name for the stage, e.g. "Clothed Contact".
    /// </summary>
    public string StageName { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for this specific sub-beat, e.g. "First Kiss".
    /// </summary>
    public string SubBeatName { get; set; } = string.Empty;

    /// <summary>
    /// Short prompt hints the LLM should draw from when writing this beat.
    /// </summary>
    public List<string> Hints { get; set; } = [];

    /// <summary>
    /// BeatCode of the next sub-beat; null when this is the terminal beat ("8g").
    /// </summary>
    public string? NextBeatCode { get; set; }

    /// <summary>
    /// How many turns must elapse in this beat before the cursor advances.
    /// Use int.MaxValue for the terminal beat to prevent automatic advancement.
    /// </summary>
    public int MinTurnsBeforeAdvance { get; set; } = 1;
}
