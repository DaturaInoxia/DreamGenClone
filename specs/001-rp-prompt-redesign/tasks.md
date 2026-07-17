---
description: "Task list for RP Prompt Redesign feature implementation"
---

# Tasks: RP Prompt Redesign

**Input**: Design documents from `/specs/001-rp-prompt-redesign/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/
**Branch**: `001-rp-prompt-redesign`

**Tests**: Tests are included — the spec mandates independent slot testability (FR-036, SC-008), legacy removal verification (SC-010), and 6-dimension enrichment validation (SC-009). The existing RP test suite (70+ files in `DreamGenClone.Tests/RolePlay/`) is the established pattern.

**Organization**: Tasks are grouped by user story (P1 → P2 → P3) to enable independent implementation and testing of each story. The 17-slot architecture is frozen by spec contract; slot tasks are distributed across the user stories that exercise them.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

This is a layered .NET 9 web application. Paths use the repository's existing project structure:

- **Domain enums**: `DreamGenClone.Domain/RolePlay/`
- **Application logic (slots, builder, context)**: `DreamGenClone.Web/Application/RolePlay/Prompts/`
- **Domain session entity**: `DreamGenClone.Web/Domain/RolePlay/`
- **Infrastructure config/persistence**: `DreamGenClone.Infrastructure/Configuration/`, `DreamGenClone.Infrastructure/Persistence/`
- **Tests**: `DreamGenClone.Tests/RolePlay/Prompts/`
- **DI registration**: `DreamGenClone.Web/Program.cs`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Domain enums, config options, and project scaffolding for the new prompt architecture.

- [ ] T001 [P] Create `PromptZone` enum (A, B, C) in `DreamGenClone.Domain/RolePlay/PromptZone.cs`
- [ ] T002 [P] Create `PromptVariant` enum (Character, Narrative) in `DreamGenClone.Domain/RolePlay/PromptVariant.cs`
- [ ] T003 [P] Create `ActorProfileKind` enum (Player, NpcPresent, NpcNonPresent, Narrative, Custom) in `DreamGenClone.Domain/RolePlay/ActorProfileKind.cs`
- [ ] T004 [P] Create `PromptSlotId` enum (17 slots + WorldState conditional sub-slot) in `DreamGenClone.Domain/RolePlay/PromptSlotId.cs` per data-model.md frozen contract
- [ ] T005 [P] Create `RolePlayPromptOptions` in `DreamGenClone.Infrastructure/Configuration/RolePlayPromptOptions.cs` with `RecommendedInitialMaxPromptChars` (35000) and recommended compression-threshold seed values — used ONLY by session-creation seeder, never by runtime prompt builder

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story slot can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T006 Add nullable session-scoped config properties (`MaxPromptChars`, `ContextWindowTurns`, `ScenarioCompressionTurnThreshold`, `HistoryFullDetailTurnBand`, `HistoryNarrativeOnlyTurnBand`, `SessionMemoryLongTermTurnThreshold`) to `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`
- [ ] T007 Add idempotent `ALTER TABLE Sessions` migrations for the 6 new columns in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` (follow existing `MaxMilestonesToInject` pattern at `:1227-1235`)
- [ ] T008 Create `PhaseRuleOfThumb` table migration + 6-row seed (Opening, BuildUp, Committed, Approaching, Climax, Reset) in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- [ ] T009 [P] Create `IPhaseRuleOfThumbRepository` interface + SQLite implementation in `DreamGenClone.Infrastructure/Persistence/` with `GetByPhaseAsync(string phase)`
- [ ] T010 Extend `SessionService.SaveRolePlayAsync` in `DreamGenClone.Web/Application/Sessions/` to persist the 6 new session columns
- [ ] T011 Extend session-creation path (`CreateRolePlaySessionRequest` / `SessionService`) to seed new sessions with `RolePlayPromptOptions` recommended values (MaxPromptChars=35000, etc.) — seeding only, NOT runtime defaults
- [ ] T012 [P] Create `IPromptSlot` interface in `DreamGenClone.Web/Application/RolePlay/Prompts/IPromptSlot.cs` per `contracts/prompt-slot-contract.md`
- [ ] T013 [P] Create `PromptBuildContext` immutable record in `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs` per `contracts/prompt-build-context.md` (includes `ResolvedScenarioData`, `ResolvedThemeData`, `ResolvedIntensityData`, `ResolvedWritingStyleData`, `WorldStateData` sub-records)
- [ ] T014 [P] Create `ActorProfile` record in `DreamGenClone.Web/Application/RolePlay/Prompts/ActorProfile.cs` per `contracts/actor-profile-contract.md`
- [ ] T015 Create `ActorProfileResolver` in `DreamGenClone.Web/Application/RolePlay/Prompts/ActorProfileResolver.cs` implementing resolution rules from `contracts/actor-profile-contract.md` (uses `RolePlayScenePresenceHelper`; fail-fast on unknown actor)
- [ ] T016 [P] Create `PromptBudgetEnforcer` in `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBudgetEnforcer.cs` per `contracts/token-budget-contract.md` (trim priority order, never-trim invariants, critical-overflow path)
- [ ] T017 Create `RolePlayPromptBuilder` in `DreamGenClone.Web/Application/RolePlay/Prompts/RolePlayPromptBuilder.cs` — receives `IEnumerable<IPromptSlot>`, sorts by Zone then Order, runs slots, enforces budget, logs at Information/Warning/Critical per FR-030/FR-037. Startup assertion: exactly 17 distinct slots registered with frozen Zone/Order.
- [ ] T018 [P] Create `SlotText` and `BudgetEnforcementResult` records in `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBudgetEnforcer.cs` (same file as T016)
- [ ] T019 [P] Create test directory `DreamGenClone.Tests/RolePlay/Prompts/` with `GlobalUsings`-compatible imports

**Checkpoint**: Foundation ready — slot implementation can now begin per user story.

---

## Phase 3: User Story 1 — Scene-Grounded Character Continuation (Priority: P1) 🎯 MVP

**Goal**: Replace the opening "You are continuing..." boilerplate with immediate scene grounding (location + phase), actor assignment, turn context, and scene location lock. Implement the Character variant of the core Zone A slots and the actor-aware Character Data slot. Wire the new builder into `RolePlayContinuationService` and delete the legacy `BuildPromptAsync` path.

**Independent Test**: Start any role-play session, advance several turns, and continue as a character. Verify the prompt opens with current scene location + phase, then "Continue as: {name} ({role})", with no "You are continuing an interactive role-play scene" text. Verify actor-inappropriate character data is filtered to comparison-only references.

### Tests for User Story 1

- [ ] T020 [P] [US1] Create `SlotContractTests.cs` scaffold in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` with shared `PromptBuildContext` test fixture builder
- [ ] T021 [P] [US1] Contract test for `SceneAnchorSlot` in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` (FR-005, FR-036, SC-008)
- [ ] T022 [P] [US1] Contract test for `ActorAssignmentSlot` in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` (FR-006, FR-036)
- [ ] T023 [P] [US1] Contract test for `TurnContextSlot` in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` (FR-007, FR-036)
- [ ] T024 [P] [US1] Contract test for `SceneLocationLockSlot` in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` (FR-008, FR-036)
- [ ] T025 [P] [US1] Contract test for `CharacterDataSlot` in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` (FR-010, FR-011, FR-036)
- [ ] T026 [P] [US1] Create `ActorProfileResolverTests.cs` in `DreamGenClone.Tests/RolePlay/Prompts/ActorProfileResolverTests.cs` covering 5 profiles × variant matrix + fail-fast on unknown actor
- [ ] T027 [P] [US1] Create `LegacyRemovalTests.cs` in `DreamGenClone.Tests/RolePlay/Prompts/LegacyRemovalTests.cs` asserting `BuildPromptAsync` is deleted from `RolePlayContinuationService.cs` (SC-010) and `IPromptInjector`/`SceneDirectionCoordinator` are deleted
- [ ] T028 [P] [US1] Create `PromptBuilderTests.cs` scaffold in `DreamGenClone.Tests/RolePlay/Prompts/PromptBuilderTests.cs` with end-to-end Character-variant build test asserting Zone A ordering and no "You are continuing..." header

### Implementation for User Story 1

- [ ] T029 [P] [US1] Implement `SceneAnchorSlot` (Slot 1, Zone A) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/SceneAnchorSlot.cs` — location + phase one-liner replacing "You are continuing..." header (FR-005)
- [ ] T030 [P] [US1] Implement `ActorAssignmentSlot` (Slot 2, Zone A) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/ActorAssignmentSlot.cs` — "Continue as: {name} ({role})" for Character variant (FR-006)
- [ ] T031 [P] [US1] Implement `TurnContextSlot` (Slot 3, Zone A) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/TurnContextSlot.cs` — turn number, response position, pacing-aware position guidance (FR-007); replaces duplicate `TurnContextInjector`
- [ ] T032 [P] [US1] Implement `SceneLocationLockSlot` (Slot 4, Zone A) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/SceneLocationLockSlot.cs` — hard constraint: current location + continuity rule (FR-008)
- [ ] T033 [P] [US1] Implement `CharacterDataSlot` (Slot 5, Zone B, trimmable) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/CharacterDataSlot.cs` — actor-aware filtering (full self + partners, comparison-only for non-present), merged appearance + behavioral text (FR-010, FR-011)
- [ ] T034 [US1] Refactor `RolePlayContinuationService` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` to delegate prompt construction to `RolePlayPromptBuilder` — delete the ~1,400-line `BuildPromptAsync` method (SC-010)
- [ ] T035 [US1] Delete `IPromptInjector` interface, `SceneDirectionCoordinator`, `PromptInjectionContext`, and the 13 injector implementations in `DreamGenClone.Web/Application/RolePlay/` (FR-028, R5) — content absorbed into slots
- [ ] T036 [US1] Register `IPromptSlot` implementations (Slots 1-5) and `RolePlayPromptBuilder` in `DreamGenClone.Web/Program.cs` DI (R11)
- [ ] T037 [US1] Add Information logs for prompt build call path in `RolePlayPromptBuilder` (FR-037: SessionId, Actor, Phase, Chars, SlotsFired) and Debug logs in each Zone A slot
- [ ] T038 [US1] Validate build: `dotnet build DreamGenClone.sln` and run `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlay.Prompts"`

**Checkpoint**: User Story 1 fully functional — Character-variant prompts open with scene grounding, actor filtering works, legacy path deleted.

---

## Phase 4: User Story 6 — Content Deduplication (Priority: P1)

**Goal**: Implement the Zone C directive slots (Theme Contract, Behavioral Frames, Final Instruction) so each content category appears exactly once. Complete the deduplication mandate by ensuring no coordinator-injected duplicates remain.

**Independent Test**: Build any prompt and search for duplicate blocks. Verify theme contract, behavioral frames, turn context, intensity directives, and final instruction each appear exactly once.

### Tests for User Story 6

- [ ] T039 [P] [US6] Contract test for `ThemeContractSlot` in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` (FR-018, FR-027, FR-036)
- [ ] T040 [P] [US6] Contract test for `BehavioralFramesSlot` in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` (FR-019, FR-027, FR-036)
- [ ] T041 [P] [US6] Contract test for `FinalInstructionSlot` Character variant in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` (FR-023, FR-027, FR-036)
- [ ] T042 [P] [US6] Add deduplication assertion tests to `PromptBuilderTests.cs` — each content category appears exactly once (FR-027, SC-002)

### Implementation for User Story 6

- [ ] T043 [P] [US6] Implement `ThemeContractSlot` (Slot 12, Zone C) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/ThemeContractSlot.cs` — active theme + phase guidance + directives + steering rank, exactly once (FR-018); absorbs `ThemeContractInjector` + `ThemeAIGuidanceInjector`
- [ ] T044 [P] [US6] Implement `BehavioralFramesSlot` (Slot 13, Zone C, non-present trimmable) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/BehavioralFramesSlot.cs` — filtered by actor, NPC agency lives here, exactly once (FR-019); absorbs `BehavioralFrameInjector` + `HusbandAftermathInjector`
- [ ] T045 [P] [US6] Implement `FinalInstructionSlot` (Slot 17, Zone C) Character variant in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/FinalInstructionSlot.cs` — POV, word target, variant constraints, last content before generation (FR-023); absorbs `FinalDirectiveInjector`
- [ ] T046 [US6] Register Slots 12, 13, 17 in `DreamGenClone.Web/Program.cs` DI
- [ ] T047 [US6] Verify no residual `IPromptInjector` registrations remain in `DreamGenClone.Web/Program.cs` (FR-028)

**Checkpoint**: User Stories 1 AND 6 both work — Character-variant prompts are deduplicated and scene-grounded.

---

## Phase 5: User Story 2 — Narrative Scene Synthesis (Priority: P1)

**Goal**: Implement the Narrative variant as a first-class prompt variant. Every slot branches on `PromptVariant.Narrative` to produce omniscient, third-person, zero-dialogue content with no POV persona injection.

**Independent Test**: In any session with multiple characters, generate a Narrative response. Verify: no POV persona text, lighter character data format for all characters, final instruction specifies third-person omniscient with zero-dialogue constraint, output is unified scene synthesis.

### Tests for User Story 2

- [ ] T048 [P] [US2] Add Narrative-variant contract tests for `ActorAssignmentSlot`, `CharacterDataSlot`, `BehavioralFramesSlot`, `FinalInstructionSlot` in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` (FR-002, FR-026)
- [ ] T049 [P] [US2] Add Narrative-variant end-to-end test to `PromptBuilderTests.cs` asserting zero "POV Persona" text (SC-004), lighter character data, zero-dialogue constraint in final instruction
- [ ] T050 [P] [US2] Add `ActorProfileResolverTests` case for `PromptIntent.Narrative` → `ActorProfileKind.Narrative` resolution

### Implementation for User Story 2

- [ ] T051 [US2] Add Narrative-variant branch to `ActorAssignmentSlot.WriteAsync` — "Write as omniscient narrator" (FR-006, R12)
- [ ] T052 [US2] Add Narrative-variant branch to `CharacterDataSlot.WriteAsync` — all chars, lighter format, no persona, no intimate self-awareness (FR-026, R12)
- [ ] T053 [US2] Add Narrative-variant branch to `BehavioralFramesSlot.WriteAsync` — all frames included (FR-019, FR-026, R12)
- [ ] T054 [US2] Add Narrative-variant branch to `FinalInstructionSlot.WriteAsync` — 3rd person omniscient, 300-500 words, zero-dialogue hard constraint, physical detail checklist (positions, contact, sensations, sounds, rhythm) (FR-023, FR-026, R12)
- [ ] T055 [US2] Ensure `ActorProfileResolver` returns `Kind == Narrative` for `PromptIntent.Narrative` and suppresses all POV persona injection across slots (S-025)
- [ ] T056 [US2] Validate build + run Narrative-variant tests: `dotnet test --filter "FullyQualifiedName~RolePlay.Prompts"`

**Checkpoint**: User Stories 1, 6, AND 2 all work — both Character and Narrative variants produce correct, deduplicated prompts.

---

## Phase 6: User Story 3 — Token Budget Enforcement (Priority: P2)

**Goal**: Enforce the configurable `MaxPromptChars` budget with the FR-029 trim priority order. Implement the remaining Zone B trimmable slots (Scenario Context, Current Location, Writing Style, Scene Continuity Anchor) and the trim orchestrator integration.

**Independent Test**: Configure a session with `MaxPromptChars=35000`. Run continuations through multiple turns. Verify: prompt never exceeds 35,000 chars, trim warnings appear in logs, Zone A + Theme Contract + Final Instruction are never trimmed.

### Tests for User Story 3

- [ ] T057 [P] [US3] Create `PromptBudgetEnforcerTests.cs` in `DreamGenClone.Tests/RolePlay/Prompts/PromptBudgetEnforcerTests.cs` — trim priority order, never-trim invariants, critical-overflow path, fail-fast on missing/invalid `MaxPromptChars` (FR-004, FR-029, FR-030)
- [ ] T058 [P] [US3] Contract test for `ScenarioContextSlot` in `SlotContractTests.cs` (FR-012, FR-036)
- [ ] T059 [P] [US3] Contract test for `CurrentLocationSlot` in `SlotContractTests.cs` (FR-013, FR-036)
- [ ] T060 [P] [US3] Contract test for `WritingStyleSlot` in `SlotContractTests.cs` (FR-014, FR-036) — includes fail-fast on missing phase Rule-of-Thumb and missing profile default
- [ ] T061 [P] [US3] Contract test for `SceneContinuityAnchorSlot` in `SlotContractTests.cs` (FR-017, FR-036)
- [ ] T062 [P] [US3] Add budget-enforcement end-to-end test to `PromptBuilderTests.cs` — 35K cap holds across 100 turns (SC-006), trim warning logged with SessionId/Actor/PreTrim/PostTrim (FR-030)

### Implementation for User Story 3

- [ ] T063 [P] [US3] Implement `ScenarioContextSlot` (Slot 6, Zone B, trimmable) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/ScenarioContextSlot.cs` — progressive compression using `ScenarioCompressionTurnThreshold` (full turns 1-N, compressed 2-3 lines after); fail-fast if threshold missing (FR-012, FR-012a)
- [ ] T064 [P] [US3] Implement `CurrentLocationSlot` (Slot 7, Zone B, trimmable) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/CurrentLocationSlot.cs` — current scene full, occupied one-line, others omitted (FR-013)
- [ ] T065 [P] [US3] Implement `WritingStyleSlot` (Slot 8, Zone B, last-resort trim) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/WritingStyleSlot.cs` — timeless desc/example always kept, phase Rule-of-Thumb from `PhaseRuleOfThumb` table, profile default as separate element; fail-fast on either missing (FR-014)
- [ ] T066 [P] [US3] Implement `SceneContinuityAnchorSlot` (Slot 11, Zone B, low trim) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/SceneContinuityAnchorSlot.cs` — cross-perceptions only, drop self-perceptions (FR-017); absorbs `ScenePresenceInjector`
- [ ] T067 [US3] Wire `PromptBudgetEnforcer` into `RolePlayPromptBuilder.BuildAsync` post-slot assembly phase (R7) — two-phase: build all, then trim
- [ ] T068 [US3] Add Warning log on trim and Critical log on overflow in `RolePlayPromptBuilder` per `contracts/token-budget-contract.md` (FR-030)
- [ ] T069 [US3] Register Slots 6, 7, 8, 11 in `DreamGenClone.Web/Program.cs` DI
- [ ] T070 [US3] Validate build + run budget tests: `dotnet test --filter "FullyQualifiedName~RolePlay.Prompts"`

**Checkpoint**: User Stories 1, 6, 2, AND 3 all work — prompts stay within budget with correct trim priority.

---

## Phase 7: User Story 4 — Long-Running Session Continuity (Priority: P2)

**Goal**: Implement tiered interaction history compression and 3-tier session memory. Rewrite the encounter enrichment prompt to capture 6 dimensions and add secondary encounter-detection signals.

**Independent Test**: Run a session through 15+ turns spanning multiple encounters. Verify: full detail for last 2-3 turns, narrative-only summaries for turns 4-6, encounter memory summaries for turns 7+. Character callbacks appear naturally.

### Tests for User Story 4

- [ ] T071 [P] [US4] Contract test for `InteractionHistorySlot` in `SlotContractTests.cs` (FR-015, FR-036) — 3-tier compression using configured turn bands
- [ ] T072 [P] [US4] Contract test for `SessionMemorySlot` in `SlotContractTests.cs` (FR-016, FR-036) — 3 tiers (long-term backstory, medium-term encounters, short-term milestones)
- [ ] T073 [P] [US4] Create `EncounterEnrichmentPromptTests.cs` in `DreamGenClone.Tests/RolePlay/Prompts/EncounterEnrichmentPromptTests.cs` — assert 6-dimension capture, SC-009 (≥4 of 6 dimensions present)
- [ ] T074 [P] [US4] Add tiered-history end-to-end test to `PromptBuilderTests.cs` — 15+ turn session shows correct tier boundaries

### Implementation for User Story 4

- [ ] T075 [P] [US4] Implement `InteractionHistorySlot` (Slot 9, Zone B, trimmable priority 1) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/InteractionHistorySlot.cs` — 3-tier compression using `HistoryFullDetailTurnBand`, `HistoryNarrativeOnlyTurnBand`, `ContextWindowTurns`; fail-fast if thresholds missing (FR-015, FR-012a, R3)
- [ ] T076 [P] [US4] Implement `SessionMemorySlot` (Slot 10, Zone B, trimmable priority 4) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/SessionMemorySlot.cs` — 3 tiers using `SessionMemoryLongTermTurnThreshold`; encounter summaries from `RolePlayV2EncounterSummaries`; fail-fast if threshold missing (FR-016, FR-012a)
- [ ] T077 [US4] Rewrite enrichment prompt in `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` per `contracts/encounter-enrichment-contract.md` — 6 dimensions, Narrative response as primary source (FR-033, FR-035, R9)
- [ ] T078 [US4] Extend `TryDetectEncounterBoundaryAsync` in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` with 4 secondary signals (scene change after intimacy, time passage, boundary language, Climax→Reset phase transition); each signal writes `RolePlayDebugEventRecord` with `EventKind="EncounterBoundaryDetected"` (FR-034, R8)
- [ ] T079 [US4] Register Slots 9, 10 in `DreamGenClone.Web/Program.cs` DI
- [ ] T080 [US4] Validate build + run continuity tests: `dotnet test --filter "FullyQualifiedName~RolePlay.Prompts"`

**Checkpoint**: User Stories 1, 6, 2, 3, AND 4 all work — long-running sessions maintain coherent tiered memory.

---

## Phase 8: User Story 5 — Dynamic World State Awareness (Priority: P3)

**Goal**: Implement the conditional World State slot (Slot 4a) ready for B-062. The slot fires only when `WorldStateData` is populated; silently omitted otherwise.

**Independent Test**: With world state data populated, verify Zone A includes a World State section. With B-062 not implemented, verify the slot is silently omitted without error.

### Tests for User Story 5

- [ ] T081 [P] [US5] Contract test for `WorldStateSlot` in `SlotContractTests.cs` (FR-009, FR-036) — `ShouldWrite` returns true when `WorldState` non-null, false when null; output format matches GAP-5
- [ ] T082 [P] [US5] Add conditional-omission test to `PromptBuilderTests.cs` — slot silently omitted when `WorldState` is null

### Implementation for User Story 5

- [ ] T083 [P] [US5] Implement `WorldStateSlot` (Slot 4a, Zone A, conditional) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/WorldStateSlot.cs` — `ShouldWrite` returns `context.WorldState is not null`; format per GAP-5 (Day N, time phase, weather, world rhythm, temporal pressure) (FR-009, R10)
- [ ] T084 [US5] Register `WorldStateSlot` in `DreamGenClone.Web/Program.cs` DI (conditional registration acceptable)
- [ ] T085 [US5] Validate build + run World State tests: `dotnet test --filter "FullyQualifiedName~RolePlay.Prompts"`

**Checkpoint**: All 6 user stories complete — full 17-slot architecture operational.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Remaining Zone C slots, engine data hygiene, final wiring, and validation.

- [ ] T086 [P] Implement `ScenarioGuidanceSlot` (Slot 14, Zone C, low trim) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/ScenarioGuidanceSlot.cs` — phase steering, suppress resistance band when threshold crossed (FR-020); absorbs `BeatStageInjector`
- [ ] T087 [P] Implement `IntensityPacingSlot` (Slot 15, Zone C) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/IntensityPacingSlot.cs` — merged escalation + scene-time-direction + available positions (FR-021); absorbs `IntensityContractInjector` + `EscalationInjector` + `SceneTimeDirectionInjector` + `PositionListInjector`
- [ ] T088 [P] Implement `UserDirectionSlot` (Slot 16, Zone C, conditional) in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/UserDirectionSlot.cs` — fires only when user provided real direction; omit generic "Continue naturally" (FR-022)
- [ ] T089 [P] Contract tests for Slots 14, 15, 16 in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` (FR-020, FR-021, FR-022, FR-036)
- [ ] T090 Register Slots 14, 15, 16 in `DreamGenClone.Web/Program.cs` DI
- [ ] T091 [P] Audit all slots for engine data hygiene — no raw adaptive stat numbers, raw intensity profile GUIDs, confidence values, or uninterpreted resistance band labels in prompt output (FR-031)
- [ ] T092 [P] Replace "HARD CONSTRAINT" label dilution (~25 instances → targeted use only where genuinely warranted) across all slot implementations (FR-032)
- [ ] T093 Add Debug-level slot diagnostics (which slots fired, per-slot char counts) to `RolePlayPromptBuilder` and each slot (FR-037)
- [ ] T094 Run full RP test suite: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlay"` — fix any regressions
- [ ] T095 Run quickstart.md validation steps 1-6 (verify 17 slots registered, no legacy path, fail-fast on missing MaxPromptChars, prompt size ≤35K, deduplication, Narrative no POV persona)
- [ ] T096 [P] Update `specs/001-rp-prompt-redesign/quickstart.md` if any verification commands changed during implementation
- [ ] T097 Performance validation: measure prompt build time over 100 builds, confirm ≤20% increase vs. baseline (SC-007)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately. All T001-T005 are parallelizable.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories. T006-T011 (schema/config) and T012-T019 (contracts/builder) can proceed in parallel within the phase.
- **User Stories (Phases 3-8)**: All depend on Foundational phase completion.
  - **US1 (Phase 3)** is the MVP — must complete first; it deletes the legacy path and wires the builder.
  - **US6 (Phase 4)** depends on US1 (shares `PromptBuilderTests.cs`, requires builder wired).
  - **US2 (Phase 5)** depends on US1 + US6 (adds Narrative branches to slots implemented in US1/US6).
  - **US3 (Phase 6)** depends on US1 (requires builder wired; implements budget enforcement + remaining Zone B slots).
  - **US4 (Phase 7)** depends on US3 (tiered history interacts with budget trim priority; requires `InteractionHistorySlot` trimmable infrastructure).
  - **US5 (Phase 8)** depends on US1 (conditional slot pattern established in US1).
- **Polish (Phase 9)**: Depends on all user stories being complete. Implements remaining Zone C slots (14, 15, 16) and runs final validation.

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational — no dependencies on other stories. **MVP**.
- **US6 (P1)**: Can start after US1 — shares test scaffolding, requires builder wired.
- **US2 (P1)**: Can start after US1 + US6 — adds Narrative branches to existing slots.
- **US3 (P2)**: Can start after US1 — independent of US6/US2 (different slots).
- **US4 (P2)**: Can start after US3 — tiered history requires budget trim infrastructure.
- **US5 (P3)**: Can start after US1 — independent conditional slot.

### Within Each User Story

- Tests written FIRST (and FAIL) before implementation, per TDD pattern.
- Slot implementations are parallelizable within a story (different files).
- Builder wiring / DI registration / validation tasks are sequential (depend on slots existing).
- Story complete before moving to next priority.

### Parallel Opportunities

- All Setup tasks (T001-T005) are parallel — different files, no dependencies.
- Foundational schema/config tasks (T007-T011) parallel with contract/interface tasks (T012-T018).
- Within US1: all slot implementations (T029-T033) are parallel; all contract tests (T020-T028) are parallel.
- Within US6: slot implementations (T043-T045) are parallel; tests (T039-T042) are parallel.
- Within US3: slot implementations (T063-T066) are parallel; tests (T057-T062) are parallel.
- Within US4: slot implementations (T075-T076) are parallel; tests (T071-T074) are parallel.
- Polish slot implementations (T086-T088) are parallel.

---

## Parallel Example: User Story 1

```bash
# Launch all contract tests for User Story 1 together:
Task: "T021 [P] [US1] Contract test for SceneAnchorSlot"
Task: "T022 [P] [US1] Contract test for ActorAssignmentSlot"
Task: "T023 [P] [US1] Contract test for TurnContextSlot"
Task: "T024 [P] [US1] Contract test for SceneLocationLockSlot"
Task: "T025 [P] [US1] Contract test for CharacterDataSlot"

# Launch all slot implementations for User Story 1 together:
Task: "T029 [P] [US1] Implement SceneAnchorSlot"
Task: "T030 [P] [US1] Implement ActorAssignmentSlot"
Task: "T031 [P] [US1] Implement TurnContextSlot"
Task: "T032 [P] [US1] Implement SceneLocationLockSlot"
Task: "T033 [P] [US1] Implement CharacterDataSlot"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (domain enums + options)
2. Complete Phase 2: Foundational (schema migrations, contracts, builder skeleton)
3. Complete Phase 3: User Story 1 (Zone A slots + Character Data + legacy deletion)
4. **STOP and VALIDATE**: Test User Story 1 independently — Character prompts open with scene grounding, legacy path gone.
5. Deploy/demo if ready.

### Incremental Delivery

1. Setup + Foundational → Foundation ready.
2. Add US1 → Test independently → MVP (scene-grounded Character prompts, legacy deleted).
3. Add US6 → Test independently → Deduplication complete (each directive once).
4. Add US2 → Test independently → Narrative variant first-class.
5. Add US3 → Test independently → Budget enforcement live.
6. Add US4 → Test independently → Tiered memory + enriched encounters.
7. Add US5 → Test independently → World State slot ready for B-062.
8. Polish → All 17 slots live, engine hygiene, full validation.

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together.
2. Once Foundational is done:
   - Developer A: US1 (MVP critical path) → US6 → US2
   - Developer B: US3 (after US1) → US4
   - Developer C: US5 (after US1) → Polish slots 14/15/16
3. Stories integrate independently; US2 must wait for US1+US6 slot implementations.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- [Story] label maps task to specific user story for traceability.
- Each user story is independently completable and testable.
- Tests written FIRST (TDD) — verify they FAIL before implementation.
- Commit after each task or logical group.
- Stop at any checkpoint to validate story independently.
- **Repo Hard Rule**: No hardcoded runtime defaults for any RP behavior control. All thresholds (`MaxPromptChars`, compression turn bands, phase Rule-of-Thumb) are UI-backed persisted config with fail-fast diagnostics. The 35,000 value is a documented recommended initial config value, NOT a code default.
- **Frozen Contract**: The 17-slot architecture (zone, order, trim eligibility) is normative. Implementation MUST NOT add, remove, reorder, or re-zone slots without a spec amendment. Startup assertion in `RolePlayPromptBuilder` enforces this.
- **Full Replacement**: The legacy `BuildPromptAsync` method, `SceneDirectionCoordinator`, `IPromptInjector` interface, and all 13 injectors are deleted (SC-010, FR-028). No hybrid mode.
