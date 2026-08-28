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

    // ── B-034: Unified "Wife Willingness to Cheat" (Option A) ─────────────
    // Coefficients for ComputeWillingnessToCheat (see WifeWillingnessCalculator).
    // Wife-owned terms (Desire/Loyalty, SeductionReceptivity/BoundaryFirmness) dominate;
    // Husband marital deficit is secondary. Persisted config — not hardcoded (repo rule).

    /// <summary>Weight of (Desire − Loyalty) in the willingness score.</summary>
    public double WillingnessDesireLoyaltyWeight { get; set; } = 0.5;

    /// <summary>Weight of (SeductionReceptivity − BoundaryFirmness) in the willingness score.</summary>
    public double WillingnessBehaviorWeight { get; set; } = 0.5;

    /// <summary>Weight of ((100−Attentiveness) + (100−IntimacyAvailability)) in the willingness score.</summary>
    public double WillingnessMaritalDeficitWeight { get; set; } = 0.25;

    /// <summary>Upper bound of the NO verdict band (0..NoMax → NO).</summary>
    public int WillingnessVerdictNoMax { get; set; } = 40;

    /// <summary>Upper bound of the MAYBE verdict band (NoMax+1..MaybeMax → MAYBE; above → YES).</summary>
    public int WillingnessVerdictMaybeMax { get; set; } = 70;

    /// <summary>Directive text emitted with the NO verdict (willingness ≤ WillingnessVerdictNoMax).</summary>
    public string WillingnessVerdictNoDirective { get; set; } =
        "She will not cross — she stays loyal, deflects, and resists initiation or consent.";

    /// <summary>Directive text emitted with the MAYBE verdict (NoMax &lt; willingness ≤ MaybeMax).</summary>
    public string WillingnessVerdictMaybeDirective { get; set; } =
        "She is uncertain — she may yield only to sustained, genuine pursuit; hesitation is her default.";

    /// <summary>Directive text emitted with the YES verdict (willingness &gt; MaybeMax).</summary>
    public string WillingnessVerdictYesDirective { get; set; } =
        "She will cross when the opportunity is plausible — she initiates or yields; the Ceiling governs how far she goes.";

    // ── B-077: Gap-aware steering ────────────────────────────────────────

    /// <summary>
    /// Master switch for gap-aware steering. When true, both UI and background
    /// steer-generation paths append a willingness-gap context block if the Wife
    /// is below the target verdict tier.
    /// </summary>
    public bool WillingnessGapSteeringEnabled { get; set; }

    /// <summary>
    /// Template prose for the gap-aware steering block. Supports placeholders:
    /// {WifeName}, {Willingness}, {Verdict}, {Ceiling}, {TargetVerdict}.
    /// Per-role gap-closing hints are appended after this template.
    /// Empty string → block not emitted (fail-fast when Enabled but missing).
    /// </summary>
    public string WillingnessGapSteeringDirective { get; set; } = string.Empty;
}
