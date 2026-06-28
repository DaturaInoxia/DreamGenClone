# Implementation Plan: Multi-Encounter Climax Time-Skip — Two-Turn Split

**Branch**: `001-split-time-skip` | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-split-time-skip/spec.md`

## Summary

Split the multi-encounter climax time-skip instruction from a single combined directive ("Close the current encounter naturally. Then advance time…") into two sequential phases injected across two separate Continue turns: CloseScene ("Close the current encounter naturally.") then AdvanceTime ("Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life."). The old combined behavior is permanently replaced (no configuration toggle). User instructions defer the pending phase (no cancel mechanism). State persists across session save/load cycles.

## Technical Context

**Language/Version**: C# 13 / .NET 9  
**Primary Dependencies**: ASP.NET Core (Blazor), Microsoft.Data.Sqlite, Serilog  
**Storage**: SQLite (existing `RolePlayV2AdaptiveStates` table)  
**Testing**: xUnit (DreamGenClone.Tests project)  
**Target Platform**: Windows (local-first)  
**Project Type**: ASP.NET Core Blazor web application with 4-project layered architecture (Web, Application, Domain, Infrastructure)  
**Performance Goals**: No change from baseline — time-skip injection latency is <1ms (in-memory state mutation before AI call)  
**Constraints**: Must not break existing sessions (back-compat migration). Must follow no-fallback rules from `.github/copilot-instructions.md`. No new external dependencies.  
**Scale/Scope**: Per-session state mutation in single-user local app. Changes touch 3 source files + 2 test files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [x] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

**Gate Result**: PASS — all gates clear. No violations to justify.

**Post-Phase-1 Re-evaluation**: PASS — no changes. All gates remain clear after design artifacts (research.md, data-model.md, contracts/, quickstart.md) are complete.

## Project Structure

### Documentation (this feature)

```text
specs/001-split-time-skip/
├── spec.md              # Feature specification (/speckit.specify output)
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/RolePlay/
├── AdaptiveScenarioState.cs          # Add TimeSkipPhase enum, replace TimeSkipPending
DreamGenClone.Infrastructure/RolePlay/
├── RolePlayStateRepository.cs        # Schema migration, read/write phase column
DreamGenClone.Web/Application/RolePlay/
├── RolePlayEngineService.cs          # Overflow loop gate logic, prompt selection, pipeline-batch
DreamGenClone.Tests/RolePlay/
├── MultiEncounterTimeSkipTests.cs    # Update directive text assertions, add phase transition tests
```

**Structure Decision**: No new projects or directories. This feature is an internal behavior change to the existing multi-encounter climax time-skip system within the established 4-project layered architecture. Changes are limited to the Domain model (state), Infrastructure (persistence), Application (engine logic), and Tests (coverage).

## Complexity Tracking

> No constitution violations. No entries required.
