# Tasks: Chat and Roleplay Command Actions

**Input**: Design documents from /specs/001-roleplay-command-actions/
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/roleplay-command-actions-contract.md

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish shared domain artifacts used by all stories.

- [X] T001 Add command operation metadata model in DreamGenClone.Web/Domain/RolePlay/CommandOperationMetadata.cs
- [X] T002 [P] Add continuation request/response models in DreamGenClone.Web/Domain/RolePlay/ContinueAsRequest.cs and DreamGenClone.Web/Domain/RolePlay/ContinueAsResult.cs
- [X] T003 [P] Add deterministic participant ordering helper in DreamGenClone.Web/Domain/RolePlay/ContinueAsOrdering.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build shared validation and orchestration seams that block all user stories until complete.

- [X] T004 Create submit/continue validation interface in DreamGenClone.Web/Application/RolePlay/IRolePlayCommandValidator.cs
- [X] T005 Implement shared validator rules in DreamGenClone.Web/Application/RolePlay/RolePlayCommandValidator.cs
- [X] T006 [P] Register validator dependency and related options in DreamGenClone.Web/Program.cs
- [X] T007 [P] Extend engine contracts for batch continue and clear operations in DreamGenClone.Web/Application/RolePlay/IRolePlayEngineService.cs
- [X] T008 [P] Extend continuation contracts for deterministic participant sequencing in DreamGenClone.Web/Application/RolePlay/IRolePlayContinuationService.cs
- [X] T009 Add foundational validator coverage in DreamGenClone.Tests/RolePlay/RolePlayCommandValidatorTests.cs

**Checkpoint**: Foundation ready. User story implementation can now start.

---

## Phase 3: User Story 1 - Submit Story Instructions (Priority: P1) 🎯 MVP

**Goal**: Allow instruction submissions through the instruction flow without character selection and display them in interaction history.

**Independent Test**: Select Instruction, submit via plus control, and confirm visible instruction interaction plus empty-text validation.

### Tests for User Story 1

- [X] T010 [P] [US1] Add instruction-without-character validation tests in DreamGenClone.Tests/RolePlay/RolePlayUnifiedPromptValidationTests.cs
- [X] T011 [P] [US1] Add instruction-visibility timeline tests in DreamGenClone.Tests/RolePlay/RolePlayInstructionFlowTests.cs

### Implementation for User Story 1

- [X] T012 [US1] Update instruction submit wiring for plus control in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [X] T013 [US1] Add submission source and instruction fields in DreamGenClone.Web/Domain/RolePlay/UnifiedPromptSubmission.cs
- [X] T014 [US1] Enforce explicit instruction route handling in DreamGenClone.Web/Application/RolePlay/RolePlayPromptRouter.cs
- [X] T015 [US1] Ignore character context for instruction processing in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T016 [US1] Persist and render visible instruction interactions in DreamGenClone.Web/Domain/RolePlay/InteractionType.cs and DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs
- [X] T017 [US1] Add information/error logs for instruction lifecycle in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs and DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Direct Character Message and Narrative Expansion (Priority: P2)

**Goal**: Require character selection for Message and Narrative by Character, then generate output aligned to selected character POV and prompt guidance.

**Independent Test**: Submit Message and Narrative by Character with and without selected character and verify correct validation and generation behavior.

### Tests for User Story 2

- [X] T018 [P] [US2] Add intent-specific route tests for Message and Narrative by Character in DreamGenClone.Tests/RolePlay/RolePlayIntentRoutingTests.cs
- [X] T019 [P] [US2] Add character-required submit tests for Message/Narrative by Character in DreamGenClone.Tests/RolePlay/RolePlayUnifiedPromptValidationTests.cs

### Implementation for User Story 2

- [X] T020 [US2] Enforce character-required submit states for Message and Narrative by Character in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [X] T021 [US2] Validate character availability by action mode in DreamGenClone.Web/Application/RolePlay/RolePlayIdentityOptionsService.cs
- [X] T022 [US2] Implement Message direction and tone/mood generation behavior in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T023 [US2] Implement Narrative by Character expansion behavior in DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs and DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T024 [US2] Update action labels and rendering cues for character narrative outputs in DreamGenClone.Web/Domain/RolePlay/PromptIntent.cs and DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Continue Conversation As Selected Participants (Priority: P3)

**Goal**: Support multi-participant Continue As generation, optional non-character narrative progression, clear-all reset, and parity with overflow continue behavior.

**Independent Test**: Select multiple participants and optional narrative in Continue As, execute Continue, confirm one output per participant plus optional narrative, and verify Clear unselects all.

### Tests for User Story 3

- [X] T025 [P] [US3] Add multi-participant deterministic ordering tests in DreamGenClone.Tests/RolePlay/RolePlayContinueAsSelectionTests.cs
- [X] T026 [P] [US3] Add continue parity tests for popup and overflow entry points in DreamGenClone.Tests/RolePlay/RolePlayContinueParityTests.cs
- [X] T027 [P] [US3] Add clear-resets-all tests for participant and narrative selection state in DreamGenClone.Tests/RolePlay/RolePlayContinueClearTests.cs

### Implementation for User Story 3

- [X] T028 [US3] Implement batch continue request handling and clear semantics in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T029 [US3] Implement deterministic per-participant continuation generation in DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs
- [X] T030 [US3] Add Continue As multi-select participant controls and narrative toggle in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [X] T031 [US3] Enforce behavior-mode filtering for all selected participants in DreamGenClone.Web/Application/RolePlay/BehaviorModeService.cs
- [X] T032 [US3] Route popup Continue and overflow Continue through shared continuation path in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor and DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T033 [US3] Add structured logs for continue batch execution and clear actions in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs and DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor

**Checkpoint**: All user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final hardening, verification, and documentation updates.

- [X] T034 [P] Update roleplay verification flow in specs/001-roleplay-command-actions/quickstart.md
- [X] T035 Run roleplay regression suite and capture results in DreamGenClone.Tests/RolePlay/
- [X] T036 [P] Record final implementation trace in specs/001-roleplay-command-actions/implementation-trace.md
- [X] T037 Verify log-level configurability and structured log output docs in DreamGenClone.Web/appsettings.Development.json and DreamGenClone.Web/appsettings.json

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): No dependencies.
- Foundational (Phase 2): Depends on Setup completion and blocks all user stories.
- User Story phases (Phase 3-5): Depend on Foundational completion.
- Polish (Phase 6): Depends on completion of selected user stories.

### User Story Dependencies

- US1 (P1): Starts immediately after Foundational; no dependency on other user stories.
- US2 (P2): Starts after Foundational; independent of US1 behavior, but touches shared files.
- US3 (P3): Starts after Foundational; independent test flow, but touches shared workspace/engine files.

### Within Each User Story

- Tests first, then implementation.
- Validation/routing changes before UI wiring where possible.
- Story-specific logging before final checkpoint.

---

## Parallel Opportunities

- Setup: T002 and T003 can run in parallel.
- Foundational: T006, T007, and T008 can run in parallel after T004 starts.
- US1: T010 and T011 can run in parallel; T016 and T017 can run in parallel after T015.
- US2: T018 and T019 can run in parallel.
- US3: T025, T026, and T027 can run in parallel.
- Polish: T034 and T036 can run in parallel.

### Parallel Example: User Story 1

- Task: T010 Add instruction-without-character validation tests in DreamGenClone.Tests/RolePlay/RolePlayUnifiedPromptValidationTests.cs
- Task: T011 Add instruction-visibility timeline tests in DreamGenClone.Tests/RolePlay/RolePlayInstructionFlowTests.cs

### Parallel Example: User Story 2

- Task: T018 Add intent-specific route tests in DreamGenClone.Tests/RolePlay/RolePlayIntentRoutingTests.cs
- Task: T019 Add character-required submit tests in DreamGenClone.Tests/RolePlay/RolePlayUnifiedPromptValidationTests.cs

### Parallel Example: User Story 3

- Task: T025 Add multi-participant deterministic ordering tests in DreamGenClone.Tests/RolePlay/RolePlayContinueAsSelectionTests.cs
- Task: T026 Add continue parity tests in DreamGenClone.Tests/RolePlay/RolePlayContinueParityTests.cs
- Task: T027 Add clear-resets-all tests in DreamGenClone.Tests/RolePlay/RolePlayContinueClearTests.cs

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1 and Phase 2.
2. Complete all US1 tasks in Phase 3.
3. Validate US1 independent test criteria.
4. Demo/deploy MVP slice.

### Incremental Delivery

1. Foundation first (Phase 1-2).
2. Deliver US1, validate, then US2, validate, then US3.
3. Finish with Phase 6 hardening and verification.

### Parallel Team Strategy

1. Team completes Setup + Foundational together.
2. After Foundational:
   - Developer A: US1
   - Developer B: US2
   - Developer C: US3
3. Merge and run full roleplay regression.
