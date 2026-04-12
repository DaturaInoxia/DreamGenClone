# Service Contracts: Adaptive Scenario Selection Engine Redesign 2

Context: This feature introduces internal service contracts for deterministic scenario selection and narrative phase progression. DreamGenClone remains a local-first app with internal contracts between Application and Infrastructure layers.

## IScenarioSelectionEngine

Layer: Application interface, Infrastructure implementation  
Purpose: Evaluate scenario candidates and determine commitment outcomes.

```csharp
public interface IScenarioSelectionEngine
{
    Task<ScenarioSelectionResult> EvaluateAsync(
        RolePlayAdaptiveState adaptiveState,
        IReadOnlyList<ScenarioCandidateInput> candidates,
        ScenarioSelectionContext context,
        CancellationToken cancellationToken = default);
}
```

Behavioral contract:
- Computes fit score per candidate in [0.0, 1.0].
- Applies tie rule when top-two delta <= 0.10 and requests deferral.
- Returns selected scenario only when commitment gates are met.
- Includes rationale and ranked alternatives for auditability.

## INarrativePhaseManager

Layer: Application interface, Infrastructure implementation  
Purpose: Evaluate and execute phase transitions.

```csharp
public interface INarrativePhaseManager
{
    Task<PhaseTransitionResult> EvaluateTransitionAsync(
        RolePlayAdaptiveState adaptiveState,
        NarrativeSignalSnapshot signals,
        CancellationToken cancellationToken = default);

    Task<RolePlayAdaptiveState> ApplyResetAsync(
        RolePlayAdaptiveState adaptiveState,
        ResetTrigger trigger,
        CancellationToken cancellationToken = default);
}
```

Behavioral contract:
- Enforces ordered phases: BuildUp, Committed, Approaching, Climax, Reset.
- Enforces transition thresholds from spec clarifications.
- Supports reset-first manual override semantics.
- Emits transition reason metadata suitable for logging.

## IScenarioGuidanceContextFactory

Layer: Application interface, Infrastructure implementation  
Purpose: Build prompt-ready scenario guidance context for continuation services.

```csharp
public interface IScenarioGuidanceContextFactory
{
    Task<ScenarioGuidanceContext> CreateAsync(
        RolePlaySession session,
        RolePlayAdaptiveState adaptiveState,
        CancellationToken cancellationToken = default);
}
```

Behavioral contract:
- Produces phase-aware context for build-up, committed, approaching, climax, and reset.
- Reflects active scenario framing and excludes contradictory framing when committed.
- Carries signal summary needed for prompt templates and debug logs.

## Override and Transition Rules

Required invariants:
- Manual scenario override at any phase must force Reset before next BuildUp.
- Committed -> Approaching gate: score >= 60, desire >= 65, restraint <= 45, interactions >= 3 since commitment.
- Approaching -> Climax gate: score >= 80, desire >= 75, restraint <= 35, interactions >= 2 in approaching.
- Explicit user-triggered climax override is allowed from Approaching with policy/safety checks.

## Observability Contract

All implementations must emit structured Information logs for:
- Candidate evaluation summary
- Commitment decision
- Phase transitions
- Reset execution
- Manual override events

Minimum fields: SessionId, Phase, ActiveScenarioId, Reason, InteractionCount.
