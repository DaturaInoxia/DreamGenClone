# Tasks: OtherMan Seduction Archetype

**Input**: Design documents from `/specs/066-otherman-seduction/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

**Tests**: Included per the spec's acceptance scenarios — catalog tests and prompt injection tests are explicitly required.

**Organization**: Grouped by user story to enable independent implementation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Exact file paths in descriptions

---

## Phase 1: Setup

**Goal**: Verify project state, no new infrastructure needed.

- [X] T001 Run `dotnet build DreamGenClone.sln` from repo root and confirm 0 errors before starting
- [X] T002 [P] Run `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~SteerRoleIntent"` and confirm existing catalog tests pass

---

## Phase 2: Foundational — Archetype Catalog (US1 prerequisite)

**Goal**: Create the `SeductionArchetype` record and `SeductionArchetypeCatalog` static class in the Domain layer. This is the single source of truth for all 8 archetype definitions. No user story can proceed without this.

**Independent test**: Call `SeductionArchetypeCatalog.Get("Competent")` and verify the returned record has correct Id, DisplayName, and Description.

- [X] T003 [P] Create `SeductionArchetype` record with Id, DisplayName, Description fields in `DreamGenClone.Domain/StoryAnalysis/SeductionArchetypeCatalog.cs`
- [X] T004 Create `SeductionArchetypeCatalog` static class with all 8 archetypes (Charmer, Competent, Confidante, Tease, Protector, Dominant, Mysterious, Situational) as `IReadOnlyList<SeductionArchetype> All` in `DreamGenClone.Domain/StoryAnalysis/SeductionArchetypeCatalog.cs`
- [X] T005 Implement `SeductionArchetypeCatalog.Get(string id)` — case-insensitive lookup returning `SeductionArchetype?` in `DreamGenClone.Domain/StoryAnalysis/SeductionArchetypeCatalog.cs`
- [X] T006 Implement `SeductionArchetypeCatalog.BuildGuidance(IReadOnlyList<string> archetypeIds)` — returns combined prose: `"{DisplayName}: {Description}"` per archetype joined with space, null for empty input, silently skips unrecognized IDs in `DreamGenClone.Domain/StoryAnalysis/SeductionArchetypeCatalog.cs`
- [X] T007 [P] Create unit tests: verify All has exactly 8 entries, Get returns null for null/empty/unknown, BuildGuidance returns null for null/empty, BuildGuidance returns correct combined text for known Ids, output is deterministic in `DreamGenClone.Tests/StoryAnalysis/SeductionArchetypeCatalogTests.cs`
- [X] T008 Build Domain project and run catalog tests: `dotnet build DreamGenClone.Domain/DreamGenClone.Domain.csproj && dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~SeductionArchetypeCatalog"` — all must pass

---

## Phase 3: User Story 1 — Configure OtherMan Seduction Style Per Character (P1)

**Goal**: Add `SeductionArchetypes` property to the `Character` entity so scenario authors can assign zero-to-many archetypes per OtherMan character. Character persists the list as a JSON array within the existing scenario blob.

**Independent test**: Create a Character, set `SeductionArchetypes = ["Competent", "Confidante"]`, serialize/deserialize via System.Text.Json, verify the list round-trips correctly and defaults to `[]` for new Characters.

- [X] T009 Add `public List<string> SeductionArchetypes { get; set; } = [];` property to `Character` class in `DreamGenClone.Web/Domain/Scenarios/Character.cs`
- [X] T010 Verify `System.Text.Json` round-trip: existing scenario JSON without `SeductionArchetypes` key deserializes with `[]` (backward compatible), new JSON with the key round-trips correctly. No test file needed — confirm via existing scenario deserialization tests still pass: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~Scenario"` — all must pass

---

## Phase 4: User Story 2 — Research-Backed Role-Level Defaults (P1)

**Goal**: Update `SteerRoleIntentCatalog` OtherMan TOWARDS intent and `GetRoleContext("OtherMan")` with research-backed seduction guidance that serves as the fallback when no per-character archetypes are configured.

**Independent test**: Inspect the updated catalog text. Verify it references archetype behavioral modes with concrete genre examples, not generic courtship advice.

- [X] T011 Replace `("OtherMan", "Towards")` entry in `SteerRoleIntentCatalog.AllEntries` with genre-grounded text that references archetype behavioral modes (display physical competence, build emotional intimacy, calibrate verbal seduction, exploit proximity) with concrete examples in `DreamGenClone.Domain/StoryAnalysis/SteerRoleIntentCatalog.cs`
- [X] T012 Update `GetRoleContext("OtherMan")` return value to reference the archetype framework without prescribing a specific archetype — describe the narrative job in terms encompassing all 8 archetypes in `DreamGenClone.Domain/StoryAnalysis/SteerRoleIntentCatalog.cs`
- [X] T013 Run existing steer role intent tests: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~SteerRoleIntent"` — all must pass with updated text

---

## Phase 5: User Story 3 — Seduction Guidance in Continuation Prompts (P2)

**Goal**: Inject per-character seduction archetype guidance into continuation prompts via `CharacterDataSlot.AppendCharacterRoleIntents()`. When a character has `Role == "OtherMan"` and non-empty `SeductionArchetypes`, append the guidance after the role context.

**Independent test**: Build a prompt with an OtherMan character configured with ["Competent", "Confidante"]. Verify the prompt's "Character Role Intents" section contains "Seduction style:" line with combined archetype guidance. Build with no archetypes — verify NO "Seduction style:" line.

- [X] T014 Extend `CharacterDataSlot.AppendCharacterRoleIntents()` to append `"  Seduction style: {BuildGuidance(SeductionArchetypes)}"` after the role intent line when `character.Role == "OtherMan"` (case-insensitive) AND `SeductionArchetypes` is non-empty in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/CharacterDataSlot.cs`
- [X] T015 Verify injection respects the contract: guidance only fires for OtherMan role, falls back to catalog-only when archetypes empty, uses `SeductionArchetypeCatalog.BuildGuidance()` as sole source, line is trimmable (Slot 5 IsTrimEligible). No code change needed — confirm by reading `CharacterDataSlot.cs` line-by-line against contract invariants.
- [X] T016 [P] Create/extend unit tests: verify prompt contains "Seduction style:" when OtherMan has archetypes, absent when no archetypes, absent when role is Husband/Wife even if archetypes configured, multiple OtherMan characters each get independent guidance in `DreamGenClone.Tests/RolePlay/Prompts/CharacterDataSlotTests.cs`
- [X] T017 Run prompt injection tests: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~CharacterDataSlot"` — all must pass, new test cases must pass

---

## Phase 6: User Story 4 — Scenario Author UI for Archetype Selection (P3)

**Goal**: Add a multi-select archetype picker to the scenario editor's character settings panel so authors can assign seduction archetypes to OtherMan characters with a live preview of the blended guidance text.

**Independent test**: Open scenario editor, navigate to OtherMan character, select "Competent" and "Confidante" in the archetype picker, verify a preview shows the combined guidance text, save and re-open — selections persist.

- [X] T018 Add archetype multi-select UI section to the character settings panel in the scenario editor. Section header: "Seduction Archetypes". Show all 8 archetypes with checkboxes/chips (DisplayName + short description). Only visible when character role is OtherMan in `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor`
- [X] T019 Add live preview textbox showing `SeductionArchetypeCatalog.BuildGuidance(selectedIds)` output. Update on selection change. Place below the archetype selector. Read-only, labeled "Prompt Preview" in `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor`
- [X] T020 Bind archetype selections to `character.SeductionArchetypes` list. Selection changes update the list. Save persists through existing scenario save flow (JSON blob in SQLite). Re-open restores selections in `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor`
- [X] T021 Manual verification: build the Web project (`dotnet build DreamGenClone.Web/DreamGenClone.csproj`), start the app, open an existing scenario with an OtherMan character, verify the archetype section appears for OtherMan but not for Husband/Wife, select/deselect archetypes, save, re-open, confirm persistence.

---

## Phase 7: Polish & Cross-Cutting

**Goal**: Full build, all tests pass, final validation.

- [X] T022 Run full solution build: `dotnet build DreamGenClone.sln` — 0 errors, 0 new warnings
- [X] T023 Run full test suite for affected areas: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~Seduction|FullyQualifiedName~CharacterDataSlot|FullyQualifiedName~SteerRoleIntent|FullyQualifiedName~Scenario"` — all pass
- [X] T024 Verify B-077 (gap-aware steering) compatibility: confirm `WillingnessSteerGapResolver` and `CharacterDataSlot` do not conflict — both may appear in the same prompt (archetype defines behavioral style, B-077 defines gap-closing tactical objective). Run existing B-077 tests: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~Willingness"` — all must pass
- [X] T025 Run quickstart validation per `specs/066-otherman-seduction/quickstart.md`: build, test, manual UI check

---

## Dependencies

```
Phase 1 (Setup)
    ↓
Phase 2 (Catalog)  ←── blocks all user stories
    ↓
Phase 3 (US1: Character data model)  ←── depends on Phase 2
    ↓
Phase 4 (US2: Catalog fallback)  ←── depends on Phase 2, parallels US1
    ↓
Phase 5 (US3: Prompt injection)  ←── depends on Phase 3 + Phase 4
    ↓
Phase 6 (US4: UI)  ←── depends on Phase 3 (needs Character property)
    ↓
Phase 7 (Polish)
```

- Phase 2 is the critical blocking phase — US1, US2, US3, US4 all depend on the catalog existing.
- US1 (Phase 3) and US2 (Phase 4) can run in parallel after Phase 2.
- US3 (Phase 5) needs both the Character property (US1) and the updated catalog fallback (US2).
- US4 (Phase 6) needs the Character property (US1) but not the prompt injection (US3).

## Parallel Opportunities

| Phase | Parallel tasks |
|-------|---------------|
| Phase 1 | T001, T002 |
| Phase 2 | T003, T007 (catalog tests) can run in parallel with T004-T006 |
| Phase 3-4 | US1 (T009-T010) and US2 (T011-T013) are fully parallel after Phase 2 |
| Phase 5 | T016 (tests) parallel with T014-T015 |
| Phase 7 | T022, T023, T024, T025 all parallel |

## Implementation Strategy

**MVP (P1 only)**: Complete Phase 1 → Phase 2 → Phase 3 + Phase 4. At this point the catalog exists, characters can be configured, and the role-level fallback is updated. The feature is functional for steering prompts (which use `SteerRoleIntentCatalog`) even without continuation prompt injection.

**P2 addition**: Add Phase 5 (CharacterDataSlot injection). Now the archetype guidance appears in continuation prompts on every turn.

**Full feature**: Add Phase 6 (UI). Authors can configure archetypes without manually editing JSON.
