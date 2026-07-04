# Implementation Plan: Full Prompt Injection Refactor

**Branch**: `001-prompt-injection-refactor` | **Date**: 2026-06-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-prompt-injection-refactor/spec.md`

> **Phase numbering**: This document's design phases (Phase 0: research, Phase 1: design/contracts) are pre-implementation phases. The implementation phase numbering (Phase 1–6) is in [tasks.md](tasks.md). These are separate numbering schemes.

## Summary

Centralize the 37+ independently-added prompt injects in `RolePlayContinuationService.BuildPromptAsync` into a coordinated service (`SceneDirectionCoordinator`) with a priority-sorted injector loop. Replace hardcoded phase-detection logic (`BuildFramingGuards`) with marker-driven decisions resolved by `SceneDirectionResolver` from theme phase guidance prose. The engine owns turn structure (position 1 sets anchor, position 2+ follows); themes own narrative behavior (pacing, deepening, time shifting) via markers. Key deliverables: `IPromptInjector` interface, 12 injector implementations, `PromptInjectionContext` record, `SceneDirectionResolver` completion with `DeepeningPolicy`, coordinated logging, efcbf70f regression test, and phase guidance prose migration from code to theme seed data.

## Technical Context

**Language/Version**: C# 12, .NET 9  
**Primary Dependencies**: ASP.NET Core (Blazor), Serilog, Entity Framework Core (SQLite), existing domain models under `DreamGenClone.*` projects  
**Storage**: SQLite via EF Core (existing `DreamGenClone.Web/data/dreamgenclone.dev.db`) — themes, sessions, guidance prose persisted in DB  
**Testing**: xUnit (DreamGenClone.Tests project)  
**Target Platform**: ASP.NET Core web application (server-rendered Blazor)  
**Project Type**: Web application (ASP.NET Core Blazor with layered .NET architecture)  
**Performance Goals**: Prompt assembly is string concatenation — not a bottleneck. No specific performance targets beyond "not regressing existing response times."  
**Constraints**: 
- Refactored prompts must be structurally equivalent to current output (same behavioral directives present, critical strings unchanged)
- Zero hardcoded phase-branching in injectors (`if phase == "Climax"` emitting C# text)
- Injectors fail fast on exceptions; no catch-log-skip
- Migration ordering: themes updated before old code removed
**Scale/Scope**: 
- 37+ existing injects consolidated into ~12 behavioral injectors
- ~23 data-assembly blocks remain inline (scenario, characters, locations, memory, stats, etc.)
- 4 .NET projects touched: Web, Application, Domain, Tests
- Theme seed data: need to enumerate all existing themes and their guidance fields

**NEEDS CLARIFICATION (to be resolved in Phase 0 research)**:
- Current injector catalog: exact line ranges and text of each inject in `RolePlayContinuationService.BuildPromptAsync` and `RolePlayAssistantPrompts.BuildFramingGuards`
- Existing `SceneDirectionResolver.cs` structure: precise scaffolding, enum definitions, missing helpers
- Theme data model: `RPTheme` entity structure, guidance prose fields per phase, how `GetThemePhaseGuidanceLines` works
- Seed data: existing theme count, names, current phase guidance content for migration
- Existing test patterns: how `SceneWritingDirectivePromptTests` and `SessionMemoryInjectionTests` build session state and assert prompt text

## Constitution Check — Post-Design Re-evaluation

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Design Check (passed)

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [x] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

### Post-Design Re-evaluation

- [x] **Local-first runtime preserved**: ✓ No cloud dependencies introduced. All changes are internal refactoring of prompt assembly within existing Web/Application projects.
- [x] **Module boundaries and adapter seams explicit**: ✓ New injectors live in `Web/Application/RolePlay/Injectors/`. `SceneDirectionResolver` lives in same namespace. `IPromptInjector` interface defines the seam. Coordinator is the sole orchestrator.
- [x] **.NET layered architecture preserved**: ✓ Domain change is a single enum + field on `SceneDirection`. Application/Web owns interfaces and coordinator. Tests are in Tests project. No cross-project dependency violations.
- [x] **Deterministic state transitions and JSON contract validation test-covered**: ✓ New `SceneDirectionResolverTests` and `PromptInjectorCaptureTests` add deterministic coverage. Existing prompt tests preserved.
- [x] **SQLite-default persistence**: ✓ Theme phase guidance prose uses existing SQLite-backed `RPThemePhaseGuidance` entity. No new persistence store introduced.
- [x] **Serilog primary logging framework**: ✓ FR-016 through FR-018 explicitly require Serilog structured logging with configurable levels.
- [x] **Logging coverage across layers**: ✓ Coordinator emits Information log per prompt build. Exceptions propagate and are caught by the calling layer's existing error handling.
- [x] **Configurable log levels**: ✓ FR-018 ensures coordinator logging is configurable via settings.

All 8 constitution principles pass. No violations or complexity exceptions needed.

## Project Structure

### Documentation (this feature)

```text
specs/001-prompt-injection-refactor/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── injector-catalog.md  # 37+ inject catalog
└── tasks.md             # (created by /speckit.tasks)
```

### Source Code (repository root)

```text
DreamGenClone.Web/Application/RolePlay/           (modified)
├── SceneDirectionCoordinator.cs                   # Full implementation (currently stub)
├── IPromptInjector.cs                             # New interface
├── PromptInjectionContext.cs                      # New context record
├── Injectors/                                     # New directory (12 files)
│   ├── TurnContextInjector.cs
│   ├── TimeLocationInjector.cs
│   ├── BehavioralFrameInjector.cs
│   ├── ThemeContractInjector.cs
│   ├── ThemeAIGuidanceInjector.cs
│   ├── IntensityContractInjector.cs
│   ├── EscalationInjector.cs
│   ├── DirectorNoteInjector.cs
│   ├── SceneTimeDirectionInjector.cs
│   ├── PositionListInjector.cs
│   ├── BeatStageInjector.cs
│   └── FinalDirectiveInjector.cs
├── RolePlayContinuationService.cs                 # Refactor BuildPromptAsync loop, remove Append* methods
├── RolePlayAssistantPrompts.cs                    # Strip BuildFramingGuards, keep utility helpers
├── SceneDirectionResolver.cs                      # Complete 5 missing helper methods

DreamGenClone.Domain/RolePlay/                     (modified)
├── SceneDirection.cs                              # Add DeepeningPolicy enum + Deepening field

DreamGenClone.Tests/RolePlay/                      (modified + new)
├── SceneDirectionResolverTests.cs                 # New unit tests for resolver
├── PromptInjectorCaptureTests.cs                  # New structural parity + negative assertion tests
├── SceneWritingDirectivePromptTests.cs            # Existing (preserved)
├── SessionMemoryInjectionTests.cs                 # Existing (preserved)
├── PromptSanitizerTests.cs                        # Existing (preserved)
├── RolePlayIntentRoutingTests.cs                  # Existing + new efcbf70f regression test

Theme seed data (multiple files)                   # Add [Deepening:subsequent-actors] marker + migrated prose
```

**Structure Decision**: Existing layered .NET architecture preserved. New code goes in `DreamGenClone.Web/Application/RolePlay/Injectors/` directory and `DreamGenClone.Domain/RolePlay/` (enum only). Test files go in existing `DreamGenClone.Tests/RolePlay/`. No new projects, no new module boundaries — purely internal refactoring within existing project structure.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations. Complexity is appropriate for the existing 4-project structure. No new projects, repositories, or patterns introduced.
