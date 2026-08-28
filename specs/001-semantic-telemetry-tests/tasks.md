# Tasks: Semantic Telemetry and Event-Driven Evidence

**Input**: Design documents from `/specs/001-semantic-telemetry-tests/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/service-contracts.md`, `quickstart.md`

**Tests**: Tests are explicitly required by the specification and are included in each user story phase.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare feature scaffolding and diagnostics reason-code baseline.

- [X] T001 Create semantic telemetry task scaffolding notes in `specs/001-semantic-telemetry-tests/quickstart.md`
- [X] T002 Add semantic diagnostics reason-code constants in `DreamGenClone.Domain/RolePlay/RPThemeModels.cs`
- [X] T003 [P] Add semantic debug-view placeholders for event/confidence/delta fields in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement one canonical semantic configuration path, fail-fast contract checks, and shared delta structures before story work.

**CRITICAL**: No user story implementation starts before this phase completes.

- [X] T004 Implement canonical semantic mapping source resolution path in `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs`
- [X] T005 Implement explicit missing-config fail-fast guard (no fallback branch) in `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs`
- [X] T006 [P] Add semantic event evidence and delta breakdown models in `DreamGenClone.Web/Domain/RolePlay/RolePlayAdaptiveState.cs`
- [X] T007 Add confidence-range validation contract with out-of-range hard failure in `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs`
- [X] T008 Add shared semantic-step diagnostic emission for failure/no-contribution states in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [X] T009 [P] Add foundational no-fallback decision-path tests in `DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceTests.cs`

**Checkpoint**: Foundational semantic contract is enforced with one active configuration source and explicit fail-fast behavior.

---

## Phase 3: User Story 1 - Inspect Semantic Telemetry in Debug Workspace (Priority: P1) 🎯 MVP

**Goal**: Surface per-interaction semantic events, confidence, and applied/capped/suppressed deltas with explicit no-contribution and failure diagnostics.

**Independent Test**: Run one debug-eligible interaction and verify telemetry includes event trace, confidence, delta breakdown, and fail-fast/no-contribution visibility.

### Tests for User Story 1

- [X] T010 [P] [US1] Add unit tests for semantic telemetry mapping to debug output fields in `DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceTests.cs`
- [X] T011 [P] [US1] Add unit tests proving invalid semantic payload fails explicitly with zero semantic deltas in `DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceTests.cs`
- [X] T012 [P] [US1] Add integration test verifying debug workspace renders semantic event/confidence/delta trace for one interaction in `DreamGenClone.Tests/RolePlay/RolePlayContinueAsSelectionTests.cs`

### Implementation for User Story 1

- [X] T013 [US1] Implement semantic telemetry projection (event id, confidence, applied/capped/suppressed deltas) in `DreamGenClone.Infrastructure/RolePlay/RolePlayAdaptiveStateService.cs`
- [X] T014 [US1] Integrate semantic telemetry payload into engine response diagnostics in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [X] T015 [US1] Render semantic telemetry section and explicit no-contribution state in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T016 [US1] Add Information-level semantic processing logs for major call paths in `DreamGenClone.Infrastructure/RolePlay/RolePlayAdaptiveStateService.cs`

**Checkpoint**: User Story 1 works independently with visible semantic telemetry and explicit diagnostics.

---

## Phase 4: User Story 2 - Validate Semantic Evidence Influences Theme Decisions (Priority: P1)

**Goal**: Ensure semantic evidence changes theme ordering and candidate fit behavior, including corruption progression from semantic intent.

**Independent Test**: Run controlled interactions (including "lie to husband" semantic intent) and verify ranking/fit outcomes update without corruption keywords.

### Tests for User Story 2

- [X] T017 [P] [US2] Add integration test for corruption progression from semantic intent without corruption keyword triggers in `DreamGenClone.Tests/RolePlay/RolePlayContinueAsSelectionTests.cs`
- [X] T018 [P] [US2] Add integration test verifying primary/secondary theme ordering changes from semantic evidence in `DreamGenClone.Tests/RolePlay/PhaseLifecycleTransitionTests.cs`
- [X] T019 [P] [US2] Add end-to-end regression test validating candidate fit behavior changes with semantic evidence in `DreamGenClone.Tests/RolePlay/RolePlayContinueAsSelectionTests.cs`

### Implementation for User Story 2

- [X] T020 [US2] Apply semantic evidence deltas into adaptive state snapshot generation in `DreamGenClone.Infrastructure/RolePlay/RolePlayAdaptiveStateService.cs`
- [X] T021 [US2] Update scenario selection fit computation to consume finalized semantic+keyword snapshot in `DreamGenClone.Web/Application/RolePlay/ScenarioSelectionService.cs`
- [X] T022 [US2] Ensure engine pipeline passes semantic-updated snapshot to selection and transition flow in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [X] T023 [US2] Add semantic-mapping model extensions required for ranking/fit attribution in `DreamGenClone.Web/Domain/RolePlay/RPThemeModels.cs`

**Checkpoint**: User Story 2 independently proves semantic evidence can alter ordering and candidate fit outcomes.

---

## Phase 5: User Story 3 - Enforce Safety Guards Against Over-Accumulation and Locked Themes (Priority: P2)

**Goal**: Keep cap/cooldown and blocked-theme locks authoritative under repeated semantic signals.

**Independent Test**: Replay adjacent repeated semantic events and blocked-theme scenarios to verify bounded accumulation and locked-zero behavior.

### Tests for User Story 3

- [X] T024 [P] [US3] Add unit tests for cap/cooldown suppression on repeated adjacent-turn semantic events in `DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceTests.cs`
- [X] T025 [P] [US3] Add regression tests proving blocked themes remain locked at zero despite semantic evidence in `DreamGenClone.Tests/RolePlay/PhaseLifecycleTransitionTests.cs`
- [X] T026 [P] [US3] Add fail-fast tests for unknown semantic event identifiers and missing mapping config in `DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceTests.cs`

### Implementation for User Story 3

- [X] T027 [US3] Enforce cap/cooldown guard application before semantic delta commit in `DreamGenClone.Infrastructure/RolePlay/RolePlayAdaptiveStateService.cs`
- [X] T028 [US3] Enforce blocked-theme lock zeroing during semantic evidence application in `DreamGenClone.Infrastructure/RolePlay/RolePlayAdaptiveStateService.cs`
- [X] T029 [US3] Ensure phase lifecycle consumes lock-safe evidence values only in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

**Checkpoint**: User Story 3 independently confirms guardrails prevent runaway accumulation and lock bypass.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, documentation, and full-suite confidence checks.

- [X] T030 [P] Document manual debug verification trace steps and expected telemetry fields in `specs/001-semantic-telemetry-tests/quickstart.md`
- [X] T031 Run RolePlay semantic-focused test filter and record results in `specs/001-semantic-telemetry-tests/quickstart.md`
- [X] T032 Run full RolePlay test suite and record pass/fail evidence in `specs/001-semantic-telemetry-tests/quickstart.md`
- [X] T033 Verify and document one active decision path with no fallback branches for semantic config source in `specs/001-semantic-telemetry-tests/research.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Starts immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1 and blocks all user stories.
- **Phase 3 (US1)**: Depends on Phase 2.
- **Phase 4 (US2)**: Depends on Phase 2 and can run in parallel with US1 after foundational completion.
- **Phase 5 (US3)**: Depends on Phase 2 and can run in parallel with US1/US2 after foundational completion.
- **Phase 6 (Polish)**: Depends on completed target user stories.

### User Story Dependencies

- **US1**: No dependency on other user stories.
- **US2**: Uses shared foundational semantic structures; no direct dependency on US1 completion.
- **US3**: Uses shared foundational semantic structures; no direct dependency on US1/US2 completion.

### Within Each User Story

- Tests are written before or alongside implementation and must fail before fixes pass.
- Service/model updates precede pipeline/UI integration.
- Story-specific checkpoints validate independent completeness.

---

## Parallel Execution Examples

### User Story 1

- Run T010, T011, and T012 in parallel (different test scopes/files).
- Run T013 and T015 in parallel after T014 contract shape is agreed.

### User Story 2

- Run T017, T018, and T019 in parallel (independent scenario assertions).
- Run T021 and T023 in parallel, then integrate with T022.

### User Story 3

- Run T024, T025, and T026 in parallel.
- Run T027 and T028 together, then complete T029 integration.

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Deliver Phase 3 (US1) for debug semantic telemetry visibility and explicit fail-fast diagnostics.
3. Validate US1 independently before expanding behavior changes.

### Incremental Delivery

1. Foundation first (canonical source, no fallback, fail-fast contract).
2. Deliver US1 telemetry observability.
3. Deliver US2 ranking/fit behavior influence.
4. Deliver US3 safety guardrail hardening.
5. Finish with polish and verification evidence.

### Parallel Team Strategy

1. Team aligns on Phase 2 foundational contracts.
2. Engineer A implements US1 UI/telemetry.
3. Engineer B implements US2 ordering/fit integration.
4. Engineer C implements US3 cap/cooldown/lock guardrails.
5. Merge in Phase 6 with full regression verification.

---

## Addendum: Semantic-to-Stat Effects Follow-up

**Purpose**: Extend semantic processing so configured semantic events can update adaptive character stats (Desire/Restraint/Tension/Connection) in addition to theme evidence.

## Phase 7: Setup (Semantic Stat Effects)

**Purpose**: Add persistence and domain scaffolding for semantic-to-stat mappings.

- [X] T034 Add `RPSemanticStatMapping` domain model in `DreamGenClone.Domain/RolePlay/RPThemeModels.cs`
- [X] T035 Add `RPThemeSemanticStatMappings` schema and indexes in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- [X] T036 [P] Wire semantic stat mapping load/save/clone persistence in `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs`

---

## Phase 8: Foundational (Blocking Contracts)

**Purpose**: Enforce canonical source resolution and fail-fast validation for semantic stat mappings.

**CRITICAL**: No runtime stat-effect work starts before this phase completes.

- [X] T037 Add canonical semantic stat mapping resolution API in `DreamGenClone.Application/RolePlay/IRPThemeService.cs`
- [X] T038 Implement no-fallback semantic stat mapping resolution by profile in `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs`
- [X] T039 [P] Add fail-fast validation for stat mapping rows (required fields, confidence range, stat keys) in `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs`
- [X] T040 [P] Add foundational resolver tests for single decision path and missing-config failures in `DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceTests.cs`

**Checkpoint**: Semantic stat mappings resolve from one configured source path with explicit failure behavior.

---

## Phase 9: User Story 4 - Apply Semantic Events To Adaptive Stats (Priority: P1)

**Goal**: Semantic events can directly update adaptive stats so gate-driving values (for example Desire) can progress from semantic intent.

**Independent Test**: Trigger one semantic event and verify the expected stat delta is applied before phase-gate evaluation.

### Tests for User Story 4

- [X] T041 [P] [US4] Add unit tests for semantic stat delta application in `DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceTests.cs`
- [X] T042 [P] [US4] Add fail-fast tests for unknown semantic stat event and confidence-out-of-range in `DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceTests.cs`
- [X] T043 [P] [US4] Add cap/cooldown/blocked suppression tests for semantic stat deltas in `DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceTests.cs`

### Implementation for User Story 4

- [X] T044 [US4] Add semantic stat delta models to adaptive state telemetry in `DreamGenClone.Web/Domain/RolePlay/RolePlayAdaptiveState.cs`
- [X] T045 [US4] Apply semantic stat mappings during interaction processing in `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs`
- [X] T046 [US4] Emit semantic stat delta diagnostics and debug metadata in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

**Checkpoint**: Semantic events can move adaptive stats through configured mappings with bounded, auditable behavior.

---

## Phase 10: User Story 5 - Configure Semantic Stat Mappings In Theme UI (Priority: P1)

**Goal**: Admin users can manage semantic-to-stat mappings via dedicated Theme UI CRUD with defaults/dropdowns.

**Independent Test**: Create/update/delete semantic stat mappings in Theme UI and verify persistence round-trip.

### Tests for User Story 5

- [X] T047 [P] [US5] Add RP theme persistence tests for semantic stat mapping round-trip in `DreamGenClone.Tests/RolePlay/RPThemeCloneTests.cs`

### Implementation for User Story 5

- [X] T048 [US5] Add Semantic Stat Mappings CRUD section in `DreamGenClone.Web/Components/Pages/RPThemeDetail.razor`
- [X] T049 [US5] Add dropdown defaults (TargetStat, Direction, ReasonCode suggestions) in `DreamGenClone.Web/Components/Pages/RPThemeDetail.razor`
- [X] T050 [US5] Normalize and validate semantic stat mapping inputs before save in `DreamGenClone.Web/Components/Pages/RPThemeDetail.razor`

**Checkpoint**: Semantic stat mappings are fully configurable in UI-backed persisted data.

---

## Phase 11: User Story 6 - Unblock Phase Gates Through Semantic Stat Effects (Priority: P2)

**Goal**: Semantic stat updates can change gate outcomes when thresholds are stat-driven.

**Independent Test**: In a controlled session, semantic event raises Desire across threshold and next phase becomes eligible.

### Tests for User Story 6

- [X] T051 [P] [US6] Add integration test proving semantic Desire delta can satisfy gate threshold in `DreamGenClone.Tests/RolePlay/PhaseLifecycleTransitionTests.cs`
- [X] T052 [P] [US6] Add regression test ensuring no phase progression when semantic stat delta is suppressed or capped in `DreamGenClone.Tests/RolePlay/PhaseLifecycleTransitionTests.cs`

### Implementation for User Story 6

- [X] T053 [US6] Ensure gate evaluation consumes post-semantic stat state in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

**Checkpoint**: Stat-driven gates reflect semantic stat effects deterministically.

---

## Phase 12: Polish & Cross-Cutting (Semantic Stat Effects)

**Purpose**: Final verification, docs, and reproducible validation evidence.

- [X] T054 [P] Document semantic stat mapping configuration and verification steps in `specs/001-semantic-telemetry-tests/quickstart.md`
- [X] T055 Verify and document no-fallback semantic stat source resolution in `specs/001-semantic-telemetry-tests/research.md`
- [X] T056 Run targeted RolePlay tests for US4-US6 and record outcomes in `specs/001-semantic-telemetry-tests/quickstart.md`

---

## Addendum Dependencies & Parallelization

### Phase Dependencies

- Phase 7 -> Phase 8 -> Phase 9 -> Phase 10 -> Phase 11 -> Phase 12.
- US5 can start after Phase 8 and run in parallel with US4 where file-level dependencies permit.

### Parallel Opportunities

- Run T036, T039, and T040 in parallel after T035.
- Run T041, T042, and T043 in parallel.
- Run T051 and T052 in parallel.

### Suggested MVP Scope For Follow-up

- MVP follow-up slice: Phase 7 + Phase 8 + US4.
- This delivers direct semantic-to-stat behavior without requiring full UI rollout first.
