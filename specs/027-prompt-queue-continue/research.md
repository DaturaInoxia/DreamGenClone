# Research: Prompt Queue — Navigation-Resilient RP Submissions

**Phase**: 0 — Unknowns resolved before Phase 1 design  
**Date**: 2026-05-29  
**Feature Branch**: `027-prompt-queue-continue`

---

## Decision 1: Does `SubmitPromptAsync` actually complete when the component navigates away?

**Question**: Is the in-flight LLM call currently cancelled when `RolePlayWorkspace` is disposed?

**Finding**: No. `RolePlayWorkspace.SubmitPromptWithContinuationAsync` calls:
```
await RolePlayEngine.SubmitPromptAsync(submission, onChunk);
```
No `CancellationToken` is passed — it uses `default`. `DisposeAsync` only cancels `_finishOptionsGenerationCts` (finish-option generation), not the submission. The engine Task continues running on the thread pool until completion. DB persistence via `_stateRepository.CompleteTurnAsync()` fires regardless of component state.

**Decision**: The core LLM call and DB write already survive navigation. The actual bugs are:
1. `OnRolePlayResponseChunkAsync` (the streaming callback) tries to update component state and call `StateHasChanged` on a disposed component → `ObjectDisposedException`.
2. `LoadSessionAsync()` after engine completion is called on a disposed component → silent failure or exception.
3. No mechanism tells a returning component "a response is already in progress / has already landed".

**Rationale**: This scopes the fix precisely: the engine needs defensive null/disposed callback handling; the tracker is needed only for returning-component awareness and duplicate-submit prevention — not to make the engine call survive (it already does).

**Alternatives considered**: Wrapping the engine call in a new DI scope via `IServiceScopeFactory`. Rejected because the circuit-scoped `RolePlayEngineService` instance stays alive for same-circuit SPA navigation. Creating a new scope would bypass session cache and require an extra DB round-trip for no benefit.

---

## Decision 2: Thread-safe tracker event pattern for Blazor Server components

**Question**: How should a singleton tracker raise a status-change event to a Blazor Server component running on the circuit's synchronisation context?

**Finding**: Blazor Server components require `StateHasChanged` to be invoked via `InvokeAsync` to marshal the call onto the circuit's synchronisation context. The pattern used elsewhere in this codebase is:
```csharp
await InvokeAsync(StateHasChanged);
```
The singleton tracker fires `event Action<string>? OnJobStatusChanged` (the sessionId as arg) from a background continuation Task. The listening component's handler must call `InvokeAsync(StateHasChanged)` rather than `StateHasChanged()` directly.

**Decision**: `OnJobStatusChanged` is a plain `event Action<string>`. Component subscribes in `OnInitializedAsync` with a lambda that calls `InvokeAsync(StateHasChanged)`. Component unsubscribes in `DisposeAsync`.

**Rationale**: No separate push technology needed. The Blazor Server circuit already has a real-time connection. The event marshalling via `InvokeAsync` is the established pattern in this codebase.

**Alternatives considered**: `PeriodicTimer` alone (no event). Rejected as primary mechanism because the event gives immediate UI update. `PeriodicTimer` is retained as fallback only (FR-009a), not as primary.

---

## Decision 3: Tracker Task ownership — component fires, tracker monitors

**Question**: Should the tracker itself call `SubmitPromptAsync` (via a new DI scope) or should the component fire the call and hand the resulting Task to the tracker?

**Finding**: The component has direct access to the circuit-scoped `IRolePlayEngineService`. The engine instance already holds in-memory session cache. For same-process SPA navigation, the circuit and its scoped services stay alive.

**Decision**: Component fires `RolePlayEngine.SubmitPromptAsync(submission, trackerChunkWrapper.OnChunk)` as an un-awaited `Task` and immediately hands that Task to the tracker via `TryBeginSubmission`. The tracker wires a completion continuation that transitions status and fires the event.

**Rationale**: Simplest ownership model. No extra scope creation. No risk of scope/service lifetime mismatch. The engine's existing session cache and repositories are reused.

**Alternatives considered**: Tracker creates its own `IServiceScopeFactory` scope, resolves engine, calls it internally (the `GenericBackgroundJobWorker` pattern). Rejected for this feature because prompt submission is user-context-sensitive (identity options, session cache) and benefits from the live circuit scope.

---

## Decision 4: Chunk callback thread-safety — tracker-owned wrapper

**Question**: How can the component attach/detach its streaming callback without restarting or disrupting the in-flight engine call?

**Finding**: `SubmitPromptAsync` accepts `Func<string, Task>? onChunk` once at call time. After the call is in flight, the callback cannot be swapped in the engine. To allow the component to detach (on navigation) and reattach (on return), the tracker must pass its own wrapper delegate to the engine.

**Decision**: `RolePlayRunningSubmission` owns a `ChunkCallbackWrapper` that holds a `volatile Func<string, Task>?` field. The wrapper's own `Func<string, Task>` is what is passed to the engine at call time. The wrapper delegates to whatever the current inner callback is (atomically read). The component attaches/detaches its callback via `AttachChunkCallback` / `DetachChunkCallback`, which do atomic writes to the wrapper field.

```
Engine calls:    wrapper.InvokeAsync(chunk)
Wrapper does:    cb = Volatile.Read(ref _inner); if cb != null → try await cb(chunk) catch ObjectDisposedException → detach
```

**Rationale**: This decouples the engine's held delegate reference from the component's lifecycle. The component swaps the inner field without touching the engine call.

**Alternatives considered**: Passing `null` to engine and relying only on the in-memory tracker for post-completion notification. Rejected because streaming while the component IS active is a key existing UX feature; the callback wrapper preserves streaming when the component is present.

---

## Decision 5: PeriodicTimer fallback interval

**Question**: What poll interval should the `PeriodicTimer` fallback use?

**Finding**: The fallback exists only to recover from missed events (rare case). A 2-second interval is fast enough to feel responsive without measurable overhead. The timer is only active when a submission is in-flight, so cost is negligible.

**Decision**: 2-second interval. Configurable via a constant `SubmissionStatusPollInterval = TimeSpan.FromSeconds(2)` on `RolePlayWorkspace`. No settings entry needed (behaviour not user-facing).

**Rationale**: 2 s gives at most a 2-second lag on status recovery after a missed event. Balances responsiveness with minimal polling cost.

---

## Decision 6: Re-submit confirmation UX approach

**Question**: What is the minimal viable re-submit flow that satisfies FR-011a (pre-fill + confirmation step)?

**Finding**: The workspace already has a prompt input field (`_promptText`) and a send button with `CanSendPrompt` guard. The re-submit affordance can:
1. Set `_promptText` to the retained payload's `PromptText`.
2. Set a `_resubmitPending = true` flag that shows a confirmation banner above the send button.
3. User either clicks Confirm (fires submission) or clicks Dismiss (clears the failed entry).

**Decision**: Inline banner above the prompt input when `_resubmitPending` is true: "Last response failed. Prompt has been restored — review and re-send, or dismiss." Two buttons: [Re-send] (pre-fills prompt, calls submit) and [Dismiss] (calls `tracker.AcknowledgeFailure`, clears banner). The existing submit button is hidden while this banner is visible to prevent accidental double submission.

**Rationale**: Reuses existing prompt input and submit flow. Minimal new UI surface. Satisfies both the pre-fill and confirmation requirements.

---

## Summary of Resolved Unknowns

| Unknown | Resolution |
|---------|-----------|
| Does engine survive navigation? | Yes — no CTS tied to component; Task continues on thread pool |
| Root cause of current failure | ObjectDisposedException from chunk callback + silent post-completion failures in disposed component |
| Tracker task ownership | Component fires engine call; hands Task to tracker for monitoring |
| Chunk callback swap mechanism | Tracker-owned wrapper delegate with volatile inner field |
| Event-to-component marshalling | `event Action<string>` + `InvokeAsync(StateHasChanged)` in subscriber |
| PeriodicTimer interval | 2 seconds; active only while submission in-flight |
| Re-submit UX | Inline confirmation banner; pre-filled prompt; Confirm/Dismiss buttons |
