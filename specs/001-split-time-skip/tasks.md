# Tasks: Multi-Encounter Climax Time-Skip — Two-Turn Split

**Input**: Design documents from `/specs/001-split-time-skip/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are included per the feature specification. Each user story has corresponding test tasks that validate the behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

This project uses a 4-project layered architecture:
- `DreamGenClone.Domain/` — Domain models and enums
- `DreamGenClone.Infrastructure/` — Persistence and infrastructure
- `DreamGenClone.Web/Application/` — Application services and engine logic
- `DreamGenClone.Tests/` — Test project

---

## Phase 1: Setup (Verify Baseline)

**Purpose**: Confirm the project builds and existing tests pass before any changes

- [x] T001 Verify solution builds clean: `dotnet build DreamGenClone.sln`
- [x] T002 [P] Run existing MultiEncounterTimeSkip tests to confirm baseline: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~MultiEncounterTimeSkip"`

---

## Phase 2: Foundational — Domain Model & Schema (Blocking Prerequisites)

**Purpose**: Introduce the `TimeSkipPhase` enum and database schema migration. ALL user stories depend on these changes.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T003 Add `TimeSkipPhase` enum to `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` with values `None=0, CloseScene=1, AdvanceTime=2`
- [x] T004 Replace `public bool TimeSkipPending` with `public TimeSkipPhase CurrentTimeSkipPhase` in `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` (line ~155)
- [x] T005 Update `IsStateDirty` XML doc comment (line ~225) in `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` to reference `CurrentTimeSkipPhase` instead of `TimeSkipPending`
- [x] T006 Add `CurrentTimeSkipPhase INTEGER NOT NULL DEFAULT 0` column migration + backfill in `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` after line 1022 in `EnsureAdaptiveStateSchemaAsync`. Backfill: `UPDATE RolePlayV2AdaptiveStates SET CurrentTimeSkipPhase = 1 WHERE TimeSkipPending = 1`
- [x] T007 [P] Add `CurrentTimeSkipPhase` to INSERT column list (line ~233) in `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs`
- [x] T008 [P] Add `$currentTimeSkipPhase` to INSERT VALUES (line ~245) in `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs`
- [x] T009 [P] Add `CurrentTimeSkipPhase = excluded.CurrentTimeSkipPhase` to ON CONFLICT UPDATE (line ~280) in `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs`
- [x] T010 Replace write parameter: `$timeSkipPending` → `(int)state.CurrentTimeSkipPhase` and write 0 to legacy `$timeSkipPending` (line ~325) in `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs`
- [x] T011 Add `CurrentTimeSkipPhase` to SELECT column list at ordinal 35 (line ~520) in `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs`
- [x] T012 Update read logic at ordinal 35 with back-compat fallback to legacy `TimeSkipPending` at ordinal 34 (line ~591) in `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs`
- [x] T013 Build to verify Domain + Infrastructure changes compile: `dotnet build DreamGenClone.sln`

**Checkpoint**: Enum, state property, and persistence layer ready — engine logic can now use `CurrentTimeSkipPhase`

---

## Phase 3: User Story 1 — Natural Scene Close-Out Before Time Advance (Priority: P1) 🎯 MVP

**Goal**: Split the combined time-skip directive into two separate turns: CloseScene then AdvanceTime. The core behavioral change.

**Independent Test**: Start a multi-encounter climax scenario, let encounters progress until a boundary is detected, and observe that the AI first closes the current encounter in one response, then on the next Continue, advances to a new scene.

### Implementation for User Story 1

- [x] T014 [US1] Rewrite overflow loop pre-loop gate logic (lines ~1496-1542) in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`:
  - Read `session.AdaptiveState.CurrentTimeSkipPhase` instead of `TimeSkipPending`
  - Reset `InteractionsInCurrentEncounter = 0` for both phases (CloseScene and AdvanceTime)
  - Gate CloseScene on `InteractionsInCurrentEncounter == 0` + encounter > 1 + no user instruction
  - Gate AdvanceTime on `phase == AdvanceTime` + encounter > 1 + no user instruction (no counter gate)
  - Set `session.AdaptiveState.IsStateDirty = true` on phase mutation
  - Transition `CloseScene → AdvanceTime` (not `TimeSkipPending = false`) and `AdvanceTime → None`
  - Update debug event kinds: `MultiEncounterTimeSkipCloseSceneInjected`, `MultiEncounterTimeSkipAdvanceTimeInjected`, `MultiEncounterTimeSkipSkippedDueToUserInstruction`

- [x] T015 [US1] Rewrite per-actor prompt selection (lines ~1557-1575) in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`:
  - Split combined directive into two: CloseScene directive = `"Close the current encounter naturally."`, AdvanceTime directive = `"Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life."`
  - Add `CurrentTimeSkipPhase == TimeSkipPhase.None` guard to `isNewEncounterStart` check
  - Both directives use `PromptIntent.Instruction` (one-shot, not persistent)

- [x] T016 [US1] Add phase guard to pipeline-batch increment (line ~4107-4109) in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`: add `&& v2State.CurrentTimeSkipPhase == TimeSkipPhase.None` to the condition

- [x] T017 [US1] Add explicit comment in `AlignPromptNarrativeStateWithV2Async` (line ~4305) in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` documenting that `CurrentTimeSkipPhase`, `CurrentEncounterNumber`, and `InteractionsInCurrentEncounter` are intentionally NOT synced from the DB snapshot

- [x] T018 [US1] Update `TryDetectEncounterBoundaryAsync` (line ~4554) in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`: set `state.CurrentTimeSkipPhase = TimeSkipPhase.CloseScene` instead of `state.TimeSkipPending = true`

- [x] T019 [US1] Build and fix any remaining compile errors from `TimeSkipPending` → `CurrentTimeSkipPhase` rename across all files

### Tests for User Story 1

- [x] T020 [P] [US1] Update directive text assertions in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`:
  - `TimeSkipDirective_TextHasNoEncounterNumber`: Split assertions for CloseScene and AdvanceTime directives separately
  - `TimeSkipDirective_FocusesOnCloseAndAdvance`: Assert CloseScene directive contains "Close the current encounter", AdvanceTime directive contains "advance time" and "ordinary life"

- [x] T021 [US1] Add test `CloseScene_Phase_Transitions_To_AdvanceTime` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: verify that after boundary detection, `CurrentTimeSkipPhase == CloseScene`, and after injection, `CurrentTimeSkipPhase == AdvanceTime`

- [x] T022 [US1] Add test `AdvanceTime_Phase_Transitions_To_None` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: verify that after AdvanceTime injection, `CurrentTimeSkipPhase == None`

- [x] T023 [US1] Run US1 tests: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~MultiEncounterTimeSkip"`

**Checkpoint**: Core two-phase split functional — close-scene and advance-time fire on separate turns

---

## Phase 4: User Story 2 — User Instruction During Time-Skip Does Not Interrupt Flow (Priority: P2)

**Goal**: Verify that user-typed instructions defer (not cancel) a pending time-skip phase. The deferral logic is already implemented in US1's gate — this phase adds targeted test coverage.

**Independent Test**: Trigger a close-scene phase, then type a user instruction instead of Continue. On the next Continue, verify the close-scene instruction fires (not lost). Repeat for the advance-time phase.

### Implementation for User Story 2

- [x] T024 [US2] Verify the `HasRecentUserInstruction` guard in the overflow loop gate (US1) correctly preserves the phase for both CloseScene and AdvanceTime — the gate already leaves `CurrentTimeSkipPhase` unchanged when a user instruction is detected. No code changes expected.

### Tests for User Story 2

- [x] T025 [P] [US2] Add test `UserInstruction_Skips_CloseScene_Keeps_Phase` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: with `CurrentTimeSkipPhase == CloseScene` and a user instruction in the last 3 interactions, verify the phase remains `CloseScene` after the turn

- [x] T026 [P] [US2] Add test `UserInstruction_Skips_AdvanceTime_Keeps_Phase` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: with `CurrentTimeSkipPhase == AdvanceTime` and a user instruction in the last 3 interactions, verify the phase remains `AdvanceTime` after the turn

- [x] T027 [US2] Add test `UserInstruction_Deferred_Multiple_Times_Still_Fires` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: defer CloseScene twice via user instructions, then plain Continue — verify CloseScene fires

- [x] T028 [US2] Run US2 tests: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~MultiEncounterTimeSkip"`

**Checkpoint**: User instruction deferral verified for both time-skip phases

---

## Phase 5: User Story 3 — Time-Skip Survives Session Interruptions (Priority: P2)

**Goal**: Verify that the time-skip phase persists across session save/load cycles. The persistence is already implemented in Phase 2 — this phase adds tests to prove round-trip correctness.

**Independent Test**: Trigger close-scene, then simulate save + reload of adaptive state. Verify the phase is preserved. Repeat for advance-time.

### Tests for User Story 3

- [x] T029 [P] [US3] Add test `CurrentTimeSkipPhase_Survives_Pipeline_Save_Reload` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: set `CurrentTimeSkipPhase = CloseScene`, call save then reload adaptive state, verify `CurrentTimeSkipPhase == CloseScene`

- [x] T030 [P] [US3] Add test `AdvanceTime_Phase_Survives_Save_Reload` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: set `CurrentTimeSkipPhase = AdvanceTime`, save then reload, verify `CurrentTimeSkipPhase == AdvanceTime`

- [x] T031 [P] [US3] Add test `None_Phase_Survives_Save_Reload` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: set `CurrentTimeSkipPhase = None`, save then reload, verify `CurrentTimeSkipPhase == None`

- [x] T032 [US3] Add test `Phase_Persists_Across_Full_Session_Cycle` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: verify that after a complete overflow-loop → pipeline → save → reload cycle, the `CurrentTimeSkipPhase` value survives intact

- [x] T033 [US3] Run US3 tests: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~MultiEncounterTimeSkip"`

**Checkpoint**: Phase persistence across session boundaries verified

---

## Phase 6: User Story 4 — Existing Sessions Migrate Gracefully (Priority: P3)

**Goal**: Verify that sessions created before the two-phase update seamlessly transition. Legacy `TimeSkipPending = 1` rows must be interpreted as `CloseScene`. The backfill logic is already in Phase 2 (T006).

**Independent Test**: With a simulated old session that has `TimeSkipPending = 1`, load it after migration and verify `CurrentTimeSkipPhase == CloseScene`.

### Tests for User Story 4

- [x] T034 [US4] Add test `Legacy_TimeSkipPending_1_Backfilled_To_CloseScene` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: write `TimeSkipPending = 1` to DB, trigger migration, verify `CurrentTimeSkipPhase == CloseScene`

- [x] T035 [US4] Add test `Legacy_TimeSkipPending_0_Remains_None` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: write `TimeSkipPending = 0` to DB, trigger migration, verify `CurrentTimeSkipPhase == None`

- [x] T036 [US4] Add test `BackCompat_Read_Fallback_To_Legacy` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: verify that when `CurrentTimeSkipPhase` column is 0 but `TimeSkipPending = 1`, the read path returns `CloseScene` (back-compat read)

- [x] T037 [US4] Run US4 tests: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~MultiEncounterTimeSkip"`

**Checkpoint**: Legacy sessions migrate and function correctly

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, edge case tests, and build confirmation

- [x] T038 [P] Add test `isNewEncounterStart_False_During_AdvanceTime_Retry` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: verify that when AdvanceTime injection is skipped (user instruction), the fallback prompt is NOT `"Continue the scene naturally."`

- [x] T039 [P] Add test `PipelineBatchIncrement_Skipped_During_TimeSkip` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: verify that `InteractionsInCurrentEncounter` is not double-counted during time-skip turns

- [x] T040 [P] Add test `IsStateDirty_Set_On_Phase_Mutation` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`: verify `IsStateDirty == true` after `CurrentTimeSkipPhase` is mutated in the overflow loop

- [x] T041 Run full test suite for the feature: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~MultiEncounterTimeSkip"`

- [x] T042 Run full solution tests to check for regressions: `dotnet test DreamGenClone.sln`

- [x] T043 Run `quickstart.md` validation: verify build + all tests pass

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (baseline verified) — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Phase 2 — Core split implementation
- **User Story 2 (Phase 4)**: Depends on Phase 3 (US1 gate logic) — Deferral verification
- **User Story 3 (Phase 5)**: Depends on Phase 2 (persistence layer) — Can start in parallel with US1 implementation if desired, but tests need US1 gate logic for full-cycle test (T032)
- **User Story 4 (Phase 6)**: Depends on Phase 2 (schema migration T006) — Can start in parallel with other stories
- **Polish (Phase 7)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Phase 2. No dependencies on other stories.
- **User Story 2 (P2)**: Depends on US1 (gate logic). Must follow US1.
- **User Story 3 (P2)**: Persistence tests (T029-T031) can start after Phase 2. Full-cycle test (T032) needs US1 engine logic.
- **User Story 4 (P3)**: Can start after Phase 2. No dependency on US1/US2/US3. Tests are pure persistence/migration checks.

### Within Each User Story

- Implementation tasks before test tasks
- T019 (build & fix compile errors) MUST complete before any test task

### Parallel Opportunities

- Phase 1: T001 and T002 can run in parallel (or sequentially — both fast)
- Phase 2: T007, T008, T009 are parallelizable (different lines, same file — but fine to do together)
- Phase 3 tests: T020, T021, T022 can run in parallel after T019
- Phase 4 tests: T025, T026 can run in parallel
- Phase 5 tests: T029, T030, T031 can run in parallel
- Phase 6 tests: T034, T035 can run in parallel
- Phase 7 tests: T038, T039, T040 can all run in parallel
- US4 (Phase 6) can run in parallel with US2/US3 if desired (no engine dependency)

---

## Parallel Example: User Story 1

```bash
# After T019 (build fix), run all US1 tests in parallel:
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj \
  --filter "FullyQualifiedName~MultiEncounterTimeSkip" \
  --logger "console;verbosity=detailed"
```

## Parallel Example: Phase 2 Persistence Tasks

```text
T007 (INSERT columns), T008 (INSERT VALUES), T009 (ON CONFLICT UPDATE) are all in
different sections of the same file but touch different lines — they can be done
in a single multi-replace edit operation.
```

## Parallel Example: Test Phases 4-7

```bash
# After US1 implementation complete, run all test phases together:
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj \
  --filter "FullyQualifiedName~MultiEncounterTimeSkip"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (verify baseline)
2. Complete Phase 2: Foundational (enum + schema)
3. Complete Phase 3: User Story 1 (core split + basic tests)
4. **STOP and VALIDATE**: Build, run US1 tests, manually verify two-phase split works
5. Deploy/demo if ready

### Incremental Delivery

1. MVP = US1 (Phase 1 + 2 + 3) — Core two-phase time-skip functional
2. +US2 (Phase 4) — User instruction deferral verified
3. +US3 (Phase 5) — Session survival verified
4. +US4 (Phase 6) — Legacy migration verified
5. +Polish (Phase 7) — Edge case coverage + regression tests

### Suggested MVP Scope

Phases 1–3 deliver the entire functional change. Phases 4–7 add verification for edge cases (user instructions, session survival, migration, and additional guards). The MVP is fully functional and safe to deploy after Phase 3.
