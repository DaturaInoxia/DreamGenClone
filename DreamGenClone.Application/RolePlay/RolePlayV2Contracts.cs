using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed record ScenarioDefinition(
    string ScenarioId,
    string Name,
    int Priority = 0,
    decimal NarrativeEvidenceScore = 0m,
    decimal PreferencePriorityScore = 0m);

public sealed class ScenarioGuidanceRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string CurrentPhase { get; init; } = "BuildUp";
    public string? ActiveScenarioId { get; init; }
    public string? VariantId { get; init; }
    public double AverageDesire { get; init; } = 50;
    public double AverageRestraint { get; init; } = 50;
    public double AverageTension { get; init; } = 50;
    public double AverageConnection { get; init; } = 50;
    public double AverageDominance { get; init; } = 50;
    public double AverageLoyalty { get; init; } = 50;
    public string? SelectedWillingnessProfileId { get; init; }
    public string? HusbandAwarenessProfileId { get; init; }
    public IReadOnlyList<string> SuppressedScenarioIds { get; init; } = [];
}

public sealed class ScenarioGuidanceOutput
{
    public string GuidanceText { get; init; } = string.Empty;
    public IReadOnlyList<string> EmphasisPoints { get; init; } = [];
    public IReadOnlyList<string> AvoidancePoints { get; init; } = [];
    public string Source { get; init; } = "Fallback";
}

public sealed class ScenarioCommitResult
{
    public bool Committed { get; init; }
    public string? ScenarioId { get; init; }
    public int UpdatedConsecutiveLeadCount { get; init; }
    public string Reason { get; init; } = string.Empty;
    public ScenarioCandidateEvaluation? SelectedEvaluation { get; init; }
}

public sealed class LifecycleInputs
{
    public bool ForceReset { get; init; }
    public bool ManualOverride { get; init; }
    public int InteractionsSinceCommitment { get; init; }
    public decimal ActiveScenarioConfidence { get; init; }
    public decimal ActiveScenarioFitScore { get; init; }
    public string EvidenceSummary { get; init; } = string.Empty;
}

public sealed class PhaseTransitionResult
{
    public bool Transitioned { get; init; }
    public NarrativePhase TargetPhase { get; init; }
    public NarrativePhaseTransitionEvent? TransitionEvent { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public enum ResetReason
{
    Completion = 0,
    ManualOverride = 1,
    SafetyFallback = 2
}

public sealed class ConceptInjectionContext
{
    public IReadOnlyList<BehavioralConcept> Concepts { get; init; } = [];
    public int BudgetCap { get; init; } = 8;
    public int ReservedScenarioQuota { get; init; } = 2;
    public int ReservedWillingnessQuota { get; init; } = 1;
    public string Trigger { get; init; } = "InteractionStart";
}

public sealed class ConceptInjectionResult
{
    public IReadOnlyList<BehavioralConcept> SelectedConcepts { get; init; } = [];
    public int BudgetUsed { get; init; }
    public int BudgetCap { get; init; }
    public string Rationale { get; init; } = string.Empty;
}

public enum DecisionTrigger
{
    InteractionStart = 0,
    PhaseChanged = 1,
    SignificantStatChange = 2,
    ManualOverride = 3,
    CharacterDirectQuestion = 4,
    SceneLocationChanged = 5
}

public sealed class DecisionSubmission
{
    public string DecisionPointId { get; init; } = string.Empty;
    public string OptionId { get; init; } = string.Empty;
    public string? CustomResponseText { get; init; }
    public string ActorName { get; init; } = string.Empty;
    public string? TargetActorId { get; init; }
}

public sealed class DecisionOutcome
{
    public bool Applied { get; init; }
    public string DecisionPointId { get; init; } = string.Empty;
    public string OptionId { get; init; } = string.Empty;
    public string? TargetActorId { get; init; }
    public IReadOnlyDictionary<string, int> AppliedStatDeltas { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> PerActorStatDeltas { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
    public string AuditMetadataJson { get; init; } = "{}";
    public TransparencyMode TransparencyMode { get; init; } = TransparencyMode.Directional;
    public string Summary { get; init; } = string.Empty;
}

public sealed class OverrideRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string ActorId { get; init; } = string.Empty;
    public OverrideActorRole ActorRole { get; init; }
    public string RequestedScenarioId { get; init; } = string.Empty;
    public string SessionOwnerActorId { get; init; } = string.Empty;
}

public sealed class OverrideAuthorizationResult
{
    public bool Authorized { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class RolePlayV2DiagnosticsSnapshot
{
    public string SessionId { get; init; } = string.Empty;
    public IReadOnlyList<ScenarioCandidateEvaluation> CandidateEvaluations { get; init; } = [];
    public IReadOnlyList<NarrativePhaseTransitionEvent> TransitionEvents { get; init; } = [];
    public IReadOnlyList<DecisionPoint> DecisionPoints { get; init; } = [];
    public IReadOnlyList<UnsupportedSessionError> CompatibilityErrors { get; init; } = [];
    public string CorrelationId { get; init; } = string.Empty;
}
