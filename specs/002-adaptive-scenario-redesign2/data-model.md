# Data Model: Adaptive Scenario Selection Engine Redesign 2

**Phase**: 1 - Design & Contracts  
**Date**: 2026-04-11  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

## Entity Overview

```text
RolePlaySession
  -> RolePlayAdaptiveState
      -> ActiveScenarioId
      -> CurrentNarrativePhase
      -> ThemeTrackerState (candidates + evidence)
      -> ScenarioHistory (completed cycles)
      -> CharacterStats / PairwiseStats
```

## Modified Entities

### RolePlayAdaptiveState

Purpose: Session adaptive runtime state extended for scenario commitment lifecycle.

| Field | Type | Constraints | Description |
|---|---|---|---|
| ActiveScenarioId | string? | nullable | Committed scenario for current cycle |
| ScenarioCommitmentTimeUtc | DateTime? | nullable | Timestamp of commitment |
| CurrentNarrativePhase | NarrativePhase | required | BuildUp, Committed, Approaching, Climax, Reset |
| CompletedScenarios | int | default 0, >=0 | Number of completed cycles in session |
| ScenarioHistory | List<ScenarioMetadata> | default empty | Completed cycle metadata records |

Validation rules:
- ActiveScenarioId must be null in BuildUp and Reset.
- ActiveScenarioId must be non-null in Committed, Approaching, and Climax.
- CompletedScenarios equals ScenarioHistory.Count.

### ThemeTrackerItem

Purpose: Candidate-level runtime state used by selection and suppression logic.

| Field | Type | Constraints | Description |
|---|---|---|---|
| IsScenarioCandidate | bool | default false | Whether entry is eligible for commitment |
| NarrativeFitScore | double | 0.0 to 1.0 | Computed fit for current evaluation |
| LastCandidateEvaluationTimeUtc | DateTime? | nullable | Last fit calculation timestamp |

Validation rules:
- NarrativeFitScore is clamped to [0.0, 1.0].
- IsScenarioCandidate false requires NarrativeFitScore to be 0.0 for commitment decisions.

### ThemeCatalogEntry

Purpose: Catalog definition extended for scenario-classification behavior.

| Field | Type | Constraints | Description |
|---|---|---|---|
| ScenarioTypeClassification | string | required | atmospheric or scenario-defining |
| DirectionalKeywords | List<string> | default empty | Intent-signaling keywords |

Validation rules:
- ScenarioTypeClassification must be one of atmospheric or scenario-defining.
- DirectionalKeywords may be empty for atmospheric themes.

## New Entities

### ScenarioMetadata

Purpose: Immutable completion record for one narrative cycle.

| Field | Type | Constraints | Description |
|---|---|---|---|
| ScenarioId | string | required | Completed scenario identifier |
| CompletedAtUtc | DateTime | required | Completion timestamp |
| InteractionCount | int | >=0 | Interactions in completed cycle |
| PeakThemeScore | int | 0 to 100 | Highest score reached |
| PeakDesireLevel | int | 0 to 100 | Highest desire across cycle |
| AverageRestraintLevel | double | 0 to 100 | Average restraint during cycle |
| Notes | string? | optional | Optional annotation |

### NarrativePhase (enum)

Purpose: Canonical phase state machine indicator.

Values:
- BuildUp
- Committed
- Approaching
- Climax
- Reset

## Transition Constraints

- BuildUp -> Committed: fit >= 0.60, at least 2 build-up interactions, and no tie delta <= 0.10 unresolved.
- Committed -> Approaching: active score >= 60, average desire >= 65, average restraint <= 45, and at least 3 interactions since commitment.
- Approaching -> Climax: active score >= 80, average desire >= 75, average restraint <= 35, and at least 2 interactions in approaching, unless explicit user-triggered climax override.
- Climax -> Reset: climax completion trigger reached.
- Reset -> BuildUp: reset process complete, active scenario cleared.

## Persistence Notes

- Persistence remains SQLite-backed through existing role-play session persistence pipeline.
- New fields are added to adaptive state payloads and must be backward compatible with null/default initialization.
