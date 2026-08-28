namespace DreamGenClone.Infrastructure.Configuration;

public sealed class StoryAnalysisOptions
{
    public const string SectionName = "StoryAnalysis";

    public double SummarizeTemperature { get; set; } = 0.3;

    public int SummarizeMaxTokens { get; set; } = 500;

    public double AnalyzeTemperature { get; set; } = 0.3;

    public int AnalyzeMaxTokens { get; set; } = 800;

    public double RankTemperature { get; set; } = 0.1;

    public int RankMaxTokens { get; set; } = 200;

    public int MaxStoryTextLength { get; set; } = 12000;

    /// <summary>
    /// LLM model to use for analysis, ranking, and summarization.
    /// Falls back to LmStudioOptions.Model if not set.
    /// </summary>
    public string? Model { get; set; }

    public double RankConfidenceThreshold { get; set; } = 0.5;

    // Markdown source folder used by RP theme sync controls.
    public string RpThemeMarkdownSourcePath { get; set; } = "specs/v2/ThemeDefinitaions";

    public int AdaptiveThemeAffinityStackLimit { get; set; } = 1;

    public int AdaptiveEarlyTurnThreshold { get; set; } = 3;

    public int AdaptiveEarlyTurnPerStatDeltaCap { get; set; } = 2;

    public int AdaptivePerTurnTotalDeltaBudget { get; set; } = 10;

    public int AdaptiveThemeAffinityCapBuildUp { get; set; } = 0;

    public int AdaptiveThemeAffinityCapCommitted { get; set; } = 1;

    public int AdaptiveThemeAffinityCapApproaching { get; set; } = 1;

    public int AdaptiveThemeAffinityCapClimax { get; set; } = 2;

    public int AdaptiveThemeAffinityCapReset { get; set; } = 0;

    // Reduced score multiplier for non-active themes when active scenario is set.
    public double SuppressedEvidenceMultiplier { get; set; } = 0.20;

    // Per-interaction cap for suppressed evidence score gain.
    public double SuppressedEvidencePerTurnCap { get; set; } = 1.5;

    // Per-interaction cap for semantic event evidence score gain per theme.
    // Semantic event mappings have designed deltas of 5-22; this cap must exceed
    // SuppressedEvidencePerTurnCap to allow structured LLM-guided signals to apply.
    public double SemanticEvidencePerTurnCap { get; set; } = 25.0;

    // Per-interaction cap on the net applied semantic stat delta per character per stat.
    // Keyed by canonical stat name (Desire, Restraint, Loyalty, SelfRespect, Dominance).
    // A stat present in this dictionary is capped at the configured magnitude per turn;
    // the mapped deltas themselves are unchanged. A stat absent from the dictionary has
    // no per-turn cap (only the final-band damping below applies).
    public Dictionary<string, int> SemanticStatPerTurnCapByStat { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Per-interaction cap on the net drift applied to a behavioral dimension
    // (RuntimeEncounterStats) per character per dimension. A dimension can be fed by
    // multiple stats (e.g. BoundaryFirmness = Loyalty*0.75 + Restraint*0.90), so even with
    // each stat capped at 2 a single dimension could move up to ~4 in one turn. This cap
    // bounds the dimension drift itself. A dimension absent from the dictionary (or a value
    // <= 0) has no per-turn cap (only the final-band damping below applies).
    public Dictionary<string, int> SemanticBehavioralDimensionPerTurnCapByDimension { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Final-band damping: when a stat is being pushed toward 100 and its current value is
    // above this threshold, the applied delta is scaled down linearly toward 0 as the value
    // approaches 100 (making 100 asymptotically hard to reach).
    public int SemanticStatFinalBandHighStart { get; set; } = 70;

    // Final-band damping: when a stat is being pushed toward 0 and its current value is
    // below this threshold, the applied delta is scaled down linearly toward 0 as the value
    // approaches 0 (making 0 asymptotically hard to reach).
    public int SemanticStatFinalBandLowStart { get; set; } = 30;

    // BuildUp scenario selection fit scoring strategy key.
    public string BuildUpSelectionFitScoreStrategy { get; set; } = "weighted-blend";

    // BuildUp scenario selection tie-break strategy key.
    public string BuildUpSelectionTieBreakStrategy { get; set; } = "tie-window";

    // Tie delta threshold used by tie-break strategies.
    public double BuildUpSelectionTieDeltaThreshold { get; set; } = 0.10;

    // Commitment score threshold used after ranking.
    public double BuildUpSelectionCommitThreshold { get; set; } = 0.60;

    // Candidate gate strategy before scoring/ranking. Supported value: dominant-role.
    public string BuildUpSelectionCandidateGateStrategy { get; set; } = "dominant-role";

    // Minimum per-role score required when dominant-role gate strategy is active.
    public double BuildUpSelectionDominantRoleMinScore { get; set; } = 0.85;

    // Multiplier applied to weighted score when candidate gate fails.
    public double GateFailScorePenaltyMultiplier { get; set; } = 0.70;

    // Per-completion penalty applied to scenario candidate evidence/priority to reduce repeated picks.
    public double CompletedScenarioRepeatPenaltyPerRun { get; set; } = 0.20;

    // Lower bound for repeated-scenario score multiplier after penalties are applied.
    public double CompletedScenarioRepeatPenaltyFloor { get; set; } = 0.40;

    // Additional one-cycle multiplier for the most recently completed scenario to improve near-term variety.
    public double CompletedScenarioRecentPenaltyMultiplier { get; set; } = 0.65;

    // Theme tracker score penalty applied to the just-completed scenario during reset.
    public int CompletedScenarioThemeScorePenalty { get; set; } = 10;

    // Number of interactions the just-completed theme is suppressed from selection after reset.
    // Default 10 prevents immediate re-selection during the Reset phase.
    public int CompletedScenarioThemeCooldownTurns { get; set; } = 10;

    // Flat FitScore point deduction applied to the just-completed theme after reset.
    // Applied as a direct subtraction from the gate-adjusted FitScore (0–100 scale).
    // Configurable. Default 20 points. Set to 0 to disable.
    public decimal CompletedScenarioFitScorePenaltyPoints { get; set; } = 20m;

    // Per-cycle reduction in reset pull toward baseline for elevated stats.
    // Example: 0.10 means each completed cycle reduces reset pull by 10%.
    // Default 0 means no reduction (feature must be explicitly enabled via configuration).
    public double ResetDecayReductionPerCycle { get; set; } = 0.0;

    // Maximum total reset pull reduction from cycle scaling.
    // Default 0 means no cap (required to be explicitly configured alongside ResetDecayReductionPerCycle).
    public double ResetDecayReductionCap { get; set; } = 0.0;

    // Baseline targets used when semi-resetting adaptive stats.
    // Keys: Desire, Restraint, Tension, Connection, Dominance, Loyalty, SelfRespect.
    public Dictionary<string, int> ResetStatBaselines { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Per-cycle pull fraction toward stat baselines (cycle 1 uses index 0).
    // If cycle count exceeds this list, the last entry is reused.
    public List<double> ResetStatBaselinePullSchedule { get; set; } = [];

    // Baseline target used when semi-resetting desire.
    // Legacy fallback when ResetStatBaselines is not configured.
    public int ResetDesireBaseline { get; set; } = 50;

    // Per-cycle pull fraction toward desire baseline (cycle 1 uses index 0).
    // Legacy fallback when ResetStatBaselinePullSchedule is not configured.
    // If cycle count exceeds this list, the last entry is reused.
    public List<double> ResetDesireBaselinePullSchedule { get; set; } = [];

    // Minimum BuildUp interactions required before a scenario can be committed.
    public int BuildUpMinTurnsBeforeCommit { get; set; } = 2;
}
