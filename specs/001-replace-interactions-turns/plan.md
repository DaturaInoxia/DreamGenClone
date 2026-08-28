# Implementation Plan: Replace Interactions with Turns Throughout RP Engine and Data Model

**Branch**: `001-replace-interactions-turns` | **Date**: 2026-07-13 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-replace-interactions-turns/spec.md`

## Summary

Make `Turn` a first-class stored unit across the roleplay engine. Rename every domain field, DB column, config key, enum value, and gate JSON property that uses `*Interaction*` for phase-advancement counting to its `*Turn*` equivalent. All existing stored gate values (DB counters, theme gate JSON thresholds, config option defaults) are migrated by dividing by 3 with ceiling rounding. At runtime the engine reads and writes `Turn*` fields directly — no interaction-to-turn formula and no interaction counts feeding phase decisions. The `RolePlayInteraction` timeline entity and its `Interactions` list are explicitly out of scope.

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: ASP.NET Core Blazor, Microsoft.Data.Sqlite, Serilog, xUnit
**Project Type**: Web app with layered architecture (Web + Application + Domain + Infrastructure + Tests)
**Storage**: SQLite — `DreamGenClone.Web/data/dreamgenclone.dev.db`
**Testing**: xUnit, typically filtered to specific test classes due to ~61 pre-existing unrelated failures (see `/memories/repo/pre-existing-test-failures.md`)
**Target Platform**: Windows (.NET 9 desktop)
**Performance Goals**: None introduced by this feature (pure nomenclature + migration)
**Constraints**: Local-first; no cloud dependency for core RP flow; migration must be one-way and idempotent
**Scale/Scope**: ~15+ root field/column renames rippling into ~60+ files across Domain, Application, Infrastructure, Web, and Tests layers. No new projects. No new external dependencies.
**Key Migration Mechanics**:
- DB column renames use `ALTER TABLE ... RENAME COLUMN` (same pattern as the existing `ThemeSelectionInteractionsPerTheme → ThemeSelectionTurnsPerTheme` migration in `RPThemeService.cs:4846`).
- DB numeric values are divided by 3 with ceiling rounding via UPDATE statements.
- Theme gate JSON stored in `RPThemeMachineTransitions.GateConfigJson`: migration must parse each blob, rename `minimumInteractions` → `minimumTurns`, divide value by 3 (ceiling), and rewrite the blob.
- Config option keys in `StoryAnalysisOptions` and `appsettings.json`: renamed and any documented defaults divided by 3 if they reflect interaction units.
- Backward-compatibility read path: `RPThemeService` and `ThemeMachineEvaluator` gate validation/read must accept legacy `minimumInteractions` for un-migrated rows, divide by 3 (ceiling) for comparison only; after migration runs, all stored data uses `minimumTurns`.

## Constitution Check (Post-Design)

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow) — feature is entirely local rename + migration; no cloud added.
- [x] Module boundaries and adapter seams are explicit and swappable — all renames occur within existing adapter seams (ThemeMachineEvaluator, RolePlayStateRepository, RPThemeService, RolePlayAdaptiveStateService); no new coupled modules.
- [x] .NET layered architecture uses separate projects with enforced dependency direction — renames touch all five existing projects; no new projects added.
- [x] Deterministic state transitions and JSON contract validation are test-covered — existing tests (PhaseLifecycleTransitionTests, ThemeMachineEvaluatorTests, DecisionPointMutationTests, RolePlaySessionLifecycleTests, AdaptiveScenarioStateV2RoundTripTests) cover the renamed paths; JSON contract enforcement for `minimumTurns` extends existing gate validation tests.
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale — all migration targets SQLite; no new persistence added.
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices — log messages updated to use `Turn*` parameter names within existing Serilog structured templates; no new framework introduced.
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths — existing major-path Information logs in `RolePlayEngineService` and `ScenarioLifecycleService` are updated to use `Turn*` parameter names; coverage footprint unchanged.
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes — no logging-configuration changes; existing Serilog + `appsettings.json` configuration preserved.

No violations. No Complexity Tracking required.

## Project Structure

### Documentation (this feature)

```text
specs/001-replace-interactions-turns/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (created by /speckit.tasks, not this plan)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/RolePlay/
├── AdaptiveScenarioState.cs          # InteractionCountInPhase → TurnCountInPhase, InteractionsSinceCommitment → TurnsSinceCommitment, InteractionsInApproaching → TurnsInApproaching, InteractionsInCurrentEncounter → TurnsInCurrentEncounter
├── AdaptiveStateV2Records.cs        # ThemeScoreState.CompletionCooldownInteractions → CompletionCooldownTurns; ScenarioHistoryEntry.InteractionCount → TurnCount
├── EncounterSummaryRecord.cs       # InteractionCountInPhase → TurnCountInPhase
├── NarrativeGateProfile.cs          # NarrativeGateMetricKeys.InteractionsSinceCommitment → TurnsSinceCommitment
└── StoryAnalysis/ScenarioMetadata.cs # InteractionCount → TurnCount (legacy V1)

DreamGenClone.Application/RolePlay/
└── RolePlayContracts.cs             # LifecycleInputs.InteractionsSinceCommitment → TurnsSinceCommitment

DreamGenClone.Infrastructure/
├── RolePlay/
│   ├── DecisionPointService.cs      # Update cadence gate references to TurnCountInPhase
│   ├── ScenarioLifecycleService.cs  # Update metric key + reset assignment + trigger enum
│   ├── ThemeMachineEvaluator.cs     # Update JSON read (minimumTurns preferred; minimumInteractions legacy fallback ÷3)
│   ├── RPThemeService.cs             # Update gate config validation + verify existing RPThemeProfiles migration
│   ├── ScenarioSelectionService.cs  # Update metric key
│   └── EncounterSummaryService.cs   # Update template text + field reads
├── Persistence/SqlitePersistence.cs # Rename CREATE TABLE columns + add ALTER TABLE RENAME COLUMN migration + UPDATE divide-by-3 migration + JSON blob rewrite migration
└── Configuration/StoryAnalysisOptions.cs # Rename keys: AdaptiveEarlyTurnInteractionThreshold → AdaptiveEarlyTurnThreshold, AdaptivePerInteractionTotalDeltaBudget → AdaptivePerTurnTotalDeltaBudget, CompletedScenarioThemeCooldownInteractions → CompletedScenarioThemeCooldownTurns, BuildUpMinInteractionsBeforeCommit → BuildUpMinTurnsBeforeCommit

DreamGenClone.Web/
├── Application/RolePlay/
│   ├── RolePlayEngineService.cs         # ~40 references: InteractionCountInPhase / InteractionsSinceCommitment / InteractionsInApproaching renames; log message parameter renames
│   └── RolePlayAdaptiveStateService.cs # Cooldown decrement + reset assignments
├── Application/Assistants/RolePlayAssistantPrompts.cs # Diagnostic label alignment
└── Components/Pages/
    ├── RolePlayWorkspace.razor          # Adaptive panel labels, gate evaluation helper vars, method/param names
    ├── ThemeProfiles.razor              # Gate rule editor metric selector + help text
    ├── RPThemeDetail.razor              # Metric key reference + help text
    ├── RolePlayDebug.razor              # Debug gate threshold display fields
    ├── Home.razor                       # Session list label refinement (timeline interactions stay)
    └── RolePlaySessionsList.razor      # Session list InteractionCount component parameter (timeline, stays)

DreamGenClone.Tests/RolePlay/
├── AdaptiveScenarioStateV2RoundTripTests.cs  # Update field declarations/assertions
├── DecisionPointMutationTests.cs             # Update field assignments
├── EncounterSummaryServiceTests.cs           # Update field assertions
├── PhaseLifecycleTransitionTests.cs          # Update lifecycle input names
├── RolePlaySessionLifecycleTests.cs          # Update GateConfigJson to use minimumTurns + adjust expected values
├── RolePlayContinuationScenarioGuidanceTests.cs # Already uses TurnsInCurrentState (verify no regression)
├── RolePlayThemeMachineCommandTests.cs      # Update GateConfigJson to use minimumTurns
├── ThemeMachineEvaluatorTests.cs            # Update GateConfigJson to use minimumTurns
└── RPThemeMachineDefinitionValidationTests.cs # Update GateConfigJson + validation message assertions

DreamGenClone.Tests/StoryAnalysis/
└── ScenarioStateModelTests.cs                # Update InteractionCount → TurnCount assertions
```

**Structure Decision**: Existing layered architecture. No new projects. All changes touch existing files in Domain, Application, Infrastructure, Web, and Tests layers. Migration code lives in `SqlitePersistence.cs` alongside existing schema-evolution logic, following the same `ALTER TABLE ... RENAME COLUMN` pattern used by the prior `ThemeSelectionInteractionsPerTheme → ThemeSelectionTurnsPerTheme` migration.

## Complexity Tracking

N/A — no Constitution violations to justify.
