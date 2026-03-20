# Feature Specification: Role Play Continue Workspace Refresh

**Feature Branch**: `001-roleplay-continue-workspace`  
**Created**: 2026-03-19  
**Status**: Draft  
**Input**: User description: "the next feature is updating the role play continue workspace. use the images in the UITemplate folder as the basis for the updates. The existing continue as and message are being combined into one prompt text input with popups for options and to choose intended commands. A settings space will be created on the right side that is sizeable, it will have the behaviour mode in it. Make sure to implement the logic to execute the correct continue as and message or instruction commands. The available choices should be the characters in the scene and the persona, keep the custom character."

## Assumptions

- The updated workspace replaces separate "Continue As" and "Message" entry controls with a single prompt input experience.
- "Persona" is always available as a selectable identity alongside scene characters.
- "Custom character" remains available and behaves as an explicit selectable option.
- Existing continue-message and instruction command families remain valid and must be routed correctly based on the user-selected intended command.
- Visual direction and interaction behavior should follow the reference images in `specs/UITemplate`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Compose Continuation Prompt In One Place (Priority: P1)

As a role-play user, I can use one prompt box with contextual popups to pick who I am continuing as and what command intent I am sending, so I can continue the scene without switching between separate controls.

**Why this priority**: This is the core workflow change and directly affects every continue action.

**Independent Test**: Can be fully tested by entering a prompt, selecting identity and command intent from popups, and confirming the continuation executes in one flow.

**Acceptance Scenarios**:

1. **Given** the continue workspace is open, **When** the user focuses the unified prompt input, **Then** command and identity selection popups are available from that same input flow.
2. **Given** the user selects a character identity and a message-style command, **When** they submit the prompt, **Then** the scene continues using the selected identity and prompt text.
3. **Given** the user selects an instruction-style command, **When** they submit the prompt, **Then** the instruction pathway is used instead of the message pathway.

---

### User Story 2 - Choose From Scene Characters, Persona, Or Custom Character (Priority: P1)

As a role-play user, I can choose from scene characters, Persona, or Custom Character as the continuation identity, so I can direct narration and dialogue from the intended perspective.

**Why this priority**: Identity selection determines who is speaking/acting and is required for correct role-play behavior.

**Independent Test**: Can be fully tested by running one continuation each for scene character, Persona, and Custom Character and verifying each is executed with the selected identity.

**Acceptance Scenarios**:

1. **Given** a scene with multiple characters, **When** the user opens the identity popup, **Then** all current scene characters plus Persona and Custom Character are listed as selectable choices.
2. **Given** the user selects Persona, **When** they submit the prompt, **Then** the continuation executes under Persona.
3. **Given** the user selects Custom Character, **When** they submit the prompt, **Then** the continuation executes under Custom Character without removing existing custom-character capability.

---

### User Story 3 - Adjust Behavior Mode In Resizable Right Settings Panel (Priority: P2)

As a role-play user, I can open and resize a right-side settings panel that includes behavior mode controls, so I can tune continuation behavior while keeping the prompt workflow visible.

**Why this priority**: Behavior controls are important but secondary to core prompt submission.

**Independent Test**: Can be fully tested by resizing the right settings space, changing behavior mode, then running a continuation to confirm the updated behavior mode is applied.

**Acceptance Scenarios**:

1. **Given** the continue workspace is open, **When** the user views the right side of the layout, **Then** a settings area is present and includes behavior mode controls.
2. **Given** the settings area is visible, **When** the user resizes it, **Then** the layout adapts while maintaining usable prompt and settings interactions.
3. **Given** the user changes behavior mode, **When** they submit a continuation prompt, **Then** the selected behavior mode is applied to that request.

### Edge Cases

- What happens when the scene has no named characters? The identity options still include Persona and Custom Character so continuation remains possible.
- How does the system handle a character being removed from the scene while the popup is open? The selection list refreshes and blocks submission with stale identity choices.
- What happens when a user submits without selecting a command intent? A default intent is applied consistently and shown before submission confirmation.
- What happens when the settings panel is resized to minimum or maximum bounds? The panel respects limits and keeps both prompt input and behavior mode controls operable.
- How does the system handle invalid prompt text (empty or whitespace only)? Submission is blocked with clear, actionable validation feedback.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The continue workspace MUST provide a single unified prompt text input that replaces separate Continue As and Message input flows.
- **FR-002**: The unified prompt experience MUST provide popup selection for identity and intended command from within the same interaction flow.
- **FR-003**: Identity choices MUST include all current scene characters, Persona, and Custom Character.
- **FR-004**: The system MUST preserve existing Custom Character capability in the updated workspace.
- **FR-005**: The workspace MUST include a right-side settings area that users can resize within defined usability bounds.
- **FR-006**: The right-side settings area MUST include behavior mode selection controls.
- **FR-007**: The system MUST apply the selected behavior mode to continuation requests submitted from the unified prompt.
- **FR-008**: The system MUST route each submission to the correct execution pathway based on selected intended command (continue-message vs instruction).
- **FR-009**: If command intent is not explicitly selected, the system MUST apply a consistent default intent and expose that default to the user before execution.
- **FR-010**: The system MUST prevent submission when prompt text is empty or whitespace-only and present a clear validation message.
- **FR-011**: The updated workspace MUST align with the interaction and layout expectations represented by the reference images in `specs/UITemplate`.

### Key Entities *(include if feature involves data)*

- **Continuation Prompt**: A user submission containing prompt text, selected identity, selected command intent, and active behavior mode.
- **Identity Option**: A selectable actor source for continuation, including scene characters, Persona, and Custom Character.
- **Command Intent**: The user-selected execution type that determines whether the submission is treated as a continue-message command or an instruction command.
- **Behavior Mode Selection**: The currently active behavior mode value from the right-side settings area that affects continuation behavior.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In usability testing, at least 90% of users complete a continuation from the updated workspace in one prompt submission flow without needing separate input screens.
- **SC-002**: At least 95% of continuation submissions execute with the identity the user selected (scene character, Persona, or Custom Character) with no identity mismatch.
- **SC-003**: At least 95% of submissions execute through the intended command pathway selected by the user, with no incorrect message-vs-instruction routing.
- **SC-004**: At least 90% of users report they can find and adjust behavior mode from the right-side settings area without guidance.
- **SC-005**: During acceptance testing on supported viewport sizes, users can resize the right settings area and still complete prompt submission without blocked controls.
