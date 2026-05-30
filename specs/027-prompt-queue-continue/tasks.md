# Tasks: Prompt Queue — Navigation-Resilient RP Submissions

**Input**: Design documents from `specs/027-prompt-queue-continue/`  
**Prerequisites**: plan.md ✅ spec.md ✅ research.md ✅ data-model.md ✅ contracts/ ✅ quickstart.md ✅  
**Branch**: `027-prompt-queue-continue`

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on in-progress tasks)
- **[US1]** / **[US2]** / **[US3]**: User story phase label
- All file paths are absolute from repository root

---

## Phase 1: Setup

**Purpose**: No new projects or tooling required. Setup is limited to confirming the build is clean and that all files can be created in-situ.

- [X] T001 Verify solution builds cleanly before changes: `dotnet build DreamGenClone.sln -v minimal`

---

## Phase 2: Foundational — New Domain Types and Interface

**Purpose**: The `RolePlaySubmissionStatus` enum, `RolePlayRunningSubmission` (including `RolePlayChunkCallbackWrapper`), and `IRolePlaySubmissionTracker` interface are shared by all three user stories. Nothing in US1–US3 can be implemented without them.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Create `RolePlaySubmissionStatus` enum in `DreamGenClone.Web/Domain/RolePlay/RolePlaySubmissionStatus.cs` with values `Running`, `Completed`, `Failed`
- [X] T003 [P] Create `RolePlayChunkCallbackWrapper` class in `DreamGenClone.Web/Domain/RolePlay/RolePlayRunningSubmission.cs` with `volatile Func<string, Task>?` inner field; `Attach`, `Detach`, and `InvokeAsync` methods; `InvokeAsync` catches `ObjectDisposedException` and silently detaches without re-throwing
- [X] T004 Create `RolePlayRunningSubmission` class in `DreamGenClone.Web/Domain/RolePlay/RolePlayRunningSubmission.cs` with fields: `SessionId` (string), `Payload` (UnifiedPromptSubmission), `Status` (RolePlaySubmissionStatus — mutable), `FailureMessage` (string?), `StartedUtc` (DateTimeOffset), `ChunkCallbackWrapper` (RolePlayChunkCallbackWrapper); depends on T002, T003
- [X] T005 Create `IRolePlaySubmissionTracker` interface in `DreamGenClone.Web/Application/RolePlay/IRolePlaySubmissionTracker.cs` with members: `bool TryBeginSubmission(string sessionId, UnifiedPromptSubmission submission, Task<RolePlayInteraction> engineTask)`, `RolePlayRunningSubmission? GetEntry(string sessionId)`, `void AttachChunkCallback(string sessionId, Func<string, Task>? callback)`, `void DetachChunkCallback(string sessionId)`, `void AcknowledgeFailure(string sessionId)`, `event Action<string>? OnJobStatusChanged`; depends on T004
- [X] T006 Create `RolePlaySubmissionTracker` singleton implementation in `DreamGenClone.Web/Application/RolePlay/RolePlaySubmissionTracker.cs` backed by `ConcurrentDictionary<string, RolePlayRunningSubmission>`; `TryBeginSubmission` uses `TryAdd` atomically and wires a `ContinueWith` continuation that transitions `Status`, fires `OnJobStatusChanged(sessionId)`, and removes entry on Completed / retains on Failed; Serilog Information logs on start, completion, failure; Error log on unexpected exception; depends on T005
- [X] T007 Register `IRolePlaySubmissionTracker` as singleton in `DreamGenClone.Web/Program.cs` (same pattern as existing singletons at lines 127–135): `builder.Services.AddSingleton<RolePlaySubmissionTracker>(); builder.Services.AddSingleton<IRolePlaySubmissionTracker>(sp => sp.GetRequiredService<RolePlaySubmissionTracker>());`; depends on T006

**Checkpoint**: Build clean with all new types present and DI registered before US work begins.

---

## Phase 3: User Story 1 — Response Persists After Navigation Away (Priority: P1) 🎯 MVP

**Goal**: A submitted Continue prompt survives navigation and the completed response appears in history on return.

**Independent Test**: Submit a Continue, navigate to Home, wait, return to workspace — completed interaction present in history. Per [quickstart.md Manual Test 1](quickstart.md).

### Unit Tests for User Story 1

- [X] T008 [P] [US1] Create `DreamGenClone.Tests/RolePlay/RolePlaySubmissionTrackerTests.cs`; write tests for `TryBeginSubmission` — (a) first call returns true and adds Running entry, (b) second call for same sessionId returns false, (c) completion continuation transitions to Completed and removes entry, (d) faulted task transitions to Failed and retains entry; depends on T006
- [X] T009 [P] [US1] Add test: `AcknowledgeFailure` removes a Failed entry and unblocks the session; noop on Running or absent entry; depends on T006

### Implementation for User Story 1

- [X] T010 [US1] Harden `RolePlayEngineService` chunk callback invocation sites in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`: locate every `await onChunk(chunk)` / `onChunk?.Invoke(chunk)` call; wrap each in `try { ... } catch (ObjectDisposedException) { } catch (InvalidOperationException) { }` so the engine never faults due to a disposed component callback; no signature change required
- [X] T011 [US1] Modify `SubmitPromptWithContinuationAsync` in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`: (a) build `submission` as before; (b) fire engine call **without awaiting**: `var engineTask = RolePlayEngine.SubmitPromptAsync(submission, _tracker.GetEntry(sessionId)?.ChunkCallbackWrapper.InvokeAsync);`; (c) call `bool accepted = _tracker.TryBeginSubmission(sessionId, submission, engineTask)`; (d) if not accepted, set `_statusMessage = "A response is already in progress."` and return; (e) if accepted and awaits model, call `_tracker.AttachChunkCallback(sessionId, OnRolePlayResponseChunkAsync)`; (f) clear `_promptText`; (g) set `_isSubmitting = false` immediately (component no longer awaits completion); depends on T007, T010
- [X] T012 [US1] Add tracker injection to `RolePlayWorkspace.razor`: inject `[Inject] IRolePlaySubmissionTracker _tracker` and extract `sessionId` as a convenience property (`_session?.Id ?? string.Empty`); depends on T007
- [X] T013 [US1] Add Serilog Information log in `RolePlayWorkspace.razor` after `TryBeginSubmission` succeeds: `Logger.LogInformation("Prompt handed to tracker: session={SessionId}, intent={Intent}", sessionId, submission.Intent)`; depends on T011

**Checkpoint**: US1 is independently testable via quickstart Manual Test 1. Build must be green.

---

## Phase 4: User Story 2 — All Prompt Types Are Navigation-Resilient (Priority: P1)

**Goal**: Custom prompts and interaction commands are equally navigation-resilient.

**Independent Test**: Repeat navigate-away-and-return sequence for a custom typed prompt and an interaction command — both appear in history. Per [quickstart.md Manual Tests 2 and 4](quickstart.md).

### Unit Tests for User Story 2

- [X] T014 [P] [US2] Add tests in `RolePlaySubmissionTrackerTests.cs`: (a) `AttachChunkCallback` on a Running entry swaps the inner field; (b) `AttachChunkCallback` on absent session is a no-op (no exception); (c) `DetachChunkCallback` sets inner callback to null; depends on T008

### Implementation for User Story 2

- [X] T015 [US2] Verify the T011 submission path covers all `PromptIntent` values (Continue, Message, Narrative, Instruction) — the un-awaited engine-task pattern from T011 applies uniformly because `SubmitPromptWithContinuationAsync` is the single entry point for all prompt types; confirm no other submission path in `RolePlayWorkspace.razor` bypasses the tracker (search for `SubmitPromptAsync` calls in the file); add tracker delegation to any found bypasses; depends on T011
- [X] T016 [US2] Add `_backgroundSubmissionRunning` bool field to `RolePlayWorkspace.razor`; set it `true` in `SubmitPromptWithContinuationAsync` after `TryBeginSubmission` succeeds; set it `false` in the tracker status-changed handler (Phase 5); this field gates the processing indicator visibility; depends on T011

**Checkpoint**: US1 and US2 both independently testable. All prompt types covered. Build green.

---

## Phase 5: User Story 3 — In-Progress Indicator on Return (Priority: P2)

**Goal**: When a user returns to the workspace during an in-flight submission, a processing indicator is shown; when the response completes or fails, the workspace updates without a manual refresh.

**Independent Test**: Submit, navigate away and back quickly — processing indicator visible; await completion — indicator clears and response appears. Per [quickstart.md Manual Test 3](quickstart.md).

### Unit Tests for User Story 3

- [X] T017 [P] [US3] Add test: `OnJobStatusChanged` event fires once on task completion and once on task failure; verify argument is the correct sessionId; depends on T008

### Implementation for User Story 3

- [X] T018 [US3] Add `OnInitializedAsync` tracker query to `RolePlayWorkspace.razor` (after session is loaded): call `_tracker.GetEntry(sessionId)`; if entry is `Running` — set `_backgroundSubmissionRunning = true`, call `_tracker.AttachChunkCallback`, subscribe `OnTrackerStatusChanged` to `_tracker.OnJobStatusChanged`, start `PeriodicTimer`; if entry is `Failed` — set `_resubmitPending = true`, set `_resubmitPayload = entry.Payload`, set `_resubmitFailureMessage = entry.FailureMessage`; depends on T016
- [X] T019 [US3] Implement `OnTrackerStatusChanged(string changedSessionId)` handler in `RolePlayWorkspace.razor`: if `changedSessionId == sessionId` — call `await InvokeAsync(async () => { await LoadSessionAsync(); _backgroundSubmissionRunning = false; var entry = _tracker.GetEntry(sessionId); if (entry?.Status == RolePlaySubmissionStatus.Failed) { _resubmitPending = true; _resubmitPayload = entry.Payload; } StateHasChanged(); })`; depends on T018
- [X] T020 [US3] Add `PeriodicTimer` fallback in `RolePlayWorkspace.razor`: field `_submissionPollTimer` (`PeriodicTimer?`); start 2-second timer when a Running entry is detected; timer tick calls `OnTrackerStatusChanged(sessionId)` then stops timer if entry is no longer Running; depends on T019
- [X] T021 [US3] Update `DisposeAsync` in `RolePlayWorkspace.razor`: (a) call `_tracker.DetachChunkCallback(sessionId)` before JS cleanup; (b) unsubscribe `OnTrackerStatusChanged` from `_tracker.OnJobStatusChanged`; (c) dispose `_submissionPollTimer`; depends on T020
- [X] T022 [US3] Add re-submit affordance UI to `RolePlayWorkspace.razor`: when `_resubmitPending` is true, render inline banner above the prompt input: "Last response failed — re-submit?" with [Re-send] and [Dismiss] buttons; [Re-send] sets `_promptText = _resubmitPayload!.PromptText` and sets `_resubmitConfirmVisible = true`; [Dismiss] calls `_tracker.AcknowledgeFailure(sessionId)`, clears `_resubmitPending`, clears `_resubmitPayload`; hide the normal send button while banner is visible; depends on T018
- [X] T023 [US3] Add re-submit confirmation step: when `_resubmitConfirmVisible` is true, show "Confirm re-send?" with [Confirm] and [Cancel] buttons above the prompt; [Confirm] clears `_resubmitConfirmVisible`, calls `_tracker.AcknowledgeFailure(sessionId)`, clears `_resubmitPending`, then calls `SubmitPromptWithContinuationAsync()`; [Cancel] clears `_resubmitConfirmVisible` and returns to the error banner; depends on T022
- [X] T024 [US3] Add processing indicator to `RolePlayWorkspace.razor`: when `_backgroundSubmissionRunning` is true and component mounts (return-while-running case), render the existing pending-response indicator or a simple "Awaiting response..." message consistent with the existing `_isAwaitingModelResponse` UI; disable the send button while `_backgroundSubmissionRunning` is true; depends on T018

**Checkpoint**: All three user stories independently testable. Full manual test suite in quickstart.md should pass.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Build validation, logging audit, edge-case hardening.

- [X] T025 [P] Audit all Serilog log calls added in T006, T013 — confirm structured message templates, no string interpolation in log arguments, contextual properties include `sessionId` and `intent` where appropriate; in `DreamGenClone.Web/Application/RolePlay/RolePlaySubmissionTracker.cs` and `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T026 [P] Verify `_finishOptionsGenerationCts` cancel in existing `DisposeAsync` still fires before the tracker `DetachChunkCallback` call added in T021; confirm order is: cancel finish-options CTS → detach tracker callback → unsubscribe event → dispose timer → JS cleanup → dotNetRef dispose; in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T027 Run full solution build and confirm zero errors: `dotnet build DreamGenClone.sln -v minimal`
- [X] T028 Run existing RP test suite and confirm all tests green: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "Category=RolePlay" -v minimal` (or full suite if no filter exists)
- [ ] T029 Run quickstart.md Manual Test 1 (navigate-away Continue) end-to-end against a running local app; confirm response in history on return; record outcome

---

## Dependencies

```
T001 (verify clean build)
  └─ T002, T003 (parallel — domain types, callback wrapper)
       └─ T004 (RolePlayRunningSubmission)
            └─ T005 (IRolePlaySubmissionTracker interface)
                 └─ T006 (RolePlaySubmissionTracker implementation)
                      └─ T007 (DI registration in Program.cs)
                           ├─ T008, T009 (parallel — US1 unit tests)
                           ├─ T010 (engine ObjectDisposedException hardening)
                           └─ T012 (inject tracker into workspace)
                                └─ T011 (submission path delegates to tracker)
                                     ├─ T013 (logging)
                                     ├─ T014 (US2 tests — parallel)
                                     └─ T015 (US2 — verify all prompt types covered)
                                          └─ T016 (_backgroundSubmissionRunning flag)
                                               └─ T018 (OnInitializedAsync tracker query)
                                                    ├─ T017 (US3 event test — parallel)
                                                    └─ T019 (OnTrackerStatusChanged handler)
                                                         └─ T020 (PeriodicTimer fallback)
                                                              └─ T021 (DisposeAsync updates)
                                                                   └─ T022 (re-submit banner UI)
                                                                        └─ T023 (confirmation step)
                                                                   └─ T024 (processing indicator UI)
                                                T025, T026, T027, T028, T029 (polish — after all phases)
```

## Parallel Execution Opportunities

| Pair | Both work on different files |
|------|------------------------------|
| T002 + T003 | Both create independent types in the same file but are self-contained additions |
| T008 + T009 | Both are new test methods in the same test class |
| T008 + T010 | Test file vs engine service — no dependency |
| T014 + T015 | New test methods vs workspace implementation |
| T017 + T018 | New test vs init code |
| T025 + T026 | Both audit/review tasks; no writes overlap |

## Implementation Strategy

**MVP = Phase 1 + Phase 2 + Phase 3** — US1 alone (Continue survives navigation) delivers the core B-027 value and can be verified independently in ~30 minutes of implementation.

**Phase 4 (US2)** is a verification pass rather than new infrastructure — the submission path change in T011 already covers all prompt types. T015 confirms no bypass paths exist.

**Phase 5 (US3)** adds the return-state awareness (processing indicator, failure inline error, re-submit). This is the most UI-intensive phase but builds cleanly on the tracker already in place.

## Task Count Summary

| Phase | Tasks | Story | Parallelisable |
|-------|-------|-------|----------------|
| Phase 1: Setup | 1 | — | 0 |
| Phase 2: Foundational | 6 | — | 2 (T002, T003) |
| Phase 3: US1 | 6 | US1 | 3 (T008, T009, T010) |
| Phase 4: US2 | 3 | US2 | 1 (T014) |
| Phase 5: US3 | 8 | US3 | 1 (T017) |
| Phase 6: Polish | 5 | — | 2 (T025, T026) |
| **Total** | **29** | | |
