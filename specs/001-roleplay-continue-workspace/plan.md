# Implementation Plan: Role Play Continue Workspace Refresh

**Branch**: `001-roleplay-continue-workspace` | **Date**: 2026-03-19 | **Spec**: `D:\src\DreamGenClone\specs\001-roleplay-continue-workspace\spec.md`
**Input**: Feature specification from `D:\src\DreamGenClone\specs\001-roleplay-continue-workspace\spec.md`

## Summary

Refresh the role-play continue workspace to a single prompt-driven compose surface, with popup selectors for intended command and identity, explicit routing for message versus instruction continuation paths, and a right-side resizable settings area containing behavior mode controls. The design is anchored to the UITemplate references and implemented through existing layered services in Web/Application/Domain/Infrastructure with focused UI and orchestration updates.

## Technical Context

**Language/Version**: C# / .NET 9 (ASP.NET Core Blazor Server)  
**Primary Dependencies**: ASP.NET Core Blazor components, Microsoft.Data.Sqlite, Serilog.AspNetCore + Serilog enrichers/sinks, Microsoft.Extensions.* abstractions  
**Storage**: SQLite-backed session persistence via existing session abstractions; in-memory caches remain runtime optimization only  
**Testing**: xUnit, Microsoft.NET.Test.Sdk, coverlet.collector  
**Target Platform**: Local Windows-hosted ASP.NET Core app (interactive server render mode)  
**Project Type**: Layered .NET web application (Web host + Application + Domain + Infrastructure + Tests)  
**Performance Goals**: Prompt UI interactions (open popup/select/submit intent) should be immediate in normal local operation; measurable success criteria remain SC-001..SC-005 from spec  
**Constraints**: Local-first runtime for core flow, no mandatory cloud dependency, behavior-mode restrictions must remain enforced, command routing must be deterministic, alignment with UITemplate interaction cues  
**Scale/Scope**: Single role-play workspace page plus related role-play service logic and tests in existing solution

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
specs/001-roleplay-continue-workspace/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md
```

### Source Code (repository root)

```text
DreamGenClone.Web/
├── Components/
│   └── Pages/
│       └── RolePlayWorkspace.razor
├── Application/
│   ├── RolePlay/
│   │   ├── IRolePlayEngineService.cs
│   │   ├── IRolePlayContinuationService.cs
│   │   ├── RolePlayEngineService.cs
│   │   ├── RolePlayContinuationService.cs
│   │   └── BehaviorModeService.cs
│   └── Sessions/
└── Domain/
    └── RolePlay/
        ├── RolePlaySession.cs
        ├── RolePlayInteraction.cs
        ├── ContinueAsActor.cs
        └── BehaviorMode.cs

DreamGenClone.Tests/
└── RolePlay/
```

**Structure Decision**: Keep the existing layered architecture and implement this feature as Web UI composition updates plus Application-layer orchestration/routing updates, with Domain-level enum/DTO additions only if needed for command intent clarity.

## Phase 0: Research Focus

- Map each UITemplate image to concrete UI states/interactions and required behavior.
- Establish deterministic routing rules for unified prompt intent selection (message/narrative/instruction).
- Confirm Blazor-friendly approach for right-side resizable settings panel without introducing architectural drift.

## Phase 1: Design Focus

- Define data model additions for unified prompt submission and identity option resolution.
- Define contracts for prompt command routing and selector behavior.
- Produce validation quickstart for manual and automated verification.

## Post-Design Constitution Check

- [x] Local-first runtime preserved by continuing to use local LM Studio adapter abstraction and local persistence.
- [x] Layer boundaries remain intact: UI in Web Components, orchestration in Application services, role-play primitives in Domain.
- [x] Deterministic routing is captured as explicit command intent mapping in contracts/data model.
- [x] SQLite default persistence remains unchanged.
- [x] Serilog-centric observability remains required for new major execution paths.

## Complexity Tracking

No constitution violations identified; no complexity exemptions required.
