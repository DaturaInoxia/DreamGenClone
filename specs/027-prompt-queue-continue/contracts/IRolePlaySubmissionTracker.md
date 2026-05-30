# Interface Contract: IRolePlaySubmissionTracker

**Location**: `DreamGenClone.Web/Application/RolePlay/IRolePlaySubmissionTracker.cs`  
**DI Lifetime**: Singleton  
**Date**: 2026-05-29

---

## Purpose

`IRolePlaySubmissionTracker` is the single source of truth for in-flight RP prompt submission state within the current server process. It decouples the LLM engine call lifecycle from the Blazor component lifecycle, enabling submissions to survive page navigation.

---

## Method Contracts

### `TryBeginSubmission`

```
bool TryBeginSubmission(string sessionId, UnifiedPromptSubmission submission, Task<RolePlayInteraction> engineTask)
```

**Pre-conditions**:
- `sessionId` is non-null and non-empty.
- `submission` is non-null and pre-validated.
- `engineTask` is a running (not yet completed) `Task<RolePlayInteraction>` returned by `RolePlayEngineService.SubmitPromptAsync`.

**Post-conditions**:
- If no entry existed for `sessionId`: entry is added with status `Running`; a completion continuation is wired on `engineTask`; returns `true`.
- If an entry already existed for `sessionId` (Running **or** Failed): no change; returns `false`.

**Side effects on completion of `engineTask`**:
- On success: status transitions to `Completed`; entry is removed from dictionary; `OnJobStatusChanged(sessionId)` is fired.
- On fault/cancel: status transitions to `Failed`; entry is retained (not removed); `OnJobStatusChanged(sessionId)` is fired.

---

### `GetEntry`

```
RolePlayRunningSubmission? GetEntry(string sessionId)
```

**Returns**: The current entry for `sessionId`, or `null` if none exists.  
**Thread safety**: Safe to call from any thread; returns a snapshot of the current entry reference.

---

### `AttachChunkCallback`

```
void AttachChunkCallback(string sessionId, Func<string, Task>? callback)
```

**Behaviour**: Atomically replaces the inner callback in the entry's `ChunkCallbackWrapper`. If no entry exists for `sessionId`, this is a no-op.  
**Use**: Called by the workspace component on initialization (return while running) or at submission time.

---

### `DetachChunkCallback`

```
void DetachChunkCallback(string sessionId)
```

**Behaviour**: Sets the inner callback to `null` in the entry's wrapper. No-op if no entry exists.  
**Use**: Called by the workspace component in `DisposeAsync` when navigating away.

---

### `AcknowledgeFailure`

```
void AcknowledgeFailure(string sessionId)
```

**Pre-conditions**: Entry for `sessionId` must be in `Failed` status.  
**Post-conditions**: Entry is removed from dictionary; session is now unblocked for new submissions.  
**No-op if**: Entry is absent or in `Running` status (running entries cannot be manually dismissed).

---

### `OnJobStatusChanged` (event)

```
event Action<string>? OnJobStatusChanged
```

**Fired**: When any entry's status changes (Running→Completed, Running→Failed).  
**Argument**: `sessionId` of the changed entry.  
**Threading**: Fired from the engine's completion continuation Task; listeners must marshal to the appropriate synchronisation context (e.g., `InvokeAsync(StateHasChanged)` in Blazor Server components).  
**Subscription**: Components subscribe in `OnInitializedAsync` and **must** unsubscribe in `DisposeAsync` to prevent memory leaks and ghost callbacks from disposed component instances.

---

## Invariants

1. At most one entry per `sessionId` at any time.
2. `TryBeginSubmission` is the only path that creates entries — no entry is ever created mid-lifecycle.
3. A `Failed` entry blocks new submissions until `AcknowledgeFailure` is called.
4. A `Running` entry's task is never cancelled by the tracker.
5. `ChunkCallbackWrapper.InvokeAsync` never throws — exceptions from the inner callback are caught and the callback is silently detached.
6. All mutations to the dictionary use `ConcurrentDictionary` atomic operations; no external locking is required by callers.

---

## Non-Contracts (explicitly excluded)

- The tracker does NOT call `SubmitPromptAsync` itself. The caller (component) is responsible for starting the engine task.
- The tracker does NOT cancel in-flight tasks, even after `AcknowledgeFailure`.
- The tracker does NOT persist entries across process restarts.
- The tracker does NOT queue multiple submissions; it manages exactly one entry per session at a time.
