# Tasks: Prompt Viewer Tab on Interaction Info Modal

**Input**: Design documents from `/specs/001-prompt-viewer-tab/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/
**Tests**: Included — the plan's project structure includes a dedicated test file for the truncation helper.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: No new project structure needed — this is an additive feature on existing code. Verify the build is clean before starting.

- [x] T001 Verify clean build of the solution: `dotnet build DreamGenClone.sln` — confirm 0 errors before starting feature work

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core data model and helper that ALL user stories depend on. MUST be complete before any user story work begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T002 [P] Add `PromptText` nullable string property to `RolePlayInteraction` in `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs` — add `public string? PromptText { get; set; }` with XML doc comment explaining it stores the full LLM prompt with prior interactions block trimmed; null means "not captured"
- [x] T003 [P] Create `PromptTextTruncation` static helper class in `DreamGenClone.Web/Application/RolePlay/PromptTextTruncation.cs` — implement `TrimInteractionHistoryBlock(string fullPrompt, int edgeSize = 200)` pure function that locates the history block by its header marker (`"Recent interaction history — exact scene continuity."`), trims the block content to first N + last N characters with `"\n...\n"` separator, and returns the prompt unchanged if the block is missing, shorter than 2×N, or input is null/empty

**Checkpoint**: Foundation ready — `PromptText` property exists and truncation helper is available. User story implementation can now begin.

---

## Phase 3: User Story 1 — View the LLM prompt for any interaction (Priority: P1) 🎯 MVP

**Goal**: Add a scrollable "LLM Prompt" tab to the Interaction Info modal that displays the stored `PromptText` with copy-to-clipboard support and graceful null handling.

**Independent Test**: Manually set `PromptText` on an existing interaction in the database (via dbquery tool), open the Interaction Info modal for that interaction, and confirm the "LLM Prompt" tab shows the text in a scrollable monospace container with a copy button. Also verify an interaction with null `PromptText` shows the "No prompt data available" message.

### Implementation for User Story 1

- [x] T004 [US1] Add `SetPromptTab` method to `RolePlayWorkspace.razor` code-behind (set `_infoPopupTab = "prompt"`) in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [x] T005 [US1] Add "LLM Prompt" tab button to the Interaction Info modal tab bar in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` — position after the existing "Reasoning" tab button (around line 8187), always visible (not conditional on PromptText being non-null)
- [x] T006 [US1] Add LLM Prompt tab content block in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` — after the reasoning content block (after line 8291): when `PromptText` is non-empty, render in a scrollable monospace `<pre>` container with CSS class `rw-prompt-viewer`; when null/empty, render the message `"No prompt data available for this interaction."` in a styled info block
- [x] T007 [US1] Add copy-to-clipboard button above or beside the prompt text container in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` — use JS interop or existing clipboard pattern to copy full `PromptText` to clipboard
- [x] T008 [US1] Add CSS styles for `rw-prompt-viewer` class (scrollable, monospace, max-height constrained) in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor.css` or inline `<style>` block — follow existing `rw-` prefix convention

**Checkpoint**: User Story 1 is functional — the "LLM Prompt" tab is visible, displays prompt text (when manually set), shows "No prompt data available" for null, and supports copy-to-clipboard. Can be tested independently without real prompt capture.

---

## Phase 4: User Story 2 — Verify prompt truncation for storage efficiency (Priority: P2)

**Goal**: Implement and verify the truncation logic that trims the prior interactions block to first N + last N characters, preserving all other prompt sections in full.

**Independent Test**: Run unit tests for `PromptTextTruncation.TrimInteractionHistoryBlock` — verify long history blocks are trimmed to first 200 + last 200 chars with `"\n...\n"` separator, short blocks are unchanged, missing blocks are unchanged, and non-history sections are preserved in full.

### Tests for User Story 2

- [x] T009 [P] [US2] Write unit tests for truncation with long history block in `DreamGenClone.Tests/RolePlay/PromptTextTruncationTests.cs` — test that a prompt with a history block exceeding 400 chars is trimmed to first 200 + last 200 with `"\n...\n"` separator, and that the history block header marker is preserved
- [x] T010 [P] [US2] Write unit tests for truncation edge cases in `DreamGenClone.Tests/RolePlay/PromptTextTruncationTests.cs` — test: (a) history block shorter than 400 chars → unchanged, (b) history block header marker not found → unchanged, (c) null input → unchanged, (d) empty input → unchanged, (e) custom edgeSize parameter

### Implementation for User Story 2

- [x] T011 [US2] Verify truncation preserves non-history sections in full in `DreamGenClone.Tests/RolePlay/PromptTextTruncationTests.cs` — write a test with a prompt containing system preamble, scenario context, character descriptions, injected directives, and current turn instruction sections around the history block; assert all non-history sections are byte-identical before and after truncation

**Checkpoint**: User Story 2 is functional — truncation logic is verified by unit tests. The `PromptTextTruncation` helper correctly trims only the prior interactions block and preserves all other sections.

---

## Phase 5: User Story 3 — Prompt captured at creation time (Priority: P2)

**Goal**: Wire up `PromptText` capture at all interaction creation sites with best-effort failure handling. The prompt is captured synchronously at creation time, truncated for storage, and never retroactively populated for old interactions.

**Independent Test**: Run a roleplay continuation, then inspect the newly created `RolePlayInteraction` record (via dbquery tool or in-memory) and verify `PromptText` is populated with the truncated prompt. Also verify that if capture fails, the interaction is still created with null `PromptText` and a warning is logged.

### Implementation for User Story 3

- [x] T012 [US3] Wire up `PromptText` capture in `RolePlayContinuationService.ContinueAsync` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — after `BuildPromptAsync` returns (line 123), apply `PromptTextTruncation.TrimInteractionHistoryBlock(prompt)` and set `PromptText` on the `RolePlayInteraction` initializer (line 248); wrap in try/catch with Serilog warning on failure, leaving `PromptText` null on error
- [x] T013 [US3] Wire up `PromptText` capture in `RolePlayContinuationService.ContinueNarrativeAsync` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — same pattern: capture prompt after `BuildPromptAsync`, set `PromptText` on the interaction initializer (line 354), best-effort with try/catch
- [x] T014 [P] [US3] Wire up `PromptText` capture in `RolePlayEngineService` multi-actor overflow paths in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — set `PromptText` on interaction creation at lines 1271, 1457, 1687, 1722; the prompt variable is available from the `ContinueAsync` return value or needs to be passed through — verify the prompt is accessible at each site
- [x] T015 [P] [US3] Wire up `PromptText` capture in `InteractionRetryService` retry paths in `DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs` — set `PromptText` on retry-created interactions using the same truncation and best-effort pattern
- [x] T016 [US3] Verify no retroactive population for old interactions — ensure that `SessionService.LoadRolePlaySessionAsync` in `DreamGenClone.Web/Application/Sessions/SessionService.cs` does NOT attempt to populate `PromptText` on deserialized interactions; null `PromptText` from old sessions stays null (this is the default behavior since `string?` deserializes to null, but add an explicit comment documenting the invariant)

**Checkpoint**: User Story 3 is functional — all new interactions have `PromptText` populated (or null on best-effort failure). Old interactions remain null. The full end-to-end flow works: prompt built → truncated → captured on interaction → displayed in the "LLM Prompt" tab.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Logging, validation, and final verification across all user stories

- [x] T017 [P] Add Information-level Serilog log for prompt capture success in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — log with structured properties: `SessionId`, `InteractionId`, `PromptTextLength` (after truncation)
- [x] T018 [P] Add Warning-level Serilog log for prompt capture failure in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — log with structured properties: `SessionId`, `Exception`, `ExceptionMessage` when best-effort catch fires
- [ ] T019 Run quickstart.md validation — follow all steps in `specs/001-prompt-viewer-tab/quickstart.md` to verify the feature end-to-end
- [ ] T020 Build verification — run `dotnet build DreamGenClone.sln` and confirm 0 errors
- [ ] T021 Run unit tests — run `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "PromptText"` and confirm all truncation tests pass

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — verify clean build first
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
  - T002 (PromptText property) and T003 (truncation helper) can run in parallel
- **User Stories (Phase 3–5)**: All depend on Foundational phase completion
  - US1 (Phase 3) can start immediately after Foundational — UI tab is testable with manually-set data
  - US2 (Phase 4) can start immediately after Foundational — truncation tests are pure unit tests
  - US3 (Phase 5) depends on T003 (truncation helper) from Foundational — capture uses truncation
  - US1 and US2 can run in parallel (different files, no dependencies)
  - US3 should follow US2 (capture applies truncation, which US2 verifies)
- **Polish (Phase 6)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Depends on Foundational (T002). Can be tested independently with manually-set `PromptText`. No dependency on US2 or US3 for testing.
- **User Story 2 (P2)**: Depends on Foundational (T003). Independently testable via unit tests — no dependency on US1 or US3.
- **User Story 3 (P2)**: Depends on Foundational (T002, T003). Uses truncation helper from T003. After US3, the full end-to-end flow works (US1 tab shows real captured data).

### Within Each User Story

- Models/properties before services
- Services before UI
- Core implementation before integration
- Tests before or alongside implementation

### Parallel Opportunities

- **Phase 2**: T002 and T003 in parallel (different files)
- **Phase 3 + Phase 4**: US1 (UI tab) and US2 (truncation tests) in parallel — completely different files
- **Phase 5**: T014 and T015 in parallel (different files: `RolePlayEngineService.cs` vs `InteractionRetryService.cs`)
- **Phase 6**: T017 and T018 in parallel (same file but different log statements — coordinate)

---

## Parallel Example: User Story 1 + User Story 2

```bash
# After Foundational phase (T002, T003) is complete:

# Developer A: User Story 1 (UI tab)
Task: T004 — Add SetPromptTab method in RolePlayWorkspace.razor
Task: T005 — Add LLM Prompt tab button in RolePlayWorkspace.razor
Task: T006 — Add tab content block in RolePlayWorkspace.razor
Task: T007 — Add copy-to-clipboard in RolePlayWorkspace.razor
Task: T008 — Add CSS styles in RolePlayWorkspace.razor.css

# Developer B: User Story 2 (truncation tests) — in parallel
Task: T009 — Write truncation tests for long block in PromptTextTruncationTests.cs
Task: T010 — Write truncation edge case tests in PromptTextTruncationTests.cs
Task: T011 — Verify non-history sections preserved in PromptTextTruncationTests.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (verify clean build)
2. Complete Phase 2: Foundational (PromptText property + truncation helper)
3. Complete Phase 3: User Story 1 (UI tab)
4. **STOP and VALIDATE**: Manually set `PromptText` on an interaction via dbquery, open the modal, verify the tab displays it
5. Deploy/demo if ready — the UI tab is visible and functional

### Incremental Delivery

1. Setup + Foundational → Foundation ready (property + helper)
2. Add User Story 1 → UI tab works with manually-set data → Demo (MVP!)
3. Add User Story 2 → Truncation verified by unit tests → Storage efficiency confirmed
4. Add User Story 3 → Real prompt capture at all creation sites → Full end-to-end flow
5. Polish → Logging, validation, build verification

### Parallel Team Strategy

With two developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1 (UI tab in `RolePlayWorkspace.razor`)
   - Developer B: User Story 2 (truncation tests in `PromptTextTruncationTests.cs`)
3. After both complete:
   - Either developer: User Story 3 (capture wiring across service files)
4. Polish together

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- US1 is testable with manually-set data (no dependency on real capture)
- US2 is testable via pure unit tests (no dependency on UI or database)
- US3 wires up the real data pipeline that feeds US1's UI tab
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- No DB migration needed — `PromptText` auto-serializes in the existing `PayloadJson` JSON blob
