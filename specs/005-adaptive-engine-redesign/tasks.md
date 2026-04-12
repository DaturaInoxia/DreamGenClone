# Tasks: Adaptive Engine Redesign

**Input**: Design documents from `/specs/005-adaptive-engine-redesign/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Included — SC-010 explicitly requires automated test coverage for all new and modified behavior.

**Organization**: Tasks grouped by user story (8 stories from spec.md: P1×2, P2×3, P3×3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Exact file paths included in all descriptions

---

## Phase 1: Setup

**Purpose**: Verify baseline build health before feature work begins

- [x] T001 Verify clean build of DreamGenClone.sln and all existing tests pass

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Create shared domain models, interfaces, and persistence infrastructure that multiple user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T002 [P] Create ThemeCatalogEntry domain model (Id, Label, Description, Keywords, Weight, Category, StatAffinities, IsEnabled, IsBuiltIn, CreatedUtc, UpdatedUtc) in DreamGenClone.Domain/StoryAnalysis/ThemeCatalogEntry.cs
- [x] T003 [P] Create IThemeCatalogService interface (GetByIdAsync, GetAllAsync, SaveAsync, DeleteAsync, SeedDefaultsAsync) in DreamGenClone.Application/StoryAnalysis/IThemeCatalogService.cs
- [x] T004 [P] Add Blocked (bool, default false) and SuppressedHitCount (int, default 0) properties to ThemeTrackerItem in DreamGenClone.Web/Domain/RolePlay/RolePlayAdaptiveState.cs
- [x] T005 [P] Add SeedFromScenarioAsync(RolePlaySession, Scenario, CancellationToken) method signature to IRolePlayAdaptiveStateService in DreamGenClone.Application/StoryAnalysis/IRolePlayAdaptiveStateService.cs
- [x] T006 Add ThemeCatalog table creation (CREATE TABLE IF NOT EXISTS) to EnsureTables() and add CRUD persistence methods (SaveThemeCatalogEntryAsync, LoadThemeCatalogEntryAsync, LoadAllThemeCatalogEntriesAsync, DeleteThemeCatalogEntryAsync) in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs

**Checkpoint**: Foundation ready — shared types and persistence exist for all user stories

---

## Phase 3: User Story 1 — Data-Driven Theme Catalog Management (Priority: P1) 🎯 MVP

**Goal**: Replace hardcoded ThemeRule[] with a SQLite-backed Theme Catalog with full CRUD, seed defaults, and runtime scoring from database

**Independent Test**: Create ThemeCatalog table, seed 10 defaults, perform CRUD, verify AdaptiveStateService reads from database instead of hardcoded array

### Tests for User Story 1

- [x] T007 [P] [US1] Write ThemeCatalogServiceTests covering: SeedDefaultsAsync creates 10 entries, idempotent re-seed, CRUD save/load/delete, ID validation rejects invalid slugs, built-in entry deletion throws, GetAllAsync excludes disabled, disabled entry re-enable in DreamGenClone.Tests/StoryAnalysis/ThemeCatalogServiceTests.cs

### Implementation for User Story 1

- [x] T008 [US1] Implement ThemeCatalogService with CRUD operations, ID validation (regex `^[a-z0-9]+(-[a-z0-9]+)*$`, max 50 chars, uniqueness check), built-in deletion protection, and SeedDefaultsAsync with 10 built-in themes including StatAffinities from FR-002 table in DreamGenClone.Infrastructure/StoryAnalysis/ThemeCatalogService.cs
- [x] T009 [US1] Register IThemeCatalogService → ThemeCatalogService in DI container and call SeedDefaultsAsync during application startup in DreamGenClone.Web/Program.cs
- [x] T010 [US1] Refactor RolePlayAdaptiveStateService: inject IThemeCatalogService via constructor, remove the hardcoded static ThemeRule[] array, and update EnsureThemeCatalog() to load enabled entries from the catalog service instead of the static array in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [x] T011 [US1] Add Information-level Serilog logging for catalog seed completion, entry save, entry delete, and entry disable/enable operations in DreamGenClone.Infrastructure/StoryAnalysis/ThemeCatalogService.cs

**Checkpoint**: Theme Catalog is data-driven — CRUD works, scoring reads from SQLite, 10 defaults seeded

---

## Phase 4: User Story 2 — Renamed Character Stats and ThemeProfile Rename (Priority: P1)

**Goal**: Rename stats (Arousal→Desire, Inhibition→Restraint, Trust→Connection, Agency→Dominance) and RankingProfile→ThemeProfile across all layers with backward-compatible legacy deserialization

**Independent Test**: Load sessions with old stat names, verify transparent mapping; verify all UI labels, DB columns, and prompt context lines use new names

### Tests for User Story 2

- [x] T012 [P] [US2] Write legacy stat and profile rename tests: old stat name normalization via NormalizeLegacyStatName, session deserialization with SelectedRankingProfileId mapping, extended legacy names (Shame→Restraint, DominanceDrive→Dominance, etc.) in DreamGenClone.Tests/StoryAnalysis/LegacyStatMappingTests.cs

### Implementation for User Story 2

- [x] T013 [P] [US2] Rename stat constants in AdaptiveStatCatalog (Arousal→Desire, Inhibition→Restraint, Trust→Connection, Agency→Dominance), add NormalizeLegacyStatName(string) mapping method covering all legacy names from FR-009, and update Restraint scoring direction per FR-010 in DreamGenClone.Application/StoryAnalysis/AdaptiveStatCatalog.cs
- [x] T014 [P] [US2] Rename RankingProfile class to ThemeProfile (update class name, file name, and all references) in DreamGenClone.Domain/StoryAnalysis/RankingProfile.cs → ThemeProfile.cs
- [x] T015 [US2] Add ALTER TABLE RankingProfiles RENAME TO ThemeProfiles migration in EnsureTables(), update all ThemeProfile CRUD method names and SQL queries (Save, Load, LoadAll, Delete) in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs
- [x] T016 [P] [US2] Rename SelectedRankingProfileId → SelectedThemeProfileId in RolePlaySession with JsonPropertyName alias for legacy deserialization in DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs
- [x] T017 [P] [US2] Rename DefaultRankingProfileId → DefaultThemeProfileId in Scenario with JsonPropertyName alias for legacy deserialization in DreamGenClone.Web/Domain/Scenarios/Scenario.cs
- [x] T018 [US2] Update all stat string references (replace "Arousal" with "Desire", etc.) and apply NormalizeLegacyStatName to loaded stat dictionaries in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [x] T019 [P] [US2] Update stat string reference ("Arousal" → "Desire") in style intensity calculation in DreamGenClone.Web/Application/RolePlay/RolePlayStyleResolver.cs
- [x] T020 [P] [US2] Update SelectedRankingProfileId → SelectedThemeProfileId in validation and prompt assembly references in DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs
- [x] T021 [P] [US2] Update SelectedRankingProfileId → SelectedThemeProfileId references in session creation in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [x] T022 [US2] Rename RankingProfiles.razor → ThemeProfiles.razor and update all route paths, page titles, navigation links, labels, and component references in DreamGenClone.Web/Components/
- [x] T023 [P] [US2] Update ScenarioEditor.razor references from RankingProfile/ranking to ThemeProfile/theme in DreamGenClone.Web/Components/ScenarioEditor.razor
- [x] T024 [US2] Update all existing test files with new stat names (Desire, Restraint, Connection, Dominance) and ThemeProfile references in DreamGenClone.Tests/

**Checkpoint**: All naming is consistent — stats renamed, RankingProfile→ThemeProfile, legacy data loads cleanly

---

## Phase 5: User Story 3 — Style Profile Extended Fields (Priority: P2)

**Goal**: Add ThemeAffinities, EscalatingThemeIds, and StatBias to StyleProfile domain model and persistence

**Independent Test**: Edit StyleProfiles via UI, verify 3 new JSON columns persist and load correctly, verify "Sultry" seed defaults

### Tests for User Story 3

- [x] T025 [P] [US3] Write StyleProfileAffinityTests: save/load with ThemeAffinities JSON, save/load with EscalatingThemeIds JSON, save/load with StatBias JSON, "Sultry" seed defaults verification in DreamGenClone.Tests/StoryAnalysis/StyleProfileAffinityTests.cs

### Implementation for User Story 3

- [x] T026 [P] [US3] Add ThemeAffinities (Dictionary<string, int>), EscalatingThemeIds (List<string>), and StatBias (Dictionary<string, int>) properties to StyleProfile domain model in DreamGenClone.Domain/StoryAnalysis/ (StyleProfile.cs or equivalent location)
- [x] T027 [US3] Add three ALTER TABLE migrations (ThemeAffinities, EscalatingThemeIds, StatBias columns with JSON defaults) to EnsureTables() and update SaveStyleProfileAsync/LoadStyleProfileAsync with JSON serialization for the new columns in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs
- [x] T028 [US3] Update "Sultry" seed data in StyleProfileService to include ThemeAffinities (intimacy:2, romantic-tension:2, emotional-vulnerability:1), EscalatingThemeIds (dominance, power-dynamics, forbidden-risk, humiliation, infidelity), and StatBias (Desire:1, Connection:1) in DreamGenClone.Infrastructure/StoryAnalysis/StyleProfileService.cs (or equivalent location)

**Checkpoint**: StyleProfile has 3 new fields persisted as JSON — "Sultry" defaults include affinities, escalation IDs, and stat bias

---

## Phase 6: User Story 4 — Scenario Seeding at Session Creation (Priority: P2)

**Goal**: Implement SeedFromScenarioAsync to pre-load ThemeTracker with scenario-derived scores and initialize character stats from profiles at session creation

**Independent Test**: Create scenario with known keyword-dense content, start session, verify ThemeTracker has pre-seeded ScenarioPhaseSignal scores matching expected weights and stats reflect base + bias

### Tests for User Story 4

- [x] T029 [P] [US4] Write ScenarioSeedAdaptiveStateTests: ThemeTracker initialization from catalog, MustHave +15 ChoiceSignal, HardDealBreaker blocked with score=0, scenario text keyword scoring at 0.6×/0.4× weights, BaseStatProfile + per-char overrides + StatBias application order, StatAffinities deltas on scoring themes in DreamGenClone.Tests/StoryAnalysis/ScenarioSeedAdaptiveStateTests.cs

### Implementation for User Story 4

- [x] T030 [US4] Implement SeedFromScenarioAsync in RolePlayAdaptiveStateService: (1) load enabled catalog entries → initialize ThemeTrackerState with Score=0 per entry, (2) resolve ThemeProfile → set Blocked=true for HardDealBreaker themes, (3) apply ChoiceSignal tier seeding (MustHave +15, StronglyPrefer +8, NiceToHave +3, Dislike -5, HardDealBreaker force-zero), (4) apply MustHave +3 persistent affinity bonus in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [x] T031 [US4] Implement scenario text keyword scoring in SeedFromScenarioAsync: score Opening/Example text at 0.6× weight, Plot/Setting/Style/Characters/Locations/Objects at 0.4× weight, character stat deltas at 0.3× weight, multiply by StyleProfile.ThemeAffinities when present, write results to ScenarioPhaseSignal in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [x] T032 [US4] Implement stat initialization in SeedFromScenarioAsync: (1) load BaseStatProfile → set initial stat values, (2) merge per-character BaseStats overrides additively, (3) apply StyleProfile.StatBias additively, (4) apply ThemeCatalogEntry.StatAffinities as deltas for scoring themes in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [x] T033 [US4] Wire SeedFromScenarioAsync call in CreateSessionAsync after session object creation and before returning, when a scenario is present in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- [x] T034 [US4] Add Information-level logging for seeding pipeline: log theme count, blocked theme count, base stat profile applied, stat bias applied, top 3 seeded themes in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs

**Checkpoint**: New sessions have pre-seeded ThemeTracker and initialized stats — no more cold-start problem

---

## Phase 7: User Story 5 — Profile-Driven Style Resolution (Priority: P2)

**Goal**: Refactor style resolver to use StyleProfile.EscalatingThemeIds instead of hardcoded list, add MustHave push, and HardDealBreaker suppression

**Independent Test**: Pass different StyleProfile and ThemePreference combinations into Style Resolver, verify output label and reason reflect profile-driven logic including fallback

### Tests for User Story 5

- [x] T035 [P] [US5] Write StyleResolverProfileDrivenTests: escalation with profile EscalatingThemeIds, MustHave +1 push, HardDealBreaker suppression with "dealbreaker-suppressed" reason, fallback to legacy hardcoded list when no profile set in DreamGenClone.Tests/StoryAnalysis/StyleResolverProfileDrivenTests.cs

### Implementation for User Story 5

- [x] T036 [US5] Replace IsEscalatingTheme() hardcoded theme list with parameterized check against StyleProfile.EscalatingThemeIds, adding IReadOnlyList<string> parameter and using Contains with OrdinalIgnoreCase in DreamGenClone.Web/Application/RolePlay/RolePlayStyleResolver.cs
- [x] T037 [US5] Add fallback logic: when no StyleProfile is set on the session, use the legacy hardcoded escalating theme list (dominance, power-dynamics, forbidden-risk, humiliation, infidelity) in DreamGenClone.Web/Application/RolePlay/RolePlayStyleResolver.cs
- [x] T038 [US5] Add MustHave +1 push: when Primary theme matches a MustHave preference in the active ThemeProfile, add +1 escalation delta before ceiling clamp in DreamGenClone.Web/Application/RolePlay/RolePlayStyleResolver.cs
- [x] T039 [US5] Add HardDealBreaker suppression: when Primary or Secondary theme is a HardDealBreaker, suppress all escalation deltas and tag reason as "dealbreaker-suppressed" in DreamGenClone.Web/Application/RolePlay/RolePlayStyleResolver.cs
- [x] T040 [US5] Thread StyleProfile and ThemePreference data to ResolveEffectiveStyle call sites — update method signature and all callers in DreamGenClone.Web/Application/RolePlay/RolePlayStyleResolver.cs and callers

**Checkpoint**: Style resolution is profile-driven — escalation, MustHave push, and HardDealBreaker suppression all use profile data

---

## Phase 8: User Story 6 — Per-Interaction Affinity and StatAffinity Application (Priority: P3)

**Goal**: Extend per-interaction scoring loop with ThemeAffinities multiplication, StatAffinities deltas, and HardDealBreaker skip-scoring with SuppressedHitCount

**Independent Test**: Run interaction scoring with known StyleProfile affinity, verify multiplied scores and stat deltas; verify blocked themes accumulate SuppressedHitCount not score

### Tests for User Story 6

- [x] T041 [P] [US6] Write per-interaction affinity tests: ThemeAffinities 1.5× multiplication produces 1.5× score, StatAffinities +3 delta applied to acting character's stat, HardDealBreaker blocked theme SuppressedHitCount increments while score stays zero in DreamGenClone.Tests/StoryAnalysis/PerInteractionAffinityTests.cs

### Implementation for User Story 6

- [x] T042 [US6] Apply StyleProfile.ThemeAffinities multiplication to per-interaction theme score deltas in UpdateFromInteractionAsync scoring loop in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [x] T043 [US6] Apply ThemeCatalogEntry.StatAffinities as Character State deltas for the acting character when a theme scores during interaction in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs
- [x] T044 [US6] Implement HardDealBreaker skip-scoring: check Blocked flag before keyword scoring, increment SuppressedHitCount when keywords match, log at Debug level, ensure score remains zero in DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs

**Checkpoint**: Per-interaction scoring uses affinities and stat deltas — blocked themes tracked but never scored

---

## Phase 9: User Story 7 — Theme Preference Catalog Dropdown and UI Integration (Priority: P3)

**Goal**: Replace free-text ThemePreference Name with catalog dropdown, add CatalogId linking, auto-link migration, and catalog-sourced dropdowns in StyleProfile editor

**Independent Test**: Verify dropdowns in ThemePreference, StyleProfile ThemeAffinities, and Escalating Themes are populated from enabled ThemeCatalog entries

### Tests for User Story 7

- [x] T045 [P] [US7] Write auto-link and CatalogId tests: AutoLinkToCatalogAsync matches Name to Label, unlinked preferences flagged, CatalogId persists correctly in DreamGenClone.Tests/StoryAnalysis/ThemePreferenceCatalogLinkTests.cs

### Implementation for User Story 7

- [x] T046 [US7] Add CatalogId column (TEXT NOT NULL DEFAULT '') to ThemePreferences table via ALTER TABLE migration, update Save/Load methods to include CatalogId in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs
- [x] T047 [US7] Implement AutoLinkToCatalogAsync in ThemePreferenceService: match ThemePreference.Name against ThemeCatalogEntry.Label (case-insensitive), set CatalogId on match, log unlinked at Information level in the service that manages ThemePreferences (RankingCriteriaService.cs or its renamed equivalent)
- [x] T048 [US7] Add startup validation pass calling AutoLinkToCatalogAsync for all profiles and logging unlinked preferences per FR-034a in DreamGenClone.Web/Program.cs
- [x] T049 [US7] Replace free-text ThemePreference Name field with a dropdown populated from enabled ThemeCatalog entries in the ThemeProfile editor UI in DreamGenClone.Web/Components/ThemeProfiles.razor
- [x] T050 [US7] Add unlinked preference warning badge (visual indicator when CatalogId is empty) in the ThemePreference list view in DreamGenClone.Web/Components/ThemeProfiles.razor
- [x] T051 [US7] Add catalog-sourced dropdowns for ThemeAffinities keys and EscalatingThemeIds multi-select in the StyleProfile editor UI in DreamGenClone.Web/Components/ (StyleProfile editor component)

**Checkpoint**: All theme references use catalog dropdowns — no more free-text mismatches

---

## Phase 10: User Story 8 — AI Assistant Prompt Updates (Priority: P3)

**Goal**: Update all AI prompt text to use new terminology (ThemeProfile, Character State, Desire/Restraint/Connection/Dominance) and add seeding guidance

**Independent Test**: Inspect generated system prompt strings for updated terminology and new context sections

### Implementation for User Story 8

- [x] T052 [P] [US8] Update RolePlayAssistantPrompts: replace "Ranking Profile" with "Theme Profile", "Base Stats" with "Character State", update stat names to Desire/Restraint/Tension/Connection/Dominance, add Theme Catalog and Theme Tracker references in DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs
- [x] T053 [P] [US8] Update ScenarioAssistantPrompts: add guidance about Opening/Example 0.6× scoring weight, character description stat seeding at 0.3× weight, and StyleProfile affinity influence in DreamGenClone.Web/Application/Scenarios/ScenarioAssistantPrompts.cs
- [x] T054 [US8] Update context line in BuildUserMessage from `ranking={context.SelectedRankingProfileId}` to `theme={context.SelectedThemeProfileId}` in DreamGenClone.Web/Application/RolePlay/RolePlayAssistantService.cs

**Checkpoint**: AI prompts use consistent new terminology and include seeding guidance

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Debug panel updates, UI label consistency, and final validation

- [x] T055 [P] Update RolePlayWorkspace.razor debug panel to show SuppressedHitCount with lock icon for blocked themes and Blocked status indicator in DreamGenClone.Web/Components/RolePlayWorkspace.razor
- [x] T056 [P] Rename all "Base Stats" labels to "Character State" and update all stat labels to new names across all remaining UI surfaces in DreamGenClone.Web/Components/
- [x] T057 [P] Add Theme Catalog management tab with table view, inline editing, add/disable/delete controls to the ThemeProfiles page in DreamGenClone.Web/Components/ThemeProfiles.razor
- [x] T058 Run full build (dotnet build DreamGenClone.sln) and all tests (dotnet test) to verify zero compilation errors and all tests pass
- [x] T059 Run quickstart.md validation: build, test, and manual smoke test of catalog CRUD, session seeding, and style resolution

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — verify baseline
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational (T002, T003, T006)
- **US2 (Phase 4)**: Depends on Foundational only — **parallel with US1**
- **US3 (Phase 5)**: Depends on Foundational (T002 for ThemeCatalogEntry type references) — **parallel with US1 and US2**
- **US4 (Phase 6)**: Depends on US1 (catalog service), US2 (stat renames), US3 (StatBias/ThemeAffinities)
- **US5 (Phase 7)**: Depends on US3 (EscalatingThemeIds), Foundational (T004 Blocked flag)
- **US6 (Phase 8)**: Depends on US1 (catalog), US3 (ThemeAffinities), US4 (seeded state)
- **US7 (Phase 9)**: Depends on US1 (catalog CRUD), US3 (StyleProfile extensions)
- **US8 (Phase 10)**: Depends on US2 (renames) — **parallel with US3-US7**
- **Polish (Phase 11)**: Depends on all user stories complete

### User Story Dependency Graph

```text
Foundational ──┬── US1 (P1) ──┬─────────────────── US4 (P2) ── US6 (P3)
               │              │                      ↑
               ├── US2 (P1) ──┤──── US8 (P3)        │
               │              │                      │
               └── US3 (P2) ──┴── US5 (P2)    ──────┘
                              │
                              └── US7 (P3)
```

### Within Each User Story

- Tests MUST be written first and FAIL before implementation
- Domain models before services
- Services before wiring/integration
- Core logic before UI components
- Logging added after core logic works

### Parallel Opportunities

**After Foundational completes, three story tracks can run in parallel:**

- **Track A**: US1 → US4 → US6 (catalog → seeding → per-interaction)
- **Track B**: US2 → US8 (renames → prompt updates)
- **Track C**: US3 → US5 (style extensions → style resolution)
- **Track D** (joins A+C): US7 (UI dropdowns, after US1+US3)

---

## Parallel Example: After Foundational

```text
Track A (US1):  T007 ─── T008 ── T009 ── T010 ── T011
Track B (US2):  T012 ─── T013 ┬─ T015 ── T018 ── T022 ── T024
                         T014 ┘  T016 ┐
                         T019 ─  T017 ┤─ T020 ── T021 ── T023
Track C (US3):  T025 ─── T026 ── T027 ── T028
```

---

## Implementation Strategy

### MVP Scope

**User Story 1 (Theme Catalog) + Foundational = Minimum Viable Increment**

After completing Phase 2 + Phase 3, the application has a data-driven theme catalog replacing the hardcoded array. This is independently deployable and testable.

### Incremental Delivery

1. **Increment 1** (MVP): Foundational + US1 — data-driven catalog
2. **Increment 2**: US2 — renames (purely mechanical, low risk)
3. **Increment 3**: US3 — StyleProfile extensions (additive columns)
4. **Increment 4**: US4 + US5 — seeding + style resolution (core engine intelligence)
5. **Increment 5**: US6 + US7 + US8 — per-interaction scoring + UI polish + prompts
6. **Increment 6**: Polish — debug panel, label cleanup, validation
