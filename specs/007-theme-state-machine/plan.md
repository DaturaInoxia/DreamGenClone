# Implementation Plan: Theme State Machine Continuity

**Branch**: `007-theme-state-machine` | **Date**: 2026-05-08 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/007-theme-state-machine/spec.md`

## Summary

Implement a reusable RP V2 theme state machine framework that enforces continuity through deterministic, persisted transitions. The first machine (`infidelity-brief-disappearance`) is configured and managed through RP theme UI, evaluated in the V2 runtime pipeline, persisted per session with pinned definition versioning, and enforced in both candidate selection and prompt guidance. The design enforces one resolution path (`ActiveScenario -> RPTheme -> ThemeMachineDefinition`), explicit fail-fast behavior when required config is missing/ambiguous, and admin-only mutation/migration operations.

## Technical Context

**Language/Version**: C# / .NET 9 / Blazor Server  
**Primary Dependencies**: Microsoft.Data.Sqlite, System.Text.Json, Serilog, ASP.NET Core DI/logging abstractions  
**Storage**: SQLite (existing persistence stack) with new theme-machine definition and diagnostic persistence; adaptive-state row remains the per-session runtime anchor  
**Testing**: xUnit (`DreamGenClone.Tests`) with RolePlay-focused regression coverage  
**Target Platform**: Windows local-first runtime (single-node, no cloud requirement)  
**Project Type**: Modular layered .NET web app (Domain, Application, Infrastructure, Web, Tests)  
**Performance Goals**: Keep additional machine evaluation overhead low enough to avoid visible interaction latency regression; deterministic transition evaluation with small transition sets per cycle  
**Constraints**: No fallback/default machine behavior; exactly one active machine per session; admin-only machine mutate/migrate actions; pinned machine version for in-progress sessions; fail-fast on missing/invalid required config  
**Scale/Scope**: First production machine for one theme family; one active machine per session; sessions may run hundreds of interactions and require persisted resumability

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

## Project Structure

### Documentation (this feature)

```text
specs/007-theme-state-machine/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── service-contracts.md
└── tasks.md               # Created by /speckit.tasks
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
└── RolePlay/
  ├── RPThemeModels.cs                      # Extend with machine definition entities
  ├── AdaptiveScenarioState.cs              # Add machine snapshot payload
  └── ThemeMachineModels.cs                 # NEW (state machine value objects)

DreamGenClone.Application/
└── RolePlay/
  ├── IRPThemeService.cs                    # Extend with machine CRUD/activation/migration
  ├── IRolePlayStateRepository.cs           # Extend with machine diagnostics read/write
  ├── IRolePlayDiagnosticsRepository.cs     # Extend with machine diagnostics query
  ├── IThemeMachineEvaluator.cs             # NEW runtime evaluator contract
  └── IThemeMachineAuthorizationService.cs  # NEW admin authorization contract

DreamGenClone.Infrastructure/
├── Persistence/
│   └── SqlitePersistence.cs                  # Add/ensure machine-related schema artifacts
└── RolePlay/
  ├── RPThemeService.cs                     # Persist/validate machine definitions
  ├── RolePlayStateRepository.cs            # Persist/load machine snapshot + events
  ├── RolePlayDiagnosticsRepository.cs      # Query machine diagnostics
  ├── ThemeMachineEvaluator.cs              # NEW deterministic transition evaluator
  └── ThemeMachineAuthorizationService.cs   # NEW admin-only authorization implementation

DreamGenClone.Web/
├── Program.cs                                # Register new services
├── Application/RolePlay/
│   ├── RolePlayEngineService.cs              # Evaluate machine in V2 pipeline
│   ├── RolePlayContinuationService.cs        # Inject machine directives into prompt contract
│   └── RolePlayAssistantPrompts.cs           # Format machine directive prompt helpers
└── Components/Pages/
  ├── RPThemeDetail.razor                   # Machine editing UI
  ├── RPThemes.razor                        # Machine status/activation visibility
  └── RolePlayWorkspace.razor               # Runtime machine state visibility (diagnostics)

DreamGenClone.Tests/
└── RolePlay/
  ├── RolePlaySessionLifecycleTests.cs
  ├── RolePlayContinueAsSelectionTests.cs
  ├── RolePlayContinuationScenarioGuidanceTests.cs
  ├── PhaseLifecycleTransitionTests.cs
  ├── ThemeMachineEvaluatorTests.cs         # NEW
  ├── ThemeMachinePersistenceTests.cs       # NEW
  └── ThemeMachineAuthorizationTests.cs     # NEW
```

**Structure Decision**: Keep the existing five-project layered architecture. Add machine contracts in Application, implementations in Infrastructure/Web integration points, and keep persistence in SQLite through current repository patterns.

## Phase Plan

### Phase 0 - Research (completed)

- Completed [research.md](research.md) with decisions for configuration source, deterministic selection, version pinning, cooldown gates, authorization, diagnostics, and integration points.
- Resolved technical unknowns without leaving `NEEDS CLARIFICATION` items.

### Phase 1 - Design and Contracts (completed)

- Completed [data-model.md](data-model.md) with entities, relationships, validation rules, and transition model.
- Completed [contracts/service-contracts.md](contracts/service-contracts.md) for service interfaces and runtime directive contracts.
- Completed [quickstart.md](quickstart.md) for implementation/verification flow.

### Phase 2 - Task Planning (next command)

- Generate dependency-ordered implementation tasks with `/speckit.tasks`.
- Ensure tasks include no-fallback enforcement checks, admin-only authorization tests, and transition determinism tests.

## Post-Design Constitution Check

- [x] Local-first runtime preserved
- [x] Module boundaries remain layered and swappable
- [x] Deterministic transition logic is explicit in design/contracts
- [x] JSON contract validation points are defined for persisted machine snapshots
- [x] SQLite remains the default persistence backend
- [x] Serilog and structured diagnostics remain the observability model
- [x] Cross-layer logging coverage is represented in integration plan
- [x] Configurable log levels remain unchanged

## Complexity Tracking

No constitution violations or exceptional complexity justifications are required for this plan.
