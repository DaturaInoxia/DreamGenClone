# Tasks: Adaptive Scenario Selection Engine Redesign 2

**Input**: Design documents from /specs/002-adaptive-scenario-redesign2/
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare feature scaffolding and wiring points used by all stories.

- [X] T001 Create feature task baseline notes in specs/002-adaptive-scenario-redesign2/quickstart.md
- [X] T002 Create scenario engine interface stubs in DreamGenClone.Application/StoryAnalysis/IScenarioSelectionEngine.cs, DreamGenClone.Application/StoryAnalysis/INarrativePhaseManager.cs, and DreamGenClone.Application/StoryAnalysis/IScenarioGuidanceContextFactory.cs
- [X] T003 [P] Register scenario engine service placeholders in DreamGenClone.Web/Program.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core domain/state infrastructure required before user story implementation.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Add narrative phase enum in DreamGenClone.Domain/StoryAnalysis/NarrativePhase.cs
- [X] T005 Add scenario completion model in DreamGenClone.Domain/StoryAnalysis/ScenarioMetadata.cs
- [X] T006 Extend adaptive state model for phase and scenario history in DreamGenClone.Web/Domain/RolePlay/RolePlayAdaptiveState.cs
- [X] T007 [P] Extend theme candidate tracking fields in DreamGenClone.Web/Domain/RolePlay/RolePlayAdaptiveState.cs
- [X] T008 Implement backward-compatible session serialization handling for new adaptive fields in DreamGenClone.Web/Application/Sessions/SessionService.cs
- [X] T009 Create infrastructure selection engine shell in DreamGenClone.Infrastructure/StoryAnalysis/ScenarioSelectionEngine.cs
- [X] T010 Create infrastructure phase manager shell in DreamGenClone.Infrastructure/StoryAnalysis/NarrativePhaseManager.cs
- [X] T011 [P] Create guidance context factory shell in DreamGenClone.Infrastructure/StoryAnalysis/ScenarioGuidanceContextFactory.cs
- [X] T012 Wire concrete service registrations in DreamGenClone.Web/Program.cs
- [X] T013 Add foundational deterministic state tests in DreamGenClone.Tests/StoryAnalysis/ScenarioStateModelTests.cs

**Checkpoint**: Foundation complete. User story phases can proceed.

---

## Phase 3: User Story 1 - Commit to a Single Scenario Direction (Priority: P1) MVP

**Goal**: Commit to one active scenario per cycle using fit scoring and tie deferral.

**Independent Test**: Run interaction sequences with competing candidates and verify single commitment, tie deferral, and non-active suppression behavior.

### Tests for User Story 1

- [X] T014 [P] [US1] Add fit-score ranking tests in DreamGenClone.Tests/StoryAnalysis/ScenarioSelectionEngineTests.cs
- [X] T015 [P] [US1] Add tie deferral threshold tests for delta <= 0.10 in DreamGenClone.Tests/StoryAnalysis/ScenarioSelectionEngineTests.cs
- [X] T016 [P] [US1] Add BuildUp to Committed gate tests (fit >= 0.60 and >=2 interactions) in DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceScenarioTests.cs

### Implementation for User Story 1

- [X] T017 [US1] Implement deterministic candidate fit scoring and ranking in DreamGenClone.Infrastructure/StoryAnalysis/ScenarioSelectionEngine.cs
- [X] T018 [US1] Implement tie deferral and additional-interaction re-evaluation policy in DreamGenClone.Infrastructure/StoryAnalysis/ScenarioSelectionEngine.cs
- [X] T019 [US1] Integrate selection engine into adaptive update flow in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [X] T020 [US1] Implement active scenario commitment persistence fields update in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [X] T021 [US1] Implement non-active scenario suppression after commitment in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [X] T022 [US1] Add Information-level commitment and ranking logs in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Progress Through a Complete Narrative Cycle (Priority: P2)

**Goal**: Enforce ordered narrative phases and semi-reset behavior across cycles.

**Independent Test**: Simulate full cycles and verify transitions, reset outcomes, and reset-first override semantics.

### Tests for User Story 2

- [X] T023 [P] [US2] Add Committed to Approaching threshold tests in DreamGenClone.Tests/StoryAnalysis/NarrativePhaseManagerTests.cs
- [X] T024 [P] [US2] Add Approaching to Climax threshold and interaction-count tests in DreamGenClone.Tests/StoryAnalysis/NarrativePhaseManagerTests.cs
- [X] T025 [P] [US2] Add semi-reset and cycle-history tests in DreamGenClone.Tests/StoryAnalysis/ScenarioResetCycleTests.cs
- [X] T026 [P] [US2] Add manual scenario override reset-first tests in DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceScenarioTests.cs

### Implementation for User Story 2

- [X] T027 [US2] Implement ordered phase transition evaluation rules in DreamGenClone.Infrastructure/StoryAnalysis/NarrativePhaseManager.cs
- [X] T028 [US2] Implement Committed to Approaching gate (score/desire/restraint/interactions) in DreamGenClone.Infrastructure/StoryAnalysis/NarrativePhaseManager.cs
- [X] T029 [US2] Implement Approaching to Climax gate and explicit climax override support in DreamGenClone.Infrastructure/StoryAnalysis/NarrativePhaseManager.cs
- [X] T030 [US2] Implement semi-reset state adjustment and scenario completion history writes in DreamGenClone.Infrastructure/StoryAnalysis/NarrativePhaseManager.cs
- [X] T031 [US2] Integrate phase manager transitions and reset execution in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [X] T032 [US2] Implement manual scenario override workflow (force Reset then BuildUp priority) in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [X] T033 [US2] Add Information-level phase transition and override logs in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs

**Checkpoint**: User Story 2 is independently functional and testable.

---

## Phase 5: User Story 3 - Receive Scenario-Specific Climax Guidance (Priority: P3)

**Goal**: Generate phase-aware and scenario-specific guidance during approaching and climax.

**Independent Test**: Compare prompt guidance across different active scenarios and verify contradictory framing is excluded.

### Tests for User Story 3

- [X] T034 [P] [US3] Add guidance context generation tests by phase in DreamGenClone.Tests/RolePlay/RolePlayContinuationScenarioGuidanceTests.cs
- [X] T035 [P] [US3] Add scenario-specific framing exclusion tests in DreamGenClone.Tests/RolePlay/RolePlayContinuationScenarioGuidanceTests.cs

### Implementation for User Story 3

- [X] T036 [US3] Implement phase-aware guidance context construction in DreamGenClone.Infrastructure/StoryAnalysis/ScenarioGuidanceContextFactory.cs
- [X] T037 [US3] Integrate guidance context factory into continuation pipeline in DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs
- [X] T038 [US3] Add phase/scenario guidance injection templates in DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs
- [X] T039 [US3] Apply committed/approaching/climax framing guards in DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs
- [X] T040 [US3] Add Information-level guidance generation logs in DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs

**Checkpoint**: User Story 3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validate end-to-end quality, docs, and readiness.

- [X] T041 [P] Add end-to-end deterministic replay coverage for multi-cycle sessions in DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceScenarioTests.cs
- [X] T042 [P] Validate quickstart workflow and update command notes in specs/002-adaptive-scenario-redesign2/quickstart.md
- [X] T043 Audit structured log field consistency across services in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs and DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs
- [X] T044 Update feature implementation notes and edge-case rationale in specs/002-adaptive-scenario-redesign2/research.md
- [X] T045 Run regression test pass for affected scope in DreamGenClone.Tests/StoryAnalysis and DreamGenClone.Tests/RolePlay

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): no dependencies.
- Foundational (Phase 2): depends on Setup and blocks all user stories.
- User Story phases (Phase 3-5): depend on Foundational completion.
- Polish (Phase 6): depends on completion of desired user stories.

### User Story Dependencies

- US1 (P1): starts after Foundational, no dependency on US2 or US3.
- US2 (P2): starts after Foundational and can integrate US1 state outcomes but remains independently testable.
- US3 (P3): starts after Foundational and can proceed after guidance interfaces from Phase 2 are in place.

### Within Each User Story

- Tests first, then implementation.
- Core state/model updates before service orchestration.
- Service orchestration before logging/documentation refinements.

## Parallel Opportunities

- T003 can run in parallel with T001-T002.
- T007, T011, and T013 can run in parallel after T004-T006.
- US1 tests T014-T016 can run in parallel.
- US2 tests T023-T026 can run in parallel.
- US3 tests T034-T035 can run in parallel.
- Polish tasks T041-T042 can run in parallel.

## Parallel Example: User Story 1

- Run T014, T015, and T016 together because they touch separate assertions and test files.
- Run T020 and T022 in parallel only after T019 when state integration points are stable.

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 and Phase 2.
2. Deliver Phase 3 (US1) with passing US1 tests.
3. Validate single-scenario commitment behavior before broader phase-cycle work.

### Incremental Delivery

1. Foundation + US1 for deterministic commitment.
2. Add US2 for full cycle management and reset semantics.
3. Add US3 for scenario-specific guidance quality.
4. Finish with Phase 6 cross-cutting validation.

### Parallel Team Strategy

1. One developer finalizes domain and model tasks (T004-T008).
2. One developer builds engine services (T009-T012, T027-T030, T036).
3. One developer focuses tests and continuation/guidance integration (T014-T016, T023-T026, T034-T040).
