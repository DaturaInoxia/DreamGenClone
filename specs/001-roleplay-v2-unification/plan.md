# Implementation Plan: DreamGenClone RolePlay v2 Unified Scenario Intelligence

**Branch**: `001-roleplay-v2-unification` | **Date**: 2026-04-12 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-roleplay-v2-unification/spec.md`

## Summary

Deliver a single v2 release that unifies adaptive scenario commitment, deterministic concept-reference injection, and context-aware stat-altering decision points within one narrative cycle engine. The plan keeps the existing layered .NET architecture, persists v2 state in SQLite, enforces deterministic transitions and tie resolution (hysteresis), introduces budgeted guidance composition, and adds diagnostics-first observability for tuning.

## Technical Context

**Language/Version**: C# / .NET 9 / Blazor Server  
**Primary Dependencies**: Microsoft.Data.Sqlite 9.x, Serilog.AspNetCore 9.x, System.Text.Json, ASP.NET Core DI/logging abstractions  
**Storage**: SQLite for persisted feature data; JSON payload fields for complex nested structures in existing persistence patterns  
**Testing**: xUnit (DreamGenClone.Tests), deterministic unit tests, service-level integration tests using test persistence fixtures  
**Target Platform**: Windows local runtime (single-machine, local-first)  
**Project Type**: Layered Blazor Server application (Web, Application, Domain, Infrastructure, Tests)  
**Performance Goals**: Maintain interactive role-play turn latency suitable for local use while preserving deterministic scoring; scenario commit target within first 6 eligible interactions (SC-001)  
**Constraints**: Single active scenario per cycle, deterministic outcomes from identical input state, no legacy-session migration, safety subsystem deferred, configurable formulas and log levels without code changes  
**Scale/Scope**: Single-user local sessions with multi-cycle narratives, per-turn adaptive evaluations, bounded prompt budget composition, and auditable decision/transition logs

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

**Pre-Design Gate Result**: PASS.

## Project Structure

### Documentation (this feature)

```text
specs/001-roleplay-v2-unification/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── service-contracts.md
└── tasks.md
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
├── RolePlay/
│   ├── AdaptiveScenarioState.cs
│   ├── CharacterStatProfileV2.cs
│   ├── FormulaConfigVersion.cs
│   └── NarrativePhaseTransitionEvent.cs

DreamGenClone.Application/
├── RolePlay/
│   ├── IScenarioSelectionService.cs
│   ├── IScenarioLifecycleService.cs
│   ├── IConceptInjectionService.cs
│   ├── IDecisionPointService.cs
│   └── IRolePlayDiagnosticsService.cs

DreamGenClone.Infrastructure/
├── Persistence/
│   └── SqlitePersistence.cs
├── RolePlay/
│   ├── ScenarioSelectionService.cs
│   ├── ScenarioLifecycleService.cs
│   ├── ConceptInjectionService.cs
│   ├── DecisionPointService.cs
│   └── RolePlayDiagnosticsService.cs

DreamGenClone.Web/
├── Application/RolePlay/
│   ├── RolePlayEngineService.cs
│   ├── RolePlayContinuationService.cs
│   ├── RolePlayAssistantService.cs
│   └── RolePlayPromptComposer.cs
├── Components/
│   └── RolePlayWorkspace.razor

DreamGenClone.Tests/
├── RolePlay/
│   ├── ScenarioSelectionHysteresisTests.cs
│   ├── PhaseLifecycleTransitionTests.cs
│   ├── ConceptInjectionDeterminismTests.cs
│   ├── DecisionPointMutationTests.cs
│   ├── UnsupportedSessionVersionTests.cs
│   └── RolePlayDiagnosticsCoverageTests.cs
```

**Structure Decision**: Keep the existing 5-project layered architecture and add/extend role-play services at Application/Infrastructure boundaries. Web remains orchestration/UI host, Domain remains state model authority, Infrastructure owns SQLite persistence and adapters.

## Phase 0 Output (Research)

Research decisions are documented in [research.md](research.md), including hysteresis tie handling, guidance budget policy, override authorization boundaries, unsupported-version behavior, and logging/persistence best practices.

## Phase 1 Output (Design & Contracts)

Design artifacts are documented in [data-model.md](data-model.md), [contracts/service-contracts.md](contracts/service-contracts.md), and [quickstart.md](quickstart.md).

## Post-Design Constitution Check

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [x] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

**Post-Design Gate Result**: PASS.

## Complexity Tracking

No constitution violations requiring justification.
