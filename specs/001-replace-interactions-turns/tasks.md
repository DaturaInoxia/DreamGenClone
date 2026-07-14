# Tasks: Replace Interactions with Turns Throughout RP Engine and Data Model

**Input**: Design documents from `/specs/001-replace-interactions-turns/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/README.md, quickstart.md
**Tests**: Not requested by the spec — no test-writing tasks below. Existing tests are updated in-place as part of the renames they cover.
**Organization**: Tasks grouped by user story. Each story is independently testable per its `Independent Test` clause in `spec.md`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Exact file paths appear in every description

---

## Phase 1: Setup

**Purpose**: Establish migration safety net and verify the spec/plan artifacts are in place before touching code. No project structure changes — this feature reuses the existing 5-project layered solution.

- [x] T001 Back up `DreamGenClone.Web/data/dreamgenclone.dev.db` to `dreamgenclone.dev.db.bak-pre-turns-migration` as a one-time safety copy before any migration code runs (the migration is one-way; see `quickstart.md` §4)
- [x] T002 [P] Verify `.specify` artifacts are complete and internally consistent: `specs/001-replace-interactions-turns/{spec.md, plan.md, research.md, data-model.md, contracts/README.md, quickstart.md}` all exist and cross-reference each other

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Rename the domain fields, contract field, and constant that ALL user stories depend on. The build will not compile until Phase 3 completes these renames' downstream references, so Phase 2 + Phase 3 (US1) MUST be done as one atomic compilation unit.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete. Phases 2 and 3 are intentionally ordered so the solution compiles after T012.

- [x] T003 [P] Rename `InteractionCountInPhase` → `TurnCountInPhase`, `InteractionsSinceCommitment` → `TurnsSinceCommitment`, `InteractionsInApproaching` → `TurnsInApproaching`, `InteractionsInCurrentEncounter` → `TurnsInCurrentEncounter` in `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`
- [x] T004 [P] Rename `ThemeScoreState.CompletionCooldownInteractions` → `CompletionCooldownTurns` and `ScenarioHistoryEntry.InteractionCount` → `TurnCount` in `DreamGenClone.Domain/RolePlay/AdaptiveStateV2Records.cs`
- [x] T005 [P] Rename `EncounterSummaryRecord.InteractionCountInPhase` → `TurnCountInPhase` in `DreamGenClone.Domain/RolePlay/EncounterSummaryRecord.cs` (do NOT touch `StartInteractionIndex` / `EndInteractionIndex` — they are timeline indices, out of scope per `data-model.md` §10)
- [x] T006 [P] Rename `NarrativeGateMetricKeys.InteractionsSinceCommitment` constant → `TurnsSinceCommitment` in `DreamGenClone.Domain/RolePlay/NarrativeGateProfile.cs`
- [x] T007 [P] Rename `ScenarioMetadata.InteractionCount` → `TurnCount` (legacy V1 field) in `DreamGenClone.Domain/StoryAnalysis/ScenarioMetadata.cs`
- [x] T008 [P] Rename `LifecycleInputs.InteractionsSinceCommitment` → `TurnsSinceCommitment` in `DreamGenClone.Application/RolePlay/RolePlayContracts.cs`
- [x] T009 [P] Rename `TransitionTriggerType.InteractionCountGate` → `TurnCountGate` enum value in its declaring file under `DreamGenClone.Domain/RolePlay/` (code-only enum value; not persisted as string to DB — see `data-model.md` §7)
- [x] T010 [P] Rename `AdaptiveEarlyTurnInteractionThreshold` → `AdaptiveEarlyTurnThreshold`, `AdaptivePerInteractionTotalDeltaBudget` → `AdaptivePerTurnTotalDeltaBudget`, `CompletedScenarioThemeCooldownInteractions` → `CompletedScenarioThemeCooldownTurns`, `BuildUpMinInteractionsBeforeCommit` → `BuildUpMinTurnsBeforeCommit` properties in `DreamGenClone.Infrastructure/Configuration/StoryAnalysisOptions.cs` (conversion of default values happens in T011)

**Checkpoint**: Domain/Application renames done. Downstream Infrastructure/Web/Tests references now fail to compile — Phase 3 (US1) resolves them. Do NOT attempt to compile between Phase 2 and the end of Phase 3.

---

## Phase 3: User Story 1 — Consistent Turn-Based Phase Advancement (Priority: P1) 🎯 MVP

**Goal**: All RP engine service code, repository code, and test code compile and run against the renamed `Turn*` fields. Phase transitions trigger at the same narrative points as before because stored thresholds and counters are migrated ÷3 (ceiling). Turns is a first-class stored unit; no runtime interaction-to-turn formula in gate logic.

**Independent Test**: Start a new session with a scenario that has phase gates, advance through turns, and verify that (a) the adaptive panel shows "Turns" labels with counts that increment once per turn, (b) phase transitions trigger at the expected turn thresholds, and (c) existing session data is migrated with values divided by 3 and rounded up.

### Implementation for User Story 1

- [x] T011 [P] [US1] Update default values in `DreamGenClone.Infrastructure/Configuration/StoryAnalysisOptions.cs` for the 4 renamed properties per `data-model.md` §9: integer thresholds ÷3 ceiling (`AdaptiveEarlyTurnThreshold`, `CompletedScenarioThemeCooldownTurns`, `BuildUpMinTurnsBeforeCommit`); `AdaptivePerTurnTotalDeltaBudget` multiplied by 3 (was per-interaction, now per-turn). Confirm the exact consuming service for `AdaptivePerTurnTotalDeltaBudget` before committing the ×3 conversion (flagged in `data-model.md` §9)
- [x] T012 [US1] Update all references to `InteractionCountInPhase` / `InteractionsSinceCommitment` / `InteractionsInApproaching` / `InteractionsInCurrentEncounter` across `DreamGenClone.Infrastructure/RolePlay/DecisionPointService.cs`, `ScenarioLifecycleService.cs`, `ScenarioSelectionService.cs`, `EncounterSummaryService.cs` (from research catalog: ~30 references). Rename local variables (`interactionsGatePassed` → `turnsGatePassed`, etc.) and log parameter names to `Turn*`. Ensure NO new `interactions / 3` runtime formula is introduced on the gate evaluation path — `Turn*` fields are read directly per `research.md` R1
- [x] T013 [US1] Update `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` SQL column read/write references (~30 sites per research catalog): `InteractionCountInPhase` → `TurnCountInPhase`, `InteractionsSinceCommitment` → `TurnsSinceCommitment`, `InteractionsInApproaching` → `TurnsInApproaching`, `CompletionCooldownInteractions` → `CompletionCooldownTurns`, scenario-history `InteractionCount` → `TurnCount`, encounter-summary `InteractionCountInPhase` → `TurnCountInPhase`. KEEP `InteractionId` and timeline `Interactions` references untouched (out of scope per `data-model.md` §10)
- [x] T014 [P] [US1] Update `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (~40 references per research catalog): all `v2State.InteractionCountInPhase` reads/writes, the four reset sites on phase transition, local variables (`previousPhaseInteractionCount` → `previousPhaseTurnCount`, `proposedPhaseInteractionCount` → `proposedPhaseTurnCount`, `invariantPhaseInteractionCount` → `invariantPhaseTurnCount`), `InteractionsSinceCommitment = v2State.InteractionCountInPhase` lifecycle-input build, `HydrateV2State` mapping, and snapshot-alignment reads. Rename log message parameters (`CurrentInteractionCountInPhase` → `CurrentTurnCountInPhase`, etc.). No ÷3 formula at runtime
- [x] T015 [P] [US1] Update `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs`: `CompletionCooldownInteractions` decrement loop → `CompletionCooldownTurns` decrement, `InteractionsSinceCommitment = 0; InteractionsInApproaching = 0` reset assignments on scenario commit → `TurnsSinceCommitment = 0; TurnsInApproaching = 0`
- [x] T016 [US1] Implement SQLite schema migration in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` following the pattern at `RPThemeService.cs:4846` (`ALTER TABLE ... RENAME COLUMN`): rename 6 columns across 4 V2 tables per `data-model.md` §11 and `contracts/README.md` Contract 5 — `RolePlayV2AdaptiveStates.InteractionCountInPhase` → `TurnCountInPhase`, `.InteractionsSinceCommitment` → `TurnsSinceCommitment`, `.InteractionsInApproaching` → `TurnsInApproaching`; `RolePlayV2ThemeScores.CompletionCooldownInteractions` → `CompletionCooldownTurns`; `RolePlayV2ScenarioHistory.InteractionCount` → `TurnCount`; `RolePlayV2EncounterSummaries.InteractionCountInPhase` → `TurnCountInPhase`. Each `RENAME COLUMN` MUST be guarded by an old-column-exists + new-column-absent pragma check for idempotency
- [x] T017 [US1] Implement SQLite numeric value migration immediately after each column rename in T016's migration block in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`: `UPDATE <table> SET <new_col> = (<old_col_or_temp> + 2) / 3` (ceiling division for non-negative ints). Guard with a migration marker (SQLite `PRAGMA user_version` bump OR a `__migrations` table row) set atomically with the conversion. Migration is one-way per `research.md` R2; re-runs MUST be no-ops
- [x] T018 [P] [US1] Update `DreamGenClone.Tests/RolePlay/AdaptiveScenarioStateV2RoundTripTests.cs` field declarations and assertions: `InteractionCountInPhase = 4` → `TurnCountInPhase` (value 4 OR migrated-equivalent 2 — use post-migration turn values to assert the post-rename contract), `InteractionsSinceCommitment = 9` → `TurnsSinceCommitment` (value 3 if asserting migrated-equivalent), `InteractionsInApproaching = 1` → `TurnsInApproaching`, `CompletionCooldownInteractions = 2` → `CompletionCooldownTurns`, `InteractionCount = 8` on `ScenarioHistoryEntry` → `TurnCount`. Update all `Assert.Equal(...)` accordingly
- [x] T019 [P] [US1] Update `DreamGenClone.Tests/RolePlay/DecisionPointMutationTests.cs`: `state.InteractionCountInPhase = 3` → `state.TurnCountInPhase = 3` (turn value, not interaction value — these tests set the field directly, so the number stays 3 since it's already a unit-of-3 turn test by coincidence; verify each test's intent)
- [x] T020 [P] [US1] Update `DreamGenClone.Tests/RolePlay/EncounterSummaryServiceTests.cs`: `InteractionCountInPhase = 3` → `TurnCountInPhase` and `Assert.Equal(state.InteractionCountInPhase, r.InteractionCountInPhase)` → `TurnCountInPhase` on both sides
- [x] T021 [P] [US1] Update `DreamGenClone.Tests/RolePlay/PhaseLifecycleTransitionTests.cs`: `InteractionsSinceCommitment = ...` lifecycle-input assignments → `TurnsSinceCommitment` (values unchanged — these tests directly populate the contract, not the migrated DB)
- [x] T022 [P] [US1] Update `DreamGenClone.Tests/StoryAnalysis/ScenarioStateModelTests.cs`: `InteractionCount = 6` → `TurnCount = 6` and `Assert.Equal(6, metadata.InteractionCount)` → `metadata.TurnCount` (legacy V1 `ScenarioMetadata` field; value stays 6 since no DB migration touches this in-memory V1 model — see `data-model.md` §7)
- [x] T023 [US1] Build the full solution and resolve any remaining compile errors caused by the renames. Run the targeted test filter from `quickstart.md` §8: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~AdaptiveScenarioStateV2RoundTrip|DecisionPointMutation|EncounterSummaryService|PhaseLifecycleTransition|RolePlaySessionLifecycle|RolePlayThemeMachineCommand|ThemeMachineEvaluator|RPThemeMachineDefinitionValidation|ScenarioStateModel"`. All listed classes MUST be green (pre-existing unrelated failures per `/memories/repo/pre-existing-test-failures.md` are not blockers)

**Checkpoint**: User Story 1 fully functional and independently testable. The solution compiles, the migration runs at startup, and existing V2 sessions work against the renamed `Turn*` fields with ÷3-converted stored values. The adaptive panel still uses old labels (fixed in US3) but behavior is correct. Theme gate JSON migration (T024) is the next blocker.

---

## Phase 4: User Story 2 — Theme Data Migration and UI Theme Management (Priority: P2)

**Goal**: All theme gate configuration JSON blobs in `RPThemeMachineTransitions.GateConfigJson` are migrated from `minimumInteractions` to `minimumTurns` (value ÷3 ceiling). The theme management UI (`ThemeProfiles.razor`, `RPThemeDetail.razor`) and gate config validation in `RPThemeService` use `minimumTurns` natively, with backward-compat read of `minimumInteractions` during the transition window. Theme gate rule editor, metric selector, and help text all show "Turn"/"Turns" terminology.

**Independent Test**: Open the Theme Profiles page after migration, edit a theme's gate rules, and verify that (a) existing gate rules show `minimumTurns` in their JSON configuration, (b) the metric selector dropdown shows "Turns Since Commitment", (c) help text and labels use "Turn"/"Turns", and (d) creating a new gate rule writes `minimumTurns`.

### Implementation for User Story 2

- [x] T024 [US2] Implement theme gate JSON blob migration in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` (or a dedicated migration helper called from it, following the existing migration-call style): for each row in `RPThemeMachineTransitions` whose `GateConfigJson` contains `"minimumInteractions"` and does NOT already contain `"minimumTurns"`: parse JSON, read `minimumInteractions` int, compute `minimumTurns = max(0, (minimumInteractions + 2) / 3)` (ceiling, non-negative), remove `minimumInteractions` property, add `minimumTurns` with computed value, preserve all sibling properties (`requireReturnBeatCompleted`, `returnBeatCompletionSignals`, `returnBeatTransgressorRole`, `returnBeatPartnerRole`, etc.), write blob back. Idempotent (`contracts/README.md` Contract 3 + `data-model.md` §11). Order: AFTER T017 numeric migration, in the same startup migration pass
- [x] T025 [US2] Update `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs` gate config validation (~line 4508-4513): check for `minimumTurns` first; if absent, accept `minimumInteractions` as legacy fallback and divide by 3 (ceiling) for validation only. ValidationError message text MUST reference `minimumTurns` (e.g., `"... cooldown gate config must include integer minimumTurns >= 0."`). NEW gate writes always use `minimumTurns`. Also verify `RPThemeProfiles.ThemeSelectionTurnsPerTheme` column exists (pre-migrated in prior feature — no-op assertion only, do NOT re-migrate; per `spec.md` FR-008(c))
- [x] T026 [US2] Update `DreamGenClone.Infrastructure/RolePlay/ThemeMachineEvaluator.cs` cooldown read path (~line 281-304): prefer `minimumTurns` JSON property; fall back to `minimumInteractions` ÷3 ceiling for un-migrated rows (the ONLY permitted runtime interaction-to-turn conversion, on legacy-read path only — `research.md` R5). Rename local variable `interactionsGatePassed` → `turnsGatePassed`. Update the validation error message at ~line 287 to reference `minimumTurns`
- [x] T027 [P] [US2] Update `DreamGenClone.Tests/RolePlay/RolePlaySessionLifecycleTests.cs` (lines 146, 189): `GateConfigJson = "{\"minimumInteractions\":4,...}"` → `"{\"minimumTurns\":...}"` with value ÷3 ceiling. Assess each test's intent: if the test asserts a threshold of 4 interactions, the post-rename equivalent is `minimumTurns: 2` (4 ÷ 3 = 2 ceiling). Update expected phase-advance outcomes to match the converted turn threshold — the narrative pacing should be equivalent per `research.md` R2
- [x] T028 [P] [US2] Update `DreamGenClone.Tests/RolePlay/RolePlayThemeMachineCommandTests.cs` (4 sites: lines ~86, 122, 158, 199): `minimumInteractions = 3` in gate config JSON → `minimumTurns = 1` (3 ÷ 3 = 1 ceiling). Verify each test's intent and adjust expected outcomes if the test asserts a specific count of transitions within a specific number of turns
- [x] T029 [P] [US2] Update `DreamGenClone.Tests/RolePlay/ThemeMachineEvaluatorTests.cs` (lines ~82, 158): `minimumInteractions = 3` in gate JSON → `minimumTurns = 1`. Confirm `TurnsInCurrentState` assertions still hold (this test already uses `TurnsInCurrentState` — only the gate JSON key + value change)
- [x] T030 [P] [US2] Update `DreamGenClone.Tests/RolePlay/RPThemeMachineDefinitionValidationTests.cs` (lines ~124, 164): `GateConfigJson = "{\"minimumInteractions\":3,...}"` → `"{\"minimumTurns\":1,...}"`. Update validation-message assertions that reference `minimumInteractions` to expect `minimumTurns` text
- [x] T031 [US2] Update UI gate rule editor metric selector and help text in `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor` (lines ~1170, 3143, 5298, 5383): replace `NarrativeGateMetricKeys.InteractionsSinceCommitment` references with `NarrativeGateMetricKeys.TurnsSinceCommitment` (the constant was renamed in T006). Update help text at ~line 2005 from `"Stored as the Climax -> Reset rule using InteractionsSinceCommitment"` → `"... using TurnsSinceCommitment"`. Update any visible dropdown labels from "Interactions Since Commitment" → "Turns Since Commitment"
- [x] T032 [P] [US2] Update UI gate rule editor references and help text in `DreamGenClone.Web/Components/Pages/RPThemeDetail.razor` (line ~1170): `NarrativeGateMetricKeys.InteractionsSinceCommitment` → `NarrativeGateMetricKeys.TurnsSinceCommitment`. Update any visible labels and help text to use "Turns" terminology
- [x] T033 [US2] Build solution. Manually verify per `quickstart.md` §5.3: query `SELECT TransitionId FROM RPThemeMachineTransitions WHERE GateConfigJson LIKE '%minimumInteractions%'` returns 0 rows after migration runs on the dev DB. Verify `SELECT TransitionId FROM RPThemeMachineTransitions WHERE GateConfigJson NOT LIKE '%minimumTurns%'` returns 0 rows. Run the US2 test classes from the `quickstart.md` §8 filter

**Checkpoint**: User Stories 1 AND 2 now both work independently. Theme gate JSON is fully migrated and the theme editor UI uses turn-based naming throughout. The adaptive panel (US3) still shows old labels — fixed next.

---

## Phase 5: User Story 3 — Adaptive Panel and Configuration UI Labels (Priority: P2)

**Goal**: All user-facing labels in the RP workspace adaptive panel and the debug panel that previously used "interaction" for phase-advancement counts now use "turn" terminology. No behavioral change — pure label/variable/parameter renames in Blazor components.

**Independent Test**: Open the RP workspace adaptive panel during a live session. Verify all phase progress labels use "Turns" (e.g., "Turns 5/12"). Open the debug panel and verify gate evaluation displays use turn-based labels.

### Implementation for User Story 3

- [x] T034 [P] [US3] Update `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` (~15 references per research catalog): adaptive panel phase-counter reads (`v2State?.InteractionCountInPhase ?? state.InteractionCountInPhase` → `TurnCountInPhase`; `state.InteractionsSinceCommitment` → `TurnsSinceCommitment`; `state.InteractionsInApproaching` → `TurnsInApproaching`), gate evaluation helper variables (`interactionCount` → `turnCount`, `committedInteractions` → `committedTurns`, `interactionGap` → `turnGap`, `interactionsMet` → `turnsMet`), visible labels `"Interactions {committedInteractions}/{thresholds.InteractionsMin}"` → `"Turns {committedTurns}/{thresholds.TurnsMin}"` (verify the `thresholds.InteractionsMin` property name on the threshold DTO — rename to `TurnsMin` if it lives in a renamed model; otherwise update the DTO property too), metric key reference `[NarrativeGateMetricKeys.InteractionsSinceCommitment]` → `[NarrativeGateMetricKeys.TurnsSinceCommitment]`, helper method/parameter names `GetEffectiveInteractionsSinceCommitment` → `GetEffectiveTurnsSinceCommitment`, `GetEffectiveInteractionsInApproaching` → `GetEffectiveTurnsInApproaching`, `BuildCommittedProgress(..., int interactionsSinceCommitment)` → `BuildCommittedProgress(..., int turnsSinceCommitment)`, `BuildApproachingProgress(..., int interactionsInApproaching)` → `BuildApproachingProgress(..., int turnsInApproaching)`. Do NOT touch `_lastRenderedInteractionCount` (UI render-tracking, out of scope per `data-model.md` §10) or `interactionCount = _session?.Interactions.Count` (timeline count, out of scope)
- [x] T035 [P] [US3] Update `DreamGenClone.Web/Components/Pages/RolePlayDebug.razor`: debug model property `public int InteractionCount { get; set; }` (~line 859) → `TurnCount`; JSON parse key `"interactionCount"` (~line 752) → `"turnCount"`; gate-details display reference `gateDetails.InteractionCount` (~line 368) → `gateDetails.TurnCount`; visible label text updated to "Turns"
- [x] T036 [P] [US3] Verify `DreamGenClone.Web/Components/Pages/Home.razor` (lines 43, 70) and `DreamGenClone.Web/Components/Pages/RolePlaySessionsList.razor` (line 47) — these display `@session.InteractionCount interactions` referring to the canonical session timeline message count (out of scope per `data-model.md` §10 and `spec.md` FR-013). Do NOT change. Document the verification in the implementation note: "Confirmed out-of-scope — refers to `RolePlaySession.Interactions.Count` (timeline), not phase counter"
- [x] T037 [US3] Build solution. Run the web app and manually verify the adaptive panel renders "Turns X/Y" labels for both committed-scenario progress and the Approaching phase counter. Open the debug panel and verify `gateDetails.TurnCount` displays with "Turns" label

**Checkpoint**: User Stories 1, 2, AND 3 all work independently. UI labels are fully consistent with the renamed engine. Only prompt-injection diagnostic text and log message parameter names remain (US4).

---

## Phase 6: User Story 4 — Prompt and Log Message Consistency (Priority: P3)

**Goal**: Internal log messages, prompt injection diagnostic text, and encounter summary templates that reference "interaction" when meaning "turn" are updated to "turn" terminology. Diagnostic-only changes; no user-visible behavior change.

**Independent Test**: Run a session with verbose logging enabled, trigger phase transitions, and inspect log output. Verify that phase-related log messages use "turn" (e.g., "TurnCountInPhase=5") rather than "interaction". Inspect encounter summary text for correct terminology.

### Implementation for User Story 4

- [x] T038 [P] [US4] Update log message text and structured-parameter names in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`: `"RolePlayV2 phase interaction count invariant clamp applied"` (~line 3131) → `"... turn count invariant clamp applied"`; `"CurrentInteractionCountInPhase={CurrentInteractionCountInPhase}"` (~line 3465) → `"CurrentTurnCountInPhase={CurrentTurnCountInPhase}"` with the parameter bound to the renamed `v2State.TurnCountInPhase` field. Capture any other `*Interaction*` substrings in log messages that refer to phase counters and rename to `*Turn*` equivalents. KEEP log messages that refer to the canonical timeline (`Interactions` list count) unchanged
- [x] T039 [P] [US4] Update `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryService.cs` encounter summary template text (~line 251): `"interaction {v2State.InteractionCountInPhase} in phase"` → `"turn {v2State.TurnCountInPhase} in phase"`. Update field reads at lines ~79, 191 (`InteractionCountInPhase = v2State.InteractionCountInPhase` → `TurnCountInPhase = v2State.TurnCountInPhase`)
- [x] T040 [P] [US4] Update `DreamGenClone.Web/Application/Assistants/RolePlayAssistantPrompts.cs` diagnostic label at ~line 315: `"Cooldown interactions in current state: {snapshot.TurnsInCurrentState}"` → `"Cooldown turns in current state: {snapshot.TurnsInCurrentState}"` (the value already reads from the turn-based `TurnsInCurrentState` field — only the label text is wrong). Do NOT touch the other ~6 references in this file that document the canonical `RolePlayInteraction` timeline (`data-model.md` §10) — lines ~57, 65, 66, 67, 78
- [x] T041 [US4] Build solution. Run a session with `appsettings.Development.json` `"RolePlay": "Verbose"` (or equivalent Serilog log-level override). Trigger a phase transition. Confirm log output contains `TurnCountInPhase` and `TurnsSinceCommitment` parameter names and ZERO hits for `InteractionCountInPhase` or `InteractionsSinceCommitment`. Verify encounter summary text reads "turn X in phase"

**Checkpoint**: All four user stories complete. The rename is consistent across domain, application, infrastructure, web, and tests layers. Log/diagnostic output uses "Turn" terminology throughout.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Cleanup and final validation across all stories.

- [x] T042 [P] Grep the codebase for any remaining `*Interaction*` substrings in phase-advancement code paths (exclude the documented out-of-scope list in `data-model.md` §10): `grep -rn "InteractionCountInPhase\|InteractionsSinceCommitment\|InteractionsInApproaching\|CompletionCooldownInteractions\|minimumInteractions\|InteractionCountGate" DreamGenClone.* --include=*.cs --include=*.razor`. Expected: zero hits in renamed paths; any hits must be in the out-of-scope list (timeline `Interactions`, `InteractionId`, `InteractionType`, `InteractionEvidenceSignal`, `Start/EndInteractionIndex`, `_lastRenderedInteractionCount`, CSS classes). Add the out-of-scope entries to a comment block in `SqlitePersistence.cs` near the migration code for future maintainers
- [x] T043 [P] Verify `appsettings.json` and `appsettings.Development.json` in `DreamGenClone.Web/`: if any of the 4 renamed `StoryAnalysis:Adaptive:*` keys are explicitly set, update them to the new key names with ÷3 ceiling values (×3 for `AdaptivePerTurnTotalDeltaBudget`). If only defaults are used, no file changes needed — document this in the implementation note. Per `quickstart.md` §9 the old keys silently stop binding
- [x] T044 [P] Run the full `quickstart.md` §5 verification SQL suite against `DreamGenClone.Web/data/dreamgenclone.dev.db` via the dbquery tool: §5.1 (0 rows with `*Interaction*` columns on V2 tables), §5.2 (6 new `*Turn*` columns exist), §5.3 (0 rows with `minimumInteractions`, 0 rows missing `minimumTurns`), §5.5 (`ThemeSelectionTurnsPerTheme` column exists)
- [x] T045 [P] Run the full targeted test filter from `quickstart.md` §8: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~AdaptiveScenarioStateV2RoundTrip|DecisionPointMutation|EncounterSummaryService|PhaseLifecycleTransition|RolePlaySessionLifecycle|RolePlayThemeMachineCommand|ThemeMachineEvaluator|RPThemeMachineDefinitionValidation|ScenarioStateModel"`. All listed classes MUST be green. Pre-existing unrelated failures (per `/memories/repo/pre-existing-test-failures.md`) are not blockers but MUST be separately confirmed unchanged
- [x] T046 Run the full `quickstart.md` §6 runtime UI verification pass: adaptive panel labels, ThemeProfiles metric selector, RPThemeDetail help text, save-and-reopen a gate rule to confirm JSON contains `minimumTurns`
- [x] T047 [P] Update the backlog entry for B-044 in `specs/Planning/backlog.md`: change state from `designed` → `implemented` once T001–T046 are complete; add `Tasks: specs/001-replace-interactions-turns/tasks.md` to the notes column
- [x] T048 [P] Record a repository memory note at `/memories/repo/roleplay-turns-first-class.md` documenting the post-migration invariant: "Turns is the first-class stored unit for phase advancement. Gate JSON uses `minimumTurns`. The `minimumInteractions` read path is dead code retained only for the transition window — safe to remove in a future cleanup. Interaction counts on `RolePlaySession.Interactions` are the timeline, NOT phase counters." This prevents regressions in future sessions

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately. T001 (DB backup) MUST complete before T016/T017/T024 run on the dev DB
- **Foundational (Phase 2)**: Independent of Phase 1; all T003–T010 are parallelizable across separate files
- **User Story 1 (Phase 3)**: Depends on Phase 2. T011–T022 are mostly parallel; T012 (Infrastructure service updates) is the long-pole with ~30 references. T023 (build + test) BLOCKS on T011–T022 — do not attempt to build until all upstream US1 tasks are done
- **User Story 2 (Phase 4)**: Depends on Phase 3 (US1) because T024 writes to the same `RolePlayV2*` migration block as T016/T017 and relies on the renamed schema; T025/T026 update engine code that reads the migrated JSON. T024 MUST run after T017 in the startup migration pass
- **User Story 3 (Phase 5)**: Depends on Phase 3 (US1) for the renamed domain fields and metric-key constant. Independent of US2. T034/T035 are parallel; T036 is verification-only
- **User Story 4 (Phase 6)**: Depends on Phase 3 (US1) for the renamed fields. Independent of US2/US3 at the file level, but T038 touches `RolePlayEngineService.cs` which is also modified by T014 — coordinate to avoid merge conflicts (T038 can be folded into T014 if done by the same implementer)
- **Polish (Phase 7)**: Depends on Phases 3–6 all complete. T042–T048 are parallel

### User Story Dependencies

- **US1 (P1, MVP)**: Can start after Phase 2. No dependencies on other stories. BLOCKS US2 (theme JSON migration reads the renamed schema) and US3 (adaptive panel reads renamed fields)
- **US2 (P2)**: Depends on US1. Independently testable via `quickstart.md` §5.3 SQL queries + theme editor UI walkthrough
- **US3 (P2)**: Depends on US1. Independent of US2 at the file level (different `.razor` files). Independently testable via adaptive panel + debug panel walkthrough
- **US4 (P3)**: Depends on US1. Overlaps with US1's T014 file (`RolePlayEngineService.cs`) — coordinate to avoid conflicts. Independently testable via log inspection

### Within Each User Story

- Domain/Application/Infrastructure renames come first (Phase 2)
- Service code updates next (Phase 3: T012 → T013 → T014/T015)
- Migration SQL next (Phase 3: T016 → T017; Phase 4: T024 after T017)
- Tests updated last within each story (T018–T022 for US1; T027–T030 for US2)
- Build + test checkpoint closes each story (T023, T033, T037, T041)

### Parallel Opportunities

- **Phase 2 (Setup/domain renames)**: T003–T010 are all [P] — different files, no inter-dependencies. 8 tasks parallelizable
- **Phase 3 (US1)**:
  - T011 (config), T014 (`RolePlayEngineService.cs`), T015 (`RolePlayAdaptiveStateService.cs`) are [P] — different files
  - T018, T019, T020, T021, T022 (test updates) are all [P] — different test files
  - T012 (`Infrastructure/RolePlay/* services`) and T013 (`RolePlayStateRepository.cs`) are SEQUENTIAL to each other if the same implementer handles both — they reference shared types but in different files; parallelizable if split across implementers
  - T016 → T017 are SEQUENTIAL (T17 updates values after T16 renames columns)
- **Phase 4 (US2)**:
  - T027, T028, T029, T030 (test updates) are [P] — different test files
  - T031 (`ThemeProfiles.razor`) and T032 (`RPThemeDetail.razor`) are [P] — different files
  - T024 → T025 → T026 are SEQUENTIAL (migration → validation → evaluator read)
- **Phase 5 (US3)**: T034, T035, T036 are [P] — different files
- **Phase 6 (US4)**: T038, T039, T040 are [P] — different files. T038 overlaps `RolePlayEngineService.cs` with T014 — fold into T014 if the same implementer handles both to avoid merge conflicts
- **Phase 7 (Polish)**: T042, T043, T044, T045, T047, T048 are [P] — independent verification/cleanup tasks

---

## Parallel Example: User Story 1

```bash
# Parallel track A: domain + config renames (Phase 2)
T003 AdaptiveScenarioState.cs &
T004 AdaptiveStateV2Records.cs &
T005 EncounterSummaryRecord.cs &
T006 NarrativeGateProfile.cs &
T007 ScenarioMetadata.cs &
T008 RolePlayContracts.cs &
T009 TransitionTriggerType enum &
T010 StoryAnalysisOptions.cs &
wait

# Parallel track B: service code + engine code + tests (after Phase 2)
T011 StoryAnalysisOptions defaults &  # depends on T010
T012 Infrastructure/RolePlay/* services &  # long pole
T013 RolePlayStateRepository.cs &
T014 RolePlayEngineService.cs &
T015 RolePlayAdaptiveStateService.cs &
T018 AdaptiveScenarioStateV2RoundTripTests.cs &
T019 DecisionPointMutationTests.cs &
T020 EncounterSummaryServiceTests.cs &
T021 PhaseLifecycleTransitionTests.cs &
T022 ScenarioStateModelTests.cs &
wait

# Sequential: migration code (requires T012/T013 done for compile context)
T016 column renames
T017 value ÷3 migration   # sequential after T016

# Sequential checkpoint
T023 build + targeted tests
```

---

## Implementation Strategy

**MVP scope**: User Story 1 (Phase 3). Once T001–T022 are complete and T023 passes, the engine and migration work end-to-end. The adaptive panel still displays old labels (fixed by US3) but behavior is correct — this is a safe stopping point for a first review/build.

**Incremental delivery order**:
1. **US1 first** (Phase 3) — renames + migration + tests. Long-pole: T012 (Infrastructure service updates, ~30 references). Do NOT split Phase 2 from Phase 3 across separate builds — the solution does not compile in the intermediate state.
2. **US2 next** (Phase 4) — theme JSON blob migration + `RPThemeService` validation + `ThemeMachineEvaluator` read path. Database verification (`quickstart.md` §5.3) closes this story. Critical to do immediately after US1 because the dev DB is in a partially-migrated state (columns renamed, values ÷3, but gate JSON blobs still say `minimumInteractions`) until T024 runs.
3. **US3 in parallel with US2** (Phase 5) — adaptive panel and debug panel labels. Different files from US2; parallelizable across implementers.
4. **US4 last** (Phase 6) — prompts/logs. Developer-facing only; can be folded into US1's `RolePlayEngineService.cs` work (T014 + T038) if the same implementer handles both.
5. **Polish** (Phase 7) — verification queries, full test filter, backlog update, memory note.

**Risk areas**:
- **T011 `AdaptivePerTurnTotalDeltaBudget` ×3 conversion direction**: `data-model.md` §9 flags this as the only config option where the multiplication direction is inverted (per-turn budget = per-interaction budget × 3, not ÷3). Confirm against the consuming service before committing.
- **T016/T017 migration idempotency**: each `RENAME COLUMN` and each `UPDATE` MUST be guarded so re-starts are no-ops. Use `PRAGMA user_version` bump or a `__migrations` row, set atomically with the value conversion. Migration is one-way — see `quickstart.md` §4.
- **T024 JSON blob rewrite**: preserve ALL sibling properties in each blob. Idempotency check (`NOT LIKE '%minimumTurns%' AND LIKE '%minimumInteractions%'`) prevents double-conversion on re-run.
- **T027–T030 test value updates**: each test's intent must be assessed — does the test assert "threshold of N interactions" or "threshold of N units"? Post-migration, the stored value IS turns. The narrative pacing should be equivalent (a threshold of 9 interactions → 3 turns → same effective phase-advance point), but the test's numerical assertions change.
- **`RolePlayWorkspace.razor` T034**: the `thresholds.InteractionsMin` property (if it lives on a threshold DTO) must be renamed too. Verify the DTO definition; if it lives in a Domain/Infrastructure file not covered by T003–T010, add an explicit rename task.
- **Pre-existing test failures** (`/memories/repo/pre-existing-test-failures.md`): ~61 unrelated failures (DB schema gaps for `DirectiveText`, `MaxConcurrentJobs` columns; `SceneWritingDirectivePromptTests` asserting removed Climax guards). These are NOT blockers for this feature — confirm they remain unchanged and do not mask new failures from the rename.

**Out-of-scope guard** (per `spec.md` FR-013 + `data-model.md` §10): the following `*Interaction*` references MUST NOT be renamed — they refer to the canonical `RolePlayInteraction` timeline (individual AI messages), not phase-advancement units:
- `RolePlayInteraction` entity, `RolePlaySession.Interactions` list, `InteractionId`, `InteractionType` enum
- `IInteractionCommandService`, `IInteractionRetryService`, `InteractionCommandService`
- `ISemanticInteractionAnalysisRepository`, `SemanticInteractionAnalysisJobHandler`, `SemanticInteractionAnalysisState`
- `DecisionTrigger.InteractionStart` enum value
- `InteractionEvidenceSignal` (keyword-hit accumulator on `AdaptiveStateV2`)
- `PinnedInteractionCount`, `OutputInteractionCount`, `OutputInteractionIdsJson`
- `EncounterSummaryRecord.StartInteractionIndex`, `EndInteractionIndex`
- `_lastRenderedInteractionCount`, CSS classes `rw-interaction`, `rw-interaction-pending`, `rw-interaction-body`
- `Home.razor` and `RolePlaySessionsList.razor` session-list "N interactions" labels (timeline message counts)

---

## Format Validation

All tasks above follow the checklist format: `- [ ] [TaskID] [P?] [Story?] Description with file path`. Specifically:
- Every task starts with `- [ ]`
- Every task has a sequential Task ID (T001–T048)
- `[P]` appears only on parallelizable tasks (different files, no dependencies on incomplete tasks)
- `[US1]`/`[US2]`/`[US3]`/`[US4]` appears on every task in the corresponding user story phase; Setup (Phase 1), Foundational (Phase 2), and Polish (Phase 7) tasks have NO story label per the format rules
- Every task description includes a specific file path (or, for T023/T033/T037/T041/T045/T046, a specific verification procedure)

**Total tasks**: 48
**Per user story**: US1 = 12 tasks (T011–T022 + T023), US2 = 10 tasks (T024–T033), US3 = 4 tasks (T034–T037), US4 = 4 tasks (T038–T041)
**Setup**: 2 tasks (T001–T002), **Foundational**: 8 tasks (T003–T010), **Polish**: 7 tasks (T042–T048)
**Parallel opportunities**: Phase 2 (8 tasks), Phase 3 tests (5 tasks), Phase 3 implementation (5 tasks), Phase 4 tests (4 tasks), Phase 4 UI (2 tasks), Phase 5 (3 tasks), Phase 6 (3 tasks), Phase 7 (6 tasks) — 36 of 48 tasks parallelizable