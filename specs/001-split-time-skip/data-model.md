# Data Model: Multi-Encounter Climax Time-Skip — Two-Turn Split

**Date**: 2026-06-24  
**Feature**: `001-split-time-skip`

## Entity Changes

### Modified Entity: AdaptiveScenarioState

**Table**: `RolePlayV2AdaptiveStates` (SQLite)

#### Field Replacement

| Before | After |
|--------|-------|
| `bool TimeSkipPending` | `TimeSkipPhase CurrentTimeSkipPhase` |

#### New Enum: TimeSkipPhase

| Value | Integer | Meaning |
|-------|---------|---------|
| `None` | 0 | No time-skip pending. Normal continuation flow. |
| `CloseScene` | 1 | Close-scene directive pending. Will inject "Close the current encounter naturally." on next plain Continue. |
| `AdvanceTime` | 2 | Advance-time directive pending. Will inject "Advance time to a new moment…" on next plain Continue. |

#### State Machine

```
┌─────────────────────────────────────────────────────────┐
│                                                         │
│  Encounter boundary detected                            │
│  ┌─────────────────────────┐                            │
│  │                         ▼                            │
│  │  None ──────────► CloseScene ──────► AdvanceTime ────┘
│       (normal flow)   │  pending          pending
│                       │                    │
│                       │  user instruction  │  user instruction
│                       │  (defer)           │  (defer)
│                       ▼                    ▼
│                   CloseScene           AdvanceTime
│                   (unchanged)          (unchanged)
```

**Transitions**:

| Trigger | From | To |
|---------|------|----|
| `TryDetectEncounterBoundaryAsync` advances encounter | (any) | `CloseScene` |
| Overflow loop injects close-scene directive | `CloseScene` | `AdvanceTime` |
| Overflow loop injects advance-time directive | `AdvanceTime` | `None` |
| User instruction detected (either phase) | `CloseScene` or `AdvanceTime` | (unchanged — defer) |

#### Schema Migration (Additive)

New column on `RolePlayV2AdaptiveStates`:

```sql
ALTER TABLE RolePlayV2AdaptiveStates
ADD COLUMN CurrentTimeSkipPhase INTEGER NOT NULL DEFAULT 0;
```

Backfill for existing in-flight sessions:

```sql
UPDATE RolePlayV2AdaptiveStates
SET CurrentTimeSkipPhase = 1
WHERE TimeSkipPending = 1;
```

Legacy `TimeSkipPending` column remains in schema as a dead column (always written as 0).

#### Validation Rules

- `CurrentTimeSkipPhase` MUST be one of `None` (0), `CloseScene` (1), or `AdvanceTime` (2).
- `CurrentTimeSkipPhase` is only active when `CurrentPhase == Climax` and the active theme has `ClimaxMode: multi-encounter`.
- When `CurrentTimeSkipPhase != None` and a user instruction is in the recent 3 interactions, the injection MUST be deferred (phase unchanged).
- `IsStateDirty` MUST be set to `true` whenever `CurrentTimeSkipPhase` is mutated in the overflow loop or boundary detection.

## Relationships

- **AdaptiveScenarioState** → **RolePlaySession**: One-to-one (each session has one adaptive state).
- **CurrentTimeSkipPhase** → **CurrentPhase**: Only meaningful when `CurrentPhase == Climax`.
- **CurrentTimeSkipPhase** → **CurrentEncounterNumber**: Encounter boundary detection (which sets `CloseScene`) requires `CurrentEncounterNumber > 0`.

## No New Entities

This feature modifies an existing entity only. No new tables, entities, or relationships are introduced.
