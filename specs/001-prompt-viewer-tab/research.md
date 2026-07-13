# Research: Prompt Viewer Tab on Interaction Info Modal

**Branch**: `001-prompt-viewer-tab`  
**Date**: 2026-07-13  
**Status**: Complete — all unknowns resolved

## Research Tasks

### R1: RolePlayInteraction persistence model

**Question**: How is `RolePlayInteraction` persisted? Is it a separate table with per-column mapping, or serialized inside a JSON blob?

**Decision**: `RolePlayInteraction` is NOT mapped to its own DB table. It is a nested collection on `RolePlaySession` (`List<RolePlayInteraction> Interactions`), and the entire session is serialized via `System.Text.Json` into the `PayloadJson` TEXT column on the `Sessions` table.

**Rationale**: The project uses raw ADO.NET (`Microsoft.Data.Sqlite`) with ad-hoc `ALTER TABLE` migrations — no EF Core, no per-entity table mapping. Adding a new nullable `string?` property to `RolePlayInteraction` auto-serializes/deserializes with no schema change. Existing rows deserialize with `PromptText = null` (default for `string?`).

**Alternatives considered**:
- Separate `RolePlayInteractions` table with FK to `Sessions` — rejected: would require major refactoring of the entire persistence layer and session loading logic; far out of scope for this feature.
- Adding a `PromptText` TEXT column to `Sessions` table — rejected: `PromptText` is per-interaction, not per-session; the session blob already holds all interactions.

**Key files**:
- `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs` — domain entity
- `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs` — parent entity with `Interactions` list
- `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` — `Sessions` table schema (line 58)
- `DreamGenClone.Web/Application/Sessions/SessionService.cs` — serialize/deserialize logic (lines 42, 51)

---

### R2: Prompt build pipeline and where to capture the prompt

**Question**: Where is the final LLM prompt string available, and where should `PromptText` be set on the interaction?

**Decision**: The final prompt string is available in `RolePlayContinuationService.ContinueAsync` at line 123 (`var prompt = await BuildPromptAsync(...)`). The `RolePlayInteraction` is constructed at line 248. Set `PromptText = prompt` (after truncation) in the interaction object initializer at that location.

**Rationale**: This is the single point where the complete prompt exists as a string, right before it is sent to the LLM. The interaction is created immediately after the LLM response returns. Setting `PromptText` in the initializer is synchronous and requires no additional plumbing.

**Alternatives considered**:
- Capture in `RolePlayEngineService` (the caller) — rejected: the prompt variable is local to `ContinueAsync`; passing it back would require changing the method signature or return type.
- Capture via the `RolePlayDebugEventSink` `PromptBuilt` event — rejected: debug events are a diagnostic side-channel, not the authoritative data source; coupling persistence to debug events is fragile.
- Capture lazily on first view — rejected: violates FR-002 (must capture at creation time) and the prompt may not be reconstructable later.

**Additional capture sites** (same pattern, different code paths):
- `RolePlayContinuationService.ContinueNarrativeAsync` — interaction created at line 354
- `RolePlayContinuationService` multi-actor overflow paths — interaction created at lines 416, 1271, 1457, 1687, 1722 (in `RolePlayEngineService`)
- `InteractionRetryService` — retry-created interactions

**Key files**:
- `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — `ContinueAsync` (line 90), `BuildPromptAsync` (line 450), interaction creation (line 248)
- `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — multi-actor paths
- `DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs` — retry paths

---

### R3: Interaction history block identification within the prompt

**Question**: How is the "prior interactions block" (conversation context) identified within the assembled prompt string for truncation?

**Decision**: The prior interactions block is assembled in `BuildPromptAsync` at lines 805–809 with a recognizable header line: `"Recent interaction history — exact scene continuity. Session Memory below = summarized past events for long-term context:"` followed by lines in the format `"[{InteractionType}] {ActorName}: {Content}"`. The truncation logic should identify this block by its header marker and truncate the content lines that follow, preserving the header.

**Rationale**: The block has a distinct, stable header string that the truncation function can locate. The content is line-delimited interaction entries. Truncating to first N + last N characters of the content (not the header) preserves enough context to identify it as the history block while reducing storage.

**Alternatives considered**:
- Truncate the entire prompt string to first/last N — rejected: would destroy the non-history sections (system preamble, scenario context, character descriptions, directives) which are the primary diagnostic interest.
- Add explicit delimiters around the history block at build time — rejected: would require modifying the prompt builder output (which is sent to the LLM) and could affect model behavior; the truncation should be a post-build, storage-only operation.
- Truncate by line count instead of character count — rejected: user explicitly requested character-based truncation (first N + last N characters).

**Key files**:
- `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — lines 805–809 (history block assembly)
- `DreamGenClone.Web/Domain/RolePlay/RolePlaySessionExtensions.cs` — `GetContextView()` (line 50)

---

### R4: Interaction Info modal tab structure

**Question**: How are tabs structured in the Interaction Info modal, and how do we add a new tab?

**Decision**: The modal uses a custom tab implementation with CSS classes (`rw-modal-tabs`, `rw-tab`, `rw-tab-active`) and a string state field `_infoPopupTab` (default `"info"`). Existing tabs: "Info" (always shown) and "Reasoning" (conditionally shown when `ReasoningContent` is non-empty). Add a new "LLM Prompt" tab following the same pattern — a tab button, a `SetPromptTab()` method, and a conditional content block.

**Rationale**: The existing pattern is simple and consistent. The "LLM Prompt" tab should be conditionally shown when `PromptText` is non-null (same pattern as the Reasoning tab). The content block should use a scrollable, monospace-styled container (same as the reasoning content block) with an added copy-to-clipboard button.

**Alternatives considered**:
- Use a Blazor component library (MudBlazor, Bootstrap) for tabs — rejected: the project uses custom CSS tabs; introducing a library for one tab is unnecessary and inconsistent.
- Always show the tab (even when PromptText is null) with the "No prompt data available" message — rejected: the spec says to handle null gracefully with an informational message; showing the tab conditionally (like Reasoning) is cleaner, but the "No prompt data" message should still be shown when the tab is opened for an interaction without prompt data. Decision: always show the tab, display "No prompt data available" message when PromptText is null/empty. This is more discoverable than hiding the tab.

**Key files**:
- `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` — modal (lines 8174–8297), tab state (line 7269), tab methods (lines 7352–7354)

---

### R5: Truncation value of N

**Question**: What value should N be (first N + last N characters of the prior interactions block)?

**Decision**: N = 200 characters. This is enough to show the header line plus the first/last interaction entry, clearly identifying the block as the interaction history.

**Rationale**: The history block header is ~120 characters. With N=200, the user sees the header plus the beginning of the first interaction entry and the end of the last interaction entry — enough to confirm "this is the conversation history" without storing potentially thousands of characters of prior turns.

**Alternatives considered**:
- N = 50 — too short; would cut off the header itself.
- N = 500 — more context but unnecessary for identification; the user explicitly said "just enough to show the user it is the interaction history."
- N = 100 — marginal; might not include a full interaction entry line.

**Note**: N should be a named constant (not magic number) so it can be adjusted during implementation if needed.
