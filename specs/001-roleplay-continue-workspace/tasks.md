# Tasks: Role Play Continue Workspace Refresh

**Input**: Design documents from `/specs/001-roleplay-continue-workspace/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare workspace-specific UI scaffolding and traceability assets.

- [X] T001 Create UITemplate-to-implementation trace notes in specs/001-roleplay-continue-workspace/implementation-trace.md
- [X] T002 Create role-play workspace styling scaffold in DreamGenClone.Web/wwwroot/css/roleplay-workspace.css
- [X] T003 [P] Create role-play workspace interaction script scaffold in DreamGenClone.Web/wwwroot/js/roleplay-workspace.js
- [X] T004 Wire feature css/js includes for role-play workspace in DreamGenClone.Web/Components/App.razor

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared prompt-intent and routing primitives required by all user stories.

**⚠️ CRITICAL**: No user story tasks start until this phase is complete.

- [X] T005 Add unified prompt intent enum in DreamGenClone.Web/Domain/RolePlay/PromptIntent.cs
- [X] T006 Add unified prompt submission model in DreamGenClone.Web/Domain/RolePlay/UnifiedPromptSubmission.cs
- [X] T007 Add identity option model in DreamGenClone.Web/Domain/RolePlay/IdentityOption.cs
- [X] T008 Define prompt routing interface in DreamGenClone.Web/Application/RolePlay/IRolePlayPromptRouter.cs
- [X] T009 Implement deterministic prompt routing table in DreamGenClone.Web/Application/RolePlay/RolePlayPromptRouter.cs
- [X] T010 Add identity-options service contract in DreamGenClone.Web/Application/RolePlay/IRolePlayIdentityOptionsService.cs
- [X] T011 Implement identity-options resolver service in DreamGenClone.Web/Application/RolePlay/RolePlayIdentityOptionsService.cs
- [X] T012 Register new role-play services in DI container in DreamGenClone.Web/Program.cs
- [X] T013 [P] Add foundational routing/validation tests in DreamGenClone.Tests/RolePlay/RolePlayPromptRouterTests.cs

**Checkpoint**: Foundation ready; user story implementation can proceed.

---

## Phase 3: User Story 1 - Unified Prompt With Intent Routing (Priority: P1) 🎯 MVP

**Goal**: Replace split Continue As + Message flow with one prompt input and intended-command popup routing.

**Independent Test**: Submit prompt from one input with intent selection (`Message`, `Narrative`, `Instruction`) and verify correct command path is executed each time.

### Tests for User Story 1

- [X] T014 [P] [US1] Add unified prompt submit validation tests in DreamGenClone.Tests/RolePlay/RolePlayUnifiedPromptValidationTests.cs
- [X] T015 [P] [US1] Add intent-to-command routing behavior tests in DreamGenClone.Tests/RolePlay/RolePlayIntentRoutingTests.cs

### Implementation for User Story 1

- [X] T016 [US1] Extend role-play engine contract for unified prompt submission in DreamGenClone.Web/Application/RolePlay/IRolePlayEngineService.cs
- [X] T017 [US1] Implement unified prompt submission orchestration in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T018 [US1] Extend continuation service contract for intent-aware generation in DreamGenClone.Web/Application/RolePlay/IRolePlayContinuationService.cs
- [X] T019 [US1] Implement intent-aware continuation/instruction execution in DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs
- [X] T020 [US1] Refactor workspace compose section to single prompt input and intent popup in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [X] T021 [US1] Add workspace submit-ready and popup state styles in DreamGenClone.Web/wwwroot/css/roleplay-workspace.css
- [X] T022 [US1] Add workspace popup open/close and keyboard handling logic in DreamGenClone.Web/wwwroot/js/roleplay-workspace.js
- [X] T023 [US1] Add Information-level logs for unified prompt route selection/execution in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Scene Character/Persona/Custom Identity Selection (Priority: P1)

**Goal**: Provide identity options from scene characters + persona + custom character and preserve custom-character flow.

**Independent Test**: Open identity popup and verify scene characters, persona, and custom character are available; submit once for each and verify selected identity is used.

### Tests for User Story 2

- [X] T024 [P] [US2] Add identity option resolution tests in DreamGenClone.Tests/RolePlay/RolePlayIdentityOptionsTests.cs
- [X] T025 [P] [US2] Add custom-character selection and fallback tests in DreamGenClone.Tests/RolePlay/RolePlayCustomCharacterTests.cs

### Implementation for User Story 2

- [X] T026 [US2] Implement scene/persona/custom identity resolution rules in DreamGenClone.Web/Application/RolePlay/RolePlayIdentityOptionsService.cs
- [X] T027 [US2] Integrate behavior-mode filtering into identity resolution in DreamGenClone.Web/Application/RolePlay/RolePlayIdentityOptionsService.cs
- [X] T028 [US2] Update actor-name resolution for persona and custom identities in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T029 [US2] Replace static actor dropdown with identity popup list in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [X] T030 [US2] Add custom identity entry/clear interactions in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [X] T031 [US2] Add identity list visual states matching UITemplate references in DreamGenClone.Web/wwwroot/css/roleplay-workspace.css
- [X] T032 [US2] Add Information-level logs for identity selection source and final actor mapping in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs

**Checkpoint**: User Story 2 is independently functional and testable.

---

## Phase 5: User Story 3 - Resizable Right Settings Panel With Behavior Mode (Priority: P2)

**Goal**: Add right-side resizable settings space and ensure behavior mode changes affect subsequent submissions.

**Independent Test**: Resize settings panel, change behavior mode, and submit prompt; verify mode constraints and mode application are enforced.

### Tests for User Story 3

- [X] T033 [P] [US3] Add behavior-mode-at-submit enforcement tests in DreamGenClone.Tests/RolePlay/RolePlayBehaviorModeSubmitTests.cs
- [X] T034 [P] [US3] Add settings panel width bounds persistence tests in DreamGenClone.Tests/RolePlay/RolePlaySettingsPanelTests.cs

### Implementation for User Story 3

- [X] T035 [US3] Add workspace settings panel state model in DreamGenClone.Web/Domain/RolePlay/WorkspaceSettingsState.cs
- [X] T036 [US3] Refactor workspace layout to two-pane prompt + right settings panel in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [X] T037 [US3] Implement behavior mode control placement in right settings panel in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [X] T038 [US3] Implement resizable right-panel behavior and min/max constraints in DreamGenClone.Web/wwwroot/js/roleplay-workspace.js
- [X] T039 [US3] Add right-panel resize and settings visual styling in DreamGenClone.Web/wwwroot/css/roleplay-workspace.css
- [X] T040 [US3] Apply behavior mode snapshot on unified prompt submission path in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T041 [US3] Add Information-level logs for settings resize and behavior-mode application in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor

**Checkpoint**: User Story 3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final alignment, regression checks, and validation.

- [X] T042 [P] Update role-play workspace usage notes in specs/001-roleplay-continue-workspace/quickstart.md
- [X] T043 Execute full role-play regression tests in DreamGenClone.Tests/RolePlay/
- [X] T044 Run visual parity checklist against UITemplate references in specs/001-roleplay-continue-workspace/implementation-trace.md
- [X] T045 Verify structured logging coverage for new paths and configurable levels in DreamGenClone.Web/appsettings.Development.json

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): starts immediately.
- Foundational (Phase 2): depends on Setup completion; blocks all user stories.
- User stories (Phase 3-5): depend on Foundational completion.
- Polish (Phase 6): depends on completion of all target user stories.

### User Story Dependencies

- US1 (P1): starts after Phase 2; no dependency on US2/US3.
- US2 (P1): starts after Phase 2; can run in parallel with US1 but merges into same workspace UI.
- US3 (P2): starts after Phase 2 and after core workspace refactor points from US1 are merged.

### Within Each User Story

- Tests first (write and confirm they fail before implementation).
- Contracts/interfaces before orchestration.
- Orchestration before UI wiring.
- UI behavior before styling polish.
- Logging updates by the end of each story.

### Parallel Opportunities

- T003 can run in parallel with T002.
- T013 can run in parallel with T009-T012 once models/interfaces exist.
- US1 tests (T014, T015) can run in parallel.
- US2 tests (T024, T025) can run in parallel.
- US3 tests (T033, T034) can run in parallel.
- Polish tasks T042 and T045 can run in parallel before final test run.

---

## Parallel Example: User Story 1

- [X] T014 [P] [US1] Add unified prompt submit validation tests in DreamGenClone.Tests/RolePlay/RolePlayUnifiedPromptValidationTests.cs
- [X] T015 [P] [US1] Add intent-to-command routing behavior tests in DreamGenClone.Tests/RolePlay/RolePlayIntentRoutingTests.cs

## Parallel Example: User Story 2

- [X] T024 [P] [US2] Add identity option resolution tests in DreamGenClone.Tests/RolePlay/RolePlayIdentityOptionsTests.cs
- [X] T025 [P] [US2] Add custom-character selection and fallback tests in DreamGenClone.Tests/RolePlay/RolePlayCustomCharacterTests.cs

## Parallel Example: User Story 3

- [X] T033 [P] [US3] Add behavior-mode-at-submit enforcement tests in DreamGenClone.Tests/RolePlay/RolePlayBehaviorModeSubmitTests.cs
- [X] T034 [P] [US3] Add settings panel width bounds persistence tests in DreamGenClone.Tests/RolePlay/RolePlaySettingsPanelTests.cs

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate US1 independently using the spec independent test.
4. Demo/deploy MVP behavior.

### Incremental Delivery

1. Deliver US1 unified prompt routing.
2. Add US2 identity source expansion (scene/persona/custom).
3. Add US3 right-side settings and resizing behavior.
4. Execute Polish phase and quickstart validation.

### Team Parallel Strategy

1. Shared setup/foundation completed together.
2. Developer A: US1 orchestration + routing.
3. Developer B: US2 identity resolution + UI popup behavior.
4. Developer C: US3 settings panel/resizing.
5. Merge with regression tests and visual parity check.

---

## Notes

- All tasks follow strict checklist format: `- [ ] T### [P?] [US?] Description with file path`.
- `[US#]` labels are used only in user story phases.
- Test tasks are included because the feature artifacts explicitly require routing, identity, and behavior-mode coverage.
- Task ordering prioritizes deterministic routing and behavior-mode enforcement before UI polish.
