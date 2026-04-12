# Implementation Plan: Adaptive Scenario Selection Engine Redesign 2

**Branch**: `002-adaptive-scenario-redesign2` | **Date**: 2026-04-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-adaptive-scenario-redesign2/spec.md`

## Summary

Replace blend-based theme selection with a deterministic, single-scenario commitment engine that advances through Build-Up -> Committed -> Approaching -> Climax -> Reset phases. The implementation adds explicit scenario fit evaluation, phase transition gates, reset-first manual override behavior, phase-aware guidance inputs, and scenario-cycle history while preserving local-first runtime, SQLite persistence, and Serilog observability.

## Technical Context

**Language/Version**: C# / .NET 9 (net9.0)  
**Primary Dependencies**: Microsoft.Data.Sqlite 9.0.0, Microsoft.Extensions.Logging.Abstractions 9.0.0, Serilog.AspNetCore 9.0.0, Serilog.Settings.Configuration 9.0.0, Serilog.Sinks.Console 6.0.0, Serilog.Sinks.File 6.0.0  
**Storage**: SQLite via existing persistence abstractions and JSON-serialized session state payloads  
**Testing**: xUnit (DreamGenClone.Tests), Microsoft.NET.Test.Sdk, coverlet collector  
**Target Platform**: Windows local runtime (Blazor Server host)  
**Project Type**: Layered .NET web application (Web + Application + Domain + Infrastructure + Tests)  
**Performance Goals**: Meet spec outcomes, including commitment convergence within 6 interactions in >=90% eligible sessions and stable active-scenario retention in >=95% committed cycles  
**Constraints**: Local-first operation, deterministic transitions, strict threshold gates from spec clarifications, reset-first manual override, no mandatory cloud dependency for core flow  
**Scale/Scope**: Single-user interactive sessions, multi-cycle role-play narratives, at least 3 completed cycles in long-session validation scenarios

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [x] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

### Post-Design Re-Check

- [x] Local-first runtime preserved by keeping all adaptive evaluation and phase control in existing local services
- [x] New scenario-selection and phase-evaluation services remain behind application interfaces to preserve swap boundaries
- [x] Layering remains unchanged: Domain models + Application abstractions + Infrastructure persistence + Web orchestration
- [x] Deterministic transition gates are explicit and testable (fixed thresholds, interaction counts, tie rules)
- [x] Persistence remains SQLite-only for this feature
- [x] Serilog structured logging remains primary and is extended to transition/override events
- [x] Information-level logs cover major call paths for selection, phase transitions, reset, and manual override
- [x] Log level controls remain configuration-driven (including Verbose)

## Project Structure

### Documentation (this feature)

```text
specs/002-adaptive-scenario-redesign2/
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
DreamGenClone.Application/
├── Abstractions/
│   └── IRolePlayDebugEventSink.cs
└── StoryAnalysis/
  ├── IScenarioSelectionEngine.cs          # NEW
  ├── INarrativePhaseManager.cs            # NEW
  └── IScenarioGuidanceContextFactory.cs   # NEW

DreamGenClone.Domain/
└── StoryAnalysis/
  ├── ScenarioMetadata.cs                  # NEW
  └── NarrativePhase.cs                    # NEW enum

DreamGenClone.Web/
├── Domain/RolePlay/
│   └── RolePlayAdaptiveState.cs            # extend with active scenario + phase + history
├── Application/RolePlay/
│   ├── RolePlayAdaptiveStateService.cs     # selection, phase transitions, reset integration
│   ├── RolePlayContinuationService.cs      # phase-aware scenario guidance context
│   └── RolePlayAssistantPrompts.cs         # scenario-specific phase guidance injection
└── Application/Sessions/
  └── SessionService.cs                    # persisted state compatibility validation

DreamGenClone.Infrastructure/
└── StoryAnalysis/
  ├── ScenarioSelectionEngine.cs           # NEW
  ├── NarrativePhaseManager.cs             # NEW
  └── ScenarioGuidanceContextFactory.cs    # NEW

DreamGenClone.Tests/
├── StoryAnalysis/
│   ├── ScenarioSelectionEngineTests.cs
│   ├── NarrativePhaseManagerTests.cs
│   └── ScenarioResetCycleTests.cs
└── RolePlay/
  ├── RolePlayAdaptiveStateServiceScenarioTests.cs
  └── RolePlayContinuationScenarioGuidanceTests.cs
```

**Structure Decision**: Keep the current layered architecture and add new scenario engine abstractions in Application, implementations in Infrastructure, state contracts in Domain/Web domain models, and orchestration integration in existing Web role-play services.

## Complexity Tracking

No constitution violations. No additional complexity justifications required.
