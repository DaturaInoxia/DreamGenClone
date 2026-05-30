# Feature Specification: Prompt Queue — Navigation-Resilient RP Submissions

**Feature Branch**: `027-prompt-queue-continue`  
**Created**: 2026-05-29  
**Status**: Draft  
**Backlog Item**: B-027  
**Input**: User description: "Prompt queue — continue processing if workspace is navigated away"

## Clarifications

### Session 2026-05-29

- Q: How far should the continuation survive — same-process navigation only, or across server restarts? -> A: Same-process navigation only; in-memory tracking is sufficient. Jobs lost on server restart are acceptable.
- Q: When the user navigates away during streaming, what should happen to streaming chunks? -> A: Abandon the streaming chunk callback when the component is disposed; let the underlying LLM call complete and save the full response to the session. No chunk buffering or replay is required.
- Q: How should the user be notified when a response is ready on return? -> A: Silently — the completed response appears in the session interaction history on the next workspace load. No toast or banner notification is required.
- Q: Should this resilience apply to all RP prompt types or only Continue? -> A: All RP prompt submission types — Continue, custom prompt, and commands — are covered uniformly.

### Session 2026-05-29 (Clarification)

- Q: What does the user see when they return to the workspace after a submission failed while they were away? -> A: Show an inline error indicator in the workspace (e.g. "Last submission failed — re-submit?") with a re-submit affordance. No toast. No silent failure.
- Q: If an LLM call hangs indefinitely, should the tracker enforce its own maximum wait duration per session entry before marking it Failed? -> A: No tracker-level timeout. Rely entirely on the model client's own request timeout to terminate hung calls. If the model client times out it raises an exception which the tracker catches as a failure, triggering the inline error flow.
- Q: When the user clicks the re-submit affordance after a background failure, what exactly happens? -> A: The tracker retains the original submission payload on failure. Clicking re-submit pre-fills the prompt with the original text and requires user confirmation before re-sending. This prevents accidental duplicate sends while keeping re-submission frictionless.
- Q: How should the workspace component learn that a background submission completed or failed while the component is mounted — event subscription only, or also a polling fallback? -> A: Both. The workspace subscribes to the tracker's status change event for immediate notification, and also runs a periodic in-component timer as a fallback in case the event is missed due to circuit interruption. The timer stops once the task resolves.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Response Persists After Navigation Away (Priority: P1)

A user submits a Continue prompt in the RP workspace, navigates to a different page before the response arrives, then returns to the workspace and finds the completed response present in the session interaction history.

**Why this priority**: This is the primary behaviour gap described in B-027. Until this is resolved, any LLM response that outlasts the page visit is silently lost, forcing the user to re-submit. This story delivers the core user value.

**Independent Test**: Can be fully tested by submitting a Continue, immediately navigating to Home, waiting for LLM latency to elapse, returning to the workspace, and verifying the response appears in history without a manual re-submit.

**Acceptance Scenarios**:

1. **Given** a user has submitted a Continue prompt, **When** they navigate away from the RP workspace before the response arrives, **Then** the underlying engine call continues running in the background without being cancelled.
2. **Given** the engine call completes while the user is away, **When** the response is persisted to the session, **Then** returning to the workspace and loading the session shows the new interaction in history.
3. **Given** the user submits a Continue and the response completes before they return, **When** they open the workspace, **Then** the response is present and the workspace is not in a submitting/locked state.
4. **Given** an in-flight submission exists for a session, **When** the user attempts to submit another prompt to the same session, **Then** the second submission is rejected with a clear indicator that a response is already in progress.

---

### User Story 2 - All Prompt Types Are Navigation-Resilient (Priority: P1)

A user submits any RP prompt type — Continue, a typed custom prompt, or an interaction command — navigates away, and on return finds the completed response in the session history.

**Why this priority**: A partial implementation that only protects Continue but loses custom prompts and commands would create inconsistent behaviour and user confusion. Uniform coverage is required from the start.

**Independent Test**: Can be fully tested by repeating the same navigate-away-and-return sequence for a custom typed prompt and for an interaction command, verifying both appear in history on return.

**Acceptance Scenarios**:

1. **Given** a user submits a custom typed prompt, **When** they navigate away mid-processing, **Then** the response is persisted and visible in history on return.
2. **Given** a user executes an interaction command, **When** they navigate away mid-processing, **Then** the command result is persisted and visible in history on return.
3. **Given** any prompt type is in-flight for a session, **When** the component is disposed due to navigation, **Then** no unhandled exception is thrown and the session data is not corrupted.

---

### User Story 3 - In-Progress Indicator on Return (Priority: P2)

A user submits a Continue prompt, navigates away, and returns to the workspace before the response has completed. The workspace displays a processing indicator so the user knows a response is still in flight, and the completed response appears when it arrives.

**Why this priority**: Without a return indicator, users may re-submit, believing their previous submission was lost. This prevents duplicate submissions and improves perceived reliability. It is P2 because the core data-loss problem (US1/US2) must be solved first.

**Independent Test**: Can be fully tested by submitting a long-running prompt, quickly navigating away and back, and verifying the workspace shows a processing state rather than an idle prompt box. Waiting for completion verifies the response then appears.

**Acceptance Scenarios**:

1. **Given** a prompt submission is in-flight and the user returns to the workspace, **When** the workspace initializes, **Then** it detects the active submission via the tracker and displays a processing indicator.
2. **Given** the workspace is showing a processing indicator for an in-flight submission, **When** the response completes, **Then** the indicator is cleared and the new interaction is shown in history.
3. **Given** the workspace detects an active submission on initialization, **When** that submission has already completed before the component finishes initializing, **Then** the workspace loads the completed response from session history without showing the processing indicator.

---

### Edge Cases

- What happens when the server restarts while a submission is in-flight? The in-memory tracker is lost; the in-flight LLM call is cancelled by process shutdown. This is accepted by scope. The user will need to re-submit.
- What happens when the component's streaming chunk callback throws `ObjectDisposedException` because the component was disposed? The engine must catch and silently drop the exception; the LLM call must not be cancelled.
- What happens when a null or disposed chunk callback is passed to the engine? The engine must treat a null callback as a no-op for each chunk; no exception is thrown and processing continues to completion.
- What happens when two browser tabs have the same session open and both submit simultaneously? The tracker uses session ID as the key. The second submission is rejected regardless of which tab sent it. Multi-tab coordination is out of scope.
- What happens when the tracker entry is not cleaned up after a failure? The tracker must retain the Failed entry until the returning component acknowledges it (reads and dismisses the error); only then is the entry removed so the session is unblocked for a new submission.
- What does the user see when returning after a background failure? An inline error indicator in the workspace with a re-submit affordance. The failed entry is not removed until the user dismisses or re-submits.
- What happens if the original submission payload is too large to retain in memory? The tracker retains the full original submission payload in the in-memory Failed entry; there is no size limit beyond available process memory, which is acceptable for same-process in-memory scope.
- What happens if the workspace component misses the tracker's status change event due to a brief circuit interruption? The periodic fallback timer detects the resolved status on its next tick and triggers a UI refresh; the timer stops once the task resolves.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: All RP prompt submission types (Continue, custom prompt, interaction commands) MUST continue processing to completion if the originating Blazor component is navigated away from or disposed during processing.
- **FR-002**: The engine MUST persist the completed response to the session database regardless of whether the originating component is still alive.
- **FR-003**: On workspace reload after navigation, completed responses MUST be present in session interaction history without any manual re-submit or page refresh action by the user.
- **FR-004**: While a submission is in-flight and the user returns to the workspace, the workspace MUST display a processing indicator and block a new submission until the in-flight task resolves.
- **FR-005**: When a component is disposed during active streaming, the streaming chunk callback MUST be disconnected without cancelling the underlying LLM or engine call; the engine continues processing silently.
- **FR-006**: The engine service's prompt submission method MUST accept a nullable chunk callback; when the callback is null or raises `ObjectDisposedException`, the engine MUST silently skip delivery of that chunk and continue to completion.
- **FR-007**: No CancellationToken sourced from the Blazor component or its disposal MUST be passed to the engine or LLM call; submission lifecycle tokens must be independent of component lifetime.
- **FR-008**: A new singleton service `IRolePlaySubmissionTracker` MUST be introduced to manage in-flight submission state keyed by session ID. It MUST be registered in the DI container at application startup.
- **FR-009**: `IRolePlaySubmissionTracker` MUST expose: a method to begin a submission (returning false if one is already active for that session), a method to query current status for a session, a method for a returning component to attach a new chunk callback to a still-running task, and an event for status change notifications. The status change event MUST be the primary notification mechanism; components subscribe on initialization and unsubscribe on disposal.
- **FR-009a**: `RolePlayWorkspace.razor` MUST also run a `PeriodicTimer`-based fallback poll against the tracker while a submission is active, in case the status change event is missed due to a circuit interruption. The timer MUST stop once the tracked task resolves (success, failure, or on component disposal).
- **FR-010**: A submission MUST be rejected (not enqueued, not queued) when the tracker already holds an active entry for the same session ID. The rejection must be surfaced to the user via a UI indicator.
- **FR-011**: The tracker MUST remove its session entry on successful completion so subsequent submissions for that session can proceed immediately. On failure, the entry MUST be retained in a `Failed` state, including the original submission payload, until the workspace component explicitly acknowledges the failure (dismiss or re-submit action), at which point the entry is removed and the session is unblocked.
- **FR-011a**: When the user invokes the re-submit affordance after a background failure, the workspace MUST pre-fill the prompt input with the original submission's prompt text and display a confirmation step before re-firing the submission; one-click immediate re-fire without confirmation is not permitted.
- **FR-012**: `RolePlayWorkspace.razor` MUST delegate prompt submission to the tracker rather than awaiting the engine call directly in the component; the component submits and returns, leaving the task running in the tracker.
- **FR-013**: On component initialization, `RolePlayWorkspace.razor` MUST query the tracker for the session ID; if a `Running` entry is found, the component MUST display a processing indicator, attach its chunk callback, subscribe to the status change event, and start the fallback poll timer; if a `Failed` entry is found, the component MUST display an inline error indicator with a re-submit affordance.
- **FR-014**: Component disposal MUST detach the component's chunk callback from any active tracker entry; it MUST NOT cancel or complete the submission task.
- **FR-015**: Persisted feature data for tracker state MUST remain in-memory only (no SQLite); the tracker is a singleton in-process service.
- **FR-016**: Application logging MUST use Serilog with structured message templates; the tracker MUST emit Information-level logs for task start, completion, and failure, and actionable Error-level logs on unexpected failure.
- **FR-017**: Log levels MUST be configurable via settings without code changes.

### Key Entities

- **`IRolePlaySubmissionTracker`** (new, `DreamGenClone.Web/Application/RolePlay/`): Singleton interface managing in-flight RP prompt submissions by session ID. Provides `TryBeginSubmission`, `GetStatus`, `AttachChunkCallback`, and an `OnJobStatusChanged` event.
- **`RolePlaySubmissionTracker`** (new, `DreamGenClone.Web/Application/RolePlay/`): Singleton implementation backed by `ConcurrentDictionary<string, RolePlayRunningSubmission>` keyed by session ID.
- **`RolePlayRunningSubmission`** (new): Internal record tracking the running `Task`, `RolePlaySubmissionStatus`, `StartedUtc`, and the original `UnifiedPromptSubmission` payload for a single in-flight submission. The payload is retained on failure to enable pre-filled re-submission.
- **`RolePlaySubmissionStatus`** (new enum): Values `Running`, `Completed`, `Failed`.
- **`RolePlayEngineService`** (modified, `DreamGenClone.Web/Application/RolePlay/`): Chunk callback parameter made nullable; null/disposed callbacks are silently skipped per chunk without aborting the call.
- **`RolePlayWorkspace.razor`** (modified, `DreamGenClone.Web/Components/Pages/`): Submit path delegates to tracker; `OnInitializedAsync` queries tracker and conditionally subscribes to status event and starts fallback poll timer; `DisposeAsync` detaches callback, unsubscribes from event, and stops timer.

## Assumptions

- `RolePlayEngineService.SubmitPromptAsync` already persists the completed interaction to the session database on success; no new persistence layer is needed.
- The Blazor application runs as a single server-side circuit per browser session; multi-circuit or multi-tab scenarios are out of scope for this feature.
- In-flight task loss on server restart is an accepted trade-off given the same-process scope constraint.
- Only one active submission per session at a time is required; a prioritised queue of multiple submissions is out of scope.
- The tracker does not enforce its own timeout on in-flight submissions; hung calls are the responsibility of the model client's configured request timeout, whose exception propagates as a tracker failure.
- Streaming chunk replay to a returning component (buffering all prior chunks and re-delivering them on `AttachChunkCallback`) is out of scope; only future chunks after reattachment are delivered.

## Dependencies

- `RolePlayEngineService.SubmitPromptAsync` — must accept nullable chunk callback (modification required)
- `RolePlayWorkspace.razor` — submission, initialization, and disposal flows (modification required)
- `DreamGenClone.Web/Program.cs` — singleton DI registration of `IRolePlaySubmissionTracker`
- Existing Serilog configuration in `appsettings.json` and `appsettings.Development.json`

## Success Criteria

### Measurable Outcomes

- **SC-001**: A submitted Continue prompt that outlasts page navigation is present in the session interaction history on the user's return, with zero manual re-submission required.
- **SC-002**: All RP prompt types (Continue, custom, command) survive navigation without data loss under the same condition.
- **SC-003**: No `ObjectDisposedException` or unhandled exception is raised when a component is disposed during active streaming.
- **SC-004**: A duplicate submission attempt while a task is in-flight is blocked 100% of the time and communicated clearly in the UI.
- **SC-005**: Build produces zero errors after implementation; all existing RP unit and integration tests remain green.
- **SC-006**: The processing indicator is shown whenever the user returns to a workspace with an active in-flight submission, and clears automatically on task completion.
- **SC-007**: When the user returns to the workspace after a background submission failure, an inline error indicator with a re-submit affordance is shown; no data is silently lost.
- **SC-008**: The re-submit affordance pre-fills the prompt with the original submission text and presents a confirmation step; the original prompt text is recoverable from the tracker's retained Failed entry.
- **SC-009**: The workspace component self-corrects its displayed status on the next timer tick if the status change event was missed; the processing indicator clears and the correct state is shown without requiring a manual page refresh.
