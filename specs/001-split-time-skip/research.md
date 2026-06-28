# Research: Multi-Encounter Climax Time-Skip — Two-Turn Split

**Date**: 2026-06-24  
**Status**: Complete

## Research Items

### 1. Existing Time-Skip Code Locations

**Decision**: Modify the existing code paths in `RolePlayEngineService.cs`, `AdaptiveScenarioState.cs`, and `RolePlayStateRepository.cs` rather than introducing new services or parallel paths.

**Rationale**: The time-skip injection is already a single, well-localized code path. The existing architecture keeps all time-skip logic in the overflow loop (lines 1496-1587 of `RolePlayEngineService.cs`), persistence in `RolePlayStateRepository.cs`, and state in `AdaptiveScenarioState.cs`. No new projects or services are warranted.

**Alternatives considered**:
- Separate `TimeSkipService` class: Rejected — would violate "no duplicated source selection logic" rule from `copilot-instructions.md` and add indirection with minimal benefit.

**Key source locations**:

| File | Lines | Purpose |
|------|-------|---------|
| `AdaptiveScenarioState.cs` | 155, 225 | `TimeSkipPending` bool field + `IsStateDirty` doc comment |
| `RolePlayStateRepository.cs` | 233, 245, 280 | INSERT columns/VALUES/ON CONFLICT for `TimeSkipPending` |
| `RolePlayStateRepository.cs` | 325 | Write parameter for `TimeSkipPending` |
| `RolePlayStateRepository.cs` | 520 | SELECT column list |
| `RolePlayStateRepository.cs` | 591 | Read: `TimeSkipPending = reader.GetInt32(34) != 0` |
| `RolePlayStateRepository.cs` | 1018-1022 | Schema migration for `TimeSkipPending` column |
| `RolePlayEngineService.cs` | 1499-1501 | Pre-loop: reset `InteractionsInCurrentEncounter` when `TimeSkipPending` |
| `RolePlayEngineService.cs` | 1508-1512 | Gate logic: `timeSkipActive` boolean check |
| `RolePlayEngineService.cs` | 1516 | Clear: `TimeSkipPending = false` |
| `RolePlayEngineService.cs` | 1557-1575 | Per-actor prompt selection (combined directive) |
| `RolePlayEngineService.cs` | 4107-4109 | Pipeline-batch `InteractionsInCurrentEncounter += generatedSinceLastEval` |
| `RolePlayEngineService.cs` | 4305+ | `AlignPromptNarrativeStateWithV2Async` |
| `RolePlayEngineService.cs` | 4554 | Boundary detection: `TimeSkipPending = true` |
| `MultiEncounterTimeSkipTests.cs` | 17-32 | Directive text assertions |

### 2. Schema Migration Strategy

**Decision**: Strategy B — Additive column with back-compat read (from `time-skip-split-instructions.md`, Finding 6).

**Rationale**: SQLite cannot easily drop/rename columns. Adding a new `CurrentTimeSkipPhase INTEGER NOT NULL DEFAULT 0` column and keeping the legacy `TimeSkipPending` column as a dead column avoids risky table rebuilds. Backfill: `UPDATE RolePlayV2AdaptiveStates SET CurrentTimeSkipPhase = 1 WHERE TimeSkipPending = 1`. On read, fall back to legacy `TimeSkipPending` if `CurrentTimeSkipPhase` is 0.

**Alternatives considered**:
- Strategy A (rename + reinterpret): Rejected — SQLite table rebuild is complex and risky for a local database with user data.
- Drop and recreate: Rejected — data loss risk is unacceptable.

### 3. `InteractionsInCurrentEncounter` Double-Increment

**Decision**: Gate the pipeline-batch increment on `CurrentTimeSkipPhase == None` (Finding 1).

**Rationale**: The counter is incremented in two places: per-interaction (line 2403) and pipeline-batch (line 4109). During time-skip turns, the pipeline-batch add would double-count. Adding `&& v2State.CurrentTimeSkipPhase == TimeSkipPhase.None` to the pipeline-batch condition prevents this.

**Alternatives considered**:
- Skip per-interaction increment during time-skip: Rejected — the counter should still reflect the real interaction count; only the batch add is redundant.
- Don't gate on `InteractionsInCurrentEncounter` for AdvanceTime: Already part of the plan — AdvanceTime gates only on phase + encounter > 1 + no user instruction.

### 4. `AlignPromptNarrativeStateWithV2Async` and Phase Sync

**Decision**: Do NOT sync `CurrentTimeSkipPhase` from DB in `AlignPromptNarrativeStateWithV2Async` (Finding 4).

**Rationale**: This method reloads state from DB mid-overflow-loop. Adding `CurrentTimeSkipPhase` to the sync list would clobber the in-memory phase transition (e.g., reset `AdvanceTime` back to `CloseScene` if DB hasn't been persisted yet). The existing fields `CurrentEncounterNumber`, `InteractionsInCurrentEncounter`, and `TimeSkipPending` are already intentionally NOT synced — `CurrentTimeSkipPhase` follows the same pattern.

**Alternatives considered**:
- Sync all fields: Rejected — would break the overflow loop's phase mutation.
- Always persist before Align: Rejected — adds unnecessary DB writes inside a hot loop.

### 5. `isNewEncounterStart` During AdvanceTime Retry

**Decision**: Add `CurrentTimeSkipPhase == TimeSkipPhase.None` guard to `isNewEncounterStart` (Finding 3).

**Rationale**: When AdvanceTime injection is skipped (user instruction active), the fallback prompt must not use "Continue the scene naturally." because the scene was already closed by the CloseScene turn. The phase check prevents this.

**Alternatives considered**:
- Always use "Continue the current encounter naturally": Rejected — semantically wrong during normal new-encounter starts.

### 6. `HasRecentUserInstruction` Invariant

**Decision**: No change needed — verified that time-skip injection interactions have `ActorName` = actor name (not "Instruction") and `GeneratedByCommand = "Continue"`, so `HasRecentUserInstruction` will not false-positive on them.

**Rationale**: The one-shot design is preserved. Confirmed by Finding 7 in the design document.

### 7. Old Behavior Replacement

**Decision**: Replace entirely — no configuration toggle.

**Rationale**: Confirmed by spec clarification Q2. The two-phase split is strictly better for narrative quality, simpler to implement (single code path), and avoids configuration complexity.

**Alternatives considered**:
- Configurable per-theme toggle: Rejected by clarification — adds unnecessary UX surface.

### 8. Cancel Mechanism

**Decision**: Defer-only — no explicit cancel mechanism.

**Rationale**: Confirmed by spec clarification Q1. Matches existing single-instruction behavior. User can effectively work around by continuing.

### 9. Test Strategy

**Decision**: Update existing `MultiEncounterTimeSkipTests.cs` directive text assertions and add new phase-transition tests. No new test file needed.

**Rationale**: The existing test file already covers the `HasRecentUserInstruction` helper and directive assertions. The split changes what text is asserted but the test structure remains valid. New tests cover the two-phase state machine transitions.

**New tests needed**:
- `CloseScene_Phase_Transitions_To_AdvanceTime`
- `AdvanceTime_Phase_Transitions_To_None`
- `UserInstruction_Skips_CloseScene_Keeps_Phase`
- `UserInstruction_Skips_AdvanceTime_Keeps_Phase`
- `isNewEncounterStart_False_During_AdvanceTime_Retry`
- `PipelineBatchIncrement_Skipped_During_TimeSkip`
- `CurrentTimeSkipPhase_Survives_Pipeline_Save_Reload`
