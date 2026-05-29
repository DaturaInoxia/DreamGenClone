# Tasks: Semantic Analysis — Dedicated Model & Concurrent Processing

**Branch**: `001-semantic-dedicated-model`  
**Input**: `specs/001-semantic-dedicated-model/` — plan.md, spec.md, data-model.md, contracts/service-contracts.md, research.md, quickstart.md

## Format: `[ID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no incomplete-task dependencies)
- **[USx]**: User story this task belongs to (US1/US2/US3)

---

## Phase 1: Setup

No project scaffolding needed — all five projects already exist in the solution.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain, infrastructure, and in-process queue/worker plumbing that MUST be complete before any user story can be implemented or tested.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T001 Add `RolePlaySemanticAnalysis = 11` to `AppFunction` enum in `DreamGenClone.Domain/ModelManager/AppFunction.cs`
- [X] T002 [P] Add `public int? MaxConcurrentJobs { get; set; }` property to `FunctionModelDefault` in `DreamGenClone.Domain/ModelManager/FunctionModelDefault.cs`
- [X] T003 Add `MaxConcurrentJobs INTEGER NULL` column to the `CREATE TABLE IF NOT EXISTS FunctionModelDefaults` statement (fresh-install path) in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- [X] T004 Add `ALTER TABLE FunctionModelDefaults ADD COLUMN MaxConcurrentJobs INTEGER NULL` under the legacy migration gate (existing-database upgrade path) in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- [X] T005 Update `FunctionDefaultRepository.GetByFunctionAsync` to read and map `MaxConcurrentJobs` from the result set in `DreamGenClone.Infrastructure/ModelManager/FunctionDefaultRepository.cs`
- [X] T006 Update `FunctionDefaultRepository.SaveAsync` to include `MaxConcurrentJobs` in the INSERT/UPDATE statement in `DreamGenClone.Infrastructure/ModelManager/FunctionDefaultRepository.cs`
- [X] T007 Create `ISemanticBackgroundJobQueue` interface (mirrors `IBackgroundJobQueue` — `EnqueueJob`, `DequeueAsync`, `MarkProcessing`, `MarkCompleted`, `MarkFailed`, `GetPendingCount`) in `DreamGenClone.Web/Application/BackgroundJobs/ISemanticBackgroundJobQueue.cs`
- [X] T008 [P] Create `SemanticBackgroundJobQueue` implementation (`Channel<BackgroundJobEnvelope>` + `ConcurrentDictionary` dedup, copy pattern from `GenericBackgroundJobQueue`) in `DreamGenClone.Web/Application/BackgroundJobs/SemanticBackgroundJobQueue.cs`
- [X] T009 [P] Create `SemanticBackgroundJobWorker : BackgroundService` — on `ExecuteAsync` read `MaxConcurrentJobs` from `IFunctionDefaultRepository.GetByFunctionAsync(AppFunction.RolePlaySemanticAnalysis)`, clamp to `[1, 16]` with default `2` when null, create `SemaphoreSlim`, run processing loop (`dequeue → acquire semaphore → Task.WhenAll → release`), on job exception log Warning with SessionId/InteractionId/exception type, set Error state, release semaphore, continue loop in `DreamGenClone.Web/Application/BackgroundJobs/SemanticBackgroundJobWorker.cs`
- [X] T010 Register `ISemanticBackgroundJobQueue` as Singleton (`SemanticBackgroundJobQueue`) and `SemanticBackgroundJobWorker` as a hosted service (`AddHostedService<SemanticBackgroundJobWorker>()`) in `DreamGenClone.Web/Program.cs`

**Checkpoint**: Build `DreamGenClone.sln` — 0 errors. Foundation ready; all user story work can now proceed.

---

## Phase 3: User Story 1 — Assign a Dedicated Model for Background Semantic Analysis (Priority: P1) 🎯 MVP

**Goal**: The `RolePlaySemanticAnalysis` function slot exists, persists independently, resolves to its own model during inference, and fails fast (no fallback) when no model is assigned. The Model Manager UI shows the row with a warning badge when unassigned.

**Independent Test**: Open Model Manager → Function Defaults table → observe "RP Semantic Analysis (Background)" row → assign any model → save → reload → verify assignment is retained. No RP session required to validate this story.

### Implementation for User Story 1

- [X] T011 [US1] Update `SemanticEventInferenceService.InferAsync` — change `AppFunction.RolePlayGeneration` to `AppFunction.RolePlaySemanticAnalysis` in the `ResolveAsync` call, wrap in `try/catch (ModelResolutionException ex)`, on catch log Warning with structured context (SessionId, InteractionId, function name) and return `SemanticEventInferenceResult { Success = false, ErrorMessage = ex.Message, Events = [] }` in `DreamGenClone.Web/Application/RolePlay/SemanticEventInferenceService.cs`
- [X] T012 [US1] Update `RolePlayEngineService.QueueSemanticInteractionAnalysisAsync` — replace `IBackgroundJobQueue` injection with `ISemanticBackgroundJobQueue` and enqueue semantic jobs to the dedicated queue in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [X] T013 [US1] Add "RP Semantic Analysis (Background)" row with model assignment dropdown to the Function Defaults table in `DreamGenClone.Web/Components/Pages/ModelManager.razor`
- [X] T014 [US1] Add warning badge/icon with tooltip "No model assigned — semantic analysis will fail until a model is selected." on the RolePlaySemanticAnalysis row, shown when `ModelId` is empty or the assigned model is disabled/deleted in `DreamGenClone.Web/Components/Pages/ModelManager.razor`
- [X] T015 [P] [US1] Create `SemanticEventInferenceServiceTests` — assert `ResolveAsync` is called with `AppFunction.RolePlaySemanticAnalysis` (mock `IModelResolutionService`); assert that when mock throws `ModelResolutionException` the service returns `Success = false` with zero events and does not rethrow in `DreamGenClone.Tests/RolePlay/SemanticEventInferenceServiceTests.cs`

**Checkpoint**: "RP Semantic Analysis (Background)" row visible in Model Manager; model assignment persists; inference service uses the new slot; `ModelResolutionException` produces `Error` state with no fallback. User Story 1 fully functional and independently testable.

---

## Phase 4: User Story 2 — Configure Maximum Parallel Semantic Jobs (Priority: P1)

**Goal**: The Max Parallel field is visible and editable on the RolePlaySemanticAnalysis row; valid values (1–16) persist and are read by the worker at startup to enforce the concurrency ceiling; an inline hint confirms restart is needed after save.

**Independent Test**: Set Max Parallel to 3. Start three concurrent RP sessions and trigger interactions in all three simultaneously. Observe semantic analysis completing for all three without one blocking another.

### Implementation for User Story 2

- [X] T016 [US2] Update `ModelManagerFacade.SaveFunctionDefaultAsync` to pass `MaxConcurrentJobs` from the UI-bound model into the saved `FunctionModelDefault` entity in `DreamGenClone.Web/Application/ModelManager/ModelManagerFacade.cs`
- [X] T017 [US2] Add "Max Parallel" numeric input (min=1, max=16, nullable) bound to `MaxConcurrentJobs` on the RolePlaySemanticAnalysis row in `DreamGenClone.Web/Components/Pages/ModelManager.razor`
- [X] T018 [US2] Add UI validation rejecting `MaxConcurrentJobs` values outside 1–16 — show inline validation message; block save until value is corrected in `DreamGenClone.Web/Components/Pages/ModelManager.razor`
- [X] T019 [US2] Show inline static hint "Takes effect on next restart." below the Max Parallel field once a value has been saved in `DreamGenClone.Web/Components/Pages/ModelManager.razor`
- [X] T020 [P] [US2] Create `FunctionDefaultRepositoryTests` — assert `SaveAsync` persists `MaxConcurrentJobs` and `GetByFunctionAsync` returns the same value; assert `GetByFunctionAsync` returns `null` for `MaxConcurrentJobs` when the column is absent (migration compatibility) in `DreamGenClone.Tests/RolePlay/FunctionDefaultRepositoryTests.cs`
- [X] T021 [P] [US2] Create `SemanticBackgroundJobWorkerTests` — assert worker reads `MaxConcurrentJobs` at startup from the repository; assert no more than `MaxConcurrentJobs` tasks run simultaneously (use delayed task simulation); assert that a job exception causes a Warning log and the loop continues to the next job without crashing in `DreamGenClone.Tests/RolePlay/SemanticBackgroundJobWorkerTests.cs`

**Checkpoint**: Max Parallel field saves and roundtrips correctly; worker respects the cap; inline hint renders; validation blocks out-of-range values. User Story 2 fully functional and independently testable.

---

## Phase 5: User Story 3 — Semantic Jobs Are Isolated from General Background Jobs (Priority: P2)

**Goal**: Semantic analysis jobs flow exclusively through `ISemanticBackgroundJobQueue` and `SemanticBackgroundJobWorker`; the `GenericBackgroundJobWorker` receives no `SemanticInteractionAnalysis` jobs. Worker isolation is verified by tests and a build-time grep check.

**Independent Test**: Trigger a batch story analysis job concurrently with several RP interactions requiring semantic analysis. Confirm both proceed independently — story jobs process via the generic worker, semantic jobs via the dedicated worker.

### Implementation for User Story 3

- [ ] T022 [US3] Add isolation assertions to `SemanticBackgroundJobWorkerTests` — assert that only `SemanticInteractionAnalysis` job types are dequeued from `ISemanticBackgroundJobQueue`; assert that a mock `IBackgroundJobQueue` receives zero `SemanticInteractionAnalysis` enqueue calls during a simulated session with multiple interactions in `DreamGenClone.Tests/RolePlay/SemanticBackgroundJobWorkerTests.cs`
- [X] T023 [P] [US3] Verify no `AppFunction.RolePlayGeneration` call remains in `SemanticEventInferenceService.cs` — run `dotnet build DreamGenClone.sln -v minimal` (0 errors) and confirm via grep that `SemanticEventInferenceService` references only `AppFunction.RolePlaySemanticAnalysis` in `DreamGenClone.Web/Application/RolePlay/SemanticEventInferenceService.cs`

**Checkpoint**: Worker isolation confirmed by tests and static check. User Story 3 complete. All three user stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation across all user stories.

- [ ] T024 Run `dotnet test DreamGenClone.Tests -v minimal` — all new test classes pass; all pre-existing tests continue to pass
- [ ] T025 [P] Run quickstart.md validation steps — assign model, verify persistence, verify model slot used, verify fail-fast (no-model path), verify concurrency cap per `specs/001-semantic-dedicated-model/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup)         → no dependencies; skip (nothing to do)
Phase 2 (Foundational)  → no dependencies; start immediately
Phase 3 (US1, P1)       → requires Phase 2 complete
Phase 4 (US2, P1)       → requires Phase 2 complete; integrates with Phase 3 (UI row must exist for Max Parallel field)
Phase 5 (US3, P2)       → requires Phase 3 and Phase 4 complete (tests verify integrated behaviour)
Phase 6 (Polish)        → requires all prior phases complete
```

### User Story Dependencies

| Story | Depends On | Can Parallel With |
|---|---|---|
| US1 (P1) | Foundational complete | US2 (different files until ModelManager.razor UI) |
| US2 (P1) | Foundational complete; US1 UI row (T013) before Max Parallel field (T017–T019) | US1 up to T013 |
| US3 (P2) | US1 + US2 complete (tests verify integrated isolation) | — |

### Within US1 (Phase 3)

```
T011 → T012 (sequential, service → engine)
T013 → T014 (sequential, add row → add badge on row)
T015 (parallel with T011–T014, different file)
```

### Within US2 (Phase 4)

```
T013 must be done (US1) before T017–T019 (Max Parallel field on the same row)
T016 → T017 → T018 → T019 (sequential, same file / same form flow)
T020, T021 (parallel with T016–T019, different files)
```

### Parallel Execution Examples

**Foundational (Phase 2)**:
- Pair 1: T001 + T002 (different files, no deps)
- Pair 2: T008 + T009 (after T007, different files)

**US1 (Phase 3)**:
- T011 + T015 (different files; tests can be written alongside implementation)

**US2 (Phase 4)**:
- T020 + T021 (different test files, no deps on each other)

---

## Implementation Strategy

### MVP Scope: User Story 1 only (Phase 2 + Phase 3)

The minimum shippable increment gives the user a dedicated model slot, fail-fast on missing assignment, and the Model Manager row. It does not require the Max Parallel UI (US2) or the isolation test verification (US3).

MVP task count: T001–T010 (Foundational) + T011–T015 (US1) = **15 tasks**

### Incremental Delivery

1. **MVP**: Phase 2 + Phase 3 → dedicated model slot end-to-end
2. **Increment 2**: Phase 4 → max parallel config, UI field, persistence roundtrip
3. **Increment 3**: Phase 5 → isolation tests + build verification
4. **Done**: Phase 6 → full validation pass

---

## Summary

| Metric | Value |
|---|---|
| Total tasks | 25 (T001–T025) |
| US1 (P1) tasks | 5 (T011–T015) |
| US2 (P1) tasks | 6 (T016–T021) |
| US3 (P2) tasks | 2 (T022–T023) |
| Foundational tasks | 10 (T001–T010) |
| Polish tasks | 2 (T024–T025) |
| Parallelizable [P] tasks | 12 |
| New files | 3 (ISemanticBackgroundJobQueue, SemanticBackgroundJobQueue, SemanticBackgroundJobWorker) |
| Modified files | 10 |
| New test classes | 3 |
| Suggested MVP scope | Phase 2 + Phase 3 (15 tasks) |
