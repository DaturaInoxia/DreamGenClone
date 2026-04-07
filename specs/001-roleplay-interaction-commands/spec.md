# Feature Specification: RolePlay Interaction Commands

**Feature Branch**: `001-roleplay-interaction-commands`  
**Created**: 2026-04-04  
**Status**: Draft  
**Input**: User description: "Add per-interaction commands to the RolePlay workspace. Each interaction in a roleplay session gets a toolbar with state flags, content editing, deletion, retry/regeneration with sibling alternatives, and session forking."

## Assumptions

- Every interaction type (User, Npc, Custom, System) receives the same set of toolbar commands with no type-specific restrictions.
- Sibling alternatives are stored alongside original interactions in the same session data structure, linked by a parent reference. Only one alternative is displayed at a time; users navigate with previous/next arrows.
- There is no upper limit on the number of sibling alternatives an interaction can have.
- "Retry with model" shows only models that have been configured in the application's Model Manager, not a hardcoded list.
- "Retry as..." shows only characters and personas participating in the current roleplay session, plus a "Narrative" option that is always present regardless of session composition.
- Forking creates a new independent session with its own copy of interactions; the original session is not modified.
- Fork operations copy only the currently active/displayed alternative for each interaction, not all sibling alternatives. The forked session starts with a clean single version per interaction.
- The active alternative index (which sibling the user is currently viewing) persists with the session so reopening a session shows the same alternative the user last selected.
- Edit mode is an ephemeral UI state; no domain flag is needed to track whether an interaction is being edited.
- Context-window trimming prioritises pinned interactions: when the total context exceeds the model's token limit, non-pinned interactions are removed oldest-first while pinned interactions are retained.
- Reference mockup images are located in `specs/RolePlayInterationCommands/` for visual guidance only; they are not normative.
- During AI generation (retry, rewrite, make longer/shorter), the interaction displays a "generating..." indicator and retry/rewrite toolbar commands are disabled until generation completes, preventing concurrent retries on the same interaction.

## Clarifications

### Session 2026-04-04

- Q: What should the user see during AI generation for retry/rewrite operations? → A: Show a "generating..." indicator on the interaction and disable retry commands until generation completes.
- Q: What happens when a retry/rewrite AI generation fails? → A: Show an inline error message on the interaction, no sibling alternative is created, and retry commands become available again.
- Q: Should forking copy all sibling alternatives or only the active one? → A: Copy only the currently active/displayed alternative for each interaction (clean fork).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Toggle Interaction State Flags (Priority: P1)

As a user, I can mark any interaction as Excluded, Hidden, or Pinned using toggle controls so that I can control which interactions the AI sees and which the UI displays.

**Why this priority**: State flags directly govern what the AI receives as context and what the user sees in the timeline, making them foundational to all downstream commands.

**Independent Test**: Can be fully tested by toggling each flag on an interaction and verifying the flag icon/tooltip appears, the flag persists after session reload, and the AI context builder respects the flag (Excluded removed from AI context, Hidden removed from UI display, Pinned retained during context trimming).

**Acceptance Scenarios**:

1. **Given** an interaction with no flags set, **When** the user toggles Excluded, **Then** an Excluded icon appears on the interaction with the tooltip "This interaction is hidden from the AI", the interaction remains visible in the UI, and the interaction is omitted from AI context on the next generation.
2. **Given** an interaction with no flags set, **When** the user toggles Hidden, **Then** a Hidden icon appears on the interaction with the tooltip "This interaction is hidden from the user interface", the interaction is no longer rendered in the timeline, and the interaction continues to be included in AI context.
3. **Given** an interaction with no flags set, **When** the user toggles Pinned, **Then** a Pinned icon appears on the interaction with the tooltip indicating prioritisation, and the interaction is retained in AI context even when older non-pinned interactions are trimmed.
4. **Given** an interaction with Excluded toggled on, **When** the user toggles Excluded again, **Then** the Excluded icon is removed and the interaction resumes being included in AI context.
5. **Given** an interaction with multiple flags set, **When** the session is saved and reopened, **Then** all flag states are preserved exactly as set.

---

### User Story 2 - Delete Interactions (Priority: P1)

As a user, I can delete a single interaction or delete an interaction and everything below it so that I can remove unwanted content from the conversation.

**Why this priority**: Deletion is an irreversible destructive action that shapes the remaining session; getting it right is critical.

**Independent Test**: Can be fully tested by invoking delete on an interaction, confirming via the dialog, and verifying the interaction (and optionally all below) is removed from the session.

**Acceptance Scenarios**:

1. **Given** an interaction in the timeline, **When** the user selects Delete, **Then** a confirmation dialog appears with the warning "This can't be undone" and buttons Cancel, Delete + below, and Delete.
2. **Given** the delete confirmation dialog is shown, **When** the user clicks Delete, **Then** only the targeted interaction (and its sibling alternatives) is removed and all other interactions remain intact.
3. **Given** the delete confirmation dialog is shown, **When** the user clicks Delete + below, **Then** the targeted interaction, all its sibling alternatives, and all interactions positioned after it in the session are removed.
4. **Given** the delete confirmation dialog is shown, **When** the user clicks Cancel, **Then** no changes are made and the dialog closes.
5. **Given** the deleted interaction had sibling alternatives, **When** Delete is executed, **Then** all alternatives of that interaction are also removed.

---

### User Story 3 - Edit Interaction Content (Priority: P2)

As a user, I can edit the text content of any interaction inline so that I can correct mistakes or adjust wording without regenerating.

**Why this priority**: Inline editing is the simplest content mutation and does not depend on AI generation, making it a natural next step after flags and deletion.

**Independent Test**: Can be fully tested by entering edit mode on an interaction, modifying text, saving, and verifying the content is updated in-place and persists across session reload.

**Acceptance Scenarios**:

1. **Given** an interaction in normal view mode, **When** the user activates Make Edit, **Then** the interaction content becomes an editable text area and the toolbar switches to edit mode showing trash, kebab menu, undo, checkmark (save), and X (cancel) controls.
2. **Given** an interaction in edit mode, **When** the user modifies text and clicks the checkmark (save), **Then** the original content is replaced with the edited text and the interaction returns to normal view mode.
3. **Given** an interaction in edit mode, **When** the user clicks X (cancel), **Then** all edits are discarded, the original content is restored, and the interaction returns to normal view mode.
4. **Given** an interaction in edit mode, **When** the user clicks undo, **Then** the most recent edit within the current edit session is reverted.
5. **Given** an interaction whose content was edited and saved, **When** the session is reloaded, **Then** the saved edited content is displayed.

---

### User Story 4 - Retry and Regenerate Interactions (Priority: P2)

As a user, I can retry or regenerate any interaction using different models, characters, or custom instructions, with each result stored as a navigable sibling alternative.

**Why this priority**: Retry/regeneration is the primary mechanism for improving AI output quality and is the most complex command group, depending on Model Manager and identity services.

**Independent Test**: Can be fully tested by invoking any retry variant on an interaction, verifying a new sibling alternative is created, and navigating between alternatives with the arrow controls.

**Acceptance Scenarios**:

1. **Given** an interaction in the timeline, **When** the user selects Retry from the retry menu, **Then** the system re-generates the interaction using a model from the configured Model Manager models, creates a new sibling alternative, and displays it as the active alternative.
2. **Given** the retry menu is open, **When** the user expands "Retry with model", **Then** a submenu lists only the models configured in the application's Model Manager.
3. **Given** the "Retry with model" submenu is showing, **When** the user selects a specific model, **Then** the system re-generates the interaction using that model and creates a new sibling alternative.
4. **Given** the retry menu is open, **When** the user expands "Retry as...", **Then** a submenu lists all characters and personas in the current roleplay session plus "Narrative" as a permanent option.
5. **Given** the "Retry as..." submenu is showing, **When** the user selects a character, **Then** the system re-generates the interaction from that character's perspective and creates a new sibling alternative with the selected character's actor name.
6. **Given** an interaction in the timeline, **When** the user selects "Make longer", **Then** the system sends the current content to the AI with a "make longer" instruction and creates a new sibling alternative with the expanded content.
7. **Given** an interaction in the timeline, **When** the user selects "Make shorter", **Then** the system sends the current content to the AI with a "make shorter" instruction and creates a new sibling alternative with the condensed content.

---

### User Story 5 - Ask to Rewrite with Custom Instructions (Priority: P2)

As a user, I can provide free-text rewrite instructions to reshape an interaction's content according to specific creative direction.

**Why this priority**: Custom rewrite completes the retry command group by enabling open-ended creative control beyond the predefined retry options.

**Independent Test**: Can be fully tested by selecting Ask to rewrite, entering a custom instruction in the dialog, confirming, and verifying the rewritten sibling alternative reflects the instruction.

**Acceptance Scenarios**:

1. **Given** an interaction in the timeline, **When** the user selects "Ask to rewrite" from the retry menu, **Then** a modal dialog titled "Custom Rewrite Instruction" appears with a text input field, a Cancel button, and a Rewrite button.
2. **Given** the rewrite dialog is open, **When** the user enters an instruction (e.g. "Make Jack burst into tears of joy") and clicks Rewrite, **Then** the system sends the current interaction content plus the custom instruction to the AI and creates a new sibling alternative.
3. **Given** the rewrite dialog is open, **When** the user clicks Cancel, **Then** the dialog closes with no changes.
4. **Given** the rewrite dialog is open, **When** the instruction field is empty and the user clicks Rewrite, **Then** the system prevents submission and provides guidance to enter an instruction.

---

### User Story 6 - Navigate Sibling Alternatives (Priority: P2)

As a user, I can navigate between sibling alternatives of an interaction using previous/next arrow controls so that I can compare and select the best version.

**Why this priority**: Alternative navigation is the display mechanism for all retry/regeneration output; without it, alternatives are invisible.

**Independent Test**: Can be fully tested by creating multiple alternatives via retry, then navigating with arrow controls and verifying each alternative's content is displayed correctly.

**Acceptance Scenarios**:

1. **Given** an interaction with two or more sibling alternatives, **When** the interaction is rendered, **Then** previous (<) and next (>) arrow controls appear alongside the interaction.
2. **Given** an interaction showing alternative 2 of 5, **When** the user clicks the next arrow, **Then** alternative 3 is displayed and the previous content is hidden.
3. **Given** an interaction showing alternative 1 of 5, **When** the user clicks the previous arrow, **Then** no navigation occurs (already at the first alternative) or it wraps to the last alternative.
4. **Given** the user navigates to alternative 3 and then saves/reloads the session, **Then** alternative 3 is still the displayed alternative after reload.
5. **Given** an interaction with only one version (no alternatives), **When** the interaction is rendered, **Then** no arrow controls are shown.

---

### User Story 7 - Fork Session from Interaction (Priority: P3)

As a user, I can fork the conversation at any interaction point to create a branched session that preserves history above or below the fork point.

**Why this priority**: Forking enables non-destructive exploration of alternative story directions without losing the original conversation, but depends on all other commands functioning first.

**Independent Test**: Can be fully tested by selecting Fork Here & Above or Fork Here & Below on an interaction, verifying a new session is created with the correct subset of interactions, and confirming the original session is unchanged.

**Acceptance Scenarios**:

1. **Given** an interaction at position N in a session with M interactions, **When** the user selects "Fork Here & Above", **Then** a new session is created containing interactions 1 through N (inclusive) with the original session's metadata, and the original session remains unchanged.
2. **Given** an interaction at position N in a session with M interactions, **When** the user selects "Fork Here & Below", **Then** a new session is created containing interactions N through M (inclusive) with the original session's metadata, and the original session remains unchanged.
3. **Given** a fork is executed, **When** the new session is created, **Then** it records a reference to the original session as its parent.
4. **Given** a fork is executed, **When** the user is navigated to the new session, **Then** the forked interactions are displayed in context with no missing or duplicated content.

---

### Edge Cases

- User toggles Hidden on an interaction that is also Excluded. Both flags should coexist independently; the interaction is both hidden from UI and excluded from AI context.
- User deletes an interaction that has sibling alternatives; all alternatives must be removed with it.
- User deletes the first interaction in the session using "Delete + below"; all interactions are removed, leaving an empty session.
- User attempts to retry an interaction while another retry is already in progress for the same interaction; retry commands are disabled during generation so this is prevented at the UI level.
- User invokes "Retry as..." when the session has no characters (only the persona); "Narrative" must still appear as an option.
- User edits an interaction that has sibling alternatives; only the currently displayed alternative's content is edited.
- User forks at the very first interaction with "Fork Here & Above"; the new session contains only that single interaction.
- User forks at the very last interaction with "Fork Here & Below"; the new session contains only that single interaction.
- Session contains hidden interactions; fork operations must include hidden interactions in the copied subset.
- User navigates alternatives and then deletes the currently displayed alternative; the display should fall back to an adjacent alternative or the original.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a per-interaction toolbar visible on every interaction in the roleplay workspace.
- **FR-002**: Toolbar MUST be available on all interaction types: User, Npc, Custom, and System.
- **FR-003**: System MUST support an Excluded boolean flag per interaction that, when enabled, removes the interaction from AI context while keeping it visible in the UI.
- **FR-004**: System MUST display an icon with the hover tooltip "This interaction is hidden from the AI" when an interaction's Excluded flag is enabled.
- **FR-005**: System MUST support a Hidden boolean flag per interaction that, when enabled, hides the interaction from the user interface while continuing to include it in AI context.
- **FR-006**: System MUST display an icon with the hover tooltip "This interaction is hidden from the user interface" when an interaction's Hidden flag is enabled.
- **FR-007**: System MUST support a Pinned boolean flag per interaction that, when enabled, prioritises the interaction for retention in AI context during context-window trimming.
- **FR-008**: System MUST display an icon with an appropriate hover tooltip when an interaction's Pinned flag is enabled.
- **FR-009**: All three state flags (Excluded, Hidden, Pinned) MUST be independently toggleable and MUST coexist without conflict.
- **FR-010**: All state flag changes MUST persist when the session is saved and reloaded.
- **FR-011**: System MUST provide a Delete command that removes a single interaction and all its sibling alternatives after user confirmation.
- **FR-012**: System MUST provide a "Delete + below" command that removes the targeted interaction, all its sibling alternatives, and all interactions positioned after it in the session after user confirmation.
- **FR-013**: The delete confirmation dialog MUST display "This can't be undone" and provide Cancel, Delete + below, and Delete action buttons.
- **FR-014**: System MUST provide a Make Edit command that switches an interaction into inline edit mode with an editable text area.
- **FR-015**: In edit mode, the toolbar MUST display trash, kebab menu, undo, checkmark (save), and X (cancel) controls.
- **FR-016**: Saving an edit MUST replace the original interaction content in-place and exit edit mode.
- **FR-017**: Cancelling an edit MUST discard all changes and restore the original content.
- **FR-018**: System MUST provide a Retry command that re-generates the interaction content using a configured model and stores the result as a new sibling alternative.
- **FR-019**: System MUST provide a "Retry with model" command that presents a submenu of models from the configured Model Manager and re-generates using the user-selected model, storing the result as a new sibling alternative.
- **FR-020**: The "Retry with model" submenu MUST show only models currently configured in the application's Model Manager.
- **FR-021**: System MUST provide a "Retry as..." command that presents a submenu of characters and personas from the current roleplay session plus a permanent "Narrative" option, and re-generates the interaction from the selected character's perspective, storing the result as a new sibling alternative.
- **FR-022**: The "Retry as..." submenu MUST include "Narrative" regardless of the session's character composition.
- **FR-023**: System MUST provide a "Make longer" command that sends the current interaction content to the AI with a length-expansion instruction and stores the result as a new sibling alternative.
- **FR-024**: System MUST provide a "Make shorter" command that sends the current interaction content to the AI with a length-reduction instruction and stores the result as a new sibling alternative.
- **FR-025**: System MUST provide an "Ask to rewrite" command that opens a modal dialog titled "Custom Rewrite Instruction" with a free-text input field, Cancel, and Rewrite buttons.
- **FR-026**: The "Ask to rewrite" dialog MUST prevent submission when the instruction field is empty.
- **FR-027**: Upon confirming a rewrite, the system MUST send the current interaction content plus the custom instruction to the AI and store the result as a new sibling alternative.
- **FR-028**: All retry and rewrite operations MUST create sibling alternatives rather than replacing the original interaction content.
- **FR-028a**: During any AI generation operation (retry, rewrite, make longer, make shorter), the system MUST display a "generating..." indicator on the affected interaction and MUST disable all retry and rewrite toolbar commands for that interaction until generation completes.
- **FR-028b**: If an AI generation operation fails (timeout, connection error, empty response), the system MUST display an inline error message on the affected interaction, MUST NOT create a sibling alternative, and MUST re-enable retry and rewrite commands so the user can retry.
- **FR-029**: System MUST display previous (<) and next (>) navigation arrows when an interaction has two or more sibling alternatives.
- **FR-030**: Only one sibling alternative MUST be visible at a time; navigating changes which alternative is displayed.
- **FR-031**: There MUST be no upper limit on the number of sibling alternatives per interaction.
- **FR-032**: The currently active alternative index MUST persist with the session so that reopening the session displays the same alternative the user last viewed.
- **FR-033**: System MUST provide a "Fork Here & Above" command that creates a new branched session containing all interactions from the start up to and including the targeted interaction, copying only the currently active alternative for each interaction.
- **FR-034**: System MUST provide a "Fork Here & Below" command that creates a new branched session containing all interactions from the targeted interaction to the end of the session, copying only the currently active alternative for each interaction.
- **FR-035**: Forked sessions MUST record a reference to the original session as their parent.
- **FR-036**: Fork operations MUST NOT modify the original session.
- **FR-037**: When building AI context for generation, the system MUST exclude interactions with the Excluded flag enabled.
- **FR-038**: When building AI context for generation, the system MUST include interactions with the Hidden flag enabled.
- **FR-039**: When trimming context to fit within model token limits, the system MUST retain Pinned interactions and remove non-pinned interactions oldest-first.
- **FR-040**: When building AI context, the system MUST include only the currently active alternative for each interaction position (not all siblings).
- **FR-041**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store.
- **FR-042**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-043**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-044**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities

- **RolePlayInteraction (extended)**: Gains boolean state flags (IsExcluded, IsHidden, IsPinned), a parent reference for alternatives (ParentInteractionId), an alternative index for ordering among siblings (AlternativeIndex), and an active alternative index tracking which sibling is currently displayed (ActiveAlternativeIndex).
- **InteractionAlternative**: A sibling version of an interaction created by retry or rewrite commands, linked to its parent interaction by ParentInteractionId. Shares the same structure as RolePlayInteraction.
- **DeleteConfirmation**: Represents the user's choice in the delete dialog: Cancel, Delete (single), or Delete + below (cascade).
- **RewriteInstruction**: Represents a user-provided custom rewrite instruction submitted through the Ask to rewrite dialog.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can toggle any state flag (Excluded, Hidden, Pinned) on an interaction within 2 seconds and see the visual indicator update immediately.
- **SC-002**: Users can delete a single interaction or cascade-delete in under 5 seconds including confirmation dialog interaction.
- **SC-003**: Users can enter edit mode, modify text, and save within 10 seconds for a typical interaction.
- **SC-004**: Retry and rewrite operations produce a new sibling alternative that is immediately navigable without page reload.
- **SC-005**: Alternative navigation between siblings completes in under 1 second with no visible loading state for locally cached alternatives.
- **SC-006**: Fork operations create a new session within 5 seconds containing the correct subset of interactions with no data loss or duplication.
- **SC-007**: AI context assembly correctly excludes all Excluded interactions and retains all Pinned interactions during context trimming in 100% of generation requests.
- **SC-008**: Session reload preserves all flag states, alternative content, and active alternative selections with no data loss.
- **SC-009**: 90% of users can discover and use the retry menu commands on first encounter without external guidance.
