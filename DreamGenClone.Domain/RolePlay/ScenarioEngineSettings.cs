namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Configurable runtime parameters for the scenario selection engine.
/// All values include defaults matching the prior hardcoded behaviour.
/// </summary>
public sealed class ScenarioEngineSettings
{
    // ── Stage A: Willingness tier thresholds (avg desire) ─────────────────
    /// <summary>Minimum average desire required for the High willingness tier.</summary>
    public double StageAHighDesireMin { get; set; } = 75;

    /// <summary>Minimum average desire required for the Medium willingness tier.</summary>
    public double StageAMediumDesireMin { get; set; } = 55;

    /// <summary>Minimum average desire required for the Low willingness tier (below = Blocked).</summary>
    public double StageALowDesireMin { get; set; } = 35;

    // ── Legacy fit-score component weights (used when no LLM fit result) ──
    /// <summary>Weight of avg Desire in the legacy ComputeFitScore formula.</summary>
    public double LegacyFitDesireWeight { get; set; } = 0.45;

    /// <summary>Weight of avg Connection in the legacy ComputeFitScore formula.</summary>
    public double LegacyFitConnectionWeight { get; set; } = 0.25;

    /// <summary>Weight of avg Tension in the legacy ComputeFitScore formula.</summary>
    public double LegacyFitTensionWeight { get; set; } = 0.30;

    // ── Candidate weighted-blend scoring weights ──────────────────────────
    /// <summary>Weight of the character-alignment score in the candidate weighted-blend.</summary>
    public double CandidateCharacterAlignmentWeight { get; set; } = 0.50;

    /// <summary>Weight of the narrative-evidence score in the candidate weighted-blend.</summary>
    public double CandidateNarrativeEvidenceWeight { get; set; } = 0.30;

    /// <summary>Weight of the preference-priority score in the candidate weighted-blend.</summary>
    public double CandidatePreferencePriorityWeight { get; set; } = 0.20;

    // ── Selection hysteresis / tie-break mechanics ────────────────────────
    /// <summary>FitScore difference below which two candidates are considered a near-tie.</summary>
    public double NearTieThreshold { get; set; } = 0.8;

    /// <summary>Number of consecutive pipeline cycles a candidate must lead before committing.</summary>
    public int RequiredConsecutiveLeadCount { get; set; } = 2;
}
