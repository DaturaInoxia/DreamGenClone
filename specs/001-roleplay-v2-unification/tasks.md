# Tasks: DreamGenClone RolePlay v2 Unified Scenario Intelligence

**Input**: Design documents from `/specs/001-roleplay-v2-unification/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/service-contracts.md, quickstart.md

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare project scaffolding and baseline configuration for v2 implementation.

- [X] T001 Create v2 role-play feature folders in DreamGenClone.Domain/RolePlay and DreamGenClone.Infrastructure/RolePlay
- [X] T002 Create v2 service interface stubs in DreamGenClone.Application/RolePlay/IScenarioSelectionService.cs, DreamGenClone.Application/RolePlay/IScenarioLifecycleService.cs, DreamGenClone.Application/RolePlay/IConceptInjectionService.cs, DreamGenClone.Application/RolePlay/IDecisionPointService.cs, and DreamGenClone.Application/RolePlay/IOverrideAuthorizationService.cs
- [X] T003 [P] Create baseline role-play diagnostics test scaffold in DreamGenClone.Tests/RolePlay/RolePlayDiagnosticsCoverageTests.cs
- [X] T004 [P] Create baseline deterministic scenario selection test scaffold in DreamGenClone.Tests/RolePlay/ScenarioSelectionHysteresisTests.cs
- [X] T005 [P] Create baseline concept determinism test scaffold in DreamGenClone.Tests/RolePlay/ConceptInjectionDeterminismTests.cs
- [X] T006 [P] Create baseline decision mutation test scaffold in DreamGenClone.Tests/RolePlay/DecisionPointMutationTests.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build shared domain/persistence/logging infrastructure required by all user stories.

**⚠️ CRITICAL**: No user story work should begin before this phase is complete.

- [X] T007 Implement core v2 domain models in DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs, DreamGenClone.Domain/RolePlay/CharacterStatProfileV2.cs, DreamGenClone.Domain/RolePlay/ScenarioCandidateEvaluation.cs, DreamGenClone.Domain/RolePlay/NarrativePhaseTransitionEvent.cs, DreamGenClone.Domain/RolePlay/ScenarioCompletionMetadata.cs, DreamGenClone.Domain/RolePlay/BehavioralConcept.cs, DreamGenClone.Domain/RolePlay/ConceptReferenceSet.cs, DreamGenClone.Domain/RolePlay/DecisionPoint.cs, DreamGenClone.Domain/RolePlay/DecisionOption.cs, DreamGenClone.Domain/RolePlay/FormulaConfigVersion.cs, and DreamGenClone.Domain/RolePlay/UnsupportedSessionError.cs
- [X] T008 [P] Add v2 enum/value object definitions for narrative phases, trigger types, transparency modes, and override roles in DreamGenClone.Domain/RolePlay/NarrativePhase.cs and DreamGenClone.Domain/RolePlay/RolePlayEnums.cs
- [X] T009 Implement SQLite schema extensions and compatibility columns for v2 entities in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs
- [X] T010 [P] Implement persistence repositories for adaptive state, evaluations, transitions, concepts, decisions, and formula versions in DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs and DreamGenClone.Infrastructure/RolePlay/RolePlayDiagnosticsRepository.cs
- [X] T011 Implement unsupported-version compatibility checks and explicit error persistence in DreamGenClone.Infrastructure/RolePlay/RolePlaySessionCompatibilityService.cs and DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T012 Implement structured Serilog event templates and correlation enrichment for v2 flows in DreamGenClone.Infrastructure/Logging/RolePlayV2LogEvents.cs and DreamGenClone.Web/Program.cs
- [X] T013 [P] Add runtime-configurable log-level settings for v2 categories in DreamGenClone.Web/appsettings.json and DreamGenClone.Web/appsettings.Development.json
- [X] T014 Wire DI registrations for new v2 services and repositories in DreamGenClone.Web/Program.cs

**Checkpoint**: Foundation complete - user stories can now be implemented and validated independently.

---

## Phase 3: User Story 1 - Commit to one coherent scenario direction (Priority: P1) 🎯 MVP

**Goal**: Commit one active scenario per cycle using two-stage gating and deterministic hysteresis.

**Independent Test**: Create competing scenario signals and verify one scenario commits, persists, and remains active until valid transition/reset.

### Tests for User Story 1

- [X] T015 [P] [US1] Add two-stage gating and candidate ranking tests in DreamGenClone.Tests/RolePlay/ScenarioSelectionHysteresisTests.cs
- [X] T016 [P] [US1] Add near-tie hysteresis commitment tests in DreamGenClone.Tests/RolePlay/ScenarioSelectionHysteresisTests.cs
- [X] T017 [P] [US1] Add deterministic tie-break ordering tests in DreamGenClone.Tests/RolePlay/ScenarioSelectionHysteresisTests.cs

### Implementation for User Story 1

- [X] T018 [US1] Implement candidate evaluation and scoring service in DreamGenClone.Infrastructure/RolePlay/ScenarioSelectionService.cs
- [X] T019 [US1] Implement willingness-tier and eligibility rule helpers in DreamGenClone.Infrastructure/RolePlay/ScenarioEligibilityService.cs
- [X] T020 [US1] Implement hysteresis lead tracking and commit decision logic in DreamGenClone.Infrastructure/RolePlay/ScenarioSelectionService.cs
- [X] T021 [US1] Persist active scenario commitment and evaluation rationale in DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs
- [X] T022 [US1] Integrate scenario selection pipeline into turn processing in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T023 [US1] Add Information-level logs for candidate scoring, tie resolution, and commitment results in DreamGenClone.Infrastructure/RolePlay/ScenarioSelectionService.cs

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Progress through a full phase lifecycle (Priority: P1)

**Goal**: Enforce ordered lifecycle BuildUp -> Committed -> Approaching -> Climax -> Reset -> BuildUp with rationale.

**Independent Test**: Simulate a full cycle and verify valid transition order, no illegal jumps, and recorded evidence.

### Tests for User Story 2

- [X] T024 [P] [US2] Add valid lifecycle transition sequence tests in DreamGenClone.Tests/RolePlay/PhaseLifecycleTransitionTests.cs
- [X] T025 [P] [US2] Add illegal transition rejection tests in DreamGenClone.Tests/RolePlay/PhaseLifecycleTransitionTests.cs
- [X] T026 [P] [US2] Add reset-to-buildup continuity tests in DreamGenClone.Tests/RolePlay/PhaseLifecycleTransitionTests.cs

### Implementation for User Story 2

- [X] T027 [US2] Implement lifecycle transition evaluator service in DreamGenClone.Infrastructure/RolePlay/ScenarioLifecycleService.cs
- [X] T028 [US2] Implement transition evidence and reason-code builders in DreamGenClone.Infrastructure/RolePlay/ScenarioLifecycleService.cs
- [X] T029 [US2] Persist NarrativePhaseTransitionEvent and ScenarioCompletionMetadata records in DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs
- [X] T030 [US2] Implement semi-reset behavior preserving continuity-relevant context in DreamGenClone.Infrastructure/RolePlay/ScenarioLifecycleService.cs
- [X] T031 [US2] Integrate lifecycle service into session turn flow in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T032 [US2] Add Information-level logs for transition decisions and reset execution in DreamGenClone.Infrastructure/RolePlay/ScenarioLifecycleService.cs

**Checkpoint**: User Stories 1 and 2 are independently functional.

---

## Phase 5: User Story 3 - Get context-appropriate reference injection (Priority: P1)

**Goal**: Select deterministic, conflict-resolved concepts under budget constraints.

**Independent Test**: Repeat identical state evaluations and confirm deterministic concept set and bounded payload.

### Tests for User Story 3

- [X] T033 [P] [US3] Add deterministic concept selection replay tests in DreamGenClone.Tests/RolePlay/ConceptInjectionDeterminismTests.cs
- [X] T034 [P] [US3] Add conflict resolution priority tests in DreamGenClone.Tests/RolePlay/ConceptInjectionDeterminismTests.cs
- [X] T035 [P] [US3] Add reserved-quota and overflow budget truncation tests in DreamGenClone.Tests/RolePlay/ConceptInjectionDeterminismTests.cs

### Implementation for User Story 3

- [X] T036 [US3] Implement concept relevance and conflict resolution service in DreamGenClone.Infrastructure/RolePlay/ConceptInjectionService.cs
- [X] T037 [US3] Implement GuidanceBudgetPolicy allocation logic (reserved quotas plus overflow pool) in DreamGenClone.Infrastructure/RolePlay/ConceptInjectionService.cs
- [X] T038 [US3] Implement deterministic truncation and tie-break ordering in DreamGenClone.Infrastructure/RolePlay/ConceptInjectionService.cs
- [X] T039 [US3] Integrate concept injection triggers at interaction start, phase change, significant stat change, and manual override in DreamGenClone.Web/Application/RolePlay/RolePlayPromptComposer.cs
- [X] T040 [US3] Add concept selection and budget diagnostics logging in DreamGenClone.Infrastructure/RolePlay/ConceptInjectionService.cs

**Checkpoint**: User Stories 1-3 are independently functional.

---

## Phase 6: User Story 4 - Use stat-altering narrative choices (Priority: P2)

**Goal**: Generate contextual decision points whose options mutate canonical stats and support custom fallback.

**Independent Test**: Trigger decision points and verify option effects persist and influence later behavior.

### Tests for User Story 4

- [X] T041 [P] [US4] Add decision-point trigger and option generation tests in DreamGenClone.Tests/RolePlay/DecisionPointMutationTests.cs
- [X] T042 [P] [US4] Add option selection stat-delta persistence tests in DreamGenClone.Tests/RolePlay/DecisionPointMutationTests.cs
- [X] T043 [P] [US4] Add custom-response fallback parsing tests in DreamGenClone.Tests/RolePlay/DecisionPointMutationTests.cs

### Implementation for User Story 4

- [X] T044 [US4] Implement decision-point generation service in DreamGenClone.Infrastructure/RolePlay/DecisionPointService.cs
- [X] T045 [US4] Implement canonical stat mutation application and audit metadata in DreamGenClone.Infrastructure/RolePlay/DecisionPointService.cs
- [X] T046 [US4] Implement directional default transparency mode with override support in DreamGenClone.Infrastructure/RolePlay/DecisionPointService.cs
- [X] T047 [US4] Implement custom-response fallback interpreter and mapping rules in DreamGenClone.Infrastructure/RolePlay/DecisionPointService.cs
- [X] T048 [US4] Integrate decision-point lifecycle into workspace orchestration in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs and DreamGenClone.Web/Components/RolePlayWorkspace.razor
- [X] T049 [US4] Add decision outcome and mutation logs in DreamGenClone.Infrastructure/RolePlay/DecisionPointService.cs

**Checkpoint**: User Story 4 is independently functional.

---

## Phase 7: User Story 5 - Persist and validate v2 state reliably (Priority: P2)

**Goal**: Guarantee v2 session persistence/reload integrity and explicit unsupported-version rejection.

**Independent Test**: Reload v2 sessions with stable state and reject non-v2 payloads without partial mutation.

### Tests for User Story 5

- [X] T050 [P] [US5] Add v2 state persistence/reload consistency tests in DreamGenClone.Tests/RolePlay/RolePlaySessionLifecycleTests.cs
- [X] T051 [P] [US5] Add unsupported-session-version rejection tests in DreamGenClone.Tests/RolePlay/UnsupportedSessionVersionTests.cs
- [X] T052 [P] [US5] Add schema compatibility and corruption-guard tests in DreamGenClone.Tests/RolePlay/UnsupportedSessionVersionTests.cs

### Implementation for User Story 5

- [X] T053 [US5] Implement v2 schema version stamping and compatibility checks in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs
- [X] T054 [US5] Implement UnsupportedSessionError generation and persistence in DreamGenClone.Infrastructure/RolePlay/RolePlaySessionCompatibilityService.cs
- [X] T055 [US5] Enforce no-partial-mutation guardrail on rejected payloads in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T056 [US5] Persist and reload formula version references and cycle history integrity in DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs
- [X] T057 [US5] Add Information and error logs for compatibility checks and rejections in DreamGenClone.Infrastructure/RolePlay/RolePlaySessionCompatibilityService.cs

**Checkpoint**: User Story 5 is independently functional.

---

## Phase 8: User Story 6 - Observe and debug engine decisions (Priority: P3)

**Goal**: Expose transparent diagnostics for scoring, transitions, concept injection, and decision outcomes.

**Independent Test**: Run end-to-end flows and verify diagnostic outputs include required evidence and rationale fields.

### Tests for User Story 6

- [X] T058 [P] [US6] Add candidate-score diagnostics coverage tests in DreamGenClone.Tests/RolePlay/RolePlayDiagnosticsCoverageTests.cs
- [X] T059 [P] [US6] Add transition evidence diagnostics coverage tests in DreamGenClone.Tests/RolePlay/RolePlayDiagnosticsCoverageTests.cs
- [X] T060 [P] [US6] Add concept and decision diagnostics coverage tests in DreamGenClone.Tests/RolePlay/RolePlayDiagnosticsCoverageTests.cs

### Implementation for User Story 6

- [X] T061 [US6] Implement diagnostics aggregation service in DreamGenClone.Infrastructure/RolePlay/RolePlayDiagnosticsService.cs
- [X] T062 [US6] Expose diagnostics snapshots through orchestration layer in DreamGenClone.Web/Application/RolePlay/RolePlayAssistantService.cs and DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs
- [X] T063 [US6] Render diagnostics visibility in workspace debug panel in DreamGenClone.Web/Components/RolePlayWorkspace.razor
- [X] T064 [US6] Add cross-flow correlation IDs and structured templates for diagnostics events in DreamGenClone.Infrastructure/Logging/RolePlayV2LogEvents.cs

**Checkpoint**: All user stories are independently functional and observable.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final hardening across all stories before release.

- [X] T065 [P] Update v2 scope boundary and deferred safety follow-up notes in specs/001-roleplay-v2-unification/spec.md
- [X] T066 [P] Add acceptance fixtures for profile examples and threshold boundaries in DreamGenClone.Tests/RolePlay/RolePlayV2AcceptanceFixtureData.cs
- [X] T067 Run quickstart validation sequence and update execution notes in specs/001-roleplay-v2-unification/quickstart.md
- [X] T068 Execute full regression and role-play focused test runs using DreamGenClone.Tests/DreamGenClone.Tests.csproj

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1) has no prerequisites.
- Foundational (Phase 2) depends on Setup and blocks all story work.
- User Story phases (Phase 3 onward) depend on completion of Foundational.
- Polish (Phase 9) depends on completion of target user stories.

### User Story Dependencies

- US1 (P1): Starts after Foundational; provides scenario commitment baseline.
- US2 (P1): Starts after Foundational; recommended after US1 for smoother lifecycle integration.
- US3 (P1): Starts after Foundational; can proceed in parallel with US2.
- US4 (P2): Starts after Foundational and benefits from US1/US2 active context.
- US5 (P2): Starts after Foundational and can run parallel with US4.
- US6 (P3): Starts after Foundational; best finalized after US1-US5 for full diagnostics coverage.

### Within Each User Story

- Test tasks are created first and should fail before implementation.
- Service logic precedes orchestration integration.
- Persistence/logging wiring follows core logic.
- Story-specific checkpoint validates independent behavior.

## Parallel Execution Examples

### User Story 1

- T015, T016, and T017 can run in parallel (same test file sections, independent test cases).

### User Story 2

- T024, T025, and T026 can run in parallel.

### User Story 3

- T033, T034, and T035 can run in parallel.

### User Story 4

- T041, T042, and T043 can run in parallel.

### User Story 5

- T050, T051, and T052 can run in parallel.

### User Story 6

- T058, T059, and T060 can run in parallel.

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 Setup.
2. Complete Phase 2 Foundational.
3. Deliver Phase 3 (US1).
4. Validate US1 independently before expanding scope.

### Incremental Delivery

1. Add US2 and US3 after MVP to complete core adaptive loop.
2. Add US4 and US5 for decision mutation and persistence hardening.
3. Add US6 for diagnostics and operational tuning.
4. Finish with Phase 9 polish and full verification.

### Parallel Team Strategy

1. Team aligns on Setup + Foundational.
2. After Foundational:
   - Engineer A: US1 and US2
   - Engineer B: US3 and US4
   - Engineer C: US5 and US6
3. Consolidate in Polish phase with full regression.

## Notes

- Tasks marked [P] are parallelizable with minimal dependency contention.
- Story labels [USx] ensure traceability to specification priorities.
- All tasks include concrete file paths to enable direct execution by implementation agents.
