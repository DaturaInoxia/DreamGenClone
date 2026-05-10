# Data Model: Theme State Machine Continuity

**Phase**: 1 - Design and Contracts  
**Date**: 2026-05-08  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

---

## Entity Overview

```text
RPTheme
  1 -> * RPThemeMachineDefinition
            1 -> * RPThemeMachineState
            1 -> * RPThemeMachineTransition

RolePlaySession
  1 -> 1 AdaptiveScenarioState
            1 -> 1 ThemeMachineSessionSnapshot (JSON payload)

ThemeMachineEvaluator
  emits -> ThemeMachineDirective
  emits -> ThemeMachineDiagnosticEvent
```

---

## New Entities

### RPThemeMachineDefinition

**Purpose**: Versioned machine definition bound to one RP theme.

| Field | Type | Constraints | Description |
|---|---|---|---|
| DefinitionId | string | PK (guid string) | Unique definition record id |
| ThemeId | string | Required, FK -> RPThemes.Id | Theme owning this machine |
| MachineKey | string | Required, slug, max 80 | Stable machine id (ex: infidelity-brief-disappearance) |
| Version | int | Required, >= 1 | Definition version number |
| Name | string | Required, max 120 | Display name |
| IsActive | bool | Required | Active definition flag |
| IsSeeded | bool | Required | Built-in seed marker |
| CreatedUtc | DateTime | Required | Audit timestamp |
| UpdatedUtc | DateTime | Required | Audit timestamp |

**Validation**:
- Unique `(ThemeId, MachineKey, Version)`.
- At most one `IsActive = true` per `(ThemeId, MachineKey)`.
- Activation blocked when required states/transitions/gates are incomplete.

---

### RPThemeMachineState

**Purpose**: Named states for one machine definition.

| Field | Type | Constraints | Description |
|---|---|---|---|
| StateId | string | PK (guid string) | Unique state record id |
| DefinitionId | string | Required, FK | Parent definition |
| StateCode | string | Required, max 80 | Stable state code |
| Label | string | Required, max 120 | UI label |
| IsInitial | bool | Required | Initial state marker |
| IsTerminal | bool | Required | Terminal marker |
| SortOrder | int | Required | UI ordering |

**Validation**:
- Unique `(DefinitionId, StateCode)`.
- Exactly one initial state per definition.

---

### RPThemeMachineTransition

**Purpose**: Directed transition with deterministic priority and gate contract.

| Field | Type | Constraints | Description |
|---|---|---|---|
| TransitionId | string | PK (guid string) | Unique transition id |
| DefinitionId | string | Required, FK | Parent definition |
| FromStateCode | string | Required | Source state |
| ToStateCode | string | Required | Target state |
| Priority | int | Required | Deterministic tie-break |
| TriggerType | string | Required | Trigger category |
| GateConfigJson | string | Required | Gate contract payload |
| BlockReasonCode | string | Required | Reason used when blocked |
| IsEnabled | bool | Required | Runtime eligibility toggle |
| CreatedUtc | DateTime | Required | Audit timestamp |
| UpdatedUtc | DateTime | Required | Audit timestamp |

**Validation**:
- Unique `(DefinitionId, FromStateCode, Priority)`.
- `FromStateCode` and `ToStateCode` must exist in parent definition.
- `GateConfigJson` must deserialize to required gate model; invalid payload fails activation.

---

### ThemeMachineSessionSnapshot

**Purpose**: Per-session runtime machine state, persisted in adaptive state payload.

| Field | Type | Constraints | Description |
|---|---|---|---|
| MachineKey | string | Required | Resolved machine id |
| ThemeId | string | Required | Resolved theme id |
| DefinitionId | string | Required | Pinned definition id |
| DefinitionVersion | int | Required | Pinned version |
| CurrentStateCode | string | Required | Active state |
| TurnsInCurrentState | int | Required, >= 0 | Interaction counter |
| ReturnBeatCompleted | bool | Required | Return-beat gate flag |
| LastTransitionId | string? | Optional | Last applied transition |
| LastTransitionUtc | DateTime? | Optional | Transition timestamp |
| LastTransitionReasonCode | string? | Optional | Transition reason |
| LastEvaluatedUtc | DateTime | Required | Last evaluation time |

**Validation**:
- Missing required fields fail load explicitly (no defaulting).
- `CurrentStateCode` must exist in pinned definition.

---

### ThemeMachineDirective

**Purpose**: Deterministic constraints emitted by evaluator for downstream services.

| Field | Type | Constraints | Description |
|---|---|---|---|
| SessionId | string | Required | Session scope |
| CurrentStateCode | string | Required | State context |
| BlockDisappearanceCandidates | bool | Required | Selection constraint |
| RequiredNarrativeBeats | list<string> | Required | Prompt obligations |
| PromptHardConstraints | list<string> | Required | Must-follow directives |
| ReasonCodes | list<string> | Required | Audit-friendly reasons |

---

### ThemeMachineDiagnosticEvent

**Purpose**: Persisted machine lifecycle diagnostics.

| Field | Type | Constraints | Description |
|---|---|---|---|
| EventId | string | PK | Event id |
| SessionId | string | Required | Session scope |
| ThemeId | string | Required | Theme scope |
| MachineKey | string | Required | Machine scope |
| DefinitionVersion | int | Required | Version context |
| EventType | string | Required | init, transition, blocked, failure, migrate, auth-denied |
| FromStateCode | string? | Optional | Source state |
| ToStateCode | string? | Optional | Target state |
| TransitionId | string? | Optional | Transition reference |
| ReasonCode | string | Required | Structured reason |
| PayloadJson | string | Required | Extended metadata |
| OccurredUtc | DateTime | Required | Event time |

---

## Transition Model: First Production Machine

Machine key: `infidelity-brief-disappearance`

| From | To | Gate Requirements | Expected Directive Effect |
|---|---|---|---|
| PublicBaseline | EncounterInProgress | disappearance trigger condition passes | allow encounter progression |
| EncounterInProgress | ReturnBeatRequired | encounter completion signal present | block new disappearance candidates |
| ReturnBeatRequired | ReintegrationCooldown | return-beat completion recorded | require reintegration narrative |
| ReintegrationCooldown | NextDisappearanceEligible | min cooldown interactions reached AND return-beat completed | re-enable disappearance eligibility |
| NextDisappearanceEligible | EncounterInProgress | new disappearance trigger passes | start next cycle |

**Determinism rule**: If multiple transitions from same source state are eligible, choose highest priority.

---

## Relationships and Cardinality

```text
RPTheme 1 -> * RPThemeMachineDefinition
RPThemeMachineDefinition 1 -> * RPThemeMachineState
RPThemeMachineDefinition 1 -> * RPThemeMachineTransition
RolePlaySession 1 -> 1 ThemeMachineSessionSnapshot
RolePlaySession 1 -> * ThemeMachineDiagnosticEvent
```

---

## Cross-Entity Invariants

1. Machine resolution path is singular: `ActiveScenario -> RPTheme -> active RPThemeMachineDefinition`.
2. Sessions pin one definition version at start; changes require explicit migrate action.
3. Non-admin users cannot mutate definitions or migrate session versions.
4. Missing/invalid required config fails explicitly; no fallback/default machine behavior is allowed.
5. `ReintegrationCooldown -> NextDisappearanceEligible` requires both cooldown interaction count and return-beat completion flag.
