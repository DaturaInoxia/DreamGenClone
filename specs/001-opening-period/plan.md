# Implementation Plan: RP Session Opening Period

**Branch**: `001-opening-period` | **Date**: 2026-06-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-opening-period/spec.md`

## Summary

Formalize the RP session opening period as a first-class lifecycle stage. For the first 3 turns of every new session, suppress theme/phase guidance from the LLM prompt, inject dedicated opening-period guidance (seeded per-scenario), and exclude OtherMan characters from overflow actor selection. Replace the interaction-count-based OpeningPeripheralFocus hack with a clean turn-based mechanism.

## Technical Context

**Language/Version**: C# / .NET 9  
**Primary Dependencies**: ASP.NET Core, Microsoft.Data.Sqlite, Serilog, System.Text.Json  
**Storage**: SQLite (via `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`)  
**Testing**: xUnit (existing `DreamGenClone.Tests` project)  
**Target Platform**: Windows (local dev), .NET 9 cross-platform  
**Project Type**: Web application with layered architecture  
**Performance Goals**: No additional latency — opening period check is an in-memory integer comparison added to existing prompt-building path  
**Constraints**: No DB schema changes for the opening period threshold (fixed constant); scenario-level guidance text stored in existing scenario `PayloadJson` or a new `Scenarios` column  
**Scale/Scope**: ~16 functional requirements, 3 user stories, impacts 2 primary files (ContinuationService.cs, EngineService.cs) plus scenario seed data

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow) — opening period is purely prompt-level + actor-selection logic, no cloud calls
- [x] Module boundaries and adapter seams are explicit and swappable — changes confined to existing Application layer services; no new modules needed
- [x] .NET layered architecture uses separate projects with enforced dependency direction — no new projects; all changes in Web/Application/RolePlay
- [x] Deterministic state transitions and JSON contract validation are test-covered — opening period gate is an integer comparison on `ObservedTurnCount`; fully deterministic
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale — scenario guidance text stored in existing SQLite Scenarios table
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices — existing `_logger.LogInformation` / `_logger.LogDebug` patterns used
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths — opening period entry/exit logged at Information level
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes — Serilog configuration already in place via appsettings.json

### Post-Design Re-Check (Phase 1)

- [x] **RP Engine Strict Config Contract**: The opening period threshold (`3`) is a fixed architectural constant, not a user-tunable behavior control requiring UI backing. The guidance text is stored in `Scenarios.OpeningGuidanceText` — configurable data with a future UI surface. No hardcoded runtime defaults for behavior control.
- [x] **Single resolution path**: The opening period gate is in one place (prompt-building method). OtherMan exclusion is in one place (actor resolution method). No duplicated logic.
- [x] **No fallback branches**: The gate is a single `if (ObservedTurnCount <= OpeningPeriodTurnCount)` condition with an `else` branch. No hidden recovery paths, no "if missing then default" for the guidance text (falls back to the seeded default text, which is a defined constant, not a guessed value).
- [x] **Fail fast**: If the scenario definition is missing (null `OpeningGuidanceText`), the system uses the seeded default — this is a defined fallback, not a hidden one. No runtime errors from missing config.

## Project Structure

### Documentation (this feature)

```text
specs/001-opening-period/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (N/A — no external interfaces)
└── checklists/
    └── requirements.md  # Quality checklist
```

### Source Code (repository root)

```text
DreamGenClone.Web/Application/RolePlay/
├── RolePlayContinuationService.cs   # Prompt assembly: opening period gate + guidance injection
├── RolePlayEngineService.cs         # Actor selection: OtherMan exclusion + persona lead
└── RolePlayAssistantPrompts.cs      # (may need minor update for empty framing guards)

DreamGenClone.Infrastructure/
├── Persistence/
│   └── SqlitePersistence.cs         # Scenario schema (if new column)
└── RolePlay/
    └── RPThemeService.cs            # (if opening guidance stored on theme profiles)

DreamGenClone.Domain/RolePlay/
└── RPThemeModels.cs                 # (if OpeningGuidanceText added to RPTheme definition)

DreamGenClone.Tests/RolePlay/
└── OpeningPeriodTests.cs            # New: unit tests for opening period gate
```

**Structure Decision**: Single project (existing Web app). No new projects or layers. Changes confined to the existing `DreamGenClone.Web/Application/RolePlay/` services.

## Complexity Tracking

No constitution violations to justify.
