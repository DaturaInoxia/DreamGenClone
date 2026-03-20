# Feature Specification: Role-Play Session Screen Separation

**Feature Branch**: `001-roleplay-session-screens`  
**Created**: 2026-03-17  
**Status**: Draft  
**Input**: User description: "for the role play mode move the create session to a separate screen, after create a session it will show all the saved sessions on another screen, from there the session can be started or continued, this allows interactions and commands to be on one page on their own allowing for more space. Saved sessions can be deleted"

## Clarifications

### Session 2026-03-17

- Q: How should the system determine whether a saved session shows Start or Continue? -> A: Use an explicit persisted status field (`NotStarted` or `InProgress`) to drive action label and behavior.
- Q: Which metadata must each saved-session row display? -> A: Title, Status, Interaction Count, and Last Updated.
- Q: What delete model should saved role-play sessions use? -> A: Hard delete session and all associated interaction history immediately after confirmation.
- Q: Where should session delete actions be available? -> A: Delete action is only available on the saved-sessions page.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create Session Separately and Land on Saved Sessions (Priority: P1)

As a role-play user, I can create a new role-play session from a dedicated creation screen, and after creation I am taken to a saved-sessions screen so I can choose what to do next.

**Why this priority**: This establishes the new navigation model and removes creation controls from the interaction workspace, which is the core requested change.

**Independent Test**: Can be fully tested by creating a role-play session and confirming automatic navigation to a saved-sessions list that includes the new session.

**Acceptance Scenarios**:

1. **Given** a user is on the role-play create-session screen, **When** they submit valid session setup data, **Then** a new role-play session is saved and the user is taken to the saved-sessions screen.
2. **Given** a new session has just been created, **When** the saved-sessions screen loads, **Then** the newly created session appears in the list with enough identifying information to distinguish it.

---

### User Story 2 - Start or Continue from Saved Sessions (Priority: P1)

As a role-play user, I can start a new session or continue an existing session from a dedicated saved-sessions screen, so the interaction and command experience stays focused on a separate workspace page.

**Why this priority**: This delivers the requested split between session management and interaction execution, directly improving available workspace for interactions and commands.

**Independent Test**: Can be fully tested by opening the saved-sessions screen, selecting a listed session, and confirming navigation to a dedicated interaction page where session management controls are not the primary focus.

**Acceptance Scenarios**:

1. **Given** one or more role-play sessions exist, **When** the user selects "Start" for a session that has not begun, **Then** the app opens that session on the dedicated interaction page.
2. **Given** one or more role-play sessions exist, **When** the user selects "Continue" for a previously active session, **Then** the app opens that session on the dedicated interaction page with prior conversation context intact.
3. **Given** the user is on the dedicated interaction page, **When** they interact with the role-play workflow, **Then** commands and interactions are presented in a layout optimized for interaction space rather than session management.

---

### User Story 3 - Delete Saved Role-Play Sessions (Priority: P2)

As a role-play user, I can delete saved sessions from the saved-sessions screen so I can remove sessions I no longer want.

**Why this priority**: Session cleanup is important for long-term usability, but it is secondary to delivering the primary navigation and layout split.

**Independent Test**: Can be fully tested by deleting a saved session from the list and verifying it no longer appears or can be opened.

**Acceptance Scenarios**:

1. **Given** the saved-sessions screen shows at least one session, **When** the user confirms deletion for a selected session, **Then** that session is permanently removed from the saved-sessions list.
2. **Given** a deletion confirmation is presented, **When** the user cancels deletion, **Then** the session remains unchanged and available.

### Edge Cases

- User creates a session with missing required input; creation is blocked and clear validation feedback is shown on the create-session screen.
- No saved role-play sessions exist; the saved-sessions screen shows an empty-state message and a clear path back to create a session.
- User attempts to open or delete a session that was removed by another action shortly before selection; the app shows a recovery message and refreshes the list.
- User is on the role-play interaction page and attempts to delete from there; no delete action is presented and the user must return to saved-sessions to delete.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a dedicated role-play create-session screen that is separate from the role-play interaction workspace.
- **FR-002**: After successful role-play session creation, the system MUST navigate users to a dedicated saved-sessions screen.
- **FR-003**: The saved-sessions screen MUST list all saved role-play sessions with identifying metadata sufficient for users to choose the correct session.
- **FR-003a**: Each saved-session row MUST display Title, Status, Interaction Count, and Last Updated.
- **FR-004**: The saved-sessions screen MUST allow users to start a newly created session and continue a previously active session.
- **FR-004a**: Start versus Continue behavior MUST be determined by an explicit persisted role-play session status field (`NotStarted` or `InProgress`) rather than inferred interaction count.
- **FR-005**: Selecting start or continue from the saved-sessions screen MUST open a dedicated role-play interaction page focused on commands and interactions.
- **FR-006**: The dedicated role-play interaction page MUST keep session creation and saved-session management actions out of the primary interaction area to preserve interaction space.
- **FR-007**: The saved-sessions screen MUST allow users to delete a saved role-play session.
- **FR-007a**: Delete actions for role-play sessions MUST be exposed only on the saved-sessions screen and not on the dedicated interaction page.
- **FR-008**: Deleting a saved session MUST require explicit user confirmation before permanent removal.
- **FR-009**: Once deletion is confirmed, the deleted session MUST no longer be available to open or continue.
- **FR-009a**: Confirmed deletion MUST perform irreversible hard deletion of the role-play session and all associated interaction history.
- **FR-010**: If create, list, open, continue, or delete actions fail, the system MUST present actionable user-facing error feedback and keep session data consistent.
- **FR-011**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store.
- **FR-012**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-013**: Major execution paths across components and services for create/list/open/continue/delete flows MUST emit Information-level logs and actionable failure logs.
- **FR-014**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities *(include if feature involves data)*

- **Role-Play Session**: A saved role-play conversation container with identity, title/label, creation timestamp, last-updated timestamp, explicit status (`NotStarted` or `InProgress`), and interaction history reference.
- **Session List Item**: A user-facing summary projection of a role-play session used on the saved-sessions screen with Title, Status, Interaction Count, and Last Updated to support start, continue, and delete decisions.
- **Session Deletion Request**: A user-confirmed intent to irreversibly hard-delete one saved role-play session and all associated interaction history.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At least 95% of users can create a role-play session and reach the saved-sessions screen in under 30 seconds.
- **SC-002**: At least 95% of users can open a chosen saved session from the list (start or continue) and reach the interaction page in under 10 seconds.
- **SC-003**: At least 95% of confirmed deletion actions remove the selected session from the visible list within 2 seconds.
- **SC-004**: In usability checks, at least 90% of participants report that the dedicated interaction page provides clearer focus and sufficient space for role-play commands and interactions.

## Dependencies and Assumptions

- Existing role-play session persistence and retrieval capabilities remain available for reuse.
- Existing role-play interaction behavior remains unchanged except for navigation and layout separation.
- Deletion is irreversible hard delete and applies to the saved role-play session plus its associated interaction history.
- The feature targets the current single local user model already defined for this project.
