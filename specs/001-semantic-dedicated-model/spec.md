# Feature Specification: Semantic Analysis — Dedicated Model & Concurrent Processing

**Feature Branch**: `001-semantic-dedicated-model`  
**Created**: 2026-05-28  
**Status**: Draft  
**Backlog**: B-039  
**Input**: Give the RP semantic analysis pipeline its own independent model slot (separate from RP generation) so model selection can be tuned independently, and make semantic analysis process multiple interactions concurrently with configurable parallelism degree.

## Clarifications

### Session 2026-05-28

- Q: What should the dedicated semantic worker do when a job throws an unhandled exception (e.g., LLM timeout, network error)? → A: Log the error, mark the interaction's analysis state as `Error`, release the concurrency slot, and continue to the next queued job. No retry. Same pattern as the existing generic background job worker.
- Q: Is there an upper bound on the "Max Parallel" value? → A: Cap at 16; the UI must reject values above 16 with a validation message.
- Q: Where and in what form is the restart notice displayed when "Max Parallel" is saved? → A: An inline static hint shown below the "Max Parallel" field once a value is saved (e.g. "Takes effect on next restart."); no toast or banner required.
- Q: Should the Model Manager UI proactively warn the user when no model is assigned to the semantic slot? → A: Yes — display a warning badge/icon on the "RP Semantic Analysis (Background)" row with a short tooltip when no model is assigned.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Assign a Dedicated Model for Background Semantic Analysis (Priority: P1)

As a user who runs multiple active RP sessions, I want to assign a separate, smaller model specifically for background semantic analysis so that live RP continuation generation is not affected by the semantic inference workload.

Today, semantic analysis of each RP interaction uses the same model slot (`RolePlayGeneration`) as live RP text continuation. This means a slow or busy semantic inference call can compete with the model serving real-time RP output. With a dedicated slot, users can assign a fast/small local model (e.g., a 7B model) for background semantic work while reserving a larger model for foreground RP generation.

**Why this priority**: P1 — this is the core capability described in B-039. Without it, semantic analysis has no independent configuration surface.

**Independent Test**: Open the Model Manager → Function Defaults table. Observe that "RP Semantic Analysis (Background)" appears as a distinct row. Assign any available model to it and save. Verify the saved assignment is retained. No code changes to the RP engine are needed to validate this story in isolation.

**Acceptance Scenarios**:

1. **Given** the Model Manager is open, **When** I view the Function Defaults table, **Then** a row labelled "RP Semantic Analysis (Background)" is present alongside all other function rows.
2. **Given** a model is available in the provider list, **When** I select it for the "RP Semantic Analysis (Background)" row and save, **Then** the assignment is persisted and displayed correctly on reload.
3. **Given** no model has been assigned to "RP Semantic Analysis (Background)", **When** a background semantic analysis job runs, **Then** the job records an explicit failure diagnostic for that interaction, applies no semantic deltas, and does not silently fall back to the `RolePlayGeneration` model slot.
4. **Given** a model is assigned to "RP Semantic Analysis (Background)" and a different model is assigned to `RolePlayGeneration`, **When** a background semantic job runs during an active RP continuation, **Then** each uses its own independently configured model.
5. **Given** no model has been assigned to "RP Semantic Analysis (Background)", **When** the Model Manager Function Defaults table is displayed, **Then** a warning badge/icon is shown on that row with a tooltip indicating that semantic analysis will fail until a model is assigned.

---

### User Story 2 — Configure Maximum Parallel Semantic Jobs (Priority: P1)

As a user with several concurrent RP sessions, I want to configure how many semantic analysis jobs can run in parallel so I can tune throughput against local hardware limits.

By default, background jobs process one at a time. When several sessions are active simultaneously, semantic events queue up behind each other, delaying stat updates and theme scoring. A configurable parallelism degree lets the user trade off between faster processing and GPU/CPU resource usage on their local machine.

**Why this priority**: P1 — parallelism is the second core capability described in B-039, and the dedicated model slot enables isolation needed to make per-job concurrency safe.

**Independent Test**: Set "Max Parallel" to 3 in the Model Manager. Start three concurrent RP sessions and generate interactions in all three simultaneously. Verify semantic analysis completes for all three sessions without one serializing behind another.

**Acceptance Scenarios**:

1. **Given** the Model Manager Function Defaults table is displayed, **When** I view the "RP Semantic Analysis (Background)" row, **Then** a "Max Parallel" numeric input field is visible alongside the model dropdown.
2. **Given** I set "Max Parallel" to 3 and save, **When** four semantic analysis jobs are pending simultaneously, **Then** at most 3 run concurrently; the fourth waits and eventually completes.
3. **Given** "Max Parallel" is not set (null), **When** the application starts, **Then** a built-in default parallelism of 2 is applied.
4. **Given** I change "Max Parallel" and save via the UI, **When** the save completes, **Then** an inline hint is displayed below the "Max Parallel" field reading "Takes effect on next restart."

---

### User Story 3 — Semantic Jobs Are Isolated from General Background Jobs (Priority: P2)

As a user, I want semantic analysis jobs to be processed by their own dedicated background worker, independent of other background tasks (e.g., story processing, health checks), so that a spike in semantic work does not starve or delay other system jobs.

**Why this priority**: P2 — it reinforces isolation of the semantic pipeline but is only observable at high load; the model slot and parallelism configuration are more immediately visible.

**Independent Test**: Trigger a batch story analysis job at the same time as several RP interactions requiring semantic analysis. Verify both proceed independently without one type blocking the other.

**Acceptance Scenarios**:

1. **Given** semantic analysis jobs and story processing jobs are both enqueued, **When** both workers run, **Then** semantic jobs are processed by the semantic worker and story jobs by the generic worker, independently.
2. **Given** the semantic worker is saturated at its concurrency cap, **When** a new RP interaction is added, **Then** the semantic job is queued and eventually processed; the generic background worker queue is unaffected.

---

### Edge Cases

- What happens when the model assigned to "RP Semantic Analysis (Background)" is disabled or deleted? The job must record an explicit diagnostic failure, apply no deltas, and not attempt resolution via any other model slot. The Model Manager UI must show a warning badge on the row in this state as well.
- What happens when `MaxConcurrentJobs` is set to 0 or a negative value? The UI must reject values below 1; persist only valid positive integers. Values above 16 must also be rejected with a validation message.
- What happens when the app restarts with no `FunctionModelDefault` row for `RolePlaySemanticAnalysis`? The worker starts with the built-in default parallelism of 2 and semantic jobs are attempted but will fail at model resolution if no model is assigned.
- What if a semantic job is still in-flight when the app shuts down? The job is cancelled via the `CancellationToken` passed to the handler; no partial state is persisted.
- What if a semantic job throws an unhandled exception (LLM timeout, network error, unexpected crash)? The worker must log the error, mark the affected interaction's analysis state as `Error`, release the concurrency slot, and continue processing the next queued job. No retry is attempted.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST add a `RolePlaySemanticAnalysis` function to the model function registry so users can assign a dedicated model to background semantic inference independently of RP text generation.
- **FR-002**: The system MUST update the semantic event inference service to resolve its model via the `RolePlaySemanticAnalysis` function slot; it MUST NOT fall back to `RolePlayGeneration` if no model is assigned to the semantic slot.
- **FR-003**: When no model is assigned to `RolePlaySemanticAnalysis`, a semantic analysis job MUST record an explicit diagnostic failure event for the affected interaction and apply zero semantic deltas; it MUST NOT silently continue with another model.
- **FR-004**: A `MaxConcurrentJobs` setting MUST be persisted alongside the `RolePlaySemanticAnalysis` function default, allowing users to configure the maximum number of concurrent semantic analysis jobs.
- **FR-005**: Semantic analysis background jobs MUST be processed by a dedicated background worker that is separate from the generic background job worker used for other job types.
- **FR-006**: The dedicated semantic background worker MUST enforce the configured `MaxConcurrentJobs` ceiling; when the ceiling is reached, additional semantic jobs MUST queue and wait rather than being dropped.
- **FR-007**: When `MaxConcurrentJobs` is null or not configured, the worker MUST apply a built-in default of 2 concurrent jobs.
- **FR-008**: Changes to `MaxConcurrentJobs` via the UI take effect on the next application restart; once a value is saved, the UI MUST display an inline static hint below the "Max Parallel" field reading "Takes effect on next restart."
- **FR-009**: The Model Manager Function Defaults table MUST display the "RP Semantic Analysis (Background)" row with a model assignment dropdown and a "Max Parallel" numeric input field.
- **FR-010**: The "Max Parallel" field MUST reject values below 1 or above 16; only integers in the range 1–16 MUST be accepted.
- **FR-011**: The generic background job worker MUST NOT process `SemanticInteractionAnalysis` job type after this feature is active.
- **FR-012**: Persisted feature data MUST use SQLite via the existing `FunctionModelDefault` table extended with a `MaxConcurrentJobs` column.
- **FR-013**: Application logging MUST use Serilog with structured message templates for all semantic worker lifecycle events (start, shutdown, job enqueued, job started, job completed, job failed, concurrency gate acquired/released).
- **FR-014**: Log levels MUST be configurable via settings without code changes.
- **FR-015**: When a semantic analysis job throws an unhandled exception, the worker MUST log the error with structured context (session ID, interaction ID, exception type), mark the interaction's analysis record as `Error` state, release the concurrency slot, and proceed to the next job; no retry is attempted.
- **FR-016**: The Model Manager Function Defaults table MUST display a warning badge/icon with a short tooltip on the "RP Semantic Analysis (Background)" row whenever no model is assigned or the assigned model is disabled/deleted, indicating that semantic analysis will fail until a valid model is assigned.

### Key Entities

- **AppFunction (enum)**: Identifies an LLM-served capability. Extended with `RolePlaySemanticAnalysis` to represent background semantic event inference for RP interactions.
- **FunctionModelDefault**: Persists the model assignment and inference parameters (temperature, topP, maxTokens) for an `AppFunction`. Extended with `MaxConcurrentJobs` (nullable int) to store the parallelism ceiling for the semantic worker.
- **SemanticBackgroundJobQueue**: A dedicated in-process queue for `SemanticInteractionAnalysis` jobs, isolated from the generic background job queue.
- **SemanticBackgroundJobWorker**: A `BackgroundService` that reads from `SemanticBackgroundJobQueue`, enforces the `MaxConcurrentJobs` ceiling via a semaphore, and dispatches jobs to `SemanticInteractionAnalysisJobHandler`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can assign a different model to "RP Semantic Analysis (Background)" than to "RP Generation" and both assignments persist independently across restarts.
- **SC-002**: With `MaxConcurrentJobs` set to N, no more than N semantic analysis jobs run at the same time; all pending jobs eventually complete.
- **SC-003**: When no model is assigned to the semantic slot, semantic jobs fail with a logged diagnostic rather than silently using the wrong model; the RP session continues unaffected.
- **SC-004**: Semantic background jobs do not appear in or compete with the generic background job worker queue.
- **SC-005**: With multiple active RP sessions, semantic analysis events are processed in parallel (up to the configured cap), reducing per-interaction semantic latency relative to the strictly sequential baseline.
- **SC-006**: The application builds with 0 errors and all existing tests pass after the feature is implemented.

## Assumptions

- The `GenericBackgroundJobWorker` continues to handle all non-semantic job types without modification.
- The `MaxConcurrentJobs` concurrency cap is read once at worker startup; dynamic resize during runtime is not required.
- "Fail fast" on missing `RolePlaySemanticAnalysis` model assignment applies only to the inference step; the RP session itself continues unaffected (semantic deltas are skipped for that interaction).
- This feature does not change how semantic events are stored, how theme scoring is applied, or how stat deltas are calculated — only how the model is resolved and how jobs are dispatched.
