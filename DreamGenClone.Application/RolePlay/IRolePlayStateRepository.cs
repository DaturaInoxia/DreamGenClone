using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IRolePlayStateRepository
{
    Task<RolePlayTurn> StartTurnAsync(
        string sessionId,
        string turnKind,
        string triggerSource,
        string? initiatedByActorName,
        string? inputInteractionId,
        CancellationToken cancellationToken = default);
    Task CompleteTurnAsync(
        string sessionId,
        string turnId,
        IReadOnlyList<string> outputInteractionIds,
        bool succeeded,
        string? failureReason = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RolePlayTurn>> LoadTurnsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default);
    Task SaveAdaptiveStateAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default);
    /// <summary>
    /// Persists only the semantic-analysis-owned fields (character snapshots, theme scores,
    /// tracker meta, semantic events, and breakdowns). Pipeline-managed fields (CurrentPhase,
    /// TurnCountInPhase, ActiveScenarioId, TimeSkipPhase, etc.) are intentionally left
    /// untouched — the background semantic job must never overwrite them.
    /// </summary>
    Task SaveAdaptiveStateSemanticFieldsAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default);
    /// <summary>
    /// Persists only location-owned fields (CurrentSceneLocation, CharacterLocationsJson,
    /// CharacterLocationPerceptionsJson, CurrentTimeOfDay, UpdatedUtc). Pipeline-managed
    /// fields (CurrentPhase, TurnCountInPhase, ActiveScenarioId, etc.) are intentionally
    /// left untouched — the background location-detection job must never overwrite them.
    /// </summary>
    Task SaveAdaptiveStateLocationFieldsAsync(AdaptiveScenarioState state, CancellationToken cancellationToken = default);
    Task<AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default);
    Task SaveCandidateEvaluationsAsync(IReadOnlyList<ScenarioCandidateEvaluation> evaluations, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default);
    Task SaveTransitionEventAsync(NarrativePhaseTransitionEvent transitionEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default);
    Task SaveCompletionMetadataAsync(ScenarioCompletionMetadata metadata, CancellationToken cancellationToken = default);
    Task SaveDecisionPointAsync(DecisionPoint decisionPoint, IReadOnlyList<DecisionOption> options, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DecisionOption>> LoadDecisionOptionsAsync(string decisionPointId, CancellationToken cancellationToken = default);
    Task SaveConceptInjectionAsync(string sessionId, ConceptInjectionResult result, CancellationToken cancellationToken = default);
    Task SaveFormulaVersionReferenceAsync(string sessionId, FormulaConfigVersion version, int cycleIndex, CancellationToken cancellationToken = default);
    Task SaveUnsupportedSessionErrorAsync(UnsupportedSessionError error, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default);
    Task SaveThemeMachineDiagnosticEventsAsync(IReadOnlyList<ThemeMachineDiagnosticEvent> events, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ThemeMachineDiagnosticEvent>> LoadThemeMachineDiagnosticEventsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default);
    Task SaveEncounterSummaryAsync(EncounterSummaryRecord record, CancellationToken cancellationToken = default);
    Task UpdateEncounterSummaryLlmAsync(string summaryId, string llmSummary, DateTime llmEnhancedUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EncounterSummaryRecord>> LoadEncounterSummariesForSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
