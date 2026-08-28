# Tasks: B-041 — Session Memory Context (Intimate Encounter History Injection)

**Branch**: `001-session-memory-context`
**Input**: [plan.md](plan.md), [spec.md](spec.md), [data-model.md](data-model.md), [research.md](research.md), [quickstart.md](quickstart.md), [contracts/](contracts/)
**Total tasks**: 27 | **User stories**: 4 | **Parallel opportunities**: 13

---

## Format: `[ID] [P?] [Story?] Description — file path`

- **[P]**: Parallelizable with other [P] tasks in the same phase (different files, no blocking deps)
- **[US#]**: User story label (required for Phase 3+ tasks)
- All foundational tasks (Phases 1–2) must complete before any Phase 3+ work begins

---

## Phase 1: Setup

**Purpose**: Verify solution state; no new project scaffolding required (existing .NET solution)

- [X] T001 Verify solution builds clean on branch `001-session-memory-context` — run `dotnet build DreamGenClone.sln -v minimal`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entity, config, contracts, persistence CRUD — everything all user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 Create `EncounterSummaryType` enum and `EncounterSummaryRecord` entity (all properties + computed `ActiveSummary`/`IsEnhanced`) in `DreamGenClone.Domain/RolePlay/EncounterSummaryRecord.cs`
- [X] T003 [P] Add `public List<EncounterSummaryRecord> EncounterSummaries { get; set; } = [];` property to `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`
- [X] T004 [P] Add `public int? MaxMilestonesToInject { get; set; }` property to `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`
- [X] T005 [P] Create `RolePlayMemoryOptions` sealed class (`SectionName`, `MaxMilestonesToInject=5`, `MaxArcCompletionsToInject=10`, `EnableLlmSummaryEnhancement=true`) in `DreamGenClone.Infrastructure/Configuration/RolePlayMemoryOptions.cs`
- [X] T006 [P] Create `EncounterSummaryJobPayload` with `SessionId` (string) and `CycleIndex` (int) in `DreamGenClone.Application/RolePlay/EncounterSummaryJobPayload.cs`
- [X] T007 [P] Add `public const string EncounterSummaryEnhancement = "encounter-summary-enhancement";` to `DreamGenClone.Web/Application/BackgroundJobs/BackgroundJobTypes.cs`
- [X] T008 [P] Add `CREATE TABLE IF NOT EXISTS RolePlayV2EncounterSummaries` (17 columns + index on `(SessionId, OccurredUtc DESC)`) and `ALTER TABLE Sessions ADD COLUMN MaxMilestonesToInject INTEGER NULL` (PRAGMA guard — extend existing `PRAGMA table_info(Sessions)` block, do not add a second one) in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- [X] T009 Create `IEncounterSummaryService` interface (`GenerateTemplatesAsync`, `SaveAsync`, `UpdateLlmSummaryAsync`, `LoadForSessionAsync`) in `DreamGenClone.Application/RolePlay/Abstractions/IEncounterSummaryService.cs` — per [contracts/IEncounterSummaryService.md](contracts/IEncounterSummaryService.md)
- [X] T010 Add `SaveEncounterSummaryAsync`, `UpdateEncounterSummaryLlmAsync`, and `LoadArcInteractionsAsync` (check `RolePlayInteractions.CycleIndex` column exists via PRAGMA before querying; last 30 interactions, order by OccurredUtc ASC) to `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` (or `RolePlayStateRepository.cs` — follow existing CRUD placement pattern)
- [X] T011 Add `LoadEncounterSummariesAsync` (load ALL `ArcCompletion` rows + ALL `PhaseMilestone` rows for session — no LIMIT at load time; filter at injection time) to persistence/repository and call it from `LoadAdaptiveStateAsync` (after `LoadScenarioHistoryAsync`) to populate `AdaptiveScenarioState.EncounterSummaries` in `DreamGenClone.Infrastructure/Persistence/RolePlayStateRepository.cs`

**Checkpoint**: All infrastructure ready — build Infrastructure + Domain projects with 0 errors before proceeding

---

## Phase 3: User Story 1 — AI Recalls Prior Arc Intimate Acts (Priority: P1) 🎯 MVP

**Goal**: After completing one arc, every subsequent continuation prompt contains a populated "Session Memory" block describing what happened in the prior arc.

**Independent Test**: Complete one arc (Observing→BuildUp→Climax→Reset), start a second arc, request a continuation — verify the raw prompt log contains a "Session Memory:" header with at least one populated entry. Can verify with `--Verbose` logging without a second arc by checking template rows written to `RolePlayV2EncounterSummaries` after a Climax→Reset transition.

- [X] T012 [US1] Implement `EncounterSummaryService` in `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryService.cs`: `GenerateTemplatesAsync` builds one `EncounterSummaryRecord` per character (PhaseMilestone for non-Reset transitions; ArcCompletion for Climax→Reset) using the template formats from research.md R4; guard: `if (v2State.CharacterSnapshots is { Count: 0 }) return []` (log Debug, no throw); character name lookup from `session.Characters` list; `SaveAsync` and `UpdateLlmSummaryAsync` delegate to persistence
- [X] T013 [US1] Add encounter summary generation + enqueue hook in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` after `await _stateRepository.SaveTransitionEventAsync(...)` (~L2892): call `GenerateTemplatesAsync` → save each record → append to `v2State.EncounterSummaries`; if Climax→Reset and `EnableLlmSummaryEnhancement`: enqueue one `EncounterSummaryEnhancement` job with dedup key `$"enc-summary:{session.SessionId}:{v2State.CycleIndex}"`
- [X] T014 [US1] Implement `InjectSessionMemoryBlock(sb, summaries, effectiveMilestones, effectiveArcCompletions)` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`: take most-recent M `ArcCompletion` rows (DESC LIMIT M, reverse to chronological), then most-recent N `PhaseMilestone` rows for current `CycleIndex` (DESC LIMIT N, reverse to chronological); render `[Arc N Complete — {CharName}]` / `[{FromPhase} → {ToPhase} — {CharName}]` headers with `ActiveSummary`; call after Recent Interaction History block; omit block entirely if both lists are empty; `effectiveMilestones = session.MaxMilestonesToInject ?? _memoryOptions.Value.MaxMilestonesToInject`; `effectiveArcCompletions = _memoryOptions.Value.MaxArcCompletionsToInject`
- [X] T015 [US1] Register `RolePlayMemoryOptions` (`builder.Services.Configure<RolePlayMemoryOptions>(...)`), `IEncounterSummaryService`→`EncounterSummaryService` (`AddScoped`) in `DreamGenClone.Web/Program.cs`; add `"RolePlayMemory": { "MaxMilestonesToInject": 5, "MaxArcCompletionsToInject": 10, "EnableLlmSummaryEnhancement": true }` section to `DreamGenClone.Web/appsettings.Development.json`
- [X] T016 [P] [US1] Write unit tests `GenerateTemplate_PhaseMilestone_ContainsAllFields` and `GenerateTemplate_EmptyCharacterSnapshots_ProducesMinimalSummaryWithoutThrowing` in `DreamGenClone.Tests/RolePlay/EncounterSummaryServiceTests.cs`
- [X] T017 [P] [US1] Write unit tests `InjectBlock_NoSummaries_BlockOmitted` and `InjectBlock_LlmSummaryPreferredOverTemplate` in `DreamGenClone.Tests/RolePlay/SessionMemoryInjectionTests.cs`

**Checkpoint**: US1 done — build full solution with 0 errors; run tests T016–T017; manually verify "Session Memory:" block appears in prompt after one arc

---

## Phase 4: User Story 2 — Per-Character Memory Is Perspective-Specific (Priority: P2)

**Goal**: After Climax→Reset, an async LLM job runs and writes distinct per-character intimate act prose (first-person perspective) to each character's `LlmSummary` column.

**Independent Test**: Trigger a Climax→Reset transition, wait for job to process (~30 s), query `RolePlayV2EncounterSummaries WHERE SessionId = ? AND SummaryType = 'ArcCompletion'` — verify multiple rows with distinct non-null `LlmSummary` values, one per character.

- [X] T018 [US2] Implement `EncounterSummaryJobHandler` in `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryJobHandler.cs`: `JobType = BackgroundJobTypes.EncounterSummaryEnhancement`; deserialize `EncounterSummaryJobPayload { SessionId, CycleIndex }`; load arc interactions via `LoadArcInteractionsAsync` (last 30); load all `ArcCompletion` rows for `(SessionId, CycleIndex)` to get character list; make single LLM call with all-character arc-completion prompt (see research.md R5 template — reuse the same inference service as `SemanticInteractionAnalysisJobHandler`); parse per-character prose from response; call `UpdateLlmSummaryAsync` per character row; **retry policy**: on LLM exception, `await Task.Delay(TimeSpan.FromSeconds(5))`, retry once, on second failure log `Warning` with SessionId/CycleIndex and return (no throw)
- [X] T019 [US2] Register `EncounterSummaryJobHandler` as `IBackgroundJobHandler` (`builder.Services.AddScoped<IBackgroundJobHandler, EncounterSummaryJobHandler>()`) in `DreamGenClone.Web/Program.cs`
- [X] T020 [P] [US2] Write unit test `GenerateTemplate_ArcCompletion_ContainsBeatCodeAndStats` in `DreamGenClone.Tests/RolePlay/EncounterSummaryServiceTests.cs`
- [X] T021 [P] [US2] Write unit test `InjectBlock_ArcCompletionsRenderedBeforeMilestones` in `DreamGenClone.Tests/RolePlay/SessionMemoryInjectionTests.cs`

**Checkpoint**: US2 done — build with 0 errors; run tests T020–T021; LLM job enriches `LlmSummary` rows after arc completion

---

## Phase 5: User Story 3 — Phase Milestones Track Current-Arc Escalation (Priority: P3)

**Goal**: Every non-Climax→Reset phase transition writes `PhaseMilestone` rows per character, and the N most recent milestones for the current arc appear in the "Session Memory" block.

**Independent Test**: Advance a session from BuildUp to Approaching — query `RolePlayV2EncounterSummaries WHERE SummaryType = 'PhaseMilestone' AND CycleIndex = 0` — verify one row per character with `FromPhase=BuildUp`, `ToPhase=Approaching`; request a continuation and verify the prompt contains `[BuildUp → Approaching — {CharName}]` entries.

- [X] T022 [US3] Verify (and fix if needed) that `InjectSessionMemoryBlock` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` correctly filters milestones to `CycleIndex == v2State.CycleIndex` and applies DESC LIMIT N ordering (current-arc-only constraint must be explicit — milestones from prior arcs MUST NOT appear)
- [X] T023 [P] [US3] Write unit test `InjectBlock_MaxMilestonesEnforced` (4 milestones available, `MaxMilestonesToInject`=2, verify exactly 2 injected) in `DreamGenClone.Tests/RolePlay/SessionMemoryInjectionTests.cs`
- [X] T024 [P] [US3] Write unit test `InjectBlock_MilestonesFilteredToCurrentArcOnly` (milestones from prior `CycleIndex` excluded from injection) in `DreamGenClone.Tests/RolePlay/SessionMemoryInjectionTests.cs`

**Checkpoint**: US3 done — all milestone filtering tests pass; prior-arc milestones never appear in "Session Memory" block

---

## Phase 6: User Story 4 — Configurable Memory Depth (Priority: P4)

**Goal**: Global `MaxMilestonesToInject` and `MaxArcCompletionsToInject` cap injection depth; per-session `MaxMilestonesToInject` override (set at session creation) takes precedence over global default.

**Independent Test**: Set `MaxMilestonesToInject`=2 in `appsettings.Development.json`, create a session producing 4 phase milestones, verify the prompt contains exactly 2 milestone entries. Then create a session with per-session override=8 — verify up to 8 milestones appear.

- [X] T025 [US4] Add optional `MaxMilestonesToInject` numeric input (nullable int, label "Max milestones to inject (optional)") to `DreamGenClone.Web/Components/Pages/RolePlayCreate.razor`; write the value to `session.MaxMilestonesToInject` on session create; also ensure `RolePlayStateRepository.SaveSessionAsync` writes `MaxMilestonesToInject` to the `Sessions` table column
- [X] T026 [P] [US4] Write unit test `InjectBlock_PerSessionOverrideUsedWhenPresent` (session override=8, global=5, verify 8 milestones injected) in `DreamGenClone.Tests/RolePlay/SessionMemoryInjectionTests.cs`
- [X] T027 [P] [US4] Write unit test `InjectBlock_MaxArcCompletionsEnforced` (5 arc completion sets available, `MaxArcCompletionsToInject`=3, verify exactly 3 arc entries injected) in `DreamGenClone.Tests/RolePlay/SessionMemoryInjectionTests.cs`

**Checkpoint**: US4 done — configuration override tests pass; UI field visible on session creation page

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Logging coverage across all new paths; final build and regression validation

- [X] T028 [P] Add `ILogger<T>` structured Information-level logs to `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryService.cs` (template generated, saved), `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` transition hook (summaries written count, job enqueued), and `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryJobHandler.cs` (job start, LLM success, retry, abandon) — use structured message templates, include `{SessionId}` and `{CycleIndex}` as properties
- [X] T029 Run full solution build (`dotnet build DreamGenClone.sln -v minimal`) and all tests (`dotnet test DreamGenClone.Tests`) — verify 0 errors, all new tests pass, and all previously passing tests continue to pass (SC-006)

---

## Dependencies

```
T001 (setup verify)
T002 (entity)
├── T003 [P]  T004 [P]  T005 [P]  T006 [P]  T007 [P]  T008 [P]  T009
├── T010 (persistence save/arc load)
└── T011 (persistence load + wire to state)
     └── Phase 3 (US1): T012 → T013 → T014 → T015
          │         T016 [P]  T017 [P]
          └── Phase 4 (US2): T018 → T019
               │         T020 [P]  T021 [P]
               └── Phase 5 (US3): T022 → T023 [P]  T024 [P]
                    └── Phase 6 (US4): T025 → T026 [P]  T027 [P]
                         └── Phase 7 (Polish): T028 [P] → T029
```

---

## Parallel Execution Examples

### Foundational phase (after T002 entity is created):
```
T003 (AdaptiveScenarioState) || T004 (RolePlaySession) || T005 (RolePlayMemoryOptions)
T006 (JobPayload) || T007 (BackgroundJobTypes) || T008 (SqlitePersistence schema)
```

### US1 phase (after T011 load wired):
```
T012 (EncounterSummaryService) + T013 (Engine Hook) + T014 (Prompt Injection)
  → T015 (DI registration)
T016 (unit tests) || T017 (unit tests)   ← parallel with implementation
```

### US2 phase (after T015 registration):
```
T018 (JobHandler) → T019 (register)
T020 (unit tests) || T021 (unit tests)
```

---

## Implementation Strategy

**MVP scope**: Complete Phases 1–2 (Foundational) + Phase 3 (US1) for a working end-to-end feature. US1 alone delivers SC-001 and SC-003 — the AI will recall prior arcs using template prose, even before LLM enrichment is running.

**Incremental delivery**:
1. **MVP** (Phases 1–3): Template summaries written + injected → AI recalls prior arcs
2. **+US2** (Phase 4): LLM job enriches summaries with intimate act prose → higher-quality recalls
3. **+US3** (Phase 5): Milestone filtering verified and tested → current-arc coherence
4. **+US4** (Phase 6): UI override field → user control over token budget
5. **Polish** (Phase 7): Logging + regression confirmation

**Format validation**: All 29 tasks follow `- [ ] T### [P?] [US#?] Description — file path` format ✅
