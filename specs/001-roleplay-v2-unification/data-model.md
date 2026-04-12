# Data Model: DreamGenClone RolePlay v2 Unified Scenario Intelligence

**Phase**: 1 - Design & Contracts  
**Date**: 2026-04-12  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

---

## Entity Overview

```text
CharacterStatProfileV2 ----\
WillingnessTierDefinition ---\
ScenarioEligibilityRuleSet ----> ScenarioCandidateEvaluation --> AdaptiveScenarioState
BehavioralConcept ------------/               |                    |
ConceptReferenceSet ----------/               |                    |
                                              v                    v
                                      NarrativePhaseTransitionEvent  ScenarioCompletionMetadata

AdaptiveScenarioState --> DecisionPoint --> DecisionOption
AdaptiveScenarioState --> FormulaConfigVersion
AdaptiveScenarioState --> UnsupportedSessionError (on non-v2 payload load)
```

---

## Core Entities

### CharacterStatProfileV2

**Purpose**: Canonical stat profile for active role-play participants in v2 sessions.

| Field | Type | Constraints | Description |
|---|---|---|---|
| CharacterId | string | Required | Stable character identifier |
| Desire | int | Required, bounded by profile limits | Willingness and intensity driver |
| Restraint | int | Required | Counterbalance pressure stat |
| Tension | int | Required | Narrative buildup pressure signal |
| Connection | int | Required | Relational cohesion signal |
| Dominance | int | Required | Control/assertiveness signal |
| Loyalty | int | Required | Commitment/bond signal |
| SelfRespect | int | Required | Personal-boundary stability signal |
| SnapshotUtc | DateTime | Required | Snapshot timestamp for determinism |

**Validation Rules**:
- All canonical stats are required in supported sessions.
- Missing canonical stats invalidate payload compatibility (non-v2 rejection path).

---

### AdaptiveScenarioState

**Purpose**: Per-session lifecycle aggregate for active scenario, phase progression, counters, and history.

| Field | Type | Constraints | Description |
|---|---|---|---|
| SessionId | string | Required | Owning session |
| ActiveScenarioId | string? | Null in BuildUp pre-commit | Current committed scenario |
| CurrentPhase | enum | BuildUp, Committed, Approaching, Climax, Reset | Active narrative phase |
| InteractionCountInPhase | int | >= 0 | Phase-local interaction counter |
| ConsecutiveLeadCount | int | >= 0 | Hysteresis lead counter |
| LastEvaluationUtc | DateTime | Required | Last adaptive evaluation timestamp |
| CycleIndex | int | >= 0 | Current cycle number |
| ActiveFormulaVersion | string | Required | FormulaConfigVersion key |
| CharacterSnapshots | list<CharacterStatProfileV2> | Required | Current per-character stats |

**State Transitions**:
- BuildUp -> Committed when commitment criteria and hysteresis pass.
- Committed -> Approaching when approach thresholds pass.
- Approaching -> Climax when climax thresholds pass.
- Climax -> Reset on completion trigger.
- Reset -> BuildUp after reset finalization.

---

### ScenarioCandidateEvaluation

**Purpose**: Immutable candidate scoring record for one evaluation cycle.

| Field | Type | Constraints | Description |
|---|---|---|---|
| SessionId | string | Required | Owning session |
| EvaluationId | string | Required | Evaluation correlation key |
| ScenarioId | string | Required | Candidate scenario |
| StageAWillingnessTier | string | Required | Desire-tier output |
| StageBEligible | bool | Required | Multi-stat eligibility result |
| FitScore | decimal | Deterministic computation | Composite ranking score |
| Confidence | decimal | 0..1 | Confidence estimate |
| TieBreakKey | string | Required | Stable deterministic ordering key |
| Rationale | string | Required | Human-readable diagnostics summary |
| EvaluatedUtc | DateTime | Required | Evaluation timestamp |

---

### NarrativePhaseTransitionEvent

**Purpose**: Immutable audit record for each phase transition.

| Field | Type | Constraints | Description |
|---|---|---|---|
| TransitionId | string | Required | Unique transition key |
| SessionId | string | Required | Owning session |
| FromPhase | enum | Required | Source phase |
| ToPhase | enum | Required | Target phase |
| TriggerType | enum | Required | Threshold, count gate, override, reset |
| EvidencePayload | string | JSON, required | Thresholds and supporting evidence |
| ReasonCode | string | Required | Machine-readable reason |
| OccurredUtc | DateTime | Required | Transition timestamp |

---

### ScenarioCompletionMetadata

**Purpose**: Per-cycle summary for completed scenario cycles.

| Field | Type | Constraints | Description |
|---|---|---|---|
| SessionId | string | Required | Owning session |
| CycleIndex | int | Required | Completed cycle |
| ScenarioId | string | Required | Completed active scenario |
| PeakPhase | enum | Required | Highest phase reached |
| ResetReason | string | Required | Why reset occurred |
| StartedUtc | DateTime | Required | Cycle start |
| CompletedUtc | DateTime | Required | Cycle completion |

---

### BehavioralConcept

**Purpose**: Reusable concept reference unit used in prompt guidance composition.

| Field | Type | Constraints | Description |
|---|---|---|---|
| ConceptId | string | Required | Stable concept key |
| Category | string | Required | Concept category |
| Priority | int | Required | Conflict and truncation ranking |
| TriggerConditions | string | JSON, required | Eligibility conditions |
| GuidanceText | string | Required | Guidance payload text |
| IsEnabled | bool | Required | Activation flag |

---

### ConceptReferenceSet

**Purpose**: Versioned group of concepts attached to profile/scenario/session context.

| Field | Type | Constraints | Description |
|---|---|---|---|
| ReferenceSetId | string | Required | Set identifier |
| Version | string | Required | Version tag |
| ScopeType | enum | Profile, Scenario, Session | Attachment scope |
| ScopeId | string | Required | Scope identifier |
| ConceptIds | list<string> | Required | Included concept keys |
| EffectiveUtc | DateTime | Required | Activation timestamp |

---

### DecisionPoint

**Purpose**: Generated narrative question context with option set and trigger metadata.

| Field | Type | Constraints | Description |
|---|---|---|---|
| DecisionPointId | string | Required | Unique decision key |
| SessionId | string | Required | Owning session |
| ScenarioId | string | Required | Active scenario context |
| Phase | enum | Required | Current phase |
| TriggerSource | enum | Required | Why question appeared |
| TransparencyMode | enum | Hidden, Directional, Explicit | Default Directional unless override |
| OptionIds | list<string> | Required | Ordered options |
| CreatedUtc | DateTime | Required | Creation timestamp |

---

### DecisionOption

**Purpose**: Selectable narrative choice with stat mutation intent.

| Field | Type | Constraints | Description |
|---|---|---|---|
| OptionId | string | Required | Option key |
| DecisionPointId | string | Required | Parent decision point |
| DisplayText | string | Required | User-facing option text |
| VisibilityMode | enum | Hidden, Directional, Explicit | Per-option override allowed |
| Prerequisites | string | JSON | Eligibility conditions |
| StatDeltaMap | string | JSON, required | Canonical stat mutation map |
| IsCustomResponseFallback | bool | Required | Indicates fallback option |

---

### FormulaConfigVersion

**Purpose**: Versioned formula/parameter set used during adaptive computations.

| Field | Type | Constraints | Description |
|---|---|---|---|
| FormulaVersionId | string | Required | Version key |
| Name | string | Required | Human-readable name |
| ParameterPayload | string | JSON, required | Tunable parameters |
| EffectiveFromUtc | DateTime | Required | Version activation |
| IsDefault | bool | Required | Default selection flag |

---

### UnsupportedSessionError

**Purpose**: Structured incompatibility artifact returned when non-v2 payloads are loaded.

| Field | Type | Constraints | Description |
|---|---|---|---|
| ErrorCode | string | Required | Stable code (e.g., RPV2_UNSUPPORTED_SCHEMA) |
| SessionId | string | Required | Target session |
| DetectedSchemaVersion | string? | Optional | Parsed source version |
| MissingCanonicalStats | list<string> | Required | Missing required fields |
| RecoveryGuidance | string | Required | User/operator remediation text |
| EmittedUtc | DateTime | Required | Error timestamp |

---

## Relationships

```text
AdaptiveScenarioState 1 ---- * ScenarioCandidateEvaluation
AdaptiveScenarioState 1 ---- * NarrativePhaseTransitionEvent
AdaptiveScenarioState 1 ---- * ScenarioCompletionMetadata
AdaptiveScenarioState 1 ---- * DecisionPoint
DecisionPoint 1 ----------- * DecisionOption
ConceptReferenceSet * ----- * BehavioralConcept
AdaptiveScenarioState * ---- 1 FormulaConfigVersion
AdaptiveScenarioState 1 ---- * CharacterStatProfileV2
```

---

## Determinism and Validation Notes

- Candidate ranking uses deterministic ordering: FitScore desc, Priority desc, TieBreakKey asc.
- Hysteresis commitment uses configured lead threshold and required consecutive lead count.
- Prompt budget allocation enforces reserved quotas first, then deterministic overflow allocation.
- Unauthorized overrides and unsupported session payloads are persisted/logged as structured error artifacts.
