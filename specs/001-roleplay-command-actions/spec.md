# Feature Specification: Chat and Roleplay Command Actions

**Feature Branch**: `001-roleplay-command-actions`  
**Created**: 2026-03-25  
**Status**: Draft  
**Input**: User description: "Create the next feature for chat and roleplay commands and flow, with three main actions: Instruction, Message, and Narrative by Character, and Continue As by character plus Narrative."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Submit Story Instructions (Priority: P1)

As a user, I can switch the composer action type to Instruction and submit broad or specific AI direction that guides the overall story or the next few interactions.

**Why this priority**: Instruction commands control the immediate behavior of the conversation and are a core action shown in the flow controls.

**Independent Test**: Can be fully tested by selecting Instruction, entering text, submitting with the plus control, and verifying the system records it as an instruction event visible in the interaction history.

**Acceptance Scenarios**:

1. **Given** the composer is visible, **When** the user selects Instruction and submits non-empty text with the plus button, **Then** the submission is recorded and processed as an instruction action without requiring character selection.
2. **Given** an instruction is submitted, **When** the interaction is rendered, **Then** the instruction content is visible in the interaction timeline.
3. **Given** Instruction is selected, **When** the user attempts to submit with no meaningful text, **Then** the system prevents invalid submission and shows actionable guidance.

---

### User Story 2 - Direct Character Message and Narrative Expansion (Priority: P2)

As a user, I can choose Message or Narrative by Character mode, select a character, and provide text that the AI uses to generate character-specific output.

**Why this priority**: Character-directed message and narrative outputs are the primary roleplay authoring behaviors after instruction handling.

**Independent Test**: Can be fully tested by selecting Message or Narrative by Character, selecting a character identity, submitting text, and verifying the generated output reflects mode intent and selected character point of view.

**Acceptance Scenarios**:

1. **Given** the action selector is set to Message, **When** the user selects a character and submits guidance text, **Then** the AI generates a character response that follows the requested direction, tone, or mood for that character.
2. **Given** the action selector is set to Narrative by Character, **When** the user selects a character and submits a basic phrase or tone prompt, **Then** the AI expands that prompt into narrative from that character point of view.
3. **Given** the character selector includes predefined and custom options, **When** the user changes character selection, **Then** the current selection is clearly visible before submission.

---

### User Story 3 - Continue Conversation As Selected Participants (Priority: P3)

As a user, I can use Continue As controls to choose one or more participants (for example You, NPC, or custom selections) and optionally include narrative continuation so the roleplay can advance without manual message entry.

**Why this priority**: Continue As is a productivity flow that accelerates roleplay progression while preserving control over who speaks and whether narrative is included.

**Independent Test**: Can be fully tested by opening Continue As controls, selecting one or more participants and narrative inclusion, executing Continue, and validating generated output for each selected participant plus optional scene narrative.

**Acceptance Scenarios**:

1. **Given** Continue As controls are available, **When** the user selects multiple participants and executes Continue, **Then** the system generates one interaction output per selected participant from each participant point of view.
2. **Given** narrative inclusion is enabled in Continue As, **When** Continue is executed, **Then** the system also generates narrative that moves the story forward and describes scene or tone without using a specific character point of view.
3. **Given** the user chooses Clear in Continue As controls, **When** the action is confirmed, **Then** all Continue As selections are unselected with no retained selection state.
4. **Given** the user executes Continue from Continue As controls, **When** processing begins, **Then** the behavior matches the same continuation action available from the main chat overflow control.

---

### Edge Cases

- User submits while no action type is selected due to prior state corruption.
- User submits text containing only whitespace or line breaks.
- User switches to Instruction while a character is selected from a prior action mode.
- User selects a custom character value that conflicts with an existing predefined character label.
- Continue As is invoked with no participants selected.
- Continue As selection includes multiple participants and one unavailable participant.
- User rapidly toggles action type and presses submit, causing potential misrouting of intent.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow users to choose exactly one primary submission action type from Instruction, Message, or Narrative before submit processing.
- **FR-002**: System MUST classify each submission according to the currently selected action type at the exact moment submission is triggered.
- **FR-003**: System MUST require meaningful non-empty content for manual submissions and reject empty or whitespace-only input with clear recovery guidance.
- **FR-004**: Instruction action MUST NOT require character selection and MUST ignore any previously selected character context for processing.
- **FR-005**: Instruction submissions MUST be displayed in the interaction history as visible instruction events.
- **FR-006**: System MUST provide character selection options for character-scoped actions, including predefined identities and a custom identity path.
- **FR-007**: Message action MUST require a selected character and MUST generate character output that follows the user-provided direction, mood, or tone guidance.
- **FR-008**: Narrative by Character action MUST require a selected character and MUST expand the user's short tone or phrase prompt into fuller narrative from that character point of view.
- **FR-009**: System MUST preserve and display the currently selected character identity until the user changes or clears it.
- **FR-010**: System MUST support Continue As execution using selected participant scope (such as You, NPC, and custom participant selections).
- **FR-011**: When multiple Continue As participants are selected, system MUST generate separate continuation output for each selected participant point of view.
- **FR-012**: System MUST allow Continue As to include or exclude narrative continuation as an explicit user-controlled option.
- **FR-013**: When Continue As narrative is enabled, system MUST generate additional scene-advancing narrative that is not attributed to a specific character point of view.
- **FR-014**: System MUST provide a Clear action in Continue As controls that unselects all participant and narrative options with no retained selection.
- **FR-015**: Continue As Continue action MUST invoke the same continuation behavior as the main chat overflow continue control.
- **FR-016**: System MUST prevent ambiguous routing by ensuring each send/continue operation maps to one unambiguous flow intent.
- **FR-017**: System MUST persist action-mode and participant selection state for the active conversation context so users do not need to reselect between adjacent turns, except when Clear is explicitly executed.
- **FR-018**: System MUST record each command and continue event with enough context to audit the selected action type, selected participant scope, and outcome status.
- **FR-019**: System MUST provide user-facing error feedback when command processing fails and must preserve user input context for retry.

### Key Entities *(include if feature involves data)*

- **CommandAction**: Represents a user-triggered action type (Instruction, Message, Narrative by Character) and submission metadata.
- **CharacterSelection**: Represents selected identity context for character-scoped actions, including predefined or custom choice.
- **ContinueAsSelection**: Represents selected continuation participants, narrative inclusion option, and explicit clear/continue command state.
- **ConversationTurnEvent**: Represents a processed send or continue action, including timestamp, selected intent, and outcome state.

## Assumptions

- Instruction, Message, and Narrative are mutually exclusive primary action modes for a single manual submission.
- Continue As can be executed without manually typing a new message body.
- Character-scoped action modes require explicit character context before processing.
- Instruction content is submitted using the plus control and should be visible in interaction history.
- Continue As Continue behavior is functionally equivalent to the main chat overflow continue action.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At least 95% of users can select the intended action type and submit within 10 seconds on first attempt.
- **SC-002**: At least 95% of valid submissions are processed under the same action type and character scope the user selected at submit time.
- **SC-003**: At least 90% of Continue As operations produce continuation output aligned with selected participant scope and narrative option.
- **SC-004**: At least 90% of invalid submission attempts are corrected successfully by users within one additional attempt after feedback.
- **SC-005**: User testing reports at least 4 out of 5 average confidence in understanding the difference between Instruction, Message, Narrative, and Continue As controls.
