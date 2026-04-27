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

    public int AdaptiveEarlyTurnInteractionThreshold { get; set; } = 3;

    public int AdaptiveEarlyTurnPerStatDeltaCap { get; set; } = 2;

    public int AdaptivePerInteractionTotalDeltaBudget { get; set; } = 10;

    public int AdaptiveThemeAffinityCapBuildUp { get; set; } = 0;

    public int AdaptiveThemeAffinityCapCommitted { get; set; } = 1;

    public int AdaptiveThemeAffinityCapApproaching { get; set; } = 1;

    public int AdaptiveThemeAffinityCapClimax { get; set; } = 2;

    public int AdaptiveThemeAffinityCapReset { get; set; } = 0;

    // Reduced score multiplier for non-active themes when active scenario is set.
    public double SuppressedEvidenceMultiplier { get; set; } = 0.20;

    // Per-interaction cap for suppressed evidence score gain.
    public double SuppressedEvidencePerTurnCap { get; set; } = 1.5;

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
    public double GateFailScorePenaltyMultiplier { get; set; } = 0.35;

    // Per-completion penalty applied to scenario candidate evidence/priority to reduce repeated picks.
    public double CompletedScenarioRepeatPenaltyPerRun { get; set; } = 0.20;

    // Lower bound for repeated-scenario score multiplier after penalties are applied.
    public double CompletedScenarioRepeatPenaltyFloor { get; set; } = 0.40;

    // Additional one-cycle multiplier for the most recently completed scenario to improve near-term variety.
    public double CompletedScenarioRecentPenaltyMultiplier { get; set; } = 0.65;

    // Theme tracker score penalty applied to the just-completed scenario during reset.
    public int CompletedScenarioThemeScorePenalty { get; set; } = 10;

    // Per-cycle reduction in reset pull toward baseline for elevated stats.
    // Example: 0.10 means each completed cycle reduces reset pull by 10%.
    public double ResetDecayReductionPerCycle { get; set; } = 0.10;

    // Maximum total reset pull reduction from cycle scaling.
    public double ResetDecayReductionCap { get; set; } = 0.60;

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
    public int BuildUpMinInteractionsBeforeCommit { get; set; } = 2;
}
