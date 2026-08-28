# Feature Specification: Multi-Encounter Climax Time-Skip — Two-Turn Split

**Feature Branch**: `001-split-time-skip`  
**Created**: 2026-06-24  
**Status**: Draft  
**Input**: User description: "Multi-Encounter Climax Time-Skip: Split Into Two-Turn Instructions"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Natural Scene Close-Out Before Time Advance (Priority: P1)

When the AI detects a narrative encounter boundary during a multi-encounter climax scenario, instead of rushing through both closing the current scene and jumping to a new scene in a single response, the system gives the current scene a dedicated turn to close out naturally. On the next continuation, the system then guides the model to advance time to a new moment.

**Why this priority**: This is the core behavioral change. The single combined instruction currently asks the model to do two opposing narrative actions in one response — wrap up AND start fresh. Splitting them produces noticeably better scene transitions, which is the primary user-facing value.

**Independent Test**: Start a multi-encounter climax scenario, let encounters progress until a boundary is detected, and observe that the AI first closes the current encounter in one response, then on the next Continue, advances to a new scene. The two actions happen in separate responses.

**Acceptance Scenarios**:

1. **Given** a multi-encounter climax scenario is active and an encounter boundary is detected, **When** the next Continue turn executes, **Then** the AI receives only a close-scene instruction and responds by naturally concluding the current encounter.
2. **Given** the close-scene turn has completed and the close-scene response is visible to the user, **When** the user triggers the next Continue, **Then** the AI receives only an advance-time instruction and responds by establishing a new ordinary-life scene at a different time or day.
3. **Given** the advance-time turn has completed, **When** the next Continue triggers, **Then** the system returns to normal continuation flow without time-skip instructions.

---

### User Story 2 - User Instruction During Time-Skip Does Not Interrupt the Flow (Priority: P2)

If the user types an instruction (custom narrative direction) while a time-skip phase is pending, the system defers the time-skip injection rather than dropping it. The time-skip phase resumes on the next Continue turn after the user's instruction has been processed.

**Why this priority**: Users frequently interject with custom instructions. Losing the time-skip state when a user provides direction would break multi-encounter flow and require manual correction.

**Independent Test**: Trigger a close-scene phase, then type a user instruction instead of Continue. On the next Continue, verify the close-scene instruction fires (not lost). Repeat for the advance-time phase.

**Acceptance Scenarios**:

1. **Given** the close-scene phase is pending, **When** the user submits a custom instruction, **Then** the system processes the user instruction normally and keeps the close-scene phase pending for the next Continue.
2. **Given** the advance-time phase is pending, **When** the user submits a custom instruction, **Then** the system processes the user instruction normally and keeps the advance-time phase pending for the next Continue.
3. **Given** a time-skip phase has been deferred multiple times due to consecutive user instructions, **When** the user finally triggers Continue without an instruction, **Then** the pending time-skip phase fires correctly.

---

### User Story 3 - Time-Skip Survives Session Interruptions (Priority: P2)

If the user closes the browser, navigates away, or the session ends between the two time-skip phases, the pending phase is preserved. When the user returns and continues the session, the system picks up exactly where it left off.

**Why this priority**: Sessions span multiple sittings. Losing the transitional state between visits would break the narrative flow and confuse users returning to a session.

**Independent Test**: Trigger close-scene, then close and reopen the session. Continue — verify advance-time fires. Repeat for mid-advance-time interruption.

**Acceptance Scenarios**:

1. **Given** the close-scene phase has completed and advance-time is pending, **When** the user closes the session and returns later, **Then** the advance-time phase is still pending and fires on the first Continue.
2. **Given** the close-scene phase is pending (not yet fired), **When** the user closes the session and returns later, **Then** the close-scene phase is still pending and fires on the first Continue.

---

### User Story 4 - Existing Sessions Migrate Gracefully (Priority: P3)

Any in-progress multi-encounter climax session that had the old single-instruction time-skip pending at the time of the update seamlessly transitions to the new two-phase behavior. The pending state is interpreted as the first phase (close-scene).

**Why this priority**: Existing users shouldn't experience broken sessions after the update. This is a one-time migration concern.

**Independent Test**: With an old session that has a pending time-skip, upgrade the system and continue the session. Verify close-scene fires (not the old combined instruction).

**Acceptance Scenarios**:

1. **Given** a session created before the two-phase update has a pending time-skip (old combined instruction), **When** the user continues after the update, **Then** the system treats the pending state as close-scene and injects only the close-scene instruction.

---

### Edge Cases

- **Both phases blocked by repeated user instructions**: The time-skip phase persists indefinitely until a Continue without user instruction allows injection. No timeout, no explicit cancel mechanism — the phase waits as long as needed. This is defer-only, matching the existing single-instruction time-skip behavior.
- **Close-scene response already implies a time advance**: The advance-time instruction still fires on the next turn. The model can either confirm the already-established transition or add additional time-skip context. The directive acts as guidance, not a forced action.
- **User navigates away mid-transition and never returns**: No adverse effects — the pending phase simply remains stored with the session.
- **Scenario mode changes between phases**: The time-skip phases are only injected during multi-encounter climax mode. If the mode changes, the pending phase is harmlessly ignored.
- **Encounter count doesn't double-count during time-skip turns**: The internal encounter interaction counter must not be inflated by the time-skip turns themselves, ensuring the next real encounter boundary is detected at the correct interaction count.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST split the multi-encounter climax time-skip into two sequential phases: a close-scene phase followed by an advance-time phase, each injected on a separate Continue turn.
- **FR-002**: System MUST inject only the close-scene directive on the first time-skip turn: guidance to naturally conclude the current encounter.
- **FR-003**: System MUST inject only the advance-time directive on the second time-skip turn: guidance to advance time to a new moment, different day or time, and establish ordinary life.
- **FR-004**: System MUST transition from close-scene phase to advance-time phase after the close-scene directive has been sent, and from advance-time phase to no-pending-phase after the advance-time directive has been sent.
- **FR-005**: System MUST defer time-skip injection (preserving the current phase) when a user-typed instruction is detected in the recent interaction history.
- **FR-006**: System MUST persist the current time-skip phase across session save/load cycles so the phase survives browser close, navigation, and application restart.
- **FR-007**: System MUST migrate any pre-existing pending time-skip state (from the old single-instruction design) to the new close-scene phase on upgrade.
- **FR-008**: System MUST NOT allow the time-skip close-scene phase to fire more than once per encounter boundary (it must not re-trigger mid-encounter).
- **FR-009**: System MUST return to normal continuation flow (no time-skip directives) after the advance-time phase completes.
- **FR-010**: System MUST NOT double-count interactions generated during time-skip turns when tracking encounter interaction counts for future boundary detection.
- **FR-011**: System MUST ensure the normal "new encounter start" prompt is not used during an advance-time retry (when the advance-time injection was skipped due to a user instruction), since the scene was already closed by the prior close-scene turn.
- **FR-012**: Persisted feature data MUST use SQLite.
- **FR-013**: Application logging MUST use Serilog with structured message templates and contextual properties.

### Key Entities

- **Time-Skip Phase**: Represents the current state of the two-phase time-skip flow. Has three states: no pending time-skip, close-scene pending, and advance-time pending. Persisted with the session's adaptive scenario state.
- **Encounter Boundary**: The narrative point at which the system detects a transition between encounters in a multi-encounter climax scenario. Triggers the time-skip phase to begin.

## Clarifications

### Session 2026-06-24

- Q: Can the user explicitly cancel a pending time-skip (beyond deferring it with instructions)? → A: No — defer-only. User instructions skip injection but the phase persists until the next plain Continue. No cancel UI, no auto-timeout. Matches existing single-instruction behavior.
- Q: Should the old single combined instruction remain available as a configurable option, or is it replaced entirely? → A: Replace entirely. The old combined instruction is removed. All multi-encounter climax themes always use the two-phase split with no configuration toggle.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In multi-encounter climax mode, scene close-out and time advance occur in two separate AI responses (not one combined response).
- **SC-002**: A pending time-skip phase survives a full session close and reopen cycle — the correct phase fires on the first Continue after returning.
- **SC-003**: When a user instruction is submitted during either time-skip phase, the phase is deferred (not dropped) and fires on the next Continue without user instruction.
- **SC-004**: Existing in-progress sessions with the old single-instruction time-skip pending continue working after the update, with close-scene firing as the first phase.
- **SC-005**: Encounter boundary detection continues to work correctly after time-skip phases complete — the third and subsequent encounters are detected at the expected interaction counts.

## Assumptions

- The existing multi-encounter climax mode and encounter boundary detection logic remain unchanged aside from the time-skip instruction split.
- The old single combined instruction behavior is permanently replaced (not kept as a configurable option). All multi-encounter climax themes use the two-phase split.
- The user-instruction detection window (checking recent interactions for user-typed instructions) retains its current size of 3 interactions.
- The time-skip phases only apply to multi-encounter climax mode (`ClimaxMode: multi-encounter`), not to other climax modes.
- The close-scene directive text is: "Close the current encounter naturally."
- The advance-time directive text is: "Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life."
