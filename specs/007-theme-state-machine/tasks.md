# Tasks: Theme State Machine Continuity

**Input**: Design documents from `/specs/007-theme-state-machine/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/service-contracts.md, quickstart.md

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create machine-related scaffolding across Domain and Application contracts.

- [X] T001 Create machine value-object scaffolding in DreamGenClone.Domain/RolePlay/ThemeMachineModels.cs
- [X] T002 Extend domain machine entities in DreamGenClone.Domain/RolePlay/RPThemeModels.cs
- [X] T003 [P] Add evaluator and authorization contracts in DreamGenClone.Application/RolePlay/IThemeMachineEvaluator.cs and DreamGenClone.Application/RolePlay/IThemeMachineAuthorizationService.cs
- [X] T004 [P] Extend repository contracts in DreamGenClone.Application/RolePlay/IRolePlayStateRepository.cs and DreamGenClone.Application/RolePlay/IRolePlayDiagnosticsRepository.cs
- [X] T005 Extend theme service contract for machine management and migration in DreamGenClone.Application/RolePlay/IRPThemeService.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build persistence, resolution, and service wiring required before any user story.

**CRITICAL**: User story implementation starts only after this phase completes.

- [X] T006 Add machine definition/state/transition and machine diagnostic schema setup in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs
- [X] T007 Add machine snapshot state fields in DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs and DreamGenClone.Web/Domain/RolePlay/RolePlayAdaptiveState.cs
- [X] T008 Implement machine snapshot persistence and schema evolution in DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs
- [X] T009 Implement machine diagnostic event persistence/read paths in DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs and DreamGenClone.Infrastructure/RolePlay/RolePlayDiagnosticsRepository.cs
- [X] T010 [P] Implement admin-only machine authorization service in DreamGenClone.Infrastructure/RolePlay/ThemeMachineAuthorizationService.cs
- [X] T011 [P] Register machine services in DreamGenClone.Web/Program.cs
- [X] T012 Implement single-path machine resolution service in DreamGenClone.Infrastructure/RolePlay/ThemeMachineResolutionService.cs
- [X] T013 Add fail-fast machine resolution guard integration in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs

**Checkpoint**: Foundation complete. User stories can begin.

---

## Phase 3: User Story 1 - Configure Continuity Machines in Theme Management (Priority: P1)

**Goal**: Allow admins to create, validate, activate, and migrate theme machine definitions via UI-backed persisted config.

**Independent Test**: Admin can create/activate/update a machine in RP theme UI, and non-admin mutate/migrate attempts are explicitly denied.

### Tests for User Story 1

- [X] T014 [P] [US1] Add machine authorization tests in DreamGenClone.Tests/RolePlay/ThemeMachineAuthorizationTests.cs
- [X] T015 [P] [US1] Add machine definition validation tests in DreamGenClone.Tests/RolePlay/RPThemeMachineDefinitionValidationTests.cs

### Implementation for User Story 1

- [X] T016 [US1] Implement machine definition/state/transition CRUD in DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs
- [X] T017 [US1] Implement activation workflow with strict validation in DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs
- [X] T018 [US1] Implement explicit admin-only session migrate action in DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs and DreamGenClone.Infrastructure/RolePlay/ThemeMachineAuthorizationService.cs
- [X] T019 [P] [US1] Build machine editor UI sections in DreamGenClone.Web/Components/Pages/RPThemeDetail.razor
- [X] T020 [P] [US1] Add machine status/version controls in DreamGenClone.Web/Components/Pages/RPThemes.razor
- [X] T021 [US1] Wire machine UI actions to service methods with explicit validation/auth errors in DreamGenClone.Web/Components/Pages/RPThemeDetail.razor
- [X] T022 [US1] Add Information-level logs for machine create/update/activate/migrate paths in DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs

**Checkpoint**: US1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Enforce Disappearance Lifecycle Deterministically (Priority: P1)

**Goal**: Enforce deterministic machine-driven continuity in runtime selection and prompting.

**Independent Test**: Scripted session follows the defined state sequence, blocks disallowed disappearances, and only exits cooldown when both configured conditions are met.

### Tests for User Story 2

- [X] T023 [P] [US2] Add deterministic transition priority tests in DreamGenClone.Tests/RolePlay/ThemeMachineEvaluatorTests.cs
- [X] T024 [P] [US2] Add lifecycle progression and cooldown gating tests in DreamGenClone.Tests/RolePlay/RolePlaySessionLifecycleTests.cs
- [X] T025 [P] [US2] Add candidate blocking tests for ReturnBeatRequired/ReintegrationCooldown in DreamGenClone.Tests/RolePlay/RolePlayContinueAsSelectionTests.cs
- [X] T026 [P] [US2] Add prompt directive injection tests in DreamGenClone.Tests/RolePlay/RolePlayContinuationScenarioGuidanceTests.cs

### Implementation for User Story 2

- [X] T027 [US2] Implement deterministic machine evaluator in DreamGenClone.Infrastructure/RolePlay/ThemeMachineEvaluator.cs
- [X] T028 [US2] Implement cooldown eligibility gate (min interactions + return-beat flag) in DreamGenClone.Infrastructure/RolePlay/ThemeMachineEvaluator.cs
- [X] T029 [US2] Bind and persist pinned machine definition version at session start in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T030 [US2] Integrate machine evaluation into V2 pipeline cycle in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T031 [US2] Apply machine directives to candidate filtering/commit in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs and DreamGenClone.Infrastructure/RolePlay/ScenarioSelectionService.cs
- [X] T032 [US2] Inject machine directives into continuation prompts in DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs and DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs
- [X] T033 [US2] Persist transition metadata and updated machine snapshot in DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs
- [X] T034 [US2] Add Information-level evaluator and transition logs in DreamGenClone.Infrastructure/RolePlay/ThemeMachineEvaluator.cs and DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs

**Checkpoint**: US2 is fully functional and independently testable.

---

## Phase 5: User Story 3 - Fail Fast and Diagnose Continuity Issues (Priority: P2)

**Goal**: Ensure missing/invalid machine config fails explicitly and diagnostics provide actionable machine event history.

**Independent Test**: Forced missing/ambiguous config and malformed runtime payload fail explicitly with persisted machine diagnostics that include reason codes and state context.

### Tests for User Story 3

- [X] T035 [P] [US3] Add missing/ambiguous machine resolution fail-fast tests in DreamGenClone.Tests/RolePlay/PhaseLifecycleTransitionTests.cs
- [X] T036 [P] [US3] Add malformed machine snapshot parse-failure tests in DreamGenClone.Tests/RolePlay/ThemeMachinePersistenceTests.cs
- [X] T037 [P] [US3] Add machine diagnostics repository query tests in DreamGenClone.Tests/RolePlay/RolePlayDiagnosticsRepositoryTests.cs

### Implementation for User Story 3

- [X] T038 [US3] Enforce explicit failure when resolution path does not yield exactly one machine in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T039 [US3] Enforce strict required-field parsing for machine snapshot payloads in DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs
- [X] T040 [US3] Persist machine init/transition/blocked/failure diagnostics in DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs and DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [X] T041 [US3] Extend diagnostics read model for machine events in DreamGenClone.Infrastructure/RolePlay/RolePlayDiagnosticsRepository.cs and DreamGenClone.Application/RolePlay/IRolePlayDiagnosticsRepository.cs
- [X] T042 [US3] Surface machine diagnostics and state context in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
- [X] T043 [US3] Add warning/error logs for blocked transitions, auth denials, and config failures in DreamGenClone.Infrastructure/RolePlay/ThemeMachineEvaluator.cs and DreamGenClone.Infrastructure/RolePlay/ThemeMachineAuthorizationService.cs

**Checkpoint**: US3 is fully functional and independently testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, documentation, and regression confidence across all stories.

- [X] T044 [P] Update operator/developer verification and migration notes in specs/007-theme-state-machine/quickstart.md
- [X] T045 [P] Document no-fallback and single-path evidence checklist in specs/007-theme-state-machine/research.md
- [X] T046 Execute RolePlay regression test pass and record outcomes in specs/007-theme-state-machine/quickstart.md
- [X] T047 Execute solution build verification and record outcomes in specs/007-theme-state-machine/quickstart.md

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): starts immediately.
- Foundational (Phase 2): depends on Setup completion and blocks all user stories.
- User Story phases: depend on Foundational completion.
- Polish (Phase 6): depends on desired user stories being complete.

### User Story Dependencies

- US1 (P1): starts after Foundational; no dependency on other user stories.
- US2 (P1): starts after Foundational; independent of US1 for runtime enforcement behavior.
- US3 (P2): starts after Foundational, but depends on completed machine management/runtime surfaces from US1 and US2 to provide full diagnostic coverage.

### Within Each User Story

- Tests are authored before implementation and should fail first.
- Service/domain updates precede UI integration.
- Runtime integration precedes diagnostics polishing.

### Parallel Opportunities

- Setup tasks marked [P] can run concurrently.
- Foundational tasks marked [P] can run concurrently.
- After Foundational, US1 and US2 can proceed in parallel.
- Test tasks marked [P] within each story can run in parallel.
- UI tasks and service tasks marked [P] in US1 can run in parallel.

---

## Parallel Execution Examples

### User Story 1

```bash
# Parallel validation and authorization tests
T014 ThemeMachineAuthorizationTests
T015 RPThemeMachineDefinitionValidationTests

# Parallel UI work
T019 RPThemeDetail machine editor
T020 RPThemes machine status/actions
```

### User Story 2

```bash
# Parallel runtime behavior tests
T023 ThemeMachineEvaluatorTests
T024 RolePlaySessionLifecycleTests
T025 RolePlayContinueAsSelectionTests
T026 RolePlayContinuationScenarioGuidanceTests
```

### User Story 3

```bash
# Parallel fail-fast and diagnostics tests
T035 PhaseLifecycleTransitionTests
T036 ThemeMachinePersistenceTests
T037 RolePlayDiagnosticsRepositoryTests
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational).
3. Complete Phase 3 (US1).
4. Validate US1 independently before expanding scope.

### Incremental Delivery

1. Deliver US1 for admin-configurable machine definitions.
2. Deliver US2 for deterministic runtime continuity enforcement.
3. Deliver US3 for fail-fast resilience and diagnostics visibility.
4. Finish with Phase 6 verification/documentation.

### Team Parallelization

1. Team completes Setup + Foundational together.
2. Split into parallel tracks:
   - Track A: US1 service/UI/admin flow
   - Track B: US2 evaluator/runtime integration
3. Merge into US3 diagnostics/fail-fast hardening.
