# Tasks: Finishing Move System – Catalog and Matrix Redesign

**Input**: Design documents from `/specs/007-finishing-move-catalog/`  
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/IRPThemeService-additions.md ✅, quickstart.md ✅

**Format**: `- [ ] [TaskID] [P?] [Story?] Description with file path`  
- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks in this phase)  
- **[Story]**: US1/US2/US3/US4 — which user story this delivers  
- Setup/Foundational/Polish phases: no story label

---

## Phase 1: Setup

**Purpose**: Confirm build baseline before any changes

- [X] T001 Run `dotnet build DreamGenClone.sln -v minimal` and confirm zero errors before starting

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain models, service interface, schema DDL, migration, and CRUD implementations that ALL user stories depend on.

**⚠️ CRITICAL**: US1, US2, US3, and US4 cannot begin until this phase is complete.

- [X] T002 Add 5 new domain model classes (`RPFinishLocation`, `RPFinishFacialType`, `RPFinishReceptivityLevel`, `RPFinishHisControlLevel`, `RPFinishTransitionAction`) and rename `DominanceBand` → `OtherManDominanceBand` with default `"30-59"` in `RPFinishingMoveMatrixRow` in `DreamGenClone.Domain/RolePlay/RPThemeModels.cs` — see `data-model.md` for all fields
- [X] T003 [P] Add 15 new method signatures (3 CRUD methods × 5 catalog types: `SaveFinishLocationAsync`, `ListFinishLocationsAsync`, `DeleteFinishLocationAsync`, and equivalents for FacialType/ReceptivityLevel/HisControlLevel/TransitionAction) to `DreamGenClone.Application/RolePlay/IRPThemeService.cs` — see `contracts/IRPThemeService-additions.md`
- [X] T004 Add `MigrateFinishingMoveMatrixToV2Async` private static method (detect `DominanceBand` column via `TableHasColumnAsync`, archive rows to `RPFinishingMoveMatrixRows_Archived_v2`, DROP table + indexes); update `RPFinishingMoveMatrixRows` DDL to use `OtherManDominanceBand` with UNIQUE `(DesireBand, SelfRespectBand, OtherManDominanceBand)`; call migration at top of `EnsureSupplementalTablesAsync`; append 5 new `CREATE TABLE IF NOT EXISTS` blocks (`RPFinishLocations`, `RPFinishFacialTypes`, `RPFinishReceptivityLevels`, `RPFinishHisControlLevels`, `RPFinishTransitionActions`) in `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs`
- [X] T005 Update `SaveFinishingMoveMatrixRowAsync`, `ListFinishingMoveMatrixRowsAsync`, and `SaveFinishingMoveRowWithConnectionAsync` to use `OtherManDominanceBand` (rename all `DominanceBand` column references and property accesses) in `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs`
- [X] T006 Implement all 15 new CRUD methods for the 5 catalog types using the upsert-on-Id pattern from existing matrix methods (include structured Serilog logging on save/delete) in `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs`

**Checkpoint**: `dotnet build DreamGenClone.sln` passes. Foundation ready — all user stories can now proceed.

---

## Phase 3: User Story 2 – Matrix tab shows renamed "Other Man Dominance" column (Priority: P1)

**Goal**: The Matrix tab in Theme Profiles shows "Other Man Dominance Band" as the column header, the form uses the new label, and the dropdown offers `0-29`/`30-59`/`60-100`. The seed service uses the same renamed column and new band ranges.

**Independent Test**: Open Theme Profiles → Finishing Moves → Matrix tab. Confirm column header reads "Other Man Dominance Band" and band dropdowns show `0-29`, `30-59`, `60-100`.

- [X] T007 [P] [US2] Update `FinishingMoveMatrixSeedService.cs`: rename `SeedRow.DominanceBand` parameter; update all `SeedRow` constructor calls changing band ranges from `75-100`/`50-74`/`0-49` to `60-100`/`30-59`/`0-29`; update `OtherManBehaviorModifier` switch cases to match new band keys in `DreamGenClone.Infrastructure/RolePlay/FinishingMoveMatrixSeedService.cs`
- [X] T008 [P] [US2] Update Matrix tab in ThemeProfiles.razor: rename column header from "Dominance Band" → "Other Man Dominance Band"; update form field label and `@bind` from `DominanceBand` → `OtherManDominanceBand`; update dropdown option values to `0-29`/`30-59`/`60-100` in `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor`
- [X] T009 [P] [US2] Update `MatchesFinishingMoveRow` to reference `row.OtherManDominanceBand`; replace `GetAverageAdaptiveStat(state, "Dominance")` with `GetAverageAdaptiveStatOrFallback(state, dominance, "OtherManDominance", "OtherManDom", "RivalDominance", "BullDominance")` in `AppendFinishingMoveMatrixContextAsync`; update the prompt stat-context line to say `otherManDominance` in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T010 [P] [US2] Update `FinishingMoveMatrixSeedServiceTests.cs` and `RPFinishingMoveMatrixServiceTests.cs` to assert `OtherManDominanceBand` (not `DominanceBand`) and updated band values `0-29`/`30-59`/`60-100` in `DreamGenClone.Tests/RolePlay/`

**Checkpoint**: `dotnet test` passes for all existing finishing-move tests. Matrix tab shows correct header and band options.

---

## Phase 4: User Story 1 – Curator configures finishing move catalog entries (Priority: P1)

**Goal**: Five new management tabs in the Finishing Moves section of Theme Profiles, each showing a seeded entry table with `(any)` for empty eligibility fields, disabled entries greyed out, and a working add/edit/save/delete form.

**Independent Test**: Navigate to Theme Profiles → Finishing Moves. Verify all five tabs are present, each loads seed data, `(any)` appears for empty eligibility, and an edit-and-save round-trip persists.

- [X] T011 [US1] Add tab headers for "Locations", "Facial Types", "Receptivity Levels", "His Control Levels", "Transition Actions" alongside the existing "Matrix" tab in the Finishing Moves section tab bar in `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor`
- [X] T012 [US1] Implement Locations tab: display table (Name, Category, EligibleDesireBands, EligibleSelfRespectBands, EligibleOtherManDominanceBands, SortOrder, IsEnabled) with `(any)` for empty eligibility fields, greyed row style for disabled entries, and inline edit form with category dropdown + band eligibility text fields + save/delete buttons backed by `IRPThemeService.SaveFinishLocationAsync` / `DeleteFinishLocationAsync` in `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor`
- [X] T013 [US1] Implement Facial Types tab: display table (Name, PhysicalCues, eligibility bands, IsEnabled) with `(any)` for empty fields, disabled entry greying, and edit form backed by `SaveFinishFacialTypeAsync` / `DeleteFinishFacialTypeAsync` in `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor`
- [X] T014 [US1] Implement Receptivity Levels tab: display table (Name, PhysicalCues, NarrativeCue, EligibleDesireBands, EligibleSelfRespectBands, SortOrder, IsEnabled) with `(any)` for empty fields, and edit form backed by `SaveFinishReceptivityLevelAsync` / `DeleteFinishReceptivityLevelAsync` in `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor`
- [X] T015 [US1] Implement His Control Levels tab: display table (Name, ExampleDialogue, EligibleOtherManDominanceBands, SortOrder, IsEnabled) with `(any)` for empty fields, and edit form backed by `SaveFinishHisControlLevelAsync` / `DeleteFinishHisControlLevelAsync` in `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor`
- [X] T016 [US1] Implement Transition Actions tab: display table (Name, TransitionText, all three eligibility bands, SortOrder, IsEnabled) with `(any)` for empty fields, and edit form backed by `SaveFinishTransitionActionAsync` / `DeleteFinishTransitionActionAsync` in `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor`

**Checkpoint**: All five catalog tabs load, display seed data (once US4 seed services run), and allow editing. Matrix tab rename (US2) is also visible alongside new tabs.

---

## Phase 5: User Story 4 – Seed data auto-populates all catalogs on first run (Priority: P2)

**Goal**: All five catalog tables are populated with production-quality seed entries on application start with an empty database, with no manual steps.

**Independent Test**: Delete `data/dreamgenclone.db`, start the app, open Finishing Moves — all five tabs show populated entries with correct band eligibility values.

- [X] T017 [P] [US4] Create `RPFinishLocationSeedService.cs` with ≥15 `RPFinishLocation` entries spread across all five categories (`Internal`, `External`, `Facial`, `OnBody`, `Withdrawal`) with meaningful names, descriptions, and band eligibility values in `DreamGenClone.Infrastructure/RolePlay/RPFinishLocationSeedService.cs`
- [X] T018 [P] [US4] Create `RPFinishFacialTypeSeedService.cs` with ≥6 `RPFinishFacialType` entries (e.g., Open Mouth, Eyes Closed, Eyes Open, Pearl Necklace Variant) with physical cues and band eligibility in `DreamGenClone.Infrastructure/RolePlay/RPFinishFacialTypeSeedService.cs`
- [X] T019 [P] [US4] Create `RPFinishReceptivityLevelSeedService.cs` with exactly 8 `RPFinishReceptivityLevel` entries: Begging (SortOrder 0), Enthusiastic (1), Eager (2), Accepting (3), Tolerating (4), Reluctant (5), CumDodging (6), Enduring (7) — each with PhysicalCues, NarrativeCue, and Desire+SelfRespect band eligibility in `DreamGenClone.Infrastructure/RolePlay/RPFinishReceptivityLevelSeedService.cs`
- [X] T020 [P] [US4] Create `RPFinishHisControlLevelSeedService.cs` with exactly 3 `RPFinishHisControlLevel` entries: Asks (SortOrder 0, OtherManDominance `0-29`), Leads (1, `30-59`), Commands (2, `60-100`) — each with description and ExampleDialogue in `DreamGenClone.Infrastructure/RolePlay/RPFinishHisControlLevelSeedService.cs`
- [X] T021 [P] [US4] Create `RPFinishTransitionActionSeedService.cs` with ≥6 `RPFinishTransitionAction` entries (e.g., "Verbal command", "Holds in place", "Guides with hands", "Pulls close", "Steps back", "Kneels") with TransitionText and band eligibility in `DreamGenClone.Infrastructure/RolePlay/RPFinishTransitionActionSeedService.cs`
- [X] T022 [US4] Register `RPFinishLocationSeedService`, `RPFinishFacialTypeSeedService`, `RPFinishReceptivityLevelSeedService`, `RPFinishHisControlLevelSeedService`, and `RPFinishTransitionActionSeedService` as scoped services and call `SeedDefaultsAsync()` in the startup seed block in `DreamGenClone.Web/Program.cs`
- [X] T023 [P] [US4] Add `RPFinishLocationSeedServiceTests.cs`: assert ≥15 entries seeded, all 5 categories represented, no duplicate names, IsEnabled defaults true in `DreamGenClone.Tests/RolePlay/RPFinishLocationSeedServiceTests.cs`
- [X] T024 [P] [US4] Add `RPFinishFacialTypeSeedServiceTests.cs`: assert ≥6 entries, PhysicalCues non-empty for each, IsEnabled defaults true in `DreamGenClone.Tests/RolePlay/RPFinishFacialTypeSeedServiceTests.cs`
- [X] T025 [P] [US4] Add `RPFinishReceptivityLevelSeedServiceTests.cs`: assert exactly 8 entries, all 8 canonical names present, SortOrder 0–7 assigned, PhysicalCues and NarrativeCue non-empty for each in `DreamGenClone.Tests/RolePlay/RPFinishReceptivityLevelSeedServiceTests.cs`
- [X] T026 [P] [US4] Add `RPFinishHisControlLevelSeedServiceTests.cs`: assert exactly 3 entries, names are Asks/Leads/Commands, EligibleOtherManDominanceBands set to `0-29`/`30-59`/`60-100` respectively, ExampleDialogue non-empty for each in `DreamGenClone.Tests/RolePlay/RPFinishHisControlLevelSeedServiceTests.cs`
- [X] T027 [P] [US4] Add `RPFinishTransitionActionSeedServiceTests.cs`: assert ≥6 entries, TransitionText non-empty for each, seed skips on second run (idempotent) in `DreamGenClone.Tests/RolePlay/RPFinishTransitionActionSeedServiceTests.cs`

**Checkpoint**: `dotnet test` passes for all new seed service tests. Fresh-database startup populates all five catalog tabs.

---

## Phase 6: User Story 3 – Climax-phase prompt includes catalog context (Priority: P2)

**Goal**: The assembled prompt at climax phase contains a labeled section for each eligible catalog type (plus the existing matrix section), filtered by the session's current adaptive stats. The Facial Types section is suppressed when no eligible Location has `Category = Facial`. Sections are omitted when no eligible entries exist.

**Independent Test**: Advance a session to climax phase; inspect the prompt log to confirm labeled sections for matrix + all five catalog types (or subset based on eligibility).

- [X] T028 [US3] Add `MatchesBandEligibility(string? eligibleBands, double statValue)` static helper
- [X] T029 [US3] Extend `AppendFinishingMoveMatrixContextAsync`: after the existing matrix section, load each catalog via service, filter enabled entries with `MatchesBandEligibility`, compute `hasFacialLocation` from eligible Locations, emit labeled sections `"Finishing Move – Locations"`, `"Finishing Move – Receptivity"`, `"Finishing Move – His Control"`, `"Finishing Move – Transitions"`, and (only if `hasFacialLocation`) `"Finishing Move – Facial Types"`; omit any section with zero eligible entries; log eligible counts at Debug level in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`

**Checkpoint**: Climax-phase prompt log shows matrix section + catalog sections. Band filtering works correctly across all three tiers.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Build verification and test run

- [X] T030 [P] Run `dotnet build DreamGenClone.sln -v minimal` and resolve any remaining errors or warnings
- [X] T031 Run `dotnet test DreamGenClone.sln -v minimal` and confirm all tests pass (after T030)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — verify baseline before starting
- **Phase 2 (Foundational)**: Depends on Phase 1 — **BLOCKS all user story phases**
- **Phase 3 (US2)**: Depends on Phase 2 — all tasks [P], can run in parallel with each other
- **Phase 4 (US1)**: Depends on Phase 2 — sequential (all in ThemeProfiles.razor)
- **Phase 5 (US4)**: Depends on Phase 2 — seed service files [P], can run in parallel; tests [P] can run in parallel once respective service file exists
- **Phase 6 (US3)**: Depends on Phase 2 and Phase 5 (seed data needed for meaningful end-to-end test)
- **Phase 7 (Polish)**: Depends on all prior phases

### User Story Dependencies

| Story | Priority | Depends On | Can Parallelize With |
|-------|----------|-----------|---------------------|
| US2 — Matrix rename | P1 | Foundational | US1 tasks (different files) |
| US1 — Catalog UI | P1 | Foundational | US2 tasks (different files) |
| US4 — Seed data | P2 | Foundational | US2, US1 |
| US3 — Prompt assembly | P2 | Foundational + US4 | US1 (different sections) |

### Within Phase 2 (Foundational)

- T002 first (domain models needed by all subsequent tasks)
- T003, T004 after T002 — can be done in sequence (T003 first is natural: DDL block in RPThemeService before CRUD)
- T005 after T004 (updates methods that reference the new DDL column name)
- T006 after T003 and T004 (needs interface + schema)

### Within Phase 5 (Seed Services)

- T017–T021 can be worked in parallel (5 different files)
- T022 after T017–T021 (registers all five services)
- T023 after T017, T024 after T018, T025 after T019, T026 after T020, T027 after T021 (each test depends on its service class existing)

---

## Parallel Execution Examples

### Phase 3 (US2) — All 4 tasks run in parallel
```
T007: FinishingMoveMatrixSeedService.cs
T008: ThemeProfiles.razor (matrix tab labels)
T009: RolePlayWorkspace.razor (MatchesFinishingMoveRow + stat read)
T010: Tests (FinishingMoveMatrixSeedServiceTests, RPFinishingMoveMatrixServiceTests)
```

### Phase 5 (US4) — Seed services run in parallel, then tests in parallel
```
Wave 1 (parallel): T017, T018, T019, T020, T021
Wave 2:            T022 (Program.cs registration, after all of wave 1)
Wave 3 (parallel): T023, T024, T025, T026, T027
```

### Phase 3 + Phase 4 — Can be worked simultaneously
```
Developer A: T007, T008, T010 (US2 — seed service + matrix UI updates)
Developer B: T009, T011–T016  (US2 workspace + US1 all catalog tabs)
```

---

## Implementation Strategy

**MVP scope** (delivers both P1 stories):  
Phases 1 → 2 → 3 → 4 = T001–T016

This gives a curator the renamed matrix tab and all five new catalog management tabs with working CRUD. Seed data (US4) and prompt injection (US3) complete the full feature.

**Incremental delivery order**:
1. Phases 1+2 — foundational (no visible change yet, but builds cleanly)
2. Phase 3 (US2) — matrix rename visible in UI immediately after
3. Phase 4 (US1) — five new catalog tabs visible with empty state
4. Phase 5 (US4) — tabs fill with seed data; fresh-db test passes
5. Phase 6 (US3) — climax prompts enriched with catalog context
6. Phase 7 — clean build + all tests green

**Total tasks**: 31 (T001–T031)  
**Task counts by story**: US1=6, US2=4, US3=2, US4=11, Foundational=5, Setup=1, Polish=2
