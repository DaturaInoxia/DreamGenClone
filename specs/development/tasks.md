# Tasks: B-042 — Unify Character Stats Profiles with Encounter Behavior Profiles

**Feature Branch**: `development`  
**Input**: `specs/development/` — plan.md, spec.md, data-model.md, research.md, quickstart.md, contracts/  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Data Model**: [data-model.md](data-model.md)

## Format: `[ID] [P?] [Story?] Description — file path`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[US#]**: User story label — Phase 3+ only; maps to user stories in spec.md
- Tasks follow the bottom-up implementation order from quickstart.md

---

## Phase 1: Setup

**Purpose**: Baseline verification before any changes land

- [X] T001 Verify `development` branch builds clean — run `dotnet build DreamGenClone.sln -v minimal` and confirm zero errors before starting any implementation

**Checkpoint**: Clean baseline confirmed — implementation may proceed

---

## Phase 2: Foundational — Domain Entities, Application Contracts, Persistence

**Purpose**: Bottom-up infrastructure that blocks all user stories. Every subsequent phase depends on this being complete.

**⚠️ CRITICAL**: No user story phase work until this phase reaches a clean build

### Domain Layer

- [X] T002 Create `CharacterProfile` entity with all properties: `Id` (GUID no-dashes string), `Name`, `Description`, `TargetGender`, `TargetRole`, `CharacterStats Dictionary<string,int>`, `EncounterStats Dictionary<string,int>`, `AdditionalNotes`, `FullOverride bool`, `IsSeeded bool`, `CreatedUtc`, `UpdatedUtc` — initialize `CharacterStats` and `EncounterStats` to `new()` in property initializers in `DreamGenClone.Domain/StoryAnalysis/CharacterProfile.cs`
- [X] T003 [P] Create `BehavioralDimension` sealed record with `(string Name, string TargetRole, string Tier1Text, string Tier2Text, string Tier3Text, string Tier4Text)` and `BehavioralDimensionCatalog` static class with all 14 dimension definitions inline (6 Husband: Awareness/Acceptance/Voyeurism/Participation/Encouragement/RiskTolerance; 4 Wife: DiscoveryCaution/Exhibitionism/EmotionalEngagement/PostEncounterGuilt; 4 OtherMan: HusbandAwareness/MarriageContextUse/DiscoveryRisk/PersistencePastLimits) using tier texts from data-model.md; expose `GetDimensions(string targetRole)`, `FindDimension(string targetRole, string name)`, and `ResolveTierText(string targetRole, string name, int value)` (thresholds: ≤20→Tier1, ≤50→Tier2, ≤75→Tier3, >75→Tier4) in `DreamGenClone.Domain/StoryAnalysis/BehavioralDimensionCatalog.cs`
- [X] T004 [P] Add `[Obsolete("Replaced by CharacterProfile — B-042")]` to `BaseStatProfile` class declaration in `DreamGenClone.Domain/StoryAnalysis/BaseStatProfile.cs` and `HusbandAwarenessProfile` class declaration in `DreamGenClone.Domain/StoryAnalysis/HusbandAwarenessProfile.cs`
- [X] T005 Replace `public string? HusbandAwarenessProfileId { get; set; }` with `public Dictionary<string, string> CharacterEncounterProfileIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);` in `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`
- [X] T006 Fix all compile errors caused by T005 across five files — (a) remove `HusbandAwarenessProfileId` write and stub `CharacterEncounterProfileIds` initialization in `DreamGenClone.Infrastructure/RolePlay/RolePlayEngineService.cs`; (b) replace `HusbandAwarenessProfileId` copy with `CharacterEncounterProfileIds` dictionary copy in `DreamGenClone.Infrastructure/RolePlay/SemanticInteractionAnalysisJobHandler.cs`; (c) remove the old profile binding reference in `DreamGenClone.Web/Domain/RolePlay/RolePlayWorkspace.razor`; (d) replace `HusbandAwarenessProfileId` references with `CharacterEncounterProfileIds` dictionary usage in `DreamGenClone.Tests/RolePlay/AdaptiveScenarioStateV2RoundTripTests.cs`; (e) update the `Assert.Equal("awareness-99", session.AdaptiveState.HusbandAwarenessProfileId)` assertion in `DreamGenClone.Tests/RolePlay/SessionThemeSelectionsTests.cs` — **solution must build clean after this task**

### Application Layer

- [X] T007 Create `ICharacterProfileService` interface with methods `GetAsync`, `GetAllAsync`, `GetByRoleAsync`, `SaveAsync`, `DeleteAsync`, `EnsureDefaultsAsync` per contracts/ICharacterProfileService.md in `DreamGenClone.Application/StoryAnalysis/Abstractions/ICharacterProfileService.cs`
- [X] T008 [P] Create `IBehavioralFrameGenerator` interface with `GenerateFramesAsync(IReadOnlyDictionary<string,string> characterEncounterProfileIds, IReadOnlyList<ScenarioCharacter> characters, CancellationToken)` returning `Task<IReadOnlyDictionary<string,string>>` per contracts/IBehavioralFrameGenerator.md in `DreamGenClone.Application/StoryAnalysis/Abstractions/IBehavioralFrameGenerator.cs`
- [X] T009 Update `ScenarioGuidanceRequest` record: replace `string? HusbandAwarenessProfileId` with `IReadOnlyDictionary<string,string> CharacterEncounterProfileIds` and `IReadOnlyList<ScenarioCharacter> Characters`; update `ScenarioGuidanceOutput` record: replace `string? HusbandAwarenessFrame` with `IReadOnlyDictionary<string,string> CharacterBehavioralFrames` (never null, empty when no frames) per contracts/ScenarioGuidanceContracts.md in `DreamGenClone.Application/RolePlay/RolePlayContracts.cs`
- [X] T010 Update `ScenarioGuidanceInput` and `ScenarioGuidanceContext` records with the same field replacements (`HusbandAwarenessProfileId` → `CharacterEncounterProfileIds + Characters`; `HusbandAwarenessFrame` → `CharacterBehavioralFrames`) per contracts/ScenarioGuidanceContracts.md in `DreamGenClone.Application/StoryAnalysis/Models/ScenarioEngineContracts.cs`
- [X] T011 [P] Add `[Obsolete]` attribute to `IBaseStatProfileService` and `IHusbandAwarenessProfileService` in their respective Abstractions files under `DreamGenClone.Application/`

### Persistence Layer

- [X] T012 Add `CharacterProfiles` table DDL (`CREATE TABLE IF NOT EXISTS` with all columns per data-model.md schema), and implement `SaveCharacterProfileAsync`, `LoadCharacterProfileAsync`, `LoadAllCharacterProfilesAsync`, `DeleteCharacterProfileAsync` persistence methods (serialize/deserialize `CharacterStatsJson` and `EncounterStatsJson` via `System.Text.Json`) in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- [X] T013 Add startup migration logic (run in order on every startup): (1) `DELETE FROM BaseStatProfiles WHERE Name = 'Balanced Baseline'`; (2) `INSERT OR IGNORE INTO CharacterProfiles SELECT ... FROM BaseStatProfiles` mapping `DefaultStatsJson→CharacterStatsJson`, `EncounterStatsJson='{}'`, `IsSeeded=1`; (3) `INSERT OR IGNORE INTO CharacterProfiles SELECT ... FROM HusbandAwarenessProfiles` using `json_object()` for encounter stats, neutral `'{"Desire":50,...}'` for character stats, `Notes→AdditionalNotes`, `TargetRole='Husband'`; (4) `ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CharacterEncounterProfileIdsJson TEXT NULL` guarded with `PRAGMA table_info` existence check in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- [X] T014 Add session load backward compat: in the session load path, if `CharacterEncounterProfileIdsJson` is NULL but `HusbandAwarenessProfileId` is set, find the character with `Role="Husband"` in the session's character list and synthesize `CharacterEncounterProfileIds = { husbandCharId → HusbandAwarenessProfileId }`; mark session dirty for re-save in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`

**Checkpoint**: Solution builds clean; all domain types, application interfaces, and persistence scaffolding present; migration logic in place

---

## Phase 3: US5 — Prompt Injection Updated for All Roles (P1)

**Goal**: Continuation prompt injects behavioral frame text for every character with a bound encounter profile as labeled HARD CONSTRAINTs at both injection sites (early guidance section + immediately before writing directive).

**Independent Test**: Build a `ScenarioGuidanceContext` with three character profiles bound (husband/wife/otherman); verify the output contains three distinct HARD CONSTRAINT blocks each labeled with the character's name and role; verify `FullOverride=true` with non-empty `AdditionalNotes` bypasses dimension text.

- [X] T015 [US5] Create `CharacterBehavioralFrameGenerator` implementing `IBehavioralFrameGenerator`: for each character with a bound profile, load profile from `ICharacterProfileService`, resolve each encounter dimension via `BehavioralDimensionCatalog.ResolveTierText()`, apply `FullOverride`/`AdditionalNotes` rules per contracts/IBehavioralFrameGenerator.md, format character label as `"{character.Name} ({character.Role})"`, omit characters with no bound profile or profile not found (Serilog Warning), Serilog Info on entry, Debug per frame generated in `DreamGenClone.Infrastructure/StoryAnalysis/CharacterBehavioralFrameGenerator.cs`
- [X] T016 [US5] Update `ScenarioGuidanceContextFactory`: replace `HusbandAwarenessProfileId` lookup path with call to `IBehavioralFrameGenerator.GenerateFramesAsync(state.CharacterEncounterProfileIds, session.Characters)`; store result in `ScenarioGuidanceContext.CharacterBehavioralFrames` in `DreamGenClone.Infrastructure/StoryAnalysis/ScenarioGuidanceContextFactory.cs`
- [X] T017 [US5] Update `ScenarioGuidanceGenerator`: remove `BuildHusbandAwarenessInterpretationAsync` method and its call site; ensure `ScenarioGuidanceOutput.CharacterBehavioralFrames` is populated from `ScenarioGuidanceContext.CharacterBehavioralFrames` in `DreamGenClone.Infrastructure/RolePlay/ScenarioGuidanceGenerator.cs`
- [X] T018 [P] [US5] Update early injection site in `BuildScenarioGuidanceSection`: replace single husband frame block with `foreach (var (label, frameText) in context.CharacterBehavioralFrames)` loop emitting `"HARD CONSTRAINT — {label} behavioral frame: {frameText}"` per character in `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs`
- [X] T019 [P] [US5] Update second HARD CONSTRAINT injection site (immediately before writing directive): replace single husband frame block with same `foreach` loop over `CharacterBehavioralFrames` using identical format as T018 in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`
- [X] T020 [US5] Update `ScenarioGuidanceGeneratorTests.cs`: replace husband-only frame assertions with multi-character assertions; add test cases verifying wife frame and otherman frame both appear when all three character profiles are bound; verify empty frames produce no HARD CONSTRAINT blocks in `DreamGenClone.Tests/RolePlay/ScenarioGuidanceGeneratorTests.cs`

**Checkpoint**: Prompt injection works for all three roles; ScenarioGuidanceGeneratorTests pass; empty profile dictionary produces no HARD CONSTRAINT blocks

---

## Phase 4: US1 — Unified Character Profile CRUD (P1)

**Goal**: Configuration section shows a single "Character Profiles" tab replacing the two existing tabs; profile form has labeled Character Stats group (7 canonical sliders) and Encounter Behavior group (role-specific dimension sliders); live preview updates on every slider move via synchronous `BehavioralDimensionCatalog` calls; all 25 archetypes seed on app start; old two-tab setup removed (satisfies US4 acceptance scenario 3).

**Independent Test**: Navigate to Configuration → Character Profiles; open any Husband archetype, move Voyeurism slider to 85 → live preview immediately shows Tier 4 voyeurism sentence without page interaction; create a new Wife profile, save → appears in filtered list under Wife; old "Husband Awareness Profiles" tab is gone.

- [X] T021 [US1] Create `CharacterProfileService` implementing `ICharacterProfileService`: full CRUD via `SqlitePersistence` extension methods from T012; `EnsureDefaultsAsync` seeds all 25 unified archetypes (8 Husband + 9 Wife + 8 OtherMan from spec.md tables) using INSERT OR IGNORE semantics; `SaveAsync` validates `CharacterStats` keys against `AdaptiveStatCatalog.StatNames` and `EncounterStats` keys against `BehavioralDimensionCatalog.GetDimensions(profile.TargetRole)` throwing `ArgumentException` on invalid keys; Serilog Info/Warning logging per contracts/ICharacterProfileService.md in `DreamGenClone.Infrastructure/StoryAnalysis/CharacterProfileService.cs`
- [X] T022 [P] [US1] Mark `BaseStatProfileService` class with `[Obsolete("Replaced by CharacterProfileService — B-042")]` and remove its `EnsureDefaultsAsync` invocation from app startup wiring in `DreamGenClone.Infrastructure/StoryAnalysis/BaseStatProfileService.cs`
- [X] T023 [P] [US1] Mark `HusbandAwarenessProfileService` class with `[Obsolete("Replaced by CharacterProfileService — B-042")]` and remove its `EnsureDefaultsAsync` invocation from app startup wiring in `DreamGenClone.Infrastructure/StoryAnalysis/HusbandAwarenessProfileService.cs`
- [X] T024 [US1] Replace the two existing profile tabs with a single `character-profiles` tab in `ThemeProfiles.razor`: filterable list with role dropdown (All / Husband / Wife / OtherMan); edit form with two labeled groups — **Character Stats** (7 sliders using existing `GetBaseStatValue()/SetBaseStatValue()` helper pattern) and **Encounter Behavior** (role-specific dimension sliders using new `GetEncounterStatValue()/SetEncounterStatValue()` helpers, hidden entirely when `TargetRole == "Any"`); `AdditionalNotes` textarea; `FullOverride` checkbox visible only when `AdditionalNotes` is non-empty; Live Preview `<div>` calling `BehavioralDimensionCatalog.ResolveTierText()` synchronously on `@oninput` (no service call, no async); clear `EncounterStats` and re-initialize to default 50 for the new role's dimensions when `TargetRole` changes in `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor`
- [X] T025 [US1] Update `RolePlaySessionBaseStatInitializationTests.cs`: replace `BaseStatProfile` construction and `DefaultStats` property references with `CharacterProfile` and `CharacterStats` property in `DreamGenClone.Tests/RolePlay/RolePlaySessionBaseStatInitializationTests.cs`

**Checkpoint**: Profile CRUD fully functional in UI; live preview updates correctly; 25 archetypes seed on startup; old profile tabs absent

---

## Phase 5: US4 — Existing Profiles Migrated & Old System Retired (P1)

**Goal**: Application wired with new DI registrations; old services deregistered; on first startup existing `BaseStatProfiles` and `HusbandAwarenessProfiles` rows migrate to `CharacterProfiles`; sessions holding legacy `HusbandAwarenessProfileId` resume and generate behavioral frames correctly.

**Independent Test**: Cold-start the app against a DB that has existing `HusbandAwarenessProfiles` and old sessions; run `dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/check_migration_b042.sql` to confirm migrated rows present; resume an old session and verify continuation prompt contains husband behavioral frame.

- [X] T026 [US4] Update `Program.cs` DI registrations: remove `IBaseStatProfileService → BaseStatProfileService` and `IHusbandAwarenessProfileService → HusbandAwarenessProfileService` registrations; add `ICharacterProfileService → CharacterProfileService` (singleton or scoped per existing pattern) and `IBehavioralFrameGenerator → CharacterBehavioralFrameGenerator` in `DreamGenClone.Web/Program.cs`
- [X] T027 [US4] Write `artifacts/tmp/check_migration_b042.sql` querying `SELECT TargetRole, COUNT(*) FROM CharacterProfiles GROUP BY TargetRole` and confirming `CharacterEncounterProfileIdsJson` column exists in `RolePlayV2AdaptiveStates`; run with `dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/check_migration_b042.sql` and confirm rows are present for migrated profiles

**Checkpoint**: App starts with new DI; migrated profiles visible in Character Profiles tab; old sessions load and resume without errors

---

## Phase 6: US2 — Session Creation with Per-Character Encounter Profiles (P1)

**Goal**: Session creation wizard shows a single unified profile picker per character filtered by role; selecting a profile seeds both canonical stats and binds the encounter profile ID; `CharacterEncounterProfileIds` is written to the new session's `AdaptiveState`.

**Independent Test**: Create a new session selecting "Cuckold Husband" for the husband character; confirm the session's `AdaptiveState.CharacterEncounterProfileIds` contains the husband character's entry; generate a continuation and verify the prompt includes Cuckold Husband's behavioral frame (Voyeurism Tier 4 text visible).

- [X] T028 [P] [US2] Update `RolePlayCreate.razor`: replace `_awarenessProfileId` field with `_characterEncounterProfileIds Dictionary<string,string>`; replace the existing character profile picker(s) with a single `<select>` per character loading from `ICharacterProfileService.GetByRoleAsync(character.Role)`; update `ApplyCharacterStatProfile()` to (1) apply `CharacterProfile.CharacterStats` values to the character's stat fields and (2) add `profileId` to `_characterEncounterProfileIds[characterId]`; pass `_characterEncounterProfileIds` as `CharacterEncounterProfileIds` in the session creation request in `DreamGenClone.Web/Components/Pages/RolePlayCreate.razor`
- [X] T029 [P] [US2] Update session creation write site in `RolePlayEngineService`: replace the `HusbandAwarenessProfileId` stub from T006(a) with a proper loop `foreach (var kvp in request.CharacterEncounterProfileIds) session.AdaptiveState.CharacterEncounterProfileIds[kvp.Key] = kvp.Value` in `DreamGenClone.Infrastructure/RolePlay/RolePlayEngineService.cs`

**Checkpoint**: Session creation binds per-character encounter profiles to the new session; continuation generates multi-character behavioral frames correctly

---

## Phase 7: US6 — BehavioralDimensionCatalog as Single Source of Truth (P2)

**Goal**: `BehavioralDimensionCatalog` is the only place tier descriptions exist in the codebase; it is test-covered for all boundary values and all three roles; a developer updating a tier description edits only the catalog.

**Independent Test**: All `BehavioralDimensionCatalogTests` pass with zero failures; no tier text strings exist outside `BehavioralDimensionCatalog.cs` (grep check).

- [X] T030 [US6] Create `BehavioralDimensionCatalogTests.cs` with test cases: Tier 1 at exact boundary (value=20); Tier 2 at boundaries (value=21, value=50); Tier 3 at boundaries (value=51, value=75); Tier 4 at boundaries (value=76, value=100); `GetDimensions("Husband")` returns 6 non-null dimensions; `GetDimensions("Wife")` returns 4; `GetDimensions("OtherMan")` returns 4; `ResolveTierText` for all 14 named dimensions at value=50 returns non-empty string; unknown dimension name returns empty string without throwing in `DreamGenClone.Tests/StoryAnalysis/BehavioralDimensionCatalogTests.cs`

**Checkpoint**: Catalog architecture verified by tests; single source of truth confirmed

---

## Phase 8: US3 — Behavioral Frame in RP Workspace Adaptive Panel (P2)

**Goal**: Active session adaptive panel displays the current behavioral frame text for each character with a bound encounter profile; user can switch to a different profile for any character mid-session; next continuation uses the new profile's frame; characters with no bound profile cause no error (simply omitted from HARD CONSTRAINTs).

**Independent Test**: Open an active session's adaptive panel → behavioral frame text displayed for each profiled character; change the wife's profile to a different archetype → generate a continuation → verify the new wife frame text appears in the prompt; remove a character's profile binding → continuation generates with no frame for that character.

- [X] T031 [US3] Replace the single `HusbandAwarenessProfile` change handler with per-character profile switchers in the adaptive panel section: for each character in the session, render a profile `<select>` filtered by character role loading from `ICharacterProfileService.GetByRoleAsync(character.Role)`; display the current behavioral frame text computed from the bound profile (call `IBehavioralFrameGenerator.GenerateFramesAsync` or compute inline via catalog); on change update `_session.AdaptiveState.CharacterEncounterProfileIds[character.Id]` and persist the session in `DreamGenClone.Web/Domain/RolePlay/RolePlayWorkspace.razor`

**Checkpoint**: All 6 user stories independently functional; per-character profile switching works mid-session

---

## Final Phase: Polish & Cross-Cutting Concerns

- [X] T032 Run full test suite and fix any remaining failures — `dotnet test DreamGenClone.sln`; confirm zero failing tests; all modified test files (`AdaptiveScenarioStateV2RoundTripTests.cs`, `ScenarioGuidanceGeneratorTests.cs`, `SessionThemeSelectionsTests.cs`, `RolePlaySessionBaseStatInitializationTests.cs`, `BehavioralDimensionCatalogTests.cs`) must pass in `DreamGenClone.Tests/`
- [X] T033 [P] Verify unified archetypes seeded correctly after app start using dbquery — `dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/check_character_profiles.sql` (write the SQL file if it doesn't exist); confirm 8 Husband + 9 Wife + 8 OtherMan = 25 total rows in `CharacterProfiles`

---

## Dependencies

```
Phase 2 (Foundational: T002–T014)
├── Phase 3 (US5: T015–T020)  ← needs T008, T009, T010, T012
├── Phase 4 (US1: T021–T025)  ← needs T003, T007, T012
│   └── Phase 5 (US4: T026–T027)  ← needs US5:T015 + US1:T021 for DI wiring
│       ├── Phase 6 (US2: T028–T029)  ← needs T026 (DI registration in place)
│       │   └── Phase 8 (US3: T031)   ← needs US2 per-character binding
│       └── Phase 8 (US3: T031)       ← also needs T026
└── Phase 7 (US6: T030)  ← needs T003 (catalog exists); independent of US1–US5
```

### Story Completion Order

1. **Phase 2** must finish before any story phase begins (no exceptions)
2. **US5 and US1 can run in parallel** once Phase 2 is complete — they operate on entirely different files (Infrastructure prompt/guidance vs Infrastructure service/UI)
3. **US4** bridges US5 + US1 via DI wiring; must follow both
4. **US2** requires US4 (DI registration must be active at runtime)
5. **US3** requires US2 (per-character profile binding must exist before workspace switches profiles)
6. **US6** tests can be written at any point after Phase 2 (catalog exists from T003)

---

## Parallel Execution Examples

### After Phase 2 completes — two developers can proceed independently:
- **Developer A (US5)**: T015 → T016 → T017 → T018 [P] + T019 [P] → T020
- **Developer B (US1)**: T021 → T022 [P] + T023 [P] → T024 → T025

### Within Phase 3 (US5):
- T018 (`RolePlayAssistantPrompts.cs`) and T019 (`RolePlayContinuationService.cs`) are parallel after T017

### Within Phase 4 (US1):
- T022 (`BaseStatProfileService.cs`) and T023 (`HusbandAwarenessProfileService.cs`) are parallel

### Within Phase 6 (US2):
- T028 (`RolePlayCreate.razor`) and T029 (`RolePlayEngineService.cs`) are parallel (different layers, different files)

---

## Implementation Strategy

### MVP Scope (minimum releasable increment — 28 tasks)
Phase 2 + Phase 3 (US5) + Phase 4 (US1) + Phase 5 (US4):
- Unified profile management fully functional in the UI
- Behavioral frames for all three roles inject into continuation prompts
- Existing data migrated; old system deregistered

### Increment 2
+ Phase 6 (US2): Session creation uses unified profiles for both stat seeding and encounter binding

### Full Delivery
+ Phase 7 (US6) + Phase 8 (US3) + Final Phase: Catalog test coverage, mid-session profile switching, full test suite passing

---

## Summary

| Metric | Value |
|---|---|
| Total tasks | 33 |
| Phase 1 — Setup | 1 |
| Phase 2 — Foundational | 13 |
| Phase 3 — US5 Prompt Injection (P1) | 6 |
| Phase 4 — US1 Profile CRUD (P1) | 5 |
| Phase 5 — US4 Migration & Retirement (P1) | 2 |
| Phase 6 — US2 Session Creation (P1) | 2 |
| Phase 7 — US6 Catalog Architecture (P2) | 1 |
| Phase 8 — US3 Workspace Panel (P2) | 1 |
| Final Phase — Polish | 2 |
| Parallelizable tasks [P] | 11 |
| MVP scope | Phase 2 + US5 + US1 + US4 (28 tasks) |
| Suggested MVP | Phases 2–5 (deliver working prompt injection + unified profile UI + migration) |
