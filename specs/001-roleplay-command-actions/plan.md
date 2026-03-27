# Implementation Plan: Chat and Roleplay Command Actions

**Branch**: `001-roleplay-command-actions` | **Date**: 2026-03-25 | **Spec**: `D:\src\DreamGenClone\specs\001-roleplay-command-actions\spec.md`
**Input**: Feature specification from `D:\src\DreamGenClone\specs\001-roleplay-command-actions\spec.md`

## Summary

Implement explicit chat/roleplay command flows for Instruction, Message, Narrative by Character, and Continue As so each action is unambiguous, deterministic, and visible in interaction history. The design keeps the existing layered .NET architecture and extends current roleplay workspace UI + prompt orchestration services with strong routing contracts and validation for multi-participant continuation.

## Technical Context

**Language/Version**: C# / .NET 9 (ASP.NET Core Blazor Server)
**Primary Dependencies**: ASP.NET Core Blazor components, Microsoft.Data.Sqlite, Serilog.AspNetCore + Serilog enrichers/sinks, Microsoft.Extensions logging/configuration abstractions
**Storage**: SQLite persistence for sessions/interactions; no non-SQLite exception required for this feature
**Testing**: xUnit + Microsoft.NET.Test.Sdk + coverlet.collector
**Target Platform**: Local Windows-hosted ASP.NET Core app (interactive server render mode)
**Project Type**: Layered .NET web application (Web host + Application + Domain + Infrastructure + Tests)
**Performance Goals**: Action selection and submission controls should respond immediately under normal local use; continuation for multi-selection should complete in a single deterministic operation per request
**Constraints**: Local-first execution, deterministic routing, explicit mode/identity validation, no mandatory cloud dependency, keep compatibility with existing roleplay workspace interactions
**Scale/Scope**: Roleplay workspace command handling and continuation behavior on one page with supporting application/domain services and focused tests

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
specs/001-roleplay-command-actions/
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
│   └── RolePlay/
│       ├── RolePlayEngineService.cs
│       ├── RolePlayContinuationService.cs
│       ├── RolePlayPromptRouter.cs
│       ├── RolePlayIdentityOptionsService.cs
│       └── RolePlayRoutes.cs
├── Domain/
│   └── RolePlay/
│       ├── RolePlaySession.cs
│       ├── RolePlayInteraction.cs
│       └── RolePlaySessionStatus.cs
└── Program.cs

DreamGenClone.Tests/
└── RolePlay/
    ├── RolePlayPromptRouterTests.cs
    ├── RolePlayIntentRoutingTests.cs
    ├── RolePlayIdentityOptionsTests.cs
    ├── RolePlayUnifiedPromptValidationTests.cs
    └── RolePlayBehaviorModeSubmitTests.cs
```

**Structure Decision**: Keep existing layered architecture and implement behavior by extending Web workspace controls and Application roleplay services. Domain models are updated only where needed for explicit interaction typing and continuation metadata. Existing tests are expanded to enforce deterministic routing and validation.

## Phase 0: Research Focus

- Confirm deterministic routing semantics for three action modes plus Continue As equivalence to overflow continue.
- Define validation rules for instruction-without-character and character-required actions.
- Define multi-participant continuation ordering and narrative composition behavior.

## Phase 1: Design Focus

- Define refined command and continuation entities, including validation/state transitions.
- Define interface contract for submit/continue/clear operations and outcome types.
- Produce quickstart covering manual UI verification and automated test execution.

## Post-Design Constitution Check

- [x] Local-first runtime preserved with existing local model adapter boundary.
- [x] Layer boundaries remain intact (UI in Web, orchestration in Application, state models in Domain).
- [x] Deterministic routing/validation captured in contracts and planned test coverage.
- [x] SQLite-default persistence remains unchanged.
- [x] Serilog and configurable logging requirements remain explicit for major execution paths.

## Complexity Tracking

No constitution violations identified; no complexity exemptions required.
