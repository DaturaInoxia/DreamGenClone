# Tasks: Wife Resistance & Cheating Motivation Gap

**Input**: Design documents from `specs/001-wife-resistance-motivation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/resistance-profile-api.md

**Tests**: Not explicitly requested in spec — no dedicated test tasks generated. Tests are noted as implementation validation steps.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New domain types and persistence scaffolding — no behavior yet

- [ ] T001 [P] Create `StatResistanceProfile` domain class in `DreamGenClone.Domain/StoryAnalysis/StatResistanceProfile.cs` — mirror `StatWillingnessProfile` structure: Id, Name, Description, TargetStatName (default "Loyalty"), IsDefault, List\<ResistanceThreshold\> Thresholds, CreatedUtc, UpdatedUtc
- [ ] T002 [P] Create `ResistanceThreshold` domain class in `DreamGenClone.Domain/StoryAnalysis/StatResistanceProfile.cs` (nested or same file) — mirror `WillingnessThreshold`: SortOrder, MinValue, MaxValue, ResistanceLevel, Description, PromptDirective, List\<string\> ExampleScenarios
- [ ] T003 [P] Add `SelectedResistanceProfileId` (string?) property to `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Persistence, service layer, and DI — MUST complete before ANY user story work

**⚠️ CRITICAL**: No user story implementation can begin until this phase is complete

- [ ] T004 Add `StatResistanceProfiles` CREATE TABLE + index to `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` — mirror `StatWillingnessProfiles` schema: Id TEXT PK, Name TEXT NOT NULL, Description TEXT NOT NULL, TargetStatName TEXT NOT NULL DEFAULT 'Loyalty', IsDefault INTEGER NOT NULL DEFAULT 0, ThresholdsJson TEXT NOT NULL DEFAULT '[]', CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL
- [ ] T005 Add 5 persistence method signatures to `DreamGenClone.Infrastructure/Persistence/ISqlitePersistence.cs`: `SaveStatResistanceProfileAsync`, `LoadStatResistanceProfileAsync`, `LoadDefaultStatResistanceProfileAsync`, `LoadAllStatResistanceProfilesAsync`, `DeleteStatResistanceProfileAsync`
- [ ] T006 Implement 5 persistence methods in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` — UPSERT with IsDefault reset, loaders with JSON deserialization, ORDER BY Name for list
- [ ] T007 Create `IStatResistanceProfileService` interface in `DreamGenClone.Application/StoryAnalysis/IStatResistanceProfileService.cs` — SaveAsync, ListAsync, GetAsync, GetDefaultAsync, DeleteAsync (mirror `IStatWillingnessProfileService`)
- [ ] T008 Implement `StatResistanceProfileService` in `DreamGenClone.Infrastructure/StoryAnalysis/StatResistanceProfileService.cs` — `EnsureDefaultsAsync` seeds "Married Woman Resistance" (Loyalty target, 20 contiguous bands 0–100, IsDefault=1); `SaveAsync` validates contiguous coverage, unique name, single default; `DeleteAsync` protects seeded default
- [ ] T009 Register `IStatResistanceProfileService` in `DreamGenClone.Web/Program.cs` — `builder.Services.AddScoped<IStatResistanceProfileService, StatResistanceProfileService>()` alongside existing willingness/gate registrations
- [ ] T010 Add facade passthrough methods to `DreamGenClone.Web/Application/StoryAnalysis/StoryAnalysisFacade.cs` — `SaveStatResistanceProfileAsync`, `ListStatResistanceProfilesAsync`, `DeleteStatResistanceProfileAsync`
- [ ] T011 Add `SelectedResistanceProfileId` column persistence to `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` — UPSERT parameter binding in `SaveAdaptiveStateAsync`, new load ordinal in `LoadAdaptiveStateAsync`

**Checkpoint**: Foundation ready — user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Wife Genuinely Resists Advances Until Motivation Conditions Are Met (Priority: P1) 🎯 MVP

**Goal**: ResistanceProfile band lookup + HARD CONSTRAINT prompt injection + target-aware escalation guidance. The Wife's resistance band is resolved from her raw target stat (motivation=0 initially — US2 adds the shift). Escalation guidance stops pushing past a firm-resistance band.

**Independent Test**: Create a session with Wife Loyalty=75, Restraint=70. Build a Committed-phase prompt. Verify the prompt contains `HARD CONSTRAINT — {WifeLabel} resistance directive (authoritative, overrides escalation guidance): {band directive}`. Verify escalation guidance does not push past the firm resistance band.

- [ ] T012 [US1] Implement `BuildResistanceInterpretationAsync` in `DreamGenClone.Infrastructure/RolePlay/ScenarioGuidanceGenerator.cs` — resolve the selected ResistanceProfile, find Wife character by CharacterRole, read target stat value via `CharacterStatProfileV2Accessor`, resolve threshold band for effectiveStat = targetStatValue (no motivation shift yet), return band's `PromptDirective` or empty string
- [ ] T013 [US1] Wire resistance directive into prompt build in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — call `BuildResistanceInterpretationAsync` from `ScenarioGuidanceGenerator`, inject the result as a HARD CONSTRAINT line immediately after the per-character current-state HARD CONSTRAINT lines in `AppendScenarioGuidance`
- [ ] T014 [US1] Add `AppendResistanceDirective` helper to `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` — format: `HARD CONSTRAINT — {label} resistance directive (authoritative, overrides escalation guidance): {directive}`; emit only when directive is non-empty
- [ ] T015 [US1] Make `AppendEscalationGuidance` target-aware in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — resolve Wife character stats specifically (not just actor), check resolved resistance band (call `BuildResistanceInterpretationAsync` or receive pre-resolved band), conditionally suppress push-forward lines ("Advance the scene", "Do not keep the scene at fully-clothed distance") when band indicates firm resistance; drop legacy `Tension` stat reference
- [ ] T016 [US1] Display active ResistanceProfile name and current resolved band on the RP workspace adaptive panel — add a readout line alongside the existing WillingnessProfile display in the adaptive panel Razor component

**Checkpoint**: US1 complete — Wife resistance is real. Prompt has authoritative resistance HARD CONSTRAINT. Escalation respects the band.

---

## Phase 4: User Story 2 - Multiple Motivational Drivers Influence Wife's Receptivity (Priority: P2)

**Goal**: Add 4 new behavioral dimensions (Wife BoundaryFirmness + SeductionReceptivity; Husband Attentiveness + IntimacyAvailability) to the catalog. Compute the motivation score from the 4 profile-level inputs. Apply the shift to effectiveStat so marital deficit and persistent pursuit relax the Wife's resistance band.

**Independent Test**: Configure Husband Attentiveness=15, IntimacyAvailability=10; Wife Loyalty=70, SelfRespect=30; OtherMan PersistencePastLimits=85. Verify effectiveStat is higher than raw Loyalty (motivation active). Verify new dimensions appear in CharacterProfile UI forms and flow into behavioral frame text.

- [ ] T017 [P] [US2] Add Wife `BoundaryFirmness` behavioral dimension to `DreamGenClone.Domain/StoryAnalysis/BehavioralDimensionCatalog.cs` — 4-tier text: Tier1=she firmly enforces stated limits; Tier2=holds boundaries most of the time but can be swayed; Tier3=states limits weakly, softens quickly; Tier4=does not enforce limits at all
- [ ] T018 [P] [US2] Add Wife `SeductionReceptivity` behavioral dimension to `DreamGenClone.Domain/StoryAnalysis/BehavioralDimensionCatalog.cs` — 4-tier text: Tier1=immune to pursuit; Tier2=mildly flattered but unchanged; Tier3=susceptible, persistence chips away resolve; Tier4=highly receptive, persistence draws her in
- [ ] T019 [P] [US2] Add Husband `Attentiveness` behavioral dimension to `DreamGenClone.Domain/StoryAnalysis/BehavioralDimensionCatalog.cs` — 4-tier text: Tier1=emotionally distant, she feels invisible; Tier2=intermittently attentive, takes her for granted; Tier3=generally present and engaged; Tier4=deeply attentive, actively nurtures connection
- [ ] T020 [P] [US2] Add Husband `IntimacyAvailability` behavioral dimension to `DreamGenClone.Domain/StoryAnalysis/BehavioralDimensionCatalog.cs` — 4-tier text: Tier1=sexually unavailable, dead bedroom; Tier2=sporadic, routine, she feels undesired; Tier3=generally available and engaged; Tier4=actively passionate, makes her feel pursued
- [ ] T021 [US2] Add stat-to-dimension drift rules for new Wife dimensions in `DreamGenClone.Domain/StoryAnalysis/StatToDimensionMappings.cs` — BoundaryFirmness: Restraint +0.90, Loyalty +0.75, SelfRespect +0.60; SeductionReceptivity: Restraint −0.60, Desire +0.45. Floor 0, Ceiling 100 for all
- [ ] T022 [US2] Extend `ValidateStats` in `DreamGenClone.Infrastructure/StoryAnalysis/CharacterProfileService.cs` — allow `BoundaryFirmness` and `SeductionReceptivity` for Wife role; allow `Attentiveness` and `IntimacyAvailability` for Husband role (add to the existing per-role allowed-dimension whitelist)
- [ ] T023 [US2] Implement motivation score computation in `BuildResistanceInterpretationAsync` in `DreamGenClone.Infrastructure/RolePlay/ScenarioGuidanceGenerator.cs` — resolve Husband Attentiveness/IntimacyAvailability from Husband character's RuntimeEncounterStats (default 50), resolve Wife SelfRespect from Wife's stats (default 50), resolve OtherMan PersistencePastLimits (default 50); compute `motivationScore = ((100−Attentiveness) + (100−IntimacyAvailability) + (100−SelfRespect) + PersistencePastLimits) / 4`; compute `effectiveStat = min(targetStatValue + motivationScore, 100)`

**Checkpoint**: US2 complete — motivation drivers shift the Wife's resistance band. Husband neglect, low self-respect, and persistent pursuit all contribute. New dimensions flow through frame generator and UI forms.

---

## Phase 5: User Story 3 - Configure Resistance Profiles Through the UI (Priority: P3)

**Goal**: Full CRUD UI for Resistance Profiles via a new "Resistance" tab on the Theme Profiles page, cloning the existing "Willingness" tab pattern.

**Independent Test**: Navigate to Theme Profiles → Resistance tab. Create, edit, and delete a Resistance Profile. Verify seeded default is present on first load. Verify profile survives page reload.

- [ ] T024 [US3] Add "Resistance" tab nav button to `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor` — `<button>` with `@onclick='() => _activeTab = "resistance"'` and `GetTabClass("resistance")`, placed next to the Willingness tab button
- [ ] T025 [US3] Add Resistance tab content block to `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor` — list column (profiles with select buttons, "New" button) + edit form column (Name input, TargetStatName input, Description textarea, IsDefault checkbox, Thresholds JSON textarea, Save/Create button, Delete button, error display). Clone the willingness tab structure.
- [ ] T026 [US3] Add `@code` methods for Resistance tab in `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor` — private fields (`_resistanceProfiles`, `_selectedResistanceProfileId`, `_resistanceFormName`, `_resistanceFormTargetStat`, `_resistanceFormIsDefault`, `_resistanceFormDescription`, `_resistanceThresholdsJson`, `_resistanceError`); methods: `StartCreateResistanceProfile`, `SelectResistanceProfile`, `SaveResistanceProfile`, `DeleteResistanceProfile`
- [ ] T027 [US3] Wire Resistance profile loading in `ThemeProfiles.razor` `OnInitializedAsync` — `_resistanceProfiles = await Facade.ListStatResistanceProfilesAsync()`; auto-select first profile or trigger `StartCreateResistanceProfile` if empty

**Checkpoint**: US3 complete — users can create, edit, and delete Resistance Profiles through the UI. Seeded default provides sensible out-of-box behavior.

---

## Phase 6: User Story 4 - Wife Boundary-Holding Dimensions Flow Into Behavioral Frame (Priority: P3)

**Goal**: Verify that the new Wife behavioral dimensions (added in US2) automatically flow through the existing `CharacterBehavioralFrameGenerator` pipeline and appear in the prompt as HARD CONSTRAINT per-character frame text. Verify they appear in the CharacterProfile UI encounter-stats form. No new code — validation-only phase.

**Independent Test**: Create a Wife CharacterProfile with BoundaryFirmness=85, SeductionReceptivity=15. Load in a session. Verify the prompt's HARD CONSTRAINT behavioral frame line includes tier-4 BoundaryFirmness text and tier-1 SeductionReceptivity text.

- [ ] T028 [US4] Verify new Wife dimensions appear in UI encounter-stats form — open CharacterProfiles tab, select/create a Wife profile, confirm `BoundaryFirmness` and `SeductionReceptivity` sliders appear with live tier-text preview (picked up automatically from `GetDimensions("Wife")`)
- [ ] T029 [US4] Verify new Wife dimensions flow into prompt behavioral frame — create a session with a Wife profile having configured BoundaryFirmness/SeductionReceptivity, build prompt, confirm `HARD CONSTRAINT — {label} behavioral frame` contains the tier-specific text for both dimensions (generated automatically by `CharacterBehavioralFrameGenerator.GenerateFramesAsync`)
- [ ] T030 [US4] Verify new Husband dimensions appear in UI and frame — same checks for `Attentiveness` and `IntimacyAvailability` on Husband role profiles

**Checkpoint**: US4 verified — boundary-holding dimensions are fully integrated into the behavioral frame pipeline and UI. No additional code needed beyond Phase 4 catalog entries.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Cutover, tests, and final validation

- [ ] T031 Purge existing roleplay sessions at cutover — follow B-038 pattern (delete session rows that lack the new adaptive state column, or document that existing sessions are invalidated)
- [ ] T032 [P] Create `ResistanceProfileTests.cs` in `DreamGenClone.Tests/RolePlay/` — unit tests: motivation score formula with known inputs, effectiveStat clamping at 100, missing-input defaults to 50, band resolution for boundary values (0, 50, 100), empty profile returns empty directive
- [ ] T033 [P] Create target-aware escalation tests — unit tests: push-forward lines suppressed when Wife resistance band is firm (high Loyalty); push-forward lines emitted when band is permissive (low Loyalty); legacy Tension stat no longer referenced
- [ ] T034 Build solution and run full test suite — `dotnet build DreamGenClone.sln` (0 errors), `dotnet test DreamGenClone.Tests` (all existing tests pass, new tests pass)
- [ ] T035 Run quickstart verification steps from `quickstart.md` — seeded default exists, UI CRUD works, resistance directive appears in prompt, adaptive panel displays active profile

---

## Dependencies & Execution Order

### Phase Dependency Graph

```text
Phase 1 (Setup) ──► Phase 2 (Foundational) ──┬──► Phase 3 (US1) ──► Phase 4 (US2)
                                              │
                                              ├──► Phase 5 (US3) ──► Phase 7 (Polish)
                                              │
                                              └──► Phase 6 (US4)
```

- **Phase 1 → Phase 2**: Domain classes must exist before service/persistence can reference them.
- **Phase 2 → Phases 3, 5, 6**: All stories need the ResistanceProfile service + DI + persistence. Must complete Phase 2 before any story work.
- **Phase 3 → Phase 4**: US2 extends the `BuildResistanceInterpretationAsync` created in US1 with motivation computation. US4 uses dimensions added in US2.
- **Phases 5, 6**: Can run in parallel with each other and with Phase 3 (US1) — they don't depend on prompt integration.

### Within Each Phase

- **Phase 1**: T001, T002, T003 all [P] — different files, no cross-dependencies.
- **Phase 2**: Sequential order. T004→T005→T006 (persistence chain). T007→T008 (service chain). T009→T010→T011 (integration chain). T004+T007 can start in parallel.
- **Phase 3 (US1)**: T012→T013→T014→T015→T016. Each builds on the previous. T012 must exist before prompt wiring.
- **Phase 4 (US2)**: T017–T020 [P] — all in same file but independent entries. T021 depends on catalog entries. T022 depends on catalog. T023 depends on T012+T017–T022.
- **Phase 5 (US3)**: T024→T025→T026→T027. Sequential tab construction.
- **Phase 6 (US4)**: T028→T029→T030. Verification order, but any order works.
- **Phase 7**: T031 first (cutover). T032+T033 [P] in parallel. T034 after tests. T035 last.

### Parallel Opportunities

```text
Phase 2 kickoff: T004 and T007 can start simultaneously
Phase 4: T017, T018, T019, T020 can all be written in parallel (same file, different entries)
Phase 7: T032 and T033 can be written and run in parallel
Cross-phase: Phase 5 (US3 UI) and Phase 6 (US4 verification) can run in parallel with Phase 3 (US1 prompt)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T003)
2. Complete Phase 2: Foundational (T004–T011)
3. Complete Phase 3: US1 (T012–T016)
4. **STOP and VALIDATE**: Build, test, verify US1 acceptance scenarios
5. Deploy/demo if ready

### Incremental Delivery

1. **MVP**: US1 — Wife resistance is real. ResistanceProfile drives the directive. Escalation respects it. (~16 tasks)
2. **+US2**: Motivation drivers add nuance — husband neglect and pursuit shift the band. (~7 tasks)
3. **+US3**: UI CRUD — users can configure their own resistance profiles. (~4 tasks)
4. **+US4**: Behavioral dimensions confirmed flowing — richer character modeling. (~3 tasks)
5. **Polish**: Cutover, tests, full validation. (~5 tasks)

### Suggested MVP Scope

Complete **Phase 1 + Phase 2 + Phase 3** (~16 tasks). This delivers the core value: the Wife says "we shouldn't" and *means* it. The escalation engine respects the resistance band. The seeded default profile makes it work out of the box. Everything beyond this (motivation drivers, UI customization, behavioral dimensions) is enhancement.
