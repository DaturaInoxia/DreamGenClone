# Tasks: RolePlay Interaction Commands

**Input**: Design documents from `specs/001-roleplay-interaction-commands/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Not explicitly requested in the feature specification. Test tasks are omitted.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Domain model extensions and enum definitions needed by all stories

- [x] T001 Add IsExcluded, IsHidden, IsPinned, ParentInteractionId, AlternativeIndex, and ActiveAlternativeIndex properties to RolePlayInteraction in DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs
- [x] T002 [P] Create InteractionCommand enum with all command values (ToggleExcluded through ForkBelow) in DreamGenClone.Web/Domain/RolePlay/InteractionCommand.cs
- [x] T003 [P] Create InteractionFlag enum (Excluded, Hidden, Pinned) in DreamGenClone.Web/Application/RolePlay/InteractionFlag.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Context view helper and continuation service update that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T004 Create GetContextView extension method on RolePlaySession that filters out excluded interactions, selects only active alternatives per position, and returns an ordered list in DreamGenClone.Web/Domain/RolePlay/RolePlaySessionExtensions.cs
- [x] T005 Update BuildPromptAsync in RolePlayContinuationService to use GetContextView instead of direct session.Interactions.TakeLast(12), applying flag-aware filtering and pinned-priority trimming in DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs
- [x] T006 [P] Update CloneInteraction in RolePlayBranchService to copy IsExcluded, IsHidden, and IsPinned flags when cloning interactions in DreamGenClone.Web/Application/RolePlay/RolePlayBranchService.cs

**Checkpoint**: Foundation ready — context building respects flags and alternatives, user story implementation can begin

---

## Phase 3: User Story 1 — Toggle Interaction State Flags (Priority: P1) 🎯 MVP

**Goal**: Users can mark any interaction as Excluded, Hidden, or Pinned to control AI context and UI visibility

**Independent Test**: Toggle each flag on an interaction, verify icon/tooltip appears, flag persists after session reload, AI context builder respects the flag

### Implementation for User Story 1

- [x] T007 [US1] Create IInteractionCommandService interface with ToggleFlagAsync, UpdateContentAsync, DeleteInteractionAsync, and NavigateAlternativeAsync method signatures in DreamGenClone.Web/Application/RolePlay/IInteractionCommandService.cs
- [x] T008 [US1] Implement ToggleFlagAsync in InteractionCommandService that locates interaction by ID, toggles the specified flag, saves session, and logs the toggle in DreamGenClone.Web/Application/RolePlay/InteractionCommandService.cs
- [x] T009 [US1] Register IInteractionCommandService/InteractionCommandService in DI container in DreamGenClone.Web/Program.cs
- [x] T010 [US1] Add per-interaction toolbar to RolePlayWorkspace.razor with Excluded, Hidden, and Pinned toggle buttons showing appropriate icons and tooltips in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T011 [US1] Wire toolbar flag toggle buttons to call IInteractionCommandService.ToggleFlagAsync and refresh UI state in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T012 [US1] Add visual indicators (icons with tooltips: "This interaction is hidden from the AI", "This interaction is hidden from the user interface", pinned indicator) that display when flags are enabled in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T013 [US1] Update interaction rendering to hide interactions with IsHidden flag from the timeline display while preserving them in the data in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor

**Checkpoint**: User Story 1 — flag toggles work, icons/tooltips show, flags persist, AI context correctly excludes/includes based on flags

---

## Phase 4: User Story 2 — Delete Interactions (Priority: P1)

**Goal**: Users can delete a single interaction or delete an interaction and everything below it

**Independent Test**: Delete an interaction, confirm via dialog, verify removal. Delete + below, verify cascade removal. Cancel, verify no changes.

### Implementation for User Story 2

- [x] T014 [US2] Implement DeleteInteractionAsync in InteractionCommandService that finds target interaction, removes it plus alternatives (and optionally all below), saves session, and logs deletion count in DreamGenClone.Web/Application/RolePlay/InteractionCommandService.cs
- [x] T015 [US2] Add delete confirmation dialog component with "This can't be undone" warning and Cancel, Delete + below, Delete buttons to RolePlayWorkspace in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T016 [US2] Wire delete toolbar button to show confirmation dialog and call DeleteInteractionAsync based on user choice in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor

**Checkpoint**: User Story 2 — single delete and cascade delete work, confirmation dialog prevents accidental deletion

---

## Phase 5: User Story 3 — Edit Interaction Content (Priority: P2)

**Goal**: Users can edit text content of any interaction inline without regenerating

**Independent Test**: Enter edit mode, modify text, save — content updates in-place and persists. Cancel — original content restored.

### Implementation for User Story 3

- [x] T017 [US3] Implement UpdateContentAsync in InteractionCommandService that locates interaction, replaces Content with trimmed newContent, saves session, and logs the update in DreamGenClone.Web/Application/RolePlay/InteractionCommandService.cs
- [x] T018 [US3] Add edit mode state tracking (Dictionary<string, string> for pre-edit snapshots) and edit mode toolbar (trash, kebab, undo, save checkmark, cancel X) to RolePlayWorkspace in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T019 [US3] Implement Make Edit button that switches interaction content to editable textarea, save that calls UpdateContentAsync, cancel that restores snapshot, and undo within edit session in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor

**Checkpoint**: User Story 3 — inline editing works, save persists, cancel restores, undo within edit session

---

## Phase 6: User Story 4 — Retry and Regenerate Interactions (Priority: P2)

**Goal**: Users can retry or regenerate any interaction using different models, characters, or length instructions, with each result as a navigable sibling alternative

**Independent Test**: Invoke retry, verify new sibling alternative created and displayed. Retry with model, verify specified model used. Retry as character, verify new alternative has correct actor name.

### Implementation for User Story 4

- [x] T020 [US4] Create IInteractionRetryService interface with RetryAsync, RetryWithModelAsync, RetryAsAsync, MakeLongerAsync, MakeShorterAsync, and AskToRewriteAsync method signatures in DreamGenClone.Web/Application/RolePlay/IInteractionRetryService.cs
- [x] T021 [US4] Implement InteractionRetryService core: resolve target to original interaction, build context via GetContextView, assemble retry prompt with scenario/history/actor context, call ICompletionClient.GenerateAsync, create new RolePlayInteraction alternative, update parent ActiveAlternativeIndex, add to session, save, and log in DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs
- [x] T022 [US4] Implement RetryAsync (same actor/model) and RetryWithModelAsync (specified model via IModelResolutionService override) in InteractionRetryService in DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs
- [x] T023 [US4] Implement RetryAsAsync that regenerates as a different actor type, setting InteractionType and ActorName on the new alternative in DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs
- [x] T024 [US4] Implement MakeLongerAsync and MakeShorterAsync with appropriate rewrite instructions in InteractionRetryService in DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs
- [x] T025 [US4] Register IInteractionRetryService/InteractionRetryService in DI container in DreamGenClone.Web/Program.cs
- [x] T026 [US4] Add retry dropdown menu to per-interaction toolbar with Retry, Retry with model (submenu of Model Manager models via IRegisteredModelRepository), Retry as... (submenu of session characters via IRolePlayIdentityOptionsService + Narrative), Make longer, Make shorter, Ask to rewrite in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T027 [US4] Add generating state tracking (HashSet<string> of interaction IDs) that shows "generating..." indicator and disables retry commands during AI generation in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T028 [US4] Add inline error display on generation failure that shows error message, does not create alternative, and re-enables retry commands in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor

**Checkpoint**: User Story 4 — all retry variants create alternatives, model/character selection works, generating indicator shown, errors handled inline

---

## Phase 7: User Story 5 — Ask to Rewrite with Custom Instructions (Priority: P2)

**Goal**: Users provide free-text rewrite instructions to reshape an interaction's content

**Independent Test**: Select Ask to rewrite, enter instruction, confirm — rewritten alternative reflects the instruction. Empty instruction prevented.

### Implementation for User Story 5

- [x] T029 [US5] Implement AskToRewriteAsync in InteractionRetryService that takes user instruction, validates non-empty, builds prompt with instruction, and creates new alternative in DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs
- [x] T030 [US5] Add "Custom Rewrite Instruction" modal dialog with text input field, Cancel button, and Rewrite button (disabled when input empty) to RolePlayWorkspace in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T031 [US5] Wire Ask to rewrite menu item to open dialog and on confirm call AskToRewriteAsync with the user instruction in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor

**Checkpoint**: User Story 5 — custom rewrite dialog works, empty instruction prevented, rewritten alternative created

---

## Phase 8: User Story 6 — Navigate Sibling Alternatives (Priority: P2)

**Goal**: Users navigate between sibling alternatives using previous/next arrow controls

**Independent Test**: Create multiple alternatives via retry, navigate with arrows, verify each alternative displayed. Verify active index persists on reload. Verify no arrows when single version.

### Implementation for User Story 6

- [x] T032 [US6] Implement NavigateAlternativeAsync in InteractionCommandService that resolves to original, clamps ActiveAlternativeIndex by direction, saves session, and logs navigation in DreamGenClone.Web/Application/RolePlay/InteractionCommandService.cs
- [x] T033 [US6] Add previous (<) and next (>) arrow controls to interactions that have 2+ alternatives, showing current index (e.g., "2 of 5"), hidden when single version, in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T034 [US6] Wire arrow controls to call NavigateAlternativeAsync and update displayed content to the newly active alternative in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T035 [US6] Update interaction rendering to display only the active alternative (by matching AlternativeIndex to parent's ActiveAlternativeIndex) in the timeline in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor

**Checkpoint**: User Story 6 — alternative navigation works, correct content displayed, arrows appear/disappear correctly, active index persists

---

## Phase 9: User Story 7 — Fork Session from Interaction (Priority: P3)

**Goal**: Users fork the conversation at any interaction point to create a branched session preserving history above or below

**Independent Test**: Fork Above at position N, verify new session has interactions 1..N. Fork Below, verify N..M. Verify original session unchanged. Verify parent reference set.

### Implementation for User Story 7

- [x] T036 [US7] Add ForkAboveAsync and ForkBelowAsync method signatures to IRolePlayBranchService interface in DreamGenClone.Web/Application/RolePlay/IRolePlayBranchService.cs
- [x] T037 [US7] Implement ForkAboveAsync in RolePlayBranchService that loads source session, finds target interaction index, clones session with interactions 0..target (inclusive), copies only active alternatives (flattened to AlternativeIndex 0, ParentInteractionId null), preserves flags, sets ParentSessionId, saves, and logs in DreamGenClone.Web/Application/RolePlay/RolePlayBranchService.cs
- [x] T038 [US7] Implement ForkBelowAsync in RolePlayBranchService using same pattern but taking interactions from target..end (inclusive) in DreamGenClone.Web/Application/RolePlay/RolePlayBranchService.cs
- [x] T039 [US7] Add Fork Here & Above and Fork Here & Below options to the per-interaction toolbar menu in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T040 [US7] Wire fork menu items to call ForkAboveAsync/ForkBelowAsync and navigate user to the newly created forked session in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor

**Checkpoint**: User Story 7 — fork above/below create correct session subsets, original unchanged, parent reference set, navigation to new session works

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Logging consistency, edge case handling, and cross-story validation

- [x] T041 [P] Verify all service methods emit Information-level structured logs with contextual properties (SessionId, InteractionId, Flag, Command) per FR-042 and FR-043 across DreamGenClone.Web/Application/RolePlay/
- [x] T042 [P] Verify log levels are configurable via appsettings.json including Verbose diagnostics without code changes per FR-044 in DreamGenClone.Web/appsettings.json
- [x] T043 Handle edge case: user edits the currently active alternative (not the original) — ensure only the displayed alternative's content is modified in DreamGenClone.Web/Application/RolePlay/InteractionCommandService.cs
- [x] T044 Handle edge case: user deletes interaction that has sibling alternatives — ensure all alternatives of that interaction are removed in DreamGenClone.Web/Application/RolePlay/InteractionCommandService.cs
- [x] T045 Handle edge case: delete first interaction with Delete + below — results in empty session, verify session remains valid in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [x] T046 Handle edge case: fork at first/last interaction — verify Fork Above at first creates single-interaction session, Fork Below at last creates single-interaction session in DreamGenClone.Web/Application/RolePlay/RolePlayBranchService.cs
- [x] T047 Run quickstart.md validation — build, run, and verify all commands function end-to-end

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion (T001 specifically) — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Phase 2 completion
- **User Story 2 (Phase 4)**: Depends on Phase 2 and T007/T008 from Phase 3 (shared InteractionCommandService)
- **User Story 3 (Phase 5)**: Depends on Phase 2 and T007/T008 from Phase 3 (shared InteractionCommandService)
- **User Story 4 (Phase 6)**: Depends on Phase 2 completion
- **User Story 5 (Phase 7)**: Depends on Phase 6 (T021 InteractionRetryService core)
- **User Story 6 (Phase 8)**: Depends on Phase 3 (T007/T008 InteractionCommandService) and Phase 6 (alternatives must exist to navigate)
- **User Story 7 (Phase 9)**: Depends on Phase 2 completion
- **Polish (Phase 10)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (Flags)**: After Phase 2 — fully independent
- **US2 (Delete)**: After Phase 2 + IInteractionCommandService interface (T007) — can share service creation
- **US3 (Edit)**: After Phase 2 + IInteractionCommandService interface (T007) — can share service creation
- **US4 (Retry)**: After Phase 2 — independent (new service) 
- **US5 (Rewrite)**: After US4 (shares InteractionRetryService)
- **US6 (Navigate)**: After US4 (needs alternatives to exist) + IInteractionCommandService (T007)
- **US7 (Fork)**: After Phase 2 — independent (extends existing service)

### Within Each User Story

- Interfaces before implementations
- Implementations before DI registration
- Service layer before UI layer
- Core implementation before wiring

### Parallel Opportunities

- T002 and T003 can run in parallel with each other and after T001
- T004, T005, T006 — T005 depends on T004; T006 is parallel
- Within US4: T020 → T021 → T022+T023+T024 (parallel) → T025 → T026+T027+T028 (parallel)
- US1 and US4 can run in parallel (different services, different files)
- US7 can run in parallel with US4/US5/US6 (extends different service)

---

## Parallel Example: User Story 4

```text
# Step 1: Interface (sequential)
T020: Create IInteractionRetryService interface

# Step 2: Core implementation (sequential)
T021: Implement InteractionRetryService core

# Step 3: Method implementations (parallel — same file but independent methods)
T022: RetryAsync + RetryWithModelAsync
T023: RetryAsAsync
T024: MakeLongerAsync + MakeShorterAsync

# Step 4: DI registration (sequential)
T025: Register in Program.cs

# Step 5: UI wiring (parallel — different concerns in same file)
T026: Retry dropdown menu
T027: Generating state tracking
T028: Inline error display
```

---

## Implementation Strategy

### MVP First (User Stories 1 & 2 Only)

1. Complete Phase 1: Setup (T001–T003)
2. Complete Phase 2: Foundational (T004–T006)
3. Complete Phase 3: User Story 1 — Flags (T007–T013)
4. Complete Phase 4: User Story 2 — Delete (T014–T016)
5. **STOP and VALIDATE**: Toggle flags and delete interactions end-to-end
6. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 (Flags) + US2 (Delete) → Core interaction management (MVP)
3. US3 (Edit) → Inline content editing
4. US4 (Retry) + US5 (Rewrite) → AI regeneration with alternatives
5. US6 (Navigate) → Alternative browsing
6. US7 (Fork) → Session branching
7. Polish → Cross-cutting validation
8. Each story adds value without breaking previous stories
