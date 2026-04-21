namespace DreamGenClone.Infrastructure.Logging;

public static class RolePlayLogEvents
{
    public const string DiagnosticsSnapshotPublished =
        "RolePlayV2 diagnostics snapshot: SessionId={SessionId} CorrelationId={CorrelationId} CandidateCount={CandidateCount} TransitionCount={TransitionCount} DecisionCount={DecisionCount} ErrorCount={ErrorCount}";

    public const string ScenarioCandidateEvaluated =
        "RolePlayV2 scenario candidate evaluated: SessionId={SessionId} ScenarioId={ScenarioId} Tier={StageAWillingnessTier} Eligible={StageBEligible} FitScore={FitScore}";

    public const string ScenarioCommitted =
        "RolePlayV2 scenario committed: SessionId={SessionId} ScenarioId={ScenarioId} CycleIndex={CycleIndex} Phase={Phase}";

    public const string PhaseTransitionApplied =
        "RolePlayV2 phase transition: SessionId={SessionId} FromPhase={FromPhase} ToPhase={ToPhase} TriggerType={TriggerType} ReasonCode={ReasonCode}";

    public const string ConceptInjectionBuilt =
        "RolePlayV2 concept injection built: SessionId={SessionId} SelectedConcepts={SelectedConceptCount} BudgetUsed={BudgetUsed} BudgetCap={BudgetCap}";

    public const string DecisionOutcomeApplied =
        "RolePlayV2 decision outcome applied: SessionId={SessionId} DecisionPointId={DecisionPointId} OptionId={OptionId} TransparencyMode={TransparencyMode}";

    public const string DecisionAttemptEvaluated =
        "RolePlayV2 decision attempt evaluated: SessionId={SessionId} Trigger={Trigger} HasPendingDecision={HasPendingDecision} InCooldown={InCooldown} ContextCount={ContextCount} CreatedCount={CreatedCount} SkipReasons={SkipReasons}";

    public const string DecisionPointCreated =
        "RolePlayV2 decision point created: SessionId={SessionId} DecisionPointId={DecisionPointId} Trigger={Trigger} AskingActor={AskingActor} TargetActor={TargetActor}";

    public const string DecisionPointSkipped =
        "RolePlayV2 decision point skipped: SessionId={SessionId} Trigger={Trigger} Reason={Reason}";

    public const string DirectQuestionDetected =
        "RolePlayV2 direct question detected: SessionId={SessionId} AskingActor={AskingActor} TargetActor={TargetActor}";

    public const string SceneLocationChangedDetected =
        "RolePlayV2 scene location changed: SessionId={SessionId} PreviousLocation={PreviousLocation} CurrentLocation={CurrentLocation}";

    public const string OverrideDenied =
        "RolePlayV2 override denied: SessionId={SessionId} ActorId={ActorId} ActorRole={ActorRole} Reason={Reason}";

    public const string UnsupportedSessionRejected =
        "RolePlayV2 session rejected: SessionId={SessionId} ErrorCode={ErrorCode} MissingStats={MissingCanonicalStats}";

    public const string CompatibilityCheckStarted =
        "RolePlayV2 compatibility check started: SessionId={SessionId} CorrelationId={CorrelationId}";

    public const string CompatibilityCheckPassed =
        "RolePlayV2 compatibility check passed: SessionId={SessionId} CorrelationId={CorrelationId}";
}
