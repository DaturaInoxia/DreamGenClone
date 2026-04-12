# Service Contracts: DreamGenClone RolePlay v2 Unified Scenario Intelligence

**Context**: This feature extends an internal, local-first Blazor Server system. Contracts below define Application-layer interfaces and expected behavior between Web orchestration and Infrastructure-backed services.

---

## IScenarioSelectionService

**Layer**: Application (interface) -> Infrastructure (implementation)  
**Purpose**: Evaluate candidates and decide scenario commitment with hysteresis.

```csharp
public interface IScenarioSelectionService
{
    Task<IReadOnlyList<ScenarioCandidateEvaluation>> EvaluateCandidatesAsync(
        AdaptiveScenarioState state,
        IReadOnlyList<ScenarioDefinition> candidates,
        CancellationToken cancellationToken = default);

    Task<ScenarioCommitResult> TryCommitScenarioAsync(
        AdaptiveScenarioState state,
        IReadOnlyList<ScenarioCandidateEvaluation> evaluations,
        CancellationToken cancellationToken = default);
}
```

**Invariants**:
- Stage A willingness tier is evaluated before Stage B eligibility checks.
- Near ties are resolved by hysteresis (lead threshold over N consecutive evaluations).
- Exact score ties use deterministic ordering via TieBreakKey.

---

## IScenarioLifecycleService

**Layer**: Application -> Infrastructure  
**Purpose**: Govern ordered phase transitions and cycle resets.

```csharp
public interface IScenarioLifecycleService
{
    Task<PhaseTransitionResult> EvaluateTransitionAsync(
        AdaptiveScenarioState state,
        LifecycleInputs inputs,
        CancellationToken cancellationToken = default);

    Task<AdaptiveScenarioState> ExecuteResetAsync(
        AdaptiveScenarioState state,
        ResetReason reason,
        CancellationToken cancellationToken = default);
}
```

**Invariants**:
- Allowed transition order: BuildUp -> Committed -> Approaching -> Climax -> Reset -> BuildUp.
- Every applied transition emits a NarrativePhaseTransitionEvent with evidence payload.
- Illegal jump requests are rejected with explicit error results.

---

## IConceptInjectionService

**Layer**: Application -> Infrastructure  
**Purpose**: Deterministically select and compose concept guidance under budget constraints.

```csharp
public interface IConceptInjectionService
{
    Task<ConceptInjectionResult> BuildGuidanceAsync(
        AdaptiveScenarioState state,
        ConceptInjectionContext context,
        CancellationToken cancellationToken = default);
}
```

**Invariants**:
- Same input state yields identical selected concept set and ordering.
- Conflicts are resolved via stable priority rules.
- Budget policy enforces reserved quotas first, then overflow allocation by deterministic priority.

---

## IDecisionPointService

**Layer**: Application -> Infrastructure  
**Purpose**: Generate and apply context-aware stat-altering narrative choices.

```csharp
public interface IDecisionPointService
{
    Task<DecisionPoint?> TryCreateDecisionPointAsync(
        AdaptiveScenarioState state,
        DecisionTrigger trigger,
        CancellationToken cancellationToken = default);

    Task<DecisionOutcome> ApplyDecisionAsync(
        AdaptiveScenarioState state,
        DecisionSubmission submission,
        CancellationToken cancellationToken = default);
}
```

**Invariants**:
- Default transparency mode is Directional unless explicitly overridden.
- Selected option stat deltas persist with auditable metadata.
- Custom response fallback path is available when listed options are insufficient.

---

## IOverrideAuthorizationService

**Layer**: Application -> Infrastructure  
**Purpose**: Authorize and audit manual scenario override requests.

```csharp
public interface IOverrideAuthorizationService
{
    Task<OverrideAuthorizationResult> AuthorizeAsync(
        OverrideRequest request,
        CancellationToken cancellationToken = default);
}
```

**Invariants**:
- Only session owner and operator/admin roles are authorized.
- Unauthorized attempts return explicit denial and generate audit logs.

---

## Persistence Contract Notes (SQLite)

- AdaptiveScenarioState, transition events, candidate evaluations, decision outcomes, and formula version references persist in SQLite-backed stores.
- Non-v2 session payloads are rejected with UnsupportedSessionError records and no partial mutation.
- Persistence writes and major read/write paths emit Information-level Serilog events with session/scenario correlation fields.

---

## Diagnostics Contract

Diagnostic payloads exposed by diagnostics services/log views must include:

1. Candidate scores, tiers, eligibility, and tie-resolution data.
2. Transition reason codes and threshold evidence.
3. Concept selection and truncation decisions with budget usage.
4. Decision point creation/apply outcomes and stat deltas.
5. Unauthorized override attempts and unsupported-version rejections.
