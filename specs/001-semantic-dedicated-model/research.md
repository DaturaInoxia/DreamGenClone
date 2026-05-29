# Research: Semantic Analysis — Dedicated Model & Concurrent Processing

**Phase**: 0 — Pre-design investigation  
**Branch**: `001-semantic-dedicated-model`  
**Date**: 2026-05-28

---

## Investigation 1: Database Schema Migration Strategy

**Question**: How are SQLite schema changes applied in this project? Are EF Core migrations used?

**Findings**:
- No EF Core. All schema is managed via `CREATE TABLE IF NOT EXISTS` raw SQL in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` (`InitializeAsync()`).
- Legacy migrations tracked via `AppMetadata` table key `LegacyMigrationsVersion` (current: `"2026-05-01-1"`).
- `ShouldRunLegacyMigrationsAsync()` / `MarkLegacyMigrationsCompleteAsync()` methods gate one-time ALTER TABLE runs.

**Decision**: Add `MaxConcurrentJobs INTEGER NULL` to the `FunctionModelDefaults` table via two changes:
1. Update the `CREATE TABLE IF NOT EXISTS FunctionModelDefaults (...)` statement in `SqlitePersistence.cs` for fresh installs.
2. Add `ALTER TABLE FunctionModelDefaults ADD COLUMN MaxConcurrentJobs INTEGER NULL` under the existing legacy migration gate for existing databases.

**Rationale**: Consistent with the pattern used by every other schema change in this codebase. No EF Core dependency needed.

**Alternatives Considered**: EF Core migrations — rejected because the project intentionally uses direct ADO.NET with no ORM.

---

## Investigation 2: ModelResolutionService Behaviour on Missing Function Default

**Question**: What does `ModelResolutionService.ResolveAsync` do when no `FunctionModelDefault` row exists for the requested `AppFunction`?

**Findings**:
- `IFunctionDefaultRepository.GetByFunctionAsync()` returns `null` when no row exists for the function.
- `ModelResolutionService` throws `ModelResolutionException` with the message: _"No model configured for function '{function}'. Configure a default model in Model Manager (/model-manager)."_
- No silent fallback, no partial return. Exception is thrown immediately.

**Decision**: `SemanticEventInferenceService` wraps its `ResolveAsync` call in a try/catch for `ModelResolutionException`. On catch:
1. Log the exception as a Warning with structured context (SessionId, InteractionId, AppFunction).
2. Return a failure result (no events inferred) to the caller (`SemanticInteractionAnalysisJobHandler`).
3. The job handler sets the analysis state to `Error` via `UpsertAsync`.
4. Zero semantic deltas are applied. No fallback to `RolePlayGeneration`.

**Rationale**: `ModelResolutionException` is already the fail-fast signal from the model manager. Catching it at the inference service boundary lets the rest of the semantic pipeline shut down cleanly without cascading exceptions into the worker.

**Alternatives Considered**: Letting the exception propagate to the worker — rejected because the worker already has a generic exception handler; catching at the service boundary gives a more precise diagnostic log.

---

## Investigation 3: SemanticAnalysisStatus.Error — Already Exists

**Question**: Does the `Error` state already exist in `SemanticAnalysisStatus`, or does it need to be added?

**Findings**:
- `SemanticAnalysisStatus` enum (in `DreamGenClone.Application/RolePlay/SemanticAnalysisStatus.cs`) already has:
  ```
  Idle = 0, Analyzing = 1, Complete = 2, Error = 3
  ```
- `RolePlaySemanticInteractionAnalysisState` table has `ErrorMessage TEXT NULL` column.
- `UpsertAsync(SemanticInteractionAnalysisState state)` accepts any status including `Error`.

**Decision**: No new enum values or schema changes needed for error state. The worker calls `UpsertAsync` with `Status = Error` and an `ErrorMessage` populated from the exception message.

**Rationale**: Error state is already designed and supported.

---

## Investigation 4: FunctionModelDefault — Current Schema and Extension Point

**Question**: What fields does `FunctionModelDefault` currently have, and is it straightforward to add `MaxConcurrentJobs`?

**Findings — current fields**:
- `Id` (TEXT, GUID)
- `FunctionName` (TEXT, maps to AppFunction enum name)
- `ModelId` (TEXT)
- `Temperature` (REAL)
- `TopP` (REAL)
- `MaxTokens` (INTEGER)
- `UpdatedUtc` (TEXT)

**No nullable int field exists**. `MaxConcurrentJobs` will be the first nullable field; `ModelId` is currently non-nullable (empty string when unset). This is an incidental inconsistency in the existing schema but does not affect this feature.

**Decision**: Add `int? MaxConcurrentJobs` to the `FunctionModelDefault` C# class and `MaxConcurrentJobs INTEGER NULL` to the SQLite table. The worker reads this field at startup via `IFunctionDefaultRepository.GetByFunctionAsync(AppFunction.RolePlaySemanticAnalysis)?.MaxConcurrentJobs ?? 2`.

**Rationale**: Minimal surface change. Existing repositories use typed field mapping; adding one nullable int field is straightforward and consistent.

---

## Investigation 5: Existing Concurrency Patterns to Reuse

**Question**: What concurrency patterns are already proven in the codebase?

**Findings**:
- `HealthCheckService`: `SemaphoreSlim(3) + Select(async ...) + Task.WhenAll(tasks)` — parallel bounded fan-out.
- `ClimaxBeatRepository`: `SemaphoreSlim(1,1)` — mutual exclusion for single resource.
- `RolePlayEngineService.Sessions`: `static ConcurrentDictionary<string, RolePlaySession>` — concurrent session map.
- `GenericBackgroundJobQueue`: `Channel<BackgroundJobEnvelope>` (unbounded) + `ConcurrentDictionary` for dedup/tracking.
- `GenericBackgroundJobWorker`: `await _queue.DequeueAsync()` loop — sequential, one job at a time.

**Decision**: `SemanticBackgroundJobWorker` uses the `HealthCheckService` pattern: `SemaphoreSlim(n) + Select async + Task.WhenAll` — the most natural fit for bounded parallel job dispatch from a `Channel<T>`.

**Rationale**: Already proven in this codebase. Avoids introducing new concurrency primitives.

---

## Summary of Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | `ALTER TABLE ... ADD COLUMN` in legacy migration gate | Consistent with all existing schema changes |
| 2 | Catch `ModelResolutionException` in `SemanticEventInferenceService` → return failure result | Precise diagnostic, no cascading exception, no fallback model |
| 3 | `SemanticAnalysisStatus.Error` already exists — no changes needed | Already designed |
| 4 | `int? MaxConcurrentJobs` on `FunctionModelDefault` | Minimal field addition, nullable, consistent |
| 5 | `SemaphoreSlim + Task.WhenAll` in new `SemanticBackgroundJobWorker` | Proven pattern in same codebase (HealthCheckService) |
