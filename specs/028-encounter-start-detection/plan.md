# Implementation Plan: Semantic Encounter-Start Detection & Memory Enrichment

**Branch**: `028-encounter-start-detection` | **Date**: 2026-07-08 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/028-encounter-start-detection/spec.md`

## Summary

Replace the keyword-only heuristic for sexual encounter-start detection with LLM semantic inference (same engine as existing `encounter-completed`), running universally for all themes with a global configurable confidence threshold (default 0.70). Rewrite encounter completion enrichment prompts from sterile third-person summaries to vivid first-person prose capturing who, what acts, orgasms, and sensory/emotional detail. Fix two bugs: Climax-entry clobbering the encounter start index, and missing reset of `CurrentEncounterStartInteractionIndex` after encounter boundaries.

## Technical Context

**Language/Version**: C# 13 / .NET 9  
**Primary Dependencies**: ASP.NET Core Blazor (server-side), EF Core + SQLite, Serilog, existing `ISemanticEventInferenceService` (LLM inference pipeline), existing `EncounterSummaryJobHandler` (async background job infrastructure)  
**Storage**: SQLite (via `DreamGenClone.Web/data/dreamgenclone.dev.db`) — no schema changes; new `WasEncounterStart` property on existing `RolePlayInteraction` entity  
**Testing**: xUnit 2.9.2 + `Microsoft.NET.Test.Sdk` 17.12.0 + `coverlet.collector` 6.0.2  
**Target Platform**: Windows (local-first desktop application)  
**Project Type**: ASP.NET Core Blazor web application with layered architecture (Web → Infrastructure → Application → Domain)  
**Performance Goals**: LLM inference for encounter-start runs synchronously during turn processing (same pattern as encounter-completed); keyword pre-filter minimizes unnecessary LLM calls  
**Constraints**: Local-only execution — no cloud dependency for core detection; Serilog structured logging; SQLite persistence  
**Scale/Scope**: Single-user local application; ~3 files changed, ~150 lines of code; 0 new files, 0 new dependencies

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **Local-first runtime preserved** — No new cloud dependencies. Encounter-start detection uses the existing local LLM inference pipeline (`ISemanticEventInferenceService`). Enrichment uses existing `EncounterSummaryJobHandler` async job infrastructure. All processing is local.
- [x] **Module boundaries and adapter seams are explicit and swappable** — Changes are confined to the Application layer (`RolePlayEngineService`, `EncounterSummaryJobHandler`) and Domain layer (`RolePlayInteraction` entity). No new interfaces needed; existing `ISemanticEventInferenceService` is already swappable.
- [x] **.NET layered architecture uses separate projects with enforced dependency direction** — Changes span `DreamGenClone.Web` (Application + Domain) only. No cross-layer violations. Dependency chain: Web → Infrastructure → Application → Domain is preserved.
- [x] **Deterministic state transitions and JSON contract validation are test-covered** — Encounter-start detection produces deterministic state changes (set `CurrentEncounterNumber`, `CurrentEncounterStartInteractionIndex`, `WasEncounterStart`). Same JSON-in/JSON-out pattern as existing `encounter-completed` detection. Existing `SemanticEventInferenceRequest`/`SemanticEventInferenceResult` contracts unchanged.
- [x] **Persistence uses SQLite by default** — `WasEncounterStart` is a new property on an existing SQLite-backed entity (`RolePlayInteraction`). No new tables. `EncounterCompletion` records already stored in SQLite. No persistence exceptions needed.
- [x] **Serilog is the primary logging framework** — FR-016 through FR-018 require Serilog structured logging, Information-level logs for major paths, and configurable log levels.
- [x] **Logging coverage exists across layers** — FR-013 (`EncounterStartDetected`, `EncounterStartDetectionFailed` debug events) and FR-017 (Information logs for major execution paths) ensure coverage.
- [x] **Log levels are externally configurable** — FR-018 requires Verbose diagnostics without code changes.

**Gate result**: ALL PASS — no violations to justify.

### Post-Design Re-Check (Phase 1 Complete)

- [x] All 8 gates re-evaluated — no design decisions introduced violations.
- [x] `WasEncounterStart` property on existing entity — no new tables, SQLite-only.
- [x] `EncounterStartConfidenceThreshold` in `RolePlayMemoryOptions` — follows existing options pattern, configurable via appsettings.
- [x] No new external interfaces — contracts/ is empty by design.
- [x] Agent context updated at `.github/agents/copilot-instructions.md`.

## Project Structure

### Documentation (this feature)

```text
specs/028-encounter-start-detection/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (empty — no external interfaces)
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
DreamGenClone.Web/
├── Application/
│   └── RolePlay/
│       ├── RolePlayEngineService.cs     # +TryDetectEncounterStartAsync(), +2 bug fixes
│       └── EncounterSummaryJobHandler.cs # prompt rewrite + displayName fix
└── Domain/
    └── RolePlay/
        └── RolePlayInteraction.cs       # +WasEncounterStart property
```

**Structure Decision**: No new projects or files. All changes are in existing files within the Web project's Application and Domain layers.

## Complexity Tracking

> No constitution violations — this section is intentionally empty.
