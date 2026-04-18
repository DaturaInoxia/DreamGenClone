namespace DreamGenClone.Application.StoryAnalysis.Models;

public sealed record AdaptiveScenarioSnapshot(
    string? ActiveScenarioId,
    string CurrentNarrativePhase,
    int InteractionCount,
    double AverageDesire,
    double AverageRestraint,
    double ActiveScenarioScore);

public sealed record ScenarioCandidateInput(
    string ScenarioId,
    double CharacterAlignmentScore,
    double NarrativeEvidenceScore,
    double PreferencePriorityScore,
    bool IsEligible);

public sealed record ScenarioSelectionContext(
    int BuildUpInteractionCount,
    bool ManualOverrideRequested,
    string? ManualOverrideScenarioId);

public sealed record ScenarioScoreResult(
    string ScenarioId,
    double FitScore,
    string Rationale);

public sealed record ScenarioSelectionResult(
    string? SelectedScenarioId,
    bool DeferredForTie,
    IReadOnlyList<ScenarioScoreResult> RankedCandidates,
    string Rationale);

public sealed record ScenarioTieDecision(
    bool DeferredForTie,
    string Rationale);

public sealed record NarrativeSignalSnapshot(
    int InteractionsSinceCommitment,
    int InteractionsInApproaching,
    bool ExplicitClimaxRequested,
    bool ClimaxCompletionDetected,
    bool ManualScenarioOverrideRequested,
    string? ManualOverrideScenarioId,
    int CompletedScenarios = 0);

public sealed record PhaseTransitionResult(
    string CurrentPhase,
    string NextPhase,
    bool Transitioned,
    string Reason);

public sealed record ResetTrigger(
    string Reason,
    bool FromManualOverride,
    string? RequestedScenarioId);

public sealed record ScenarioGuidanceInput(
    string SessionId,
    string CurrentPhase,
    string? ActiveScenarioId,
    string? VariantId,
    double AverageDesire,
    double AverageRestraint,
    double AverageTension,
    double AverageConnection,
    double AverageDominance,
    double AverageLoyalty,
    string? SelectedWillingnessProfileId,
    string? HusbandAwarenessProfileId,
    IReadOnlyList<string> SuppressedScenarioIds);

public sealed record ScenarioGuidanceContext(
    string Phase,
    string? ActiveScenarioId,
    string GuidanceText,
    IReadOnlyList<string> ExcludedScenarioIds);
