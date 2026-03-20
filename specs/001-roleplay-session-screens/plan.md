# Implementation Plan: Role-Play Session Screen Separation

**Branch**: `001-roleplay-session-screens` | **Date**: 2026-03-17 | **Spec**: `specs/001-roleplay-session-screens/spec.md`
**Input**: Feature specification from `specs/001-roleplay-session-screens/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Split role-play session lifecycle into three focused screens: create-session, saved-sessions, and dedicated interaction workspace. Persist explicit session status (`NotStarted`/`InProgress`) to drive `Start` vs `Continue`, show session list metadata (Title, Status, Interaction Count, Last Updated), and support confirmed hard-delete from the saved-sessions page only. Implement through existing layered services (Web UI -> Application services -> Infrastructure SQLite), preserving local-first behavior and Serilog-based observability.

## Technical Context

**Language/Version**: C# / .NET 9 (`net9.0`)  
**Primary Dependencies**: ASP.NET Core Blazor Server, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.*`, Serilog (`Serilog.AspNetCore`, `Serilog.Settings.Configuration`, sinks/enrichers)  
**Storage**: SQLite (existing application persistence)  
**Testing**: Automated test project is not yet present; phase output includes test strategy and scenarios to implement in subsequent tasks  
**Target Platform**: Windows local runtime (Blazor Server app)  
**Project Type**: Layered .NET web application (Web/Application/Domain/Infrastructure)  
**Performance Goals**: Meet spec success criteria: session open in under 10s for 95% of flows; deletion reflected in list in under 2s for 95% of confirmed actions  
**Constraints**: Local-first, no mandatory cloud dependency, SQLite-default persistence, Serilog structured logging with configurable log levels, delete action only on saved-sessions page  
**Scale/Scope**: Single-user local app; role-play lifecycle split across 3 pages with session CRUD subset (create/list/open/continue/delete)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [ ] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [ ] Module boundaries and adapter seams are explicit and swappable
- [ ] .NET layered architecture uses separate projects with enforced dependency direction
- [ ] Deterministic state transitions and JSON contract validation are test-covered
- [ ] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [ ] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [ ] Logging coverage exists across layers/components/services with Information logs for major call paths
- [ ] Log levels are externally configurable, including Verbose diagnostics without code changes

Initial gate assessment (pre-Phase 0):

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [ ] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

Gate note: deterministic transition and contract-validation tests are planned but not yet implemented in current repository state; tasks phase must include them.

## Project Structure

### Documentation (this feature)

```text
specs/001-roleplay-session-screens/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
```text
DreamGenClone.sln
DreamGenClone.Domain/
├── Contracts/
└── Templates/

DreamGenClone.Application/
├── Abstractions/
├── Sessions/
├── Templates/
└── Validation/

DreamGenClone.Infrastructure/
├── Configuration/
├── Logging/
├── Models/
├── Persistence/
└── Storage/

DreamGenClone.Web/
├── Application/
│   ├── Assistants/
│   ├── RolePlay/
│   ├── Sessions/
│   └── Story/
├── Components/
│   ├── Layout/
│   ├── Pages/
│   └── Shared/
└── data/

specs/001-roleplay-session-screens/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/
```

**Structure Decision**: Use existing layered .NET solution with feature implementation centered in `DreamGenClone.Web` UI/Application services and persistence/service updates through existing Application/Infrastructure contracts as needed. No new top-level project is required.

## Phase 0 Research Plan

Research outputs will resolve and lock the following design decisions:

1. Route and page-flow contract for create -> list -> interaction transitions.
2. Persisted status semantics for `Start` vs `Continue` behavior.
3. Saved-session row projection fields and user disambiguation strategy.
4. Hard-delete behavior boundaries and failure handling (stale row, concurrent delete).
5. Logging coverage points for create/list/open/continue/delete execution paths.

## Phase 1 Design Plan

Design outputs will include:

1. `data-model.md` with role-play session entities, list projection model, and deletion command shape.
2. `contracts/roleplay-session-flow.md` with UI/service interaction contract for page flow and actions.
3. `quickstart.md` with manual verification flow mapped to acceptance scenarios.
4. Agent context refresh by running `.specify/scripts/powershell/update-agent-context.ps1 -AgentType copilot`.

## Post-Design Constitution Check

Re-check status after Phase 1 artifacts (`research.md`, `data-model.md`, `contracts/`, `quickstart.md`) and agent-context update:

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [ ] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

Post-design gate note: only test-coverage gate remains open because implementation and test projects are part of the subsequent tasks/implementation phases.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Deterministic transition and JSON contract test coverage not yet implemented in repository | Planning phase documents required tests before code changes; repository currently has no dedicated test project | Marking gate as passed without defined test work would violate constitution expectations |
