# Implementation Plan: RolePlay Interaction Commands

**Branch**: `001-roleplay-interaction-commands` | **Date**: 2026-04-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/001-roleplay-interaction-commands/spec.md`

## Summary

Add per-interaction commands (state flags, inline editing, deletion, retry/regeneration with sibling alternatives, and session forking) to the RolePlay workspace. Extends the existing `RolePlayInteraction` domain model with boolean flags and an alternatives data structure. Adds two new application services (`IInteractionCommandService`, `IInteractionRetryService`), extends the existing `IRolePlayBranchService` with fork-above/below variants, and updates `RolePlayContinuationService` context building to respect flags. UI changes are concentrated in `RolePlayWorkspace.razor` for toolbar, menus, dialogs, and alternatives carousel.

## Technical Context

**Language/Version**: C# / .NET 9.0  
**Primary Dependencies**: Blazor Server (interactive SSR), Microsoft.Data.Sqlite, Serilog.AspNetCore, System.Text.Json  
**Storage**: SQLite (sessions stored as JSON payloads via `SqlitePersistence`)  
**Testing**: xUnit 2.9.2 with coverlet  
**Target Platform**: Windows desktop (local-first, LM Studio / Ollama backends)  
**Project Type**: Blazor web application with layered architecture  
**Performance Goals**: <1s for flag toggles and alternative navigation; <5s for AI retry operations (model-dependent)  
**Constraints**: Local-only inference; 8GB VRAM (RTX 4060 Ti); context window limited by model capacity  
**Scale/Scope**: Single-user local application; sessions with 10-200+ interactions; unlimited alternatives per interaction

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

**Gate assessment**: All checks pass. Feature extends existing layered architecture without new projects. All new services follow existing patterns (interface + implementation in Web.Application, domain models in Web.Domain, persistence via existing SQLite session JSON). Serilog already configured with structured logging, file + console sinks, and configurable levels.

## Project Structure

### Documentation (this feature)

```text
specs/001-roleplay-interaction-commands/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── interaction-commands-contract.md
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
DreamGenClone.Web/
├── Domain/RolePlay/
│   ├── RolePlayInteraction.cs          # MODIFY: add flags + alternatives fields
│   └── InteractionCommand.cs           # NEW: command enum
├── Application/RolePlay/
│   ├── IInteractionCommandService.cs   # NEW: flag/edit/delete interface
│   ├── InteractionCommandService.cs    # NEW: implementation
│   ├── IInteractionRetryService.cs     # NEW: retry/rewrite interface
│   ├── InteractionRetryService.cs      # NEW: implementation
│   ├── IRolePlayBranchService.cs       # MODIFY: add ForkAbove/ForkBelow
│   ├── RolePlayBranchService.cs        # MODIFY: implement fork variants
│   └── RolePlayContinuationService.cs  # MODIFY: flag-aware context building
├── Components/Pages/
│   └── RolePlayWorkspace.razor         # MODIFY: toolbar, menus, dialogs, carousel

DreamGenClone.Tests/
├── RolePlay/
│   ├── InteractionCommandServiceTests.cs   # NEW
│   ├── InteractionRetryServiceTests.cs     # NEW
│   ├── RolePlayBranchServiceForkTests.cs   # NEW
│   └── ContinuationContextFilterTests.cs   # NEW
```

**Structure Decision**: No new projects. All new code fits within existing `DreamGenClone.Web` (domain + application + UI) and `DreamGenClone.Tests` projects, following the established layered pattern.
