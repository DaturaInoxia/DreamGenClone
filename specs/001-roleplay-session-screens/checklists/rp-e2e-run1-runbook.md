# RP E2E Validation Runbook - Run 1

Date: 2026-04-12
Owner: Copilot + User
Environment: Local Debug + real provider
Truth priority: Desired behavior (user) > current implementation > spec references

## Scope

This run covers:
- Character setup and perspective/stat defaults
- Session lifecycle: create/list/open/start/continue/delete
- Prompt submit and continue-as behavior modes
- Decision point and adaptive state checkpoints
- Diagnostics and data integrity checks

This run does not cover:
- Non-RolePlay features unless they block RolePlay behavior

## Evidence Rules

Capture evidence at every checkpoint:
1. UI action and input values
2. Immediate UI result
3. Persisted data before and after
4. Logs and debug events with timestamps
5. Mismatch entry (Expected vs Desired vs Actual)

## Checkpoint Sequence

### C1. Scenario and Character Setup Baseline

Steps:
1. Open scenario editor and create or load a scenario with at least 2 characters.
2. Set persona default perspective mode.
3. Set each character perspective mode and base stats.
4. Save and reload scenario.

Expected (from code/spec):
- Scenario persists characters, perspective modes, and base stats.
- Reloaded scenario matches saved values.

Desired (confirm with user):
- Perspective and stat defaults should be stable and correctly seed RP session state.

Evidence to capture:
- Scenario payload before save and after reload
- UI fields before and after reload
- Any warnings/errors in logs

### C2. Session Create -> List -> Open(Start)

Steps:
1. Create session from RolePlay create screen with scenario and persona template.
2. Verify redirect to sessions list.
3. Verify row shows NotStarted and Start action.
4. Open session with Start.

Expected (from code/spec):
- New session created with NotStarted.
- Open Start transitions to InProgress when valid.
- Status mismatch should raise explicit service error with no partial mutation.

Desired (confirm with user):
- No confusing status transitions and no hidden failures.

Evidence to capture:
- Session row values in list
- Session payload before and after Start
- OpenSession logs

### C3. Unified Prompt Submit

Steps:
1. In workspace, submit Instruction intent without identity.
2. Submit Message intent with identity.
3. Submit Narrative intent with identity.

Expected (from code/spec):
- Instruction can submit without identity.
- Message and Narrative require identity.
- Interaction persisted and session modified timestamp updates.

Desired (confirm with user):
- Validation feedback should be clear and deterministic.

Evidence to capture:
- Submission payload and validation response
- Interaction list updates
- Continuation logs and debug events

### C4. Continue-As and Behavior Modes

Steps:
1. Switch behavior modes and verify allowed identities update.
2. Run continue-as for selected participants.
3. Attempt invalid participant/mode combination.

Expected (from code/spec):
- Identity availability obeys behavior mode rules.
- Invalid combinations are blocked by validator.

Desired (confirm with user):
- Mode changes should remain understandable and never silently discard intent.

Evidence to capture:
- Identity options list by mode
- ContinueAs request and response
- Validation errors for invalid combinations

### C5. Decision and Adaptive State

Steps:
1. Trigger decision point if available.
2. Apply decision option and inspect deltas.
3. Verify scenario candidate and phase transition events.

Expected (from code/spec):
- Decision application mutates adaptive state deterministically.
- Diagnostics include candidate evaluation and transition evidence when applicable.

Desired (confirm with user):
- Decision outcomes should clearly reflect selected option and not feel random.

Evidence to capture:
- Adaptive state snapshot before and after decision
- Decision and phase logs
- Session interaction context around decision

### C6. Delete and Integrity

Steps:
1. Delete session from sessions list.
2. Refresh list and attempt reopen by id.
3. Verify removal from persistence and cache.

Expected (from code/spec/checklist):
- Delete is irreversible and removes session from cache and persistence.
- Deleted session cannot be reopened.

Desired (confirm with user):
- Delete behavior is explicit, safe, and leaves no ghost data.

Evidence to capture:
- List before and after deletion
- Persistence query or service result
- Errors when attempting reopen

## Failure Injection Probes

### F1. Provider Timeout/Failure
- Induce provider timeout or unavailable provider during continuation.
- Verify error surfacing, no corrupt partial interaction, and recoverability on next submit.

### F2. Narrative Validation Retry Path
- Submit prompt likely to fail narrative validator.
- Verify retry behavior and final failure handling is deterministic.

### F3. Rapid Submissions
- Attempt burst submissions.
- Verify no state corruption and deterministic final interaction order.

### F4. Invalid Decision Option
- Apply invalid option id (if possible through controlled request path).
- Verify clear failure and no silent state drift.

## Reporting Contract

For each checkpoint and failure probe, produce one mismatch row in the run report using the template in:
- specs/001-roleplay-session-screens/checklists/rp-e2e-run1-template.md

Severity levels:
- Critical: blocks core flow or causes data corruption
- High: major functional break or misleading behavior
- Medium: wrong behavior with workaround
- Low: minor mismatch or UX clarity issue
