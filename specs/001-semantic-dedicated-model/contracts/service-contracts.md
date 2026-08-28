# Service Contracts: Semantic Analysis — Dedicated Model & Concurrent Processing

**Branch**: `001-semantic-dedicated-model`  
**Date**: 2026-05-28

This feature has no external-facing HTTP endpoints or public API surface. All contracts are internal interface contracts between services within the application.

---

## ISemanticBackgroundJobQueue

New interface in `DreamGenClone.Web/Application/BackgroundJobs/ISemanticBackgroundJobQueue.cs`

Mirrors `IBackgroundJobQueue` but is bound exclusively to semantic analysis jobs.

```csharp
public interface ISemanticBackgroundJobQueue
{
    void EnqueueJob(BackgroundJobEnvelope job);
    Task<BackgroundJobEnvelope> DequeueAsync(CancellationToken cancellationToken);
    void MarkProcessing(string jobId);
    void MarkCompleted(string jobId);
    void MarkFailed(string jobId);
    int GetPendingCount();
}
```

**Invariants**:
- Only `BackgroundJobTypes.SemanticInteractionAnalysis` jobs must be enqueued here.
- `DequeueAsync` blocks until a job is available or cancellation is requested.
- The generic `IBackgroundJobQueue` must never receive a `SemanticInteractionAnalysis` job after this feature is active.

---

## Updated IFunctionDefaultRepository

No new methods. Existing `SaveAsync` / `GetByFunctionAsync` / `GetAllAsync` signatures are unchanged. The only change is that the returned `FunctionModelDefault` now carries the new `MaxConcurrentJobs int?` property.

**GetByFunctionAsync result for RolePlaySemanticAnalysis (example)**:

```json
{
  "id": "a1b2c3d4-...",
  "functionName": "RolePlaySemanticAnalysis",
  "modelId": "mistral-7b-instruct",
  "temperature": 0.3,
  "topP": 0.9,
  "maxTokens": 512,
  "maxConcurrentJobs": 3,
  "updatedUtc": "2026-05-28T12:00:00Z"
}
```

**GetByFunctionAsync result — no model assigned (example)**:

```json
{
  "id": "a1b2c3d4-...",
  "functionName": "RolePlaySemanticAnalysis",
  "modelId": "",
  "temperature": 0.7,
  "topP": 0.9,
  "maxTokens": 500,
  "maxConcurrentJobs": null,
  "updatedUtc": "2026-05-28T12:00:00Z"
}
```

In the "no model assigned" case, `ModelResolutionService.ResolveAsync` throws `ModelResolutionException`. `SemanticEventInferenceService` catches this and returns a failure result.

---

## SemanticEventInferenceService — Updated Behaviour Contract

**Method**: `InferAsync(sessionId, interactionId, ...)`

| Scenario | Outcome |
|---|---|
| `RolePlaySemanticAnalysis` model assigned, valid | LLM call proceeds normally; events returned |
| `RolePlaySemanticAnalysis` model not assigned | `ModelResolutionException` caught → return `SemanticEventInferenceResult` with `Success = false`, `ErrorMessage` populated, zero events |
| `RolePlaySemanticAnalysis` model assigned but disabled/deleted | `ModelResolutionException` caught (same path) → failure result |
| LLM call fails (timeout, network error) | Exception propagates to `SemanticInteractionAnalysisJobHandler` → worker catches → `Error` state set |

**No fallback to `AppFunction.RolePlayGeneration` in any case.**

---

## SemanticBackgroundJobWorker — Startup Contract

At startup (`ExecuteAsync`), the worker:

1. Calls `IFunctionDefaultRepository.GetByFunctionAsync(AppFunction.RolePlaySemanticAnalysis)`.
2. Reads `MaxConcurrentJobs`. If null → use `2`.
3. Clamps to range `[1, 16]`.
4. Creates `SemaphoreSlim(maxConcurrent, maxConcurrent)`.
5. Begins processing loop: dequeue jobs, fan out up to `maxConcurrent` in parallel.

**Concurrency ceiling does not change after startup.** Changes to `MaxConcurrentJobs` via the UI take effect on the next application restart.
