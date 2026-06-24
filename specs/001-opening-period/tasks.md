# Tasks: RP Session Opening Period

**Input**: Design documents from `/specs/001-opening-period/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Not explicitly requested in spec — test tasks omitted. Add if desired.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Database migration and constant definition — prerequisites for all user stories

- [x] T001 Add `OpeningGuidanceText TEXT` column to `Scenarios` table in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` migration
- [x] T002 [P] Seed all existing scenarios with default opening-period guidance text via migration UPDATE in same file
- [x] T003 [P] Define `private const int OpeningPeriodTurnCount = 3` constant in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` (replace old `OpeningPeripheralTurnCount = 6`)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core gate logic that both US1 and US2 depend on

- [x] T004 Add `OpeningGuidanceText` property to scenario read model in `DreamGenClone.Infrastructure/RolePlay/` scenario loading code — maps DB column `Scenarios.OpeningGuidanceText` to domain model
- [x] T005 [P] Add default opening guidance text constant in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` (fallback when `OpeningGuidanceText` is null)

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Husband-wife dynamic established before love interest enters (Priority: P1) 🎯 MVP

**Goal**: For the first 3 turns of a new session, only husband and wife write responses; love interest (OtherMan) does not appear as named participant or overflow actor until turn 4.

**Independent Test**: Create a new RP session with a scenario that includes an OtherMan character. Verify OtherMan is not named in generated narratives and not selected as overflow actor for turns 1-3. Verify OtherMan appears from turn 4.

### Implementation for User Story 1

- [x] T006 [US1] Update OtherMan overflow exclusion in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` ~line 2213: change `totalInteractions < 6` to `session.AdaptiveState.ObservedTurnCount <= OpeningPeriodTurnCount`
- [x] T007 [US1] Update log message for OtherMan exclusion to reference turn count instead of interaction offset in same method
- [x] T008 [US1] Update persona-lead-during-setup check in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` ~line 2256: change `totalInteractions < 6` to `session.AdaptiveState.ObservedTurnCount <= OpeningPeriodTurnCount`
- [x] T009 [US1] Add Information-level log when opening period begins (turn 1) and ends (turn 4) for session observability

**Checkpoint**: OtherMan excluded for turns 1-3, eligible from turn 4. Persona leads during opening period.

---

## Phase 4: User Story 2 - No contradictory instructions in the prompt during opening (Priority: P1)

**Goal**: During the opening period, suppress ALL theme/phase guidance from the LLM prompt (theme contract, framing guards, hard constraints, AI notes) and inject opening-period guidance from the scenario definition instead. Remove the old OPF hard constraint.

**Independent Test**: Extract LLM prompts for turns 1-3 and verify zero theme phase guidance text. Verify opening-period guidance is present. Extract turn 4 prompt and verify theme guidance is present and opening guidance is absent.

### Implementation for User Story 2

- [x] T010 [US2] Wrap theme-guidance injection block in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` ~line 928 with opening-period gate: `if (session.AdaptiveState.ObservedTurnCount > OpeningPeriodTurnCount)` → normal theme guidance; `else` → inject opening guidance
- [x] T011 [US2] Load `OpeningGuidanceText` from scenario in the opening-period branch (or fall back to default constant if null)
- [x] T012 [US2] Suppress observer candidate menu (`AppendObservingCandidateMenuAsync`) during opening period in same gate block
- [x] T013 [US2] Suppress `BuildFramingGuards` during opening period (pass empty or gate the call at line ~923)
- [x] T014 [US2] Remove old `HARD CONSTRAINT — Opening Peripheral Focus` block in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` ~lines 1330-1348 (keep `var openingInteractionCount = ...` variable — it is still used for opening-narrative detection at line ~1363)
- [x] T015 [US2] Add Information-level log when opening-period guidance is injected vs theme guidance for session observability
- [x] T016 [US2] Ensure `ActiveScenarioId` stays null during opening period (FR-010): verify the theme tracker's observation guard or opening-period gate prevents scenario commitment before turn 4. If needed, add `if (ObservedTurnCount <= OpeningPeriodTurnCount)` guard in first-scenario-selection and BuildUp backfill paths in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` ~lines 3220 and 3370.

**Checkpoint**: Prompts for turns 1-3 contain only opening guidance. Turn 4+ prompts contain full theme guidance. Old OPF block removed.

---

## Phase 5: User Story 3 - Opening period uses turn count, not interaction count (Priority: P2)

**Goal**: The opening period threshold uses `ObservedTurnCount` (turn-based) rather than `session.Interactions.Count` (interaction-based). Single constant controls all opening-period gates.

**Independent Test**: Verify the threshold check uses `ObservedTurnCount`. Verify the opening period ends precisely at turn 4 regardless of how many interactions occurred within prior turns.

### Implementation for User Story 3

- [x] T017 [US3] Verify `ObservedTurnCount` is incremented before prompt building and actor resolution in all 4 code paths (`AddInteraction`, `Continue`, `SubmitPrompt`, `ContinueAs`) in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [x] T018 [US3] Ensure the opening-period gate uses the same `OpeningPeriodTurnCount` constant across both `ContinuationService.cs` (prompt gate) and `EngineService.cs` (OtherMan exclusion) — already done via T003, T006, T010
- [x] T019 [US3] Verify no remaining references to interaction-count threshold (`< 6`, `<= 6`) for opening-period logic — confirm `openingInteractionCount` variable and `totalInteractions` variable have their own separate uses (opening narrative detection, persona lead)

**Checkpoint**: Single constant `OpeningPeriodTurnCount = 3` controls all gates. Turn-based counting throughout.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Edge cases, validation, and final verification

- [x] T020 Add edge case handling: when scenario has no `OpeningGuidanceText` (null), fall back to default constant text
- [x] T021 Add edge case handling: opening period does NOT re-run after Reset→BuildUp cycle (verify via `ObservedTurnCount` continues incrementing, no reset)
- [x] T022 [P] Add edge case handling: opening period works correctly when `AutoNarrative` is disabled (no opening narrative generated, but gate still applies)
- [x] T023 Build and verify: `dotnet build DreamGenClone.sln` with 0 errors
- [x] T024 Create new RP session and verify end-to-end: turns 1-3 have opening guidance in prompts (no theme guidance), OtherMan excluded
- [x] T025 [P] Verify turn 4+ prompt content: extract LLM prompt for turn 4 and confirm theme phase guidance is present (exposure beat directives, BuildUp framing guards, theme hard constraints) and opening-period guidance is absent

---

## Dependencies

```
Phase 1 (Setup)
├── T001 (DB column)
├── T002 (seed data, after T001)
└── T003 (constant, parallel)
      │
Phase 2 (Foundational)
├── T004 (scenario model, after T001)
└── T005 (default text constant, parallel)
      │
Phase 3 (US1) ← after Phase 2
├── T006 (OtherMan exclusion)
├── T007 (log message, after T006)
├── T008 (persona lead, parallel with T006)
└── T009 (logs, after T006-T008)
      │
Phase 4 (US2) ← after Phase 2, independent of US1
├── T010 (prompt gate)
├── T011 (load guidance, after T010)
├── T012 (suppress observer, after T010)
├── T013 (suppress framing guards, after T010)
├── T014 (remove OPF block)
├── T015 (logs, after T010-T014)
└── T016 (ActiveScenarioId guard)
      │
Phase 5 (US3) ← after US1 + US2
├── T017 (verify ObservedTurnCount)
├── T018 (verify constant reuse)
└── T019 (verify no remaining old thresholds)
      │
Phase 6 (Polish)
├── T020 (null guidance fallback)
├── T021 (Reset cycle exclusion)
├── T022 (AutoNarrative off, parallel)
├── T023 (build)
├── T024 (end-to-end verification, after T023)
└── T025 (turn 4 prompt verification, parallel with T024)
```

## Parallel Opportunities

| Phase | Parallel Tasks |
|-------|---------------|
| Phase 1 | T002 + T003 (different files) |
| Phase 2 | T004 + T005 (different files) |
| Phase 3 | T006 + T008 (different sections of same file, independent) |
| Phase 4 | T014 can run in parallel with T010-T013 (removing old code) |
| Phase 6 | T022 parallel with T020-T021; T025 parallel with T024 |

## Implementation Strategy

**MVP (User Story 1 only)**: Complete Phase 1 + Phase 2 + Phase 3. Delivers: OtherMan excluded for 3 turns, persona leads during opening. Old OPF still present but behavior is correct.

**Full Feature**: Complete all phases. Delivers: coherent opening period with scenario-level guidance, no contradictory prompts, turn-based counting, and verified prompt content.

**Files Touched** (all under `DreamGenClone.Web/Application/RolePlay/`):
- `RolePlayContinuationService.cs` — constant, prompt gate, guidance injection, OPF removal
- `RolePlayEngineService.cs` — OtherMan exclusion, persona lead, logging

**Files Touched** (Infrastructure):
- `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` — DB migration
- `DreamGenClone.Infrastructure/RolePlay/` — Scenario model loading
