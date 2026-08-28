# Data Model: Prompt Queue — Navigation-Resilient RP Submissions

**Phase**: 1  
**Date**: 2026-05-29  
**Feature Branch**: `027-prompt-queue-continue`

---

## New Types

### `RolePlaySubmissionStatus` (enum)
**Location**: `DreamGenClone.Web/Domain/RolePlay/RolePlaySubmissionStatus.cs`

| Value | Meaning |
|-------|---------|
| `Running` | Engine call is in progress |
| `Completed` | Engine call succeeded; response persisted to DB |
| `Failed` | Engine call threw an exception; entry retained until user acknowledges |

---

### `RolePlayRunningSubmission` (record/class)
**Location**: `DreamGenClone.Web/Domain/RolePlay/RolePlayRunningSubmission.cs`  
**Scope**: Internal to tracker; not persisted

| Field | Type | Notes |
|-------|------|-------|
| `SessionId` | `string` | Key — matches the tracker dictionary key |
| `Payload` | `UnifiedPromptSubmission` | Original submission; retained for pre-filled re-submit on failure |
| `Status` | `RolePlaySubmissionStatus` | Mutable; transitions: `Running` → `Completed` or `Running` → `Failed` |
| `FailureMessage` | `string?` | Populated on failure; null on success |
| `StartedUtc` | `DateTimeOffset` | When the submission was registered |
| `ChunkCallbackWrapper` | `RolePlayChunkCallbackWrapper` | Tracker-owned wrapper; inner callback swappable at runtime |

**State Transitions**:
```
[Not Present]
    │ TryBeginSubmission
    ▼
  Running
    │                   │
    │ Task.Completed    │ Task.Faulted / Exception
    ▼                   ▼
 Completed            Failed
    │                   │
    │ (auto-removed)    │ AcknowledgeFailure (explicit user action)
    ▼                   ▼
[Not Present]       [Not Present]
```

**Invariants**:
- Only one `RolePlayRunningSubmission` may exist per `SessionId` at any time.
- A `Running` entry blocks further `TryBeginSubmission` calls for the same session.
- A `Failed` entry also blocks further submissions until explicitly acknowledged (prevents accidental re-submission race).
- `Completed` entries are removed immediately; they do not linger.

---

### `RolePlayChunkCallbackWrapper` (class)
**Location**: `DreamGenClone.Web/Domain/RolePlay/RolePlayRunningSubmission.cs` (nested or same file)  
**Scope**: Internal to `RolePlayRunningSubmission`

| Field | Type | Notes |
|-------|------|-------|
| `_inner` | `volatile Func<string, Task>?` | Current active chunk consumer; null when no component is attached |

**Methods**:
- `async Task InvokeAsync(string chunk)` — reads `_inner` atomically; if non-null, invokes it; catches `ObjectDisposedException` and silently detaches (sets `_inner = null`).
- `void Attach(Func<string, Task>? callback)` — atomic write to `_inner`.
- `void Detach()` — sets `_inner = null`.

---

### `IRolePlaySubmissionTracker` (interface)
**Location**: `DreamGenClone.Web/Application/RolePlay/IRolePlaySubmissionTracker.cs`  
**DI Lifetime**: Singleton

| Member | Type | Contract |
|--------|------|---------|
| `TryBeginSubmission(sessionId, submission, engineTask)` | `bool` | Registers entry and begins monitoring the task; returns `false` if session already has an active entry (Running or Failed) |
| `GetEntry(sessionId)` | `RolePlayRunningSubmission?` | Returns current entry or null if none |
| `AttachChunkCallback(sessionId, callback)` | `void` | Swaps inner callback in wrapper; no-op if no active entry |
| `DetachChunkCallback(sessionId)` | `void` | Sets inner callback to null; no-op if no active entry |
| `AcknowledgeFailure(sessionId)` | `void` | Removes a Failed entry; no-op if entry is Running or absent |
| `OnJobStatusChanged` | `event Action<string>?` | Fired with `sessionId` whenever status changes (Running→Completed, Running→Failed) |

---

### `RolePlaySubmissionTracker` (class)
**Location**: `DreamGenClone.Web/Application/RolePlay/RolePlaySubmissionTracker.cs`  
**DI Lifetime**: Singleton

**Internal state**:
- `ConcurrentDictionary<string, RolePlayRunningSubmission> _entries`

**Key behaviour**:
- `TryBeginSubmission`: `_entries.TryAdd(sessionId, entry)` — atomic; returns false on collision. Wires a `ContinueWith` on the engine task that transitions status, fires `OnJobStatusChanged`, and removes the entry on success.
- Completion continuation uses `TaskContinuationOptions.ExecuteSynchronously` to avoid an extra thread-pool hop; status is set before event is fired.

---

## Modified Types

### `RolePlayEngineService` (existing)
**Location**: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`  
**Change scope**: Narrow — chunk callback invocation sites only

- Anywhere `onChunk` is invoked, wrap the call in try/catch to swallow `ObjectDisposedException` (and any `InvalidOperationException` from a disposed JS interop callback). The engine must not re-throw these.
- No signature change required — `Func<string, Task>? onChunk` is already nullable and already null-checked before invocation.
- **Impact**: 1–3 call sites (wherever `onChunk?.Invoke(chunk)` or `await onChunk(chunk)` appears in the method).

### `RolePlayWorkspace.razor` (existing)
**Location**: `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`  
**Change scope**: Submission method, `OnInitializedAsync`, `DisposeAsync`, new UI state fields

**New state fields**:
- `_resubmitPending` — `bool`; true when tracker has a Failed entry for this session
- `_backgroundSubmissionRunning` — `bool`; true when tracker has a Running entry on return
- `_resubmitPayload` — `UnifiedPromptSubmission?`; cached from tracker's failed entry

**Submission method changes**:
1. Build `submission` as before.
2. Build chunk callback as before (`OnRolePlayResponseChunkAsync` or null).
3. Fire engine call **without awaiting**: `var engineTask = RolePlayEngine.SubmitPromptAsync(submission, null);` — chunk callback is NOT passed here; it is attached via tracker after registration.
4. Call `_tracker.TryBeginSubmission(sessionId, submission, engineTask)` — if returns false, show "response already in progress" indicator and return.
5. Attach chunk callback: `_tracker.AttachChunkCallback(sessionId, awaitsModel ? OnRolePlayResponseChunkAsync : null)`.
6. Subscribe to `_tracker.OnJobStatusChanged` (if not already subscribed).
7. Start `PeriodicTimer` fallback.
8. Clear `_promptText`; component does NOT await engine completion.

**`OnInitializedAsync` additions** (after session is loaded):
- Call `_tracker.GetEntry(SessionId)`.
- If `Running`: set `_backgroundSubmissionRunning = true`; attach chunk callback; subscribe event; start timer.
- If `Failed`: set `_resubmitPending = true`; set `_resubmitPayload = entry.Payload`; show inline error.

**`DisposeAsync` additions**:
- `_tracker.DetachChunkCallback(SessionId)` — detaches callback; does NOT cancel engine task.
- Unsubscribe from `_tracker.OnJobStatusChanged`.
- Stop/dispose `PeriodicTimer`.

---

## No New Persistence

All tracker state is in-memory. No new tables, migrations, or SQLite changes. The exception is documented in FR-015 with justification (same-process scope, acceptable restart loss).

---

## File Touchpoints Summary

| File | Change Type | Reason |
|------|-------------|--------|
| `DreamGenClone.Web/Domain/RolePlay/RolePlaySubmissionStatus.cs` | **New** | Status enum |
| `DreamGenClone.Web/Domain/RolePlay/RolePlayRunningSubmission.cs` | **New** | In-memory entry + callback wrapper |
| `DreamGenClone.Web/Application/RolePlay/IRolePlaySubmissionTracker.cs` | **New** | Singleton interface |
| `DreamGenClone.Web/Application/RolePlay/RolePlaySubmissionTracker.cs` | **New** | Singleton implementation |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | **Modify** | Swallow ObjectDisposedException in chunk callback invocation |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` | **Modify** | Submission, init, dispose, new UI state |
| `DreamGenClone.Web/Program.cs` | **Modify** | Singleton DI registration |
| `DreamGenClone.Tests/RolePlay/RolePlaySubmissionTrackerTests.cs` | **New** | Unit tests for tracker state machine |
