---
description: "Task list for 006-explicit-scene-writing"
---

# Tasks: Explicit Scene Writing Directives

**Input**: Design documents from `/specs/006-explicit-scene-writing/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, quickstart.md ✓

**Organization**:
- **Phase 1 (Setup)**: No prior infrastructure needed — this feature adds onto a running codebase.
- **Phase 2 (Foundational)**: None — Phase 1 (static prompt changes) has zero blocking prerequisites; it is self-contained.
- **Phase 3 (US1)**: Five static prompt-improvement tasks. No schema change. Independently testable.
- **Phase 4 (US2)**: Narrative urgency directive is part of the Climax escalation block (US1 Step 1 already addresses it). US2 has no additional code tasks beyond US1; it is verified by a separate test against US1's implementation.
- **Phase 5 (US3)**: Configurable `SceneDirective` field — domain model, persistence migration, service layer, sanitizer utility, prompt injection update, UI form update.
- **Phase 6 (Polish)**: Build verification and cleanup.
- **Phase 7 (Tweaks)**: Position-spanning directive, male climax gate (`/endclimax`), minimum 350-word response — all prompt-only changes, no schema change.
- **Phase 8 (Multi-perspective turns)**: Same-scene multi-perspective Climax turns — first/subsequent character differentiation, omniscient narrative prompt, Turn Scene Contract removal, auto-narrative fix. No schema change.
- **Phase 9 (Beat Stage Cursor)**: Persistent beat-stage tracking in adaptive state — domain properties, DB migration, static catalog, advance logic, prompt injection. Schema change to `RolePlayV2AdaptiveStates`.

---

## Phase 1: Setup

**Purpose**: Confirm build is clean before any changes.

- [x] T001 Verify `dotnet build DreamGenClone.sln` passes clean on branch `006-explicit-scene-writing`

---

## Phase 2: Foundational

**Purpose**: No foundational blocking tasks exist for this feature. Phase 1 (US1) begins immediately.

*(No tasks — Phase 3 can start directly after T001.)*

---

## Phase 3: User Story 1 — Detailed Multi-Turn Climax Scene (Priority: P1) 🎯 MVP

**Goal**: Replace the rush-to-consummation Climax directive, inject a Scene Writing Directive block at Explicit/Hardcore intensity outside BuildUp, expand the Climax framing guard, update intensity descriptions, and improve Climax fallback guidance — all via static string changes with no schema change.

**Independent Test**: Start an RP session, advance to Climax phase at Explicit intensity, send a continuation request. Verify the response describes a physical act with body/sensation detail and does not resolve the entire scene. Send a second continuation and verify the AI advances to a new act rather than ending the scene. Verify BuildUp at Explicit intensity does NOT include the Scene Writing Directive block.

### Implementation for User Story 1

- [x] T002 [P] [US1] Replace Climax single-line rush directive with sustained-scene directive set (6 lines: pacing, act variety, urgency handling) in `AppendEscalationGuidance()` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` (~line 1342)
- [x] T003 [P] [US1] Inject static Scene Writing Directive block in `BuildPromptAsync()` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` (~line 952), gated by `resolvedScale >= (int)IntensityLevel.Explicit && currentPhase != "BuildUp" && intent != PromptIntent.Instruction`; extract static directive text into private static method `GetStaticSceneDirective()`
- [x] T004 [P] [US1] Expand `BuildFramingGuards()` Climax guard from 1 directive to 5 directives (pacing, multi-turn structure, positional detail, act variety) in `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` (~line 98)
- [x] T005 [P] [US1] Replace Explicit and Hardcore descriptions in `GetDefaultDescription()` in `DreamGenClone.Domain/StoryAnalysis/IntensityLadder.cs` (~line 47): add explicit `IntensityLevel.Hardcore =>` case (do not rely on `_` fallthrough); update Explicit to 3-sentence pacing/act-variety description; update Hardcore to 2-sentence exhaustive-specificity description
- [x] T006 [P] [US1] Replace Climax case guidance text in `CreateFallback()` in `DreamGenClone.Infrastructure/StoryAnalysis/ScenarioGuidanceContextFactory.cs` (~line 57) with multi-sentence guidance covering physical detail, pacing across turns, act variety, and urgency-without-abbreviation
- [x] T007 [US1] Verify `dotnet build DreamGenClone.sln` passes clean after all US1 changes

**Checkpoint**: User Story 1 fully deliverable — Climax phase at Explicit/Hardcore intensity produces detailed multi-beat, multi-turn scene writing using static prompt improvements only.

---

## Phase 4: User Story 2 — Narrative Urgency Does Not Abbreviate the Scene (Priority: P2)

**Goal**: Confirm that the urgency-handling directive introduced in T002 (the Climax escalation block) and T003 (Scene Writing Directive Pacing and Urgency section) is sufficient to address the narrative-urgency failure mode. No additional code changes are required for US2 — it is addressed by US1's implementation.

**Independent Test**: Configure a scenario with urgency language ("they only have ten minutes"), advance to Climax phase at Explicit intensity, send two continuation requests. Verify both responses maintain multi-paragraph physical description and that urgency appears in character dialogue/tone only — not in abbreviated scene length.

*(No additional implementation tasks — US2 is covered by T002 + T003. Verify separately against the assembled prompt.)*

**Checkpoint**: US1 + US2 both independently verifiable after Phase 3.

---

## Phase 5: User Story 3 — Custom Scene Writing Directive Per Intensity Profile (Priority: P3)

**Goal**: Add a `SceneDirective` property to `IntensityProfile`, persist it via SQLite column migration, sanitize it before prompt injection, wire it through the service layer, and expose it in the `ThemeProfiles.razor` UI form with disabled state for sub-Explicit levels.

**Independent Test**: Open the Intensity Profile edit page for an Explicit profile, enter custom scene-writing text in the new Scene Writing Directive textarea, save. Start an RP session at Climax phase Explicit intensity. Verify the custom text appears in the assembled system prompt. Clear the field and save — verify the system-default static directive appears on the next turn. Open a Moderate profile form — verify the SceneDirective textarea is visible but disabled with a tooltip.

### Implementation for User Story 3

- [x] T008 [US3] Add `SceneDirective` property (`public string SceneDirective { get; set; } = string.Empty;`) to `DreamGenClone.Domain/StoryAnalysis/IntensityProfile.cs`
- [x] T009 [US3] Create `DreamGenClone.Web/Application/RolePlay/PromptSanitizer.cs` — static class with `SanitizeSceneDirective(string? input)`: truncate to 2000 chars, strip null bytes and non-printable control chars (except `\n`, `\r`, `\t`), remove lines starting with injection tokens (`SYSTEM:`, `USER:`, `ASSISTANT:`, `[INST]`, `</s>`, `###`, `<|`)
- [x] T010 [US3] Add `SceneDirective` column migration in `InitializeSchemaAsync()` in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`: check `pragma_table_info('ToneProfiles')` for `SceneDirective`; if absent `ALTER TABLE ToneProfiles ADD COLUMN SceneDirective TEXT NOT NULL DEFAULT ''`; log at Information level
- [x] T011 [US3] Add `HasToneSceneDirectiveColumnAsync()` private helper in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` following the existing `HasTonePhaseOffsetColumnsAsync` pattern; update `SaveToneProfileAsync`, `LoadToneProfileAsync`, `LoadAllToneProfilesAsync`, `ReadToneProfile` to include `SceneDirective` column when present
- [x] T012 [US3] Add `string sceneDirective = ""` parameter to `CreateAsync()` and `UpdateAsync()` in `DreamGenClone.Infrastructure/StoryAnalysis/IntensityProfileService.cs`; validate max 2000 chars (throw `ArgumentException` if exceeded); assign `existing.SceneDirective = sceneDirective?.Trim() ?? string.Empty;` before persistence call
- [x] T013 [US3] Pass `sceneDirective` parameter through `CreateIntensityProfileAsync()` and `UpdateIntensityProfileAsync()` in `DreamGenClone.Web/Application/StoryAnalysis/StoryAnalysisFacade.cs`
- [x] T014 [US3] Update Scene Writing Directive block in `BuildPromptAsync()` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` to use profile value or static default (exactly one decision path): if `PromptSanitizer.SanitizeSceneDirective(resolvedProfile?.SceneDirective)` is non-empty → use sanitized profile text; else → call `GetStaticSceneDirective()`; log at Debug level which branch was taken
- [x] T015 [US3] Add `_toneFormSceneDirective` string field to `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor` code block; add SceneDirective textarea to the intensity profile edit form below the Description textarea: disabled when `_toneFormIntensity` is below `IntensityLevel.Explicit`, with tooltip; `maxlength="2000"`; placeholder text; populate from loaded profile in `SelectTone`; clear in `StartCreateIntensity`; pass to `SaveIntensity` → facade call
- [x] T016 [US3] Verify `dotnet build DreamGenClone.sln` passes clean after all US3 changes

**Checkpoint**: User Story 3 fully deliverable — custom SceneDirective is configurable per intensity profile via UI, persisted in SQLite, sanitized before injection, and falls back to static default when empty.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T017 [P] Run full build `dotnet build DreamGenClone.sln` confirming zero warnings introduced by this feature
- [x] T018 [P] Verify BuildUp phase at Explicit intensity: assembled prompt does NOT contain Scene Writing Directive block (confirms FR-005 and D-001)
- [x] T019 [P] Verify Moderate profile form shows SceneDirective textarea disabled with tooltip (confirms FR-018 and D-009)

---

## Phase 7: Prompt Tweaks — Position Spanning, Male Climax Gate, Minimum Word Count

**Goal**: Strengthen Climax-phase directives so that (1) a single act/position spans multiple turns rather than advancing to a new one every turn, (2) the AI never writes male orgasm/ejaculation until `/endclimax` is issued, and (3) each Climax-phase response is at least 350 words.

**Design decisions**: D-011 (position spanning), D-012 (male climax gate).

### Implementation for Phase 7 Tweaks

- [x] T020 [P] Rewrite `GetStaticSceneDirective()` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`: replace single-increment-per-turn framing with position-spanning language (an act spans multiple turns; advancing means richer description, not a position change); add **Response Length** section (at least 350 words, fill with physical/sensory detail); add **Male Climax Gate** section (no male orgasm/ejaculation until `/endclimax`; if male appears to climax, continue the scene)
- [x] T021 [P] Update `AppendEscalationGuidance()` Climax block in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`: replace "advance to a new act or position" line with position-spanning directive; add line for male climax gate (`/endclimax` required); add line for minimum 350 words this turn
- [x] T022 [P] Update `BuildFramingGuards()` Climax block in `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs`: replace "Each turn must advance to a new physical act" guard with position-spanning equivalent; add new guard: male completion gated by `/endclimax`
- [x] T023 [P] Update `ScenarioGuidanceContextFactory.CreateFallback()` Climax case in `DreamGenClone.Infrastructure/StoryAnalysis/ScenarioGuidanceContextFactory.cs`: replace "each turn advances by one measured increment: a position change, a new act" with position-spanning language; add male climax gate sentence mentioning `/endclimax`
- [x] T024 [P] Add `BuildFramingGuards_Climax_ContainsEndClimaxGate` test in `DreamGenClone.Tests/RolePlay/SceneWritingDirectivePromptTests.cs`
- [x] T025 [P] Add `ScenarioGuidanceContextFactory_ClimaxFallback_ContainsEndClimaxGate` and `BuildFramingGuards_Climax_ContainsPositionSpanningDirective` tests in `DreamGenClone.Tests/RolePlay/SceneWritingDirectivePromptTests.cs`
- [x] T026 [P] Verify `dotnet build DreamGenClone.sln` and all affected tests pass

**Checkpoint**: All Climax-phase directives enforce position-spanning, male climax gate, and minimum 350-word responses. Tests cover `/endclimax` gate and position-spanning directive presence.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 3 (US1)**: Depends on T001 only — all five US1 tasks (T002–T006) are parallelizable then T007 build check
- **Phase 4 (US2)**: No additional code tasks — verified after Phase 3 complete
- **Phase 5 (US3)**: Depends on Phase 3 completion (T007 clean build); internal order: T008 → T009–T011 (parallel) → T012 → T013 → T014 → T015 → T016
- **Phase 6 (Polish)**: Depends on Phase 5 (T016) completion

### User Story Dependencies

- **US1 (P1)**: Depends only on T001 (clean build gate). Self-contained — no schema change, no service changes.
- **US2 (P2)**: No code tasks. Verified against US1 implementation.
- **US3 (P3)**: Depends on US1 being complete (needs `GetStaticSceneDirective()` private method from T003 before T014 can update it).

### Within User Story 3 — Sequential Order

| Task | Depends On | Reason |
|------|-----------|--------|
| T008 | T007 | Domain property must exist before persistence can reference it |
| T009 | T008 | Sanitizer can be written in parallel with T010/T011 but logically follows domain |
| T010 | T008 | Migration references the new `IntensityProfile.SceneDirective` concept |
| T011 | T010 | Read/write methods depend on migration helper |
| T012 | T011 | Service validates and persists the new field |
| T013 | T012 | Facade threads through service parameter |
| T014 | T003, T009 | Updates the block from T003 to use sanitizer from T009 |
| T015 | T013 | UI calls facade; requires facade parameter to be wired |
| T016 | T015 | Final build gate |

### Parallel Opportunities (US1)

T002, T003, T004, T005, T006 all touch different files and can be worked simultaneously:

```
T001 (build gate)
 ├── T002  RolePlayContinuationService.cs — AppendEscalationGuidance
 ├── T003  RolePlayContinuationService.cs — BuildPromptAsync (different location, same file — coordinate)
 ├── T004  RolePlayAssistantPrompts.cs
 ├── T005  IntensityLadder.cs
 └── T006  ScenarioGuidanceContextFactory.cs
       └── T007 (build gate — all must complete first)
```

> **Note on T002 + T003**: Both edit `RolePlayContinuationService.cs` but at different method locations (~line 1342 vs ~line 952). If working in parallel, coordinate to avoid merge conflicts.

### Parallel Opportunities (US3)

After T008:
```
T008 (IntensityProfile.cs — domain property)
 ├── T009  PromptSanitizer.cs (new file — fully parallel)
 ├── T010  SqlitePersistence.cs — InitializeSchemaAsync migration
 └── T011  SqlitePersistence.cs — Save/Load/Read methods (same file as T010 — coordinate)
       └── T012 (IntensityProfileService.cs)
             └── T013 (StoryAnalysisFacade.cs)
                   └── T014 (RolePlayContinuationService.cs)
                         └── T015 (ThemeProfiles.razor)
                               └── T016 (build gate)
```

---

## MVP Scope

**Recommended MVP**: Phase 3 only (T001–T007).

US1 alone — five static string changes with no schema change — delivers the core user-visible improvement immediately: Climax phase at Explicit/Hardcore intensity produces detailed, paced, multi-turn scene writing. No database migration, no new UI controls, no service-layer changes required.

US2 is validated by US1. US3 (configurable `SceneDirective`) is genuinely optional for an MVP; the static defaults are sufficient for all users.

---

## Implementation Strategy

1. **Start with T001** — confirm the build is clean before touching any files.
2. **US1 in parallel** — T002–T006 can be assigned simultaneously (T002 and T003 coordinate on the same file).
3. **T007 gate** — build must pass before proceeding to US3.
4. **US3 flows top-down** — domain → persistence → service → facade → prompt → UI.
5. **Polish tasks T017–T019** — run as a final pass to confirm no regressions and no accidental BuildUp injection.

---

## Phase 8: Multi-Perspective Climax Turns (Retroactive Documentation)

**Goal**: Same-scene multi-perspective Climax turns — first character in a batch advances the scene naturally, subsequent characters react to the established beat, auto-narrative fires correctly, Turn Scene Contract removed from static locations so first character is not held.

**Design note**: These tasks were implemented in a prior session. Documented here retroactively for traceability.

### Implementation for Phase 8

- [x] T027 [P] Update `ContinueAsAsync` batch loop `promptText` in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`: first character (i==0) gets "advance naturally / escalate / establish this turn's scene"; subsequent characters get "match and deepen what the first character established — do not advance to a new act"
- [x] T028 [P] Update `DetermineNarrativePrompt()` Climax return in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`: omniscient summary prompt that closes the turn after all characters have written from their own perspectives; instructs not to advance beyond what characters have established
- [x] T029 [P] Remove Turn Scene Contract from `GetStaticSceneDirective()` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — removed "hold scene / sustain same act" language that was blocking first-character progression
- [x] T030 [P] Fix `ShouldAutoNarrate()` in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`: include `InteractionType.User` in consecutive-message count so that batches containing a persona User-type interaction do not prematurely terminate the auto-narrative count

**Checkpoint**: Multi-perspective Climax turns: first character advances, subsequent react, narrative fires, scene progresses across turns.

---

## Phase 9: Beat Stage Cursor

**Goal**: Persist a beat-stage cursor (`CurrentBeatStage` 1-8, `TurnsInCurrentBeatStage`) in `RolePlayV2AdaptiveStates`. At prompt-build time inject ~6 lines of stage-specific context from a static catalog — current stage name, 3-4 relevant sub-beat prompts, next stage name. The cursor auto-advances after a configurable minimum number of turns per stage; Stage 8 never auto-advances (stays until `/endclimax`). No UI required.

**Independent Test**: Start a Climax-phase session (explicit intensity). Confirm the assembled prompt contains "Current escalation stage: Stage 1". Send N continuation turns (N = MinTurnsPerStage for stage 1). Confirm the DB shows `CurrentBeatStage = 2` and the next prompt injection references Stage 2.

### Design Decisions

| # | Decision | Resolution |
|---|---|---|
| D-013 | Where to advance the cursor? | In `UpdateFromInteractionAsync` in `RolePlayAdaptiveStateService`, after processing each Climax-phase NPC or Narrative interaction |
| D-014 | Auto-advance timing | Increment `TurnsInCurrentBeatStage` on each qualifying interaction; advance stage when count >= `MinTurnsPerStage` from catalog |
| D-015 | MinTurnsPerStage default | 2 — each stage gets at least 2 turns before cursor moves. Stage 8 MinTurnsPerStage = ∞ (never auto-advances) |
| D-016 | Cursor initialization | Set `CurrentBeatStage = 1`, `TurnsInCurrentBeatStage = 0` when phase transitions TO Climax (inside `UpdateFromInteractionAsync` phase-change logic) |
| D-017 | Cursor reset | Null out both columns when phase exits Climax or session ends |
| D-018 | Prompt injection position | Immediately before the Stage Progression Contract block in `GetStaticSceneDirective()` response, OR as a prepended block in `BuildPromptAsync` right after the `GetStaticSceneDirective()` call — 6 lines maximum |
| D-019 | Catalog location | Static class `ClimaxBeatStageCatalog` in `DreamGenClone.Web/Application/RolePlay/` — no DI, no config, no DB; populated from `beat-sheet-master.md` reference |
| D-020 | Catalog entry fields | `StageNumber` (byte), `StageName` (string), `SubBeatHints` (string[], 3-4 items drawn from beat-sheet sub-beats), `NextStageName` (string), `MinTurnsBeforeAdvance` (int) |

### Implementation for Phase 9

- [ ] T031 Add `CurrentBeatStage` (byte?) and `TurnsInCurrentBeatStage` (int, default 0) properties to `DreamGenClone.Web/Domain/RolePlay/RolePlayAdaptiveState.cs`
- [ ] T032 Add DB migration in `InitializeSchemaAsync()` in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`: check `pragma_table_info('RolePlayV2AdaptiveStates')` for `CurrentBeatStage` (INTEGER NULL) and `TurnsInCurrentBeatStage` (INTEGER NOT NULL DEFAULT 0); add each if absent; log at Information level. Update `SaveAdaptiveStateAsync` and `LoadAdaptiveStateAsync` to include both columns.
- [ ] T033 Create `DreamGenClone.Web/Application/RolePlay/ClimaxBeatStageCatalog.cs` — public static class; `IReadOnlyList<ClimaxBeatStageEntry> Stages` property with 8 entries (populated from `beat-sheet-master.md`); each entry: `StageNumber`, `StageName`, `SubBeatHints` (3-4 string prompts per stage from the sub-beats), `NextStageName`, `MinTurnsBeforeAdvance` (all 2 except Stage 8 which is `int.MaxValue`). Also expose `TryGetStage(byte stageNumber, out ClimaxBeatStageEntry entry)` helper.
- [ ] T034 Add beat-stage advance logic in `UpdateFromInteractionAsync()` in `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs`: when `currentPhase == "Climax"` and interaction type is NPC or Narrative — increment `TurnsInCurrentBeatStage`; if `TurnsInCurrentBeatStage >= catalog.MinTurnsBeforeAdvance` for current stage AND `CurrentBeatStage < 8` — advance `CurrentBeatStage` by 1 and reset `TurnsInCurrentBeatStage = 0`; log stage advance at Information level with session ID and new stage number.
- [ ] T035 Add cursor initialization in `UpdateFromInteractionAsync()` in `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs`: when phase transitions TO `Climax` (previous phase != Climax, new phase == Climax) — set `CurrentBeatStage = 1`, `TurnsInCurrentBeatStage = 0`; when phase transitions AWAY from `Climax` — null out `CurrentBeatStage`, reset `TurnsInCurrentBeatStage = 0`.
- [ ] T036 Inject stage context in `BuildPromptAsync()` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`: when `currentPhase == "Climax"` and `session.AdaptiveState.CurrentBeatStage` is not null — call `ClimaxBeatStageCatalog.TryGetStage(...)` and append a `Beat Stage Context:` block (6 lines max) immediately after the `GetStaticSceneDirective()` call. Block content: current stage number + name, 3-4 sub-beat hints, next stage name with transition permission line. Skip block (no injection) if stage is null or catalog lookup fails.
- [ ] T037 Add tests in `DreamGenClone.Tests/RolePlay/ClimaxBeatStageCatalogTests.cs` (new file): `Catalog_Contains8Stages`, `Stage8_MinTurnsBeforeAdvance_IsMaxValue`, `TryGetStage_ValidNumber_ReturnsEntry`, `TryGetStage_InvalidNumber_ReturnsFalse`. Add tests in existing or new file: `BuildPromptAsync_ClimaxWithStage_ContainsBeatStageContext`, `BuildPromptAsync_ClimaxWithoutStage_NoBeatStageContext`.
- [ ] T038 Verify `dotnet build DreamGenClone.sln` and all Phase 9 tests pass. Confirm `RolePlayV2AdaptiveStates` migration runs without error on existing DB (existing rows get NULL / 0 defaults without data loss).

**Checkpoint**: Beat stage cursor persists in DB, auto-advances after MinTurnsPerStage, initializes when Climax starts, resets on phase exit, and injects 6-line stage context into every Climax-phase prompt. Stage 8 stays until `/endclimax`.

### Phase 9 Dependency Order

```
T031 (domain properties)
 └── T032 (DB migration + save/load)
 └── T033 (catalog — fully parallel with T032)
       └── T034 (advance logic — needs domain + catalog)
       └── T035 (init/reset logic — needs domain; parallel with T034)
             └── T036 (prompt injection — needs catalog + T031)
                   └── T037 (tests — needs all above)
                         └── T038 (build + test gate)
```
