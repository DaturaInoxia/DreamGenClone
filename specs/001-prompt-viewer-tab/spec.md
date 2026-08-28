# Feature Specification: Prompt Viewer Tab on Interaction Info Modal

**Feature Branch**: `001-prompt-viewer-tab`  
**Created**: 2026-07-13  
**Status**: Draft  
**Input**: B-053 from project backlog — Prompt viewer tab on Interaction Info modal

## Clarifications

### Session 2026-07-13

- Q: Which part of the prompt is the "interaction history block" that gets truncated, and how is it identified? → A: The truncated block is the running list of prior roleplay session interactions (the conversation context), which is the majority of prompt size. All other prompt sections (system preamble, scenario context, character descriptions, injected directives, current turn instruction) are stored in full because they are the parts the user is most interested in inspecting.
- Q: How is the truncation boundary defined (sentence vs. character-based)? → A: Character-based, not sentence-based. Keep the first N characters and last N characters of the prior interactions block with a truncation indicator between them. N just needs to be enough to identify the block as the interaction history. No sentence parsing required.
- Q: What happens when prompt capture fails mid-build (exception during prompt construction)? → A: Persist the interaction with empty PromptText and log a warning. Prompt capture is best-effort and never blocks interaction creation — the AI response is preserved and the missing prompt is visible in the tab.
- Q: What is explicitly out of scope for this feature? → A: Out of scope: prompt editing, regeneration, diffing between interactions, and search/filter. In scope: read-only display, scroll, and copy-to-clipboard (copy is trivial and high-value for debugging).
- Q: What is the database migration strategy for the new PromptText column on existing data? → A: Nullable column, no default. NULL means "not captured" (old interactions), and new interactions always populate it. This aligns with FR-007 (no retroactive population) and the UI null-handling message.

## User Scenarios & Testing *(mandatory)*

### Out of Scope

The following are explicitly out of scope for this feature:
- Prompt editing or regeneration
- Prompt diffing between interactions
- Prompt search or filter within the prompt text
- Any write or modify operation on stored prompt text

In scope: read-only display, vertical scrolling, and copy-to-clipboard of the prompt text.

### User Story 1 — View the LLM prompt for any interaction (Priority: P1)

As a developer or power user debugging roleplay behavior, I want to open an interaction's detail modal and see the exact full prompt that was sent to the LLM for that turn, so I can understand why the AI responded the way it did and diagnose prompt injection issues.

**Why this priority**: This is the core value of the feature — without prompt visibility, debugging session behavior requires external capture tools or guesswork.

**Independent Test**: Can be fully tested by opening the Interaction Info modal for any completed interaction in an existing session and confirming a new "LLM Prompt" tab is present showing scrollable prompt text. This delivers immediate debugging value without any other feature dependency.

**Acceptance Scenarios**:

1. **Given** a roleplay session with at least one completed interaction, **When** the user opens the Interaction Info modal for that interaction, **Then** a new tab labeled "LLM Prompt" is visible alongside existing tabs (such as Details, Diagnostics, etc.).
2. **Given** the user has opened the Interaction Info modal and selected the "LLM Prompt" tab, **When** the prompt text is longer than the modal's visible area, **Then** the tab content area is vertically scrollable so the user can view the entire prompt.
3. **Given** the selected interaction was created before this feature was deployed (i.e., has no stored prompt text), **When** the user opens the "LLM Prompt" tab, **Then** a clear message is displayed indicating that no prompt data is available for this interaction.

---

### User Story 2 — Verify prompt truncation for storage efficiency (Priority: P2)

As a developer, I want the stored prompt text to be space-efficient by trimming the verbose interaction history section, so that database storage growth is minimized even on long-running sessions with many interactions.

**Why this priority**: Storage efficiency is a design constraint but not the primary user value. The P1 story must work first; this story ensures it works sustainably.

**Independent Test**: Can be tested by inspecting the stored prompt data for an interaction and verifying the prior interactions block contains only the first N and last N characters with a truncation marker. Does not require the modal UI to be implemented.

**Acceptance Scenarios**:

1. **Given** a prompt has been built for an interaction, **When** the prior interactions block exceeds 2×N characters in length, **Then** only the first N characters and last N characters of that block are stored, with the omitted middle content replaced by a truncation indicator (e.g., "\n...\n").
2. **Given** a prompt has been built for an interaction, **When** the prior interactions block is shorter than 2×N characters, **Then** the full block is stored without truncation (no unnecessary modification).
3. **Given** a prompt has been built and stored, **When** the stored PromptText is inspected, **Then** the system prompt preamble, scenario context, character descriptions, injected directives, and current turn instruction sections are stored in full (truncation applies only to the prior roleplay session interactions / conversation context block).

---

### User Story 3 — Prompt captured at creation time (Priority: P2)

As a developer, I want the prompt text to be captured at the moment the interaction is created (not retroactively or lazily), so that the stored prompt accurately reflects what was sent to the LLM.

**Why this priority**: Temporal accuracy is essential for debugging — if prompts are captured after modification or regenerated, they lose diagnostic value.

**Independent Test**: Can be tested by inspecting the `RolePlayInteraction` record immediately after a continuation completes and verifying the `PromptText` field matches the expected prompt structure.

**Acceptance Scenarios**:

1. **Given** a continuation request is processed, **When** a new `RolePlayInteraction` record is created, **Then** the `PromptText` field is populated synchronously at creation time with the prompt that was sent to the LLM.
2. **Given** an interaction already exists without a `PromptText` value (e.g., from a previous session), **When** the interaction is viewed, **Then** no attempt is made to retroactively reconstruct or populate the prompt text.

### Edge Cases

- **Old interactions**: What happens when an interaction was created before this feature was deployed and has no PromptText value? The "LLM Prompt" tab should display a clear "No prompt data available" message rather than an empty tab or error.
- **Short history block**: How does the system handle a prior interactions block shorter than 2×N characters? The full block should be stored without truncation — the trimming rule applies only when the block is long enough that trimming actually reduces size.
- **Extremely long prompts**: How does the system handle prompt text that exceeds reasonable storage limits? The truncation of the prior interactions block should keep overall stored size manageable; no additional hard truncation of other sections is required.
- **Empty prompt text**: What if the prompt text is empty due to a processing error? The UI should handle a null/empty value gracefully by showing the informational message rather than crashing or showing a blank screen.
- **Prompt build failure**: What happens when prompt construction or truncation throws an exception mid-build? The interaction is still persisted with an empty `PromptText` and a warning is logged. Prompt capture never blocks interaction creation — the AI response is preserved and the "LLM Prompt" tab shows the "No prompt data available" message.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST store the full prompt text on each `RolePlayInteraction` record via a new `PromptText` column.
- **FR-001a**: The `PromptText` column MUST be nullable with no default value. NULL semantically means "not captured" (applies to interactions created before this feature was deployed). New interactions MUST always populate the column (or leave it empty only on best-effort failure per FR-007a).
- **FR-002**: System MUST populate `PromptText` at prompt-build time, synchronously when the interaction is created, before the interaction is persisted.
- **FR-003**: System MUST trim the prior roleplay session interactions block (the running conversation context — i.e., the accumulated prior turns/interactions from the session that are injected into the prompt) within the stored prompt text to only the first N characters and last N characters, with a truncation indicator ("...") replacing the omitted middle content. N must be just enough to identify the block as the interaction history (exact value determined at planning). This block is the majority of prompt size. All other prompt sections (system preamble, scenario context, character descriptions, injected directives, current turn instruction) MUST be stored in full without modification — these are the sections of primary diagnostic interest.
- **FR-004**: System MUST add a scrollable "LLM Prompt" tab to the Interaction Info modal, positioned after any existing tabs such as Details or Diagnostics.
- **FR-005**: The "LLM Prompt" tab MUST display the prompt text in a monospace-styled, scrollable container to handle long prompts without layout issues.
- **FR-005a**: The "LLM Prompt" tab MUST include a copy-to-clipboard control so the user can copy the full prompt text for external analysis.
- **FR-006**: System MUST handle null or empty `PromptText` gracefully by displaying a clear informational message ("No prompt data available for this interaction") instead of showing an empty tab or throwing an error.
- **FR-007**: System MUST NOT attempt to retroactively populate `PromptText` for interactions that were created before this feature was deployed.
- **FR-007a**: Prompt capture MUST be best-effort — if prompt construction or truncation throws an exception, the interaction MUST still be persisted with an empty `PromptText`, a warning MUST be logged, and the error MUST NOT propagate to block interaction creation or the AI response.
- **FR-008**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store.
- **FR-009**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-010**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-011**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities

- **RolePlayInteraction**: Existing domain entity representing a single interaction (turn) in a roleplay session. A new `PromptText` column will be added to store the LLM prompt text captured at creation time. The column stores the full prompt with the interaction history block trimmed for storage efficiency.
- **InteractionInfoModal**: Existing UI component (modal dialog) that shows details about a specific interaction. A new "LLM Prompt" tab panel will be added alongside existing tabs to display the stored prompt text in a scrollable container.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can view the LLM prompt for any post-deployment interaction by opening the Interaction Info modal and clicking the "LLM Prompt" tab — no additional tools or steps required. Completion time under 5 seconds from modal open to prompt visible.
- **SC-002**: The stored prompt text for interactions with long prior-interactions blocks is reduced in size compared to storing the full history, with only the first N and last N characters preserved. For a typical session with 20+ interactions, less than 5% of sessions show measurable storage increase from this feature.
- **SC-003**: The prompt text stored for any interaction accurately reflects the actual prompt sent to the LLM at creation time — no reconstruction, no modification, no lazy loading.
- **SC-004**: Old interactions (pre-deployment) display a clear "No prompt data available" message rather than causing errors or showing misleading empty content.
