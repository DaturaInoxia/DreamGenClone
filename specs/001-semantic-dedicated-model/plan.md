# Implementation Plan: Semantic Analysis — Dedicated Model & Concurrent Processing

**Branch**: `001-semantic-dedicated-model` | **Date**: 2026-05-28 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/001-semantic-dedicated-model/spec.md`

## Summary

Add a dedicated `AppFunction.RolePlaySemanticAnalysis` model slot so background RP semantic analysis can be assigned its own independent model, separate from `RolePlayGeneration`. Introduce a `SemanticBackgroundJobWorker` (dedicated `BackgroundService`) that reads from an isolated `ISemanticBackgroundJobQueue` and processes up to `MaxConcurrentJobs` (default 2, max 16) semantic analysis jobs in parallel using a `SemaphoreSlim + Task.WhenAll` pattern. Wire up the new slot and concurrency setting in the Model Manager Function Defaults UI. When no model is assigned to the semantic slot, the inference service catches `ModelResolutionException`, records `Error` state, and applies zero deltas — no silent fallback to `RolePlayGeneration`.

## Technical Context

**Language/Version**: C# / .NET 9 / Blazor Server  
**Primary Dependencies**: System.Threading.Channels (existing), SemaphoreSlim (BCL), Serilog, Microsoft.Data.Sqlite (existing ADO.NET, no EF Core)  
**Storage**: SQLite via `FunctionModelDefaults` table — one `ALTER TABLE ... ADD COLUMN MaxConcurrentJobs INTEGER NULL` migration  
**Testing**: xUnit + Moq (existing patterns in `DreamGenClone.Tests/`)  
**Target Platform**: Windows local desktop (single-user, local-first)  
**Project Type**: Blazor Server web application (modular layered architecture)  
**Performance Goals**: Up to 16 concurrent semantic LLM calls; throughput is hardware-bound, no latency SLA  
**Constraints**: `MaxConcurrentJobs` change takes effect on app restart only; no dynamic semaphore resize; story analysis pipeline (StorySummarize/StoryAnalyze/StoryRank) is untouched  
**Scale/Scope**: Single user; typically 1–4 concurrent RP sessions; semantic analysis is one LLM call per interaction

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved — new worker is in-process `BackgroundService`; all LLM calls go to local provider
- [x] Module boundaries and adapter seams are explicit — `ISemanticBackgroundJobQueue` is a new interface; `SemanticBackgroundJobWorker` depends only on the queue interface and `IFunctionDefaultRepository`
- [x] .NET layered architecture respected — Domain (`AppFunction`, `FunctionModelDefault`), Application (`IFunctionDefaultRepository` — no interface change needed), Infrastructure (`FunctionDefaultRepository`, `SqlitePersistence`), Web (`SemanticBackgroundJobQueue`, `SemanticBackgroundJobWorker`, `SemanticEventInferenceService`, `ModelManager.razor`, `Program.cs`)
- [x] Deterministic state transitions — `SemanticAnalysisStatus` state machine is unchanged; worker processes jobs deterministically; semaphore ensures ordered slot acquisition
- [x] Persistence uses SQLite — `MaxConcurrentJobs` persisted in `FunctionModelDefaults` via existing ADO.NET pattern
- [x] Serilog structured logging — FR-013/FR-014 explicitly require lifecycle events with structured context
- [x] Information-level logs for major call paths — worker start/stop, job enqueue/start/complete/fail, concurrency gate
- [x] Log levels externally configurable — existing `appsettings.json` logging configuration covers this

**Post-design re-check**: All gates still pass. No constitution violations.

## Project Structure

### Documentation (this feature)

```text
specs/001-semantic-dedicated-model/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── service-contracts.md  # Phase 1 output
└── tasks.md             # Phase 2 output (created by /speckit.tasks — NOT this command)
```

### Source Code Changes

```text
DreamGenClone.Domain/
└── ModelManager/
    ├── AppFunction.cs              → add RolePlaySemanticAnalysis enum value
    └── FunctionModelDefault.cs     → add int? MaxConcurrentJobs property

DreamGenClone.Infrastructure/
├── ModelManager/
│   └── FunctionDefaultRepository.cs  → read/write MaxConcurrentJobs in SaveAsync + GetByFunctionAsync
└── Persistence/
    └── SqlitePersistence.cs          → update CREATE TABLE + add ALTER TABLE migration

DreamGenClone.Web/
├── Application/BackgroundJobs/
│   ├── ISemanticBackgroundJobQueue.cs  ← NEW interface
│   ├── SemanticBackgroundJobQueue.cs   ← NEW implementation (Channel<T> + ConcurrentDictionary dedup)
│   └── SemanticBackgroundJobWorker.cs  ← NEW BackgroundService (SemaphoreSlim + Task.WhenAll)
├── Application/RolePlay/
│   ├── SemanticEventInferenceService.cs  → change AppFunction.RolePlayGeneration → RolePlaySemanticAnalysis;
│   │                                       catch ModelResolutionException → return failure result
│   └── RolePlayEngineService.cs           → enqueue to ISemanticBackgroundJobQueue (not IBackgroundJobQueue)
├── Application/ModelManager/
│   └── ModelManagerFacade.cs             → pass MaxConcurrentJobs in SaveFunctionDefaultAsync
├── Components/Pages/
│   └── ModelManager.razor                → add Max Parallel field; inline hint; warning badge
└── Program.cs                            → register ISemanticBackgroundJobQueue + SemanticBackgroundJobWorker

DreamGenClone.Tests/
└── RolePlay/ (new test classes)
    ├── SemanticEventInferenceServiceTests.cs  ← NEW (AppFunction assertion + ModelResolutionException handling)
    ├── SemanticBackgroundJobWorkerTests.cs    ← NEW (semaphore cap enforcement)
    └── FunctionDefaultRepositoryTests.cs      → add MaxConcurrentJobs roundtrip test
```

**Structure Decision**: No new projects. All changes fit within the existing 5-project layered architecture. `ISemanticBackgroundJobQueue` and `SemanticBackgroundJobWorker` live in `DreamGenClone.Web/Application/BackgroundJobs/` alongside their generic counterparts.

## Complexity Tracking

No constitution violations. No complexity justifications needed.

## Implementation Sequence

Phases ordered to minimize blocked work. Domain changes first (no dependencies), then Infrastructure, then Web Application layer, then UI and DI wiring, then tests.

### Phase 1 — Domain: AppFunction + FunctionModelDefault

**Files**: `DreamGenClone.Domain/ModelManager/AppFunction.cs`, `FunctionModelDefault.cs`

1. Add `RolePlaySemanticAnalysis` to `AppFunction` enum (append at end).
2. Add `public int? MaxConcurrentJobs { get; set; }` to `FunctionModelDefault`.

**Verify**: Build `DreamGenClone.Domain` — 0 errors.

---

### Phase 2 — Infrastructure: Repository + Schema Migration

**Files**: `FunctionDefaultRepository.cs`, `SqlitePersistence.cs`

3. Update `FunctionDefaultRepository.GetByFunctionAsync` to map `MaxConcurrentJobs` from the result set.
4. Update `FunctionDefaultRepository.SaveAsync` to write `MaxConcurrentJobs` in the INSERT/UPDATE statement.
5. Update `SqlitePersistence.cs`:
   - Add `MaxConcurrentJobs INTEGER NULL` to the `CREATE TABLE IF NOT EXISTS FunctionModelDefaults` statement (fresh installs).
   - Add `ALTER TABLE FunctionModelDefaults ADD COLUMN MaxConcurrentJobs INTEGER NULL` under the legacy migration gate (existing databases).

**Verify**: Build `DreamGenClone.Infrastructure` — 0 errors.

---

### Phase 3 — Web Application: Queue + Worker

**Files**: New `ISemanticBackgroundJobQueue.cs`, `SemanticBackgroundJobQueue.cs`, `SemanticBackgroundJobWorker.cs`

6. Create `ISemanticBackgroundJobQueue` — mirrors `IBackgroundJobQueue` (same method signatures).
7. Create `SemanticBackgroundJobQueue` — `Channel<BackgroundJobEnvelope>` + `ConcurrentDictionary` dedup (copy pattern from `GenericBackgroundJobQueue`).
8. Create `SemanticBackgroundJobWorker : BackgroundService`:
   - On `ExecuteAsync`: read `MaxConcurrentJobs` from `IFunctionDefaultRepository.GetByFunctionAsync(AppFunction.RolePlaySemanticAnalysis)`; clamp to `[1, 16]`; default `2` if null.
   - Log worker start with resolved concurrency value.
   - Processing loop: dequeue batch → acquire semaphore slots → dispatch jobs via `Task.WhenAll` → release on complete/error.
   - On unhandled job exception: log Warning with SessionId/InteractionId/exception type → call handler's error path → release semaphore → continue loop.

**Verify**: Build `DreamGenClone.Web` — 0 errors.

---

### Phase 4 — Web Application: SemanticEventInferenceService + RolePlayEngineService

**Files**: `SemanticEventInferenceService.cs`, `RolePlayEngineService.cs`

9. In `SemanticEventInferenceService.InferAsync`:
   - Change `ResolveAsync(AppFunction.RolePlayGeneration)` → `ResolveAsync(AppFunction.RolePlaySemanticAnalysis)`.
   - Wrap the `ResolveAsync` call in `try/catch (ModelResolutionException ex)`.
   - On catch: log Warning with structured context (SessionId, InteractionId, function name, message) → return `SemanticEventInferenceResult { Success = false, ErrorMessage = ex.Message, Events = [] }`.
10. In `RolePlayEngineService.QueueSemanticInteractionAnalysisAsync`:
    - Change injection dependency from `IBackgroundJobQueue` to `ISemanticBackgroundJobQueue`.
    - Enqueue to the semantic queue instead of the generic queue.

**Verify**: Build `DreamGenClone.Web` — 0 errors; grep confirms no remaining `AppFunction.RolePlayGeneration` call in `SemanticEventInferenceService`.

---

### Phase 5 — Web Application: ModelManagerFacade + DI + Program.cs

**Files**: `ModelManagerFacade.cs`, `Program.cs`

11. Update `ModelManagerFacade.SaveFunctionDefaultAsync` to include `MaxConcurrentJobs` in the saved entity (passed from UI).
12. In `Program.cs`:
    - Register `ISemanticBackgroundJobQueue` as Singleton (`SemanticBackgroundJobQueue`).
    - Register `SemanticBackgroundJobWorker` as a hosted service (`AddHostedService<SemanticBackgroundJobWorker>()`).
    - Confirm `SemanticInteractionAnalysisJobHandler` is still registered with `IEnumerable<IBackgroundJobHandler>` for resolution by the new worker.

**Verify**: Build `DreamGenClone.Web` — 0 errors.

---

### Phase 6 — UI: ModelManager.razor

**File**: `DreamGenClone.Web/Components/Pages/ModelManager.razor`

13. In the Function Defaults table, add a **"Max Parallel"** column with a numeric input bound to `MaxConcurrentJobs` (nullable int, min=1, max=16).
14. Show inline static hint _"Takes effect on next restart."_ below the Max Parallel field once a value is saved (conditionally rendered after first save).
15. Show warning badge/icon with tooltip _"No model assigned — semantic analysis will fail until a model is selected."_ on the `RolePlaySemanticAnalysis` row when `ModelId` is empty or the assigned model is disabled/deleted.
16. Wire Save button to call `ModelManagerFacade.SaveFunctionDefaultAsync` with the `MaxConcurrentJobs` value.

**Display label** for `AppFunction.RolePlaySemanticAnalysis`: `"RP Semantic Analysis (Background)"`

**Verify**: App starts, Model Manager page loads, new row is visible with all controls.

---

### Phase 7 — Tests

17. **`SemanticEventInferenceServiceTests`**:
    - Assert `ResolveAsync` is called with `AppFunction.RolePlaySemanticAnalysis` (mock `IModelResolutionService`).
    - Assert that when mock throws `ModelResolutionException`, `InferAsync` returns `Success = false`, zero events, no exception propagated.
18. **`SemanticBackgroundJobWorkerTests`**:
    - Assert worker reads `MaxConcurrentJobs` at startup from the repository.
    - Assert no more than `MaxConcurrentJobs` concurrent tasks execute simultaneously (use `SemaphoreSlim` count tracking or task delay simulation).
    - Assert that a job exception causes the worker to log a Warning and continue to the next job (does not crash the loop).
19. **`FunctionDefaultRepositoryTests`**:
    - Assert `SaveAsync` persists `MaxConcurrentJobs` and `GetByFunctionAsync` returns the same value.
    - Assert `GetByFunctionAsync` returns `null` for `MaxConcurrentJobs` when column is absent (migration compatibility).

**Verify**: `dotnet test DreamGenClone.Tests -v minimal` — all new tests pass; all existing tests pass.

---

## Verification Checklist

- [ ] `AppFunction.RolePlaySemanticAnalysis` exists in enum
- [ ] `FunctionModelDefault.MaxConcurrentJobs` persists and roundtrips through repository
- [ ] `SemanticEventInferenceService` calls `ResolveAsync(AppFunction.RolePlaySemanticAnalysis)` — confirmed by grep and test
- [ ] No `AppFunction.RolePlayGeneration` call remains in `SemanticEventInferenceService`
- [ ] `SemanticInteractionAnalysis` jobs are enqueued to `ISemanticBackgroundJobQueue` — not `IBackgroundJobQueue`
- [ ] `GenericBackgroundJobWorker` receives no `SemanticInteractionAnalysis` jobs (confirmed by test or log inspection)
- [ ] Semaphore cap enforced — no more than `MaxConcurrentJobs` concurrent jobs (test)
- [ ] Missing model → `Error` state set + Warning log + zero deltas (test)
- [ ] Model Manager UI shows "RP Semantic Analysis (Background)" row with Max Parallel field, inline hint, warning badge
- [ ] `dotnet build DreamGenClone.sln -v minimal` — 0 errors, 0 warnings (new)
- [ ] `dotnet test DreamGenClone.Tests` — all pass


## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION]  
**Primary Dependencies**: [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION]  
**Storage**: [if applicable, default SQLite unless explicitly overridden in spec; e.g., SQLite, session storage, local storage, PostgreSQL]  
**Testing**: [e.g., pytest, XCTest, cargo test or NEEDS CLARIFICATION]  
**Target Platform**: [e.g., Linux server, iOS 15+, WASM or NEEDS CLARIFICATION]
**Project Type**: [e.g., library/cli/web-service/mobile-app/compiler/desktop-app or NEEDS CLARIFICATION]  
**Performance Goals**: [domain-specific, e.g., 1000 req/s, 10k lines/sec, 60 fps or NEEDS CLARIFICATION]  
**Constraints**: [domain-specific, e.g., <200ms p95, <100MB memory, offline-capable or NEEDS CLARIFICATION]  
**Scale/Scope**: [domain-specific, e.g., 10k users, 1M LOC, 50 screens or NEEDS CLARIFICATION]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [ ] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [ ] Module boundaries and adapter seams are explicit and swappable
- [ ] .NET layered architecture uses separate projects with enforced dependency direction
- [ ] Deterministic state transitions and JSON contract validation are test-covered
- [ ] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [ ] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [ ] Logging coverage exists across layers/components/services with Information logs for major call paths
- [ ] Log levels are externally configurable, including Verbose diagnostics without code changes

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
# [REMOVE IF UNUSED] Option 1: Single project (DEFAULT)
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# [REMOVE IF UNUSED] Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# [REMOVE IF UNUSED] Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
