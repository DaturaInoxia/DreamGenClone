# Implementation Plan: Explicit Scene Writing Directives

**Branch**: `006-explicit-scene-writing` | **Date**: 2026-04-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/006-explicit-scene-writing/spec.md`

## Summary

Fix the Climax phase prompt pipeline to produce detailed, paced, multi-turn erotic scenes at Explicit and Hardcore intensity levels. Phase 1 delivers five static prompt changes with no schema changes (replacing the rush directive in `AppendEscalationGuidance`, injecting a Scene Writing Directive block in `BuildPromptAsync`, expanding Climax framing guards, updating Explicit/Hardcore default descriptions, and improving Climax fallback guidance). Phase 2 adds a configurable `SceneDirective` field to `IntensityProfile` — persisted in SQLite via a raw ADO.NET column migration — and exposed in the `ThemeProfiles.razor` UI form with a disabled state for sub-Explicit levels.

## Technical Context

**Language/Version**: C# / .NET 9 / Blazor Server
**Primary Dependencies**: Microsoft.Data.Sqlite, Serilog, System.Text.Json
**Storage**: SQLite via raw ADO.NET (`SqlitePersistence.cs`) — single file, no ORM
**Testing**: xUnit + FluentAssertions
**Target Platform**: Windows desktop (local-first, single-user)
**Project Type**: Blazor Server web application (modular layered architecture)
**Performance Goals**: Negligible — changes are string-building operations in an in-process prompt assembler with no hot-path concerns
**Constraints**: Local-first, no cloud dependency, no EF Core. Schema changes via inline `pragma_table_info` migration in `InitializeSchemaAsync`
**Scale/Scope**: Single user, one active RP session, ~5 intensity profiles

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [x] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default — no exceptions in this feature
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

**Notes**: All changes are confined to existing project boundaries. Phase 1 touches only string-building methods — no schema change, no new services. Phase 2 adds one column to `ToneProfiles` via the existing inline migration pattern (`pragma_table_info` + `ALTER TABLE ADD COLUMN`) — no new persistence backend, no new external package. The `SceneDirective` sanitizer is a pure static utility in `DreamGenClone.Web/Application/RolePlay/` with no external dependency.

## Project Structure

### Documentation (this feature)

```text
specs/006-explicit-scene-writing/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
└── StoryAnalysis/
    └── IntensityProfile.cs                 → add SceneDirective string property (Phase 2)
    └── IntensityLadder.cs                  → update GetDefaultDescription for Explicit + Hardcore (Phase 1, Step 4)

DreamGenClone.Infrastructure/
├── Persistence/
│   └── SqlitePersistence.cs               → migrate ToneProfiles + SceneDirective column;
│                                              update SaveToneProfileAsync, LoadToneProfileAsync,
│                                              LoadAllToneProfilesAsync, ReadToneProfile (Phase 2, Step 7)
└── StoryAnalysis/
    ├── IntensityProfileService.cs          → add sceneDirective param to CreateAsync + UpdateAsync (Phase 2)
    └── ScenarioGuidanceContextFactory.cs   → update Climax fallback guidance text (Phase 1, Step 5)

DreamGenClone.Web/
├── Application/RolePlay/
│   ├── RolePlayContinuationService.cs      → AppendEscalationGuidance Climax directive set (Phase 1, Step 1);
│   │                                          inject Scene Writing Directive block (Phase 1, Step 2);
│   │                                          use profile SceneDirective or static fallback (Phase 2, Step 8)
│   ├── RolePlayAssistantPrompts.cs         → expand BuildFramingGuards Climax guard (Phase 1, Step 3)
│   └── PromptSanitizer.cs                  ← NEW static utility — strips injection tokens from SceneDirective (Phase 2)
└── Application/StoryAnalysis/
│   └── StoryAnalysisFacade.cs             → pass sceneDirective in Create/Update calls (Phase 2)
└── Components/Pages/
    └── ThemeProfiles.razor                 → add SceneDirective textarea (disabled for sub-Explicit) (Phase 2, Step 9)

DreamGenClone.Tests/
└── RolePlay/
    ├── SceneWritingDirectivePromptTests.cs  ← NEW — tests for Phase 1 prompt blocks
    └── PromptSanitizerTests.cs             ← NEW — tests for SceneDirective sanitizer (Phase 2)
```

**Structure Decision**: No new projects. All changes fit within the existing five-project layered architecture (Domain, Infrastructure, Application, Web, Tests). The new `PromptSanitizer` is a pure static utility class in `DreamGenClone.Web/Application/RolePlay/` — no dependency injection needed, no interface required for a single-use static operation.

## Complexity Tracking

No constitution violations. No complexity justifications needed.

---

## Design Decisions

| # | Decision | Resolution |
|---|---|---|
| D-001 | Suppress Scene Writing Directive in BuildUp? | Yes — check `currentPhase != "BuildUp"` before injection |
| D-002 | Stat thresholds affect SceneDirective pacing? | No — upstream directive injection is sufficient |
| D-003 | Field name: `SceneDirective` vs `ExplicitSceneDirective` | `SceneDirective` — profile level already implies Explicit/Hardcore scope |
| D-004 | Turn-spanning enforcement: prompt-only or mechanical? | Prompt-only for Phase 1. Mechanical act-tracking is a future backlog item |
| D-005 | User override ("skip ahead") handling? | Honors explicit user instruction; directive is default behavior, not a guard |
| D-006 | SceneDirective vs theme PhaseGuidance interaction? | Theme governs WHAT; SceneDirective governs HOW. Duplication acceptable; no conflict code |
| D-007 | Approaching phase scope? | Explicitly out of scope. Existing Approaching escalation guidance is unchanged |
| D-008 | SceneDirective sanitization approach? | Strip null bytes, control chars, and LLM injection tokens (e.g., `SYSTEM:`, `[INST]`, `###`) before prompt injection. Max 2000 chars enforced at service + UI layer |
| D-009 | SceneDirective UI for sub-Explicit profiles? | Show field disabled/grayed out with tooltip. Do not hide — consistent form layout is preferable |
| D-010 | Persistence: EF Core vs raw ADO.NET? | Raw ADO.NET — this project has no EF Core. Follow existing `pragma_table_info` + `ALTER TABLE ADD COLUMN` migration pattern used for all previous column additions to `ToneProfiles` |
| D-011 | Position/act spanning across turns | A single position or act is sustained and elaborated across multiple turns before any transition. "Advance" means richer description within the current act — not a required position change per turn. |
| D-012 | Male climax gate | Prompt-only enforcement: The AI is instructed not to write male orgasm/ejaculation until `/endclimax` is issued. If a male appears to climax mid-scene the scene continues — they are not done. No code state tracking needed; handled entirely in directives. |
| D-013 | Beat cursor advance — where? | In `UpdateFromInteractionAsync` in `RolePlayAdaptiveStateService`, after each Climax-phase NPC or Narrative interaction. |
| D-014 | Beat cursor advance — when? | Increment `TurnsInCurrentBeatStage` each qualifying turn; advance when count >= `MinTurnsBeforeAdvance` for the current stage. |
| D-015 | MinTurnsBeforeAdvance default | 2 for Stages 1-7. Stage 8 is `int.MaxValue` — never auto-advances; stays until `/endclimax`. |
| D-016 | Beat cursor initialization | Set `CurrentBeatStage = 1`, `TurnsInCurrentBeatStage = 0` when phase transitions TO Climax. |
| D-017 | Beat cursor reset | Null out both on phase exit from Climax or session end. |
| D-018 | Beat stage prompt injection position | Immediately after `GetStaticSceneDirective()` call in `BuildPromptAsync`, as a `Beat Stage Context:` block of ≤6 lines. |
| D-019 | Catalog location | Static class `ClimaxBeatStageCatalog` in `DreamGenClone.Web/Application/RolePlay/` — no DI, no DB, populated from `beat-sheet-master.md`. |
| D-020 | Catalog entry fields | `StageNumber`, `StageName`, `SubBeatHints` (3-4 strings), `NextStageName`, `MinTurnsBeforeAdvance`. |

---

## Implementation Phases

### Phase 1 — Static Prompt Improvements (no schema change)

**Step 1** — `RolePlayContinuationService.AppendEscalationGuidance()` (lines ~1302–1350)  
Replace single Climax line `"Deliver a decisive high-intensity beat now instead of incremental teasing."` with sustained-scene directive set (6 directives covering pacing, act variety, and urgency handling).

**Step 2** — `RolePlayContinuationService.BuildPromptAsync()` (after `AppendEscalationGuidance` call, ~line 952)  
Inject a `Scene Writing Directive` block when `resolvedScale >= (int)IntensityLevel.Explicit AND currentPhase != "BuildUp"`. Initially uses static 14-line directive text. Applies to all intent types.

**Step 3** — `RolePlayAssistantPrompts.BuildFramingGuards()` (lines ~84–104)  
Expand Climax guard from 1 directive to 5 directives covering pacing, multi-turn structure, and position/act variety.

**Step 4** — `IntensityLadder.GetDefaultDescription()` (lines ~43–50)  
Replace Explicit description (one sentence → three sentences) and Hardcore description (one sentence → two sentences detailing exhaustive specificity and per-beat/per-turn structure).

**Step 5** — `ScenarioGuidanceContextFactory.CreateFallback()` (lines ~42–68)  
Replace Climax case single-line with multi-sentence guidance including pacing, act variety, urgency handling.

### Phase 2 — Configurable SceneDirective (schema change)

**Step 6** — `DreamGenClone.Domain/StoryAnalysis/IntensityProfile.cs`  
Add: `public string SceneDirective { get; set; } = string.Empty;`

**Step 7** — `SqlitePersistence.cs` — `InitializeSchemaAsync`  
Add inline migration: check `pragma_table_info('ToneProfiles')` for `SceneDirective` column; if missing, `ALTER TABLE ToneProfiles ADD COLUMN SceneDirective TEXT NOT NULL DEFAULT ''`. Update `SaveToneProfileAsync`, `LoadToneProfileAsync`, `LoadAllToneProfilesAsync`, `ReadToneProfile` to include the column.

**Step 8** — `RolePlayContinuationService.BuildPromptAsync()`  
In the Scene Writing Directive block (Step 2), replace static text with: if `resolvedProfile.SceneDirective` is non-empty → sanitize and use that; else → use static default. Exactly one decision path; log at Debug level which branch was taken.

**Step 9** — `ThemeProfiles.razor`  
Add `SceneDirective` textarea below the `Description` textarea. Bind to `_toneFormSceneDirective`. Disable (`disabled="@IsSceneDirectiveDisabled"`) with tooltip when `_toneFormIntensity` is below `IntensityLevel.Explicit`. Label: "Scene Writing Directive". Placeholder: "Optional. Leave blank to use system default."  
Update `SaveIntensity` call path and `StartCreateIntensity`/reset to include the new field.

**Step 10** — `PromptSanitizer.cs` (new static utility)  
`public static string SanitizeSceneDirective(string input)`: truncate to 2000 chars, remove null bytes and control characters (except newline/tab), strip lines starting with known injection tokens (`SYSTEM:`, `USER:`, `ASSISTANT:`, `[INST]`, `</s>`, `###`, `<|`). Returns sanitized string.

**Step 11** — `IntensityProfileService.CreateAsync + UpdateAsync`  
Add `string sceneDirective` parameter. Validate: call `PromptSanitizer.SanitizeSceneDirective` and enforce 2000-char limit (throw `ArgumentException` if raw input exceeds 2000). Propagate to persistence.

**Step 12** — `StoryAnalysisFacade.cs`  
Pass `sceneDirective` param in `CreateIntensityProfileAsync` and `UpdateIntensityProfileAsync` calls.

### Phase 3 — Prompt Tweaks (post-implementation, no schema change)

**Step 13** — `RolePlayContinuationService.GetStaticSceneDirective()` (T020)  
Replace the single-increment-per-turn framing with position-spanning framing (a single act/position spans multiple turns; advancing means richer description, not a position change). Add a Response Length section (at least 350 words, physical/sensory detail). Add a Male Climax Gate section (no male orgasm/ejaculation until `/endclimax` is issued; if a male appears to climax the scene continues).

**Step 14** — `RolePlayContinuationService.AppendEscalationGuidance()` Climax block (T021)  
Replace the existing "advance to a new act or position" line with position-spanning directive. Add line: male characters do not orgasm until `/endclimax`. Add line: write at least 350 words this turn.

**Step 15** — `RolePlayAssistantPrompts.BuildFramingGuards()` Climax block (T022)  
Replace the "Each turn must advance to a new physical act" guard with position-spanning equivalent. Add one new guard: male completion gated by `/endclimax`.

**Step 16** — `ScenarioGuidanceContextFactory.CreateFallback()` Climax case (T023)  
Replace "each turn advances by one measured increment: a position change, a new act" text with position-spanning language. Add male climax gate sentence.

**Step 17** — `SceneWritingDirectivePromptTests.cs` (T024 + T025)  
Add three tests: `BuildFramingGuards_Climax_ContainsEndClimaxGate`, `ScenarioGuidanceContextFactory_ClimaxFallback_ContainsEndClimaxGate`, `BuildFramingGuards_Climax_ContainsPositionSpanningDirective`.

### Phase 8 — Multi-Perspective Climax Turns (Retroactive)

**Step 18** — `RolePlayEngineService.ContinueAsAsync` batch loop promptText (T027)  
First character (i==0) in a Climax batch: "advance naturally / escalate / establish this turn's scene clearly so other participants can react." Subsequent characters (i>0): "match and deepen the physical moment already established — do not advance to a new act."

**Step 19** — `RolePlayEngineService.DetermineNarrativePrompt()` Climax return (T028)  
Return omniscient summary prompt: close the turn with a rich third-person account of the full scene as all characters just described it; do not advance beyond what characters established.

**Step 20** — Remove Turn Scene Contract from static locations (T029/T030)  
Remove "hold / sustain same act / do not advance" language from `GetStaticSceneDirective`, `AppendEscalationGuidance`, and `BuildFramingGuards` so the first-character promptText can actually advance the scene.

**Step 21** — Fix `ShouldAutoNarrate()` consecutive-count logic (T030)  
Include `InteractionType.User` in the consecutive-message count so batches containing a persona User-type interaction do not incorrectly terminate the auto-narrative trigger count.

### Phase 9 — Beat Stage Cursor (schema change)

**Step 22** — `RolePlayAdaptiveState.cs`  
Add: `public byte? CurrentBeatStage { get; set; }` and `public int TurnsInCurrentBeatStage { get; set; }`.

**Step 23** — `SqlitePersistence.cs` — `InitializeSchemaAsync` + save/load (T032)  
Inline migration: check `pragma_table_info('RolePlayV2AdaptiveStates')` for `CurrentBeatStage` (INTEGER NULL) and `TurnsInCurrentBeatStage` (INTEGER NOT NULL DEFAULT 0); add each if absent; log at Information. Update `SaveAdaptiveStateAsync` and `LoadAdaptiveStateAsync` to include both columns.

**Step 24** — `ClimaxBeatStageCatalog.cs` (new static class, T033)  
Public static class in `DreamGenClone.Web/Application/RolePlay/`. Exposes `IReadOnlyList<ClimaxBeatStageEntry> Stages` with 8 entries populated from `beat-sheet-master.md`. Each entry: `StageNumber` (byte), `StageName` (string), `SubBeatHints` (string[], 3-4 items), `NextStageName` (string), `MinTurnsBeforeAdvance` (int; 2 for Stages 1-7, `int.MaxValue` for Stage 8). `TryGetStage(byte n, out ClimaxBeatStageEntry e)` helper.

**Step 25** — `RolePlayAdaptiveStateService.UpdateFromInteractionAsync()` — advance logic (T034)  
When `currentPhase == "Climax"` and interaction is NPC or Narrative: increment `TurnsInCurrentBeatStage`; if count >= `MinTurnsBeforeAdvance` for current stage AND stage < 8, advance `CurrentBeatStage` by 1 and reset counter to 0; log stage advance at Information with session ID and new stage.

**Step 26** — `RolePlayAdaptiveStateService.UpdateFromInteractionAsync()` — cursor init/reset (T035)  
On phase transition TO Climax: set `CurrentBeatStage = 1`, `TurnsInCurrentBeatStage = 0`. On phase transition AWAY from Climax: null out `CurrentBeatStage`, reset `TurnsInCurrentBeatStage = 0`.

**Step 27** — `RolePlayContinuationService.BuildPromptAsync()` — stage context injection (T036)  
When `currentPhase == "Climax"` and `CurrentBeatStage` is not null: call `ClimaxBeatStageCatalog.TryGetStage(...)`, append a `Beat Stage Context:` block (6 lines max) immediately after the `GetStaticSceneDirective()` call. Block: current stage number + name, 3-4 sub-beat hint lines, next stage name + transition permission. Skip entirely if stage is null or catalog miss.

**Step 28** — Tests (T037) + build gate (T038)  
`ClimaxBeatStageCatalogTests.cs`: Catalog_Contains8Stages, Stage8_MinTurnsBeforeAdvance_IsMaxValue, TryGetStage valid/invalid. Prompt tests: BuildPromptAsync_ClimaxWithStage_ContainsBeatStageContext, BuildPromptAsync_ClimaxWithoutStage_NoBeatStageContext. Build + test gate confirming migration safe on existing DB.
