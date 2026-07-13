# Implementation Plan: Prompt Viewer Tab on Interaction Info Modal

**Branch**: `001-prompt-viewer-tab` | **Date**: 2026-07-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-prompt-viewer-tab/spec.md`

## Summary

Add a `PromptText` nullable string property to `RolePlayInteraction` to capture the full LLM prompt at build time. Add a scrollable "LLM Prompt" tab to the Interaction Info modal in `RolePlayWorkspace.razor` to display the stored prompt. The prior session interactions block within the stored prompt is trimmed to first N + last N characters for storage efficiency; all other prompt sections are stored in full. Prompt capture is best-effort — failures log a warning and persist the interaction with null `PromptText`.

**Key finding from research**: `RolePlayInteraction` is NOT mapped to its own DB table — it is a nested collection on `RolePlaySession` serialized into the `PayloadJson` JSON blob on the `Sessions` table. Therefore **no schema migration is needed**; the new nullable property auto-serializes. Existing interactions deserialize with `PromptText = null`.

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: ASP.NET Core, Blazor Interactive Server, Microsoft.Data.Sqlite (raw ADO.NET, no EF Core), Serilog
**Storage**: SQLite — `Sessions` table with `PayloadJson` TEXT column containing serialized `RolePlaySession` (including `Interactions` list). No per-interaction table; no schema migration needed for new nullable JSON property.
**Testing**: xUnit (`DreamGenClone.Tests` project)
**Target Platform**: Windows desktop (local-first Blazor Server app)
**Project Type**: Web application (Blazor Interactive Server) with layered .NET solution (Domain, Application, Infrastructure, Web, Tests)
**Performance Goals**: Prompt capture adds negligible overhead (string assignment + truncation). No latency impact on continuation flow.
**Constraints**: Prompt capture MUST be best-effort (never block interaction creation). Stored prompt size reduced by trimming prior-interactions block to first N + last N characters.
**Scale/Scope**: Small — 1 domain property, 1 service modification point (plus retry paths), 1 UI component modification. No new projects, no new tables, no migration.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow) — prompt capture is a local string operation; no cloud dependency added
- [x] Module boundaries and adapter seams are explicit and swappable — `PromptText` is a domain property on `RolePlayInteraction`; capture happens in `RolePlayContinuationService` (Application layer); display happens in `RolePlayWorkspace.razor` (Web layer); no boundary crossings
- [x] .NET layered architecture uses separate projects with enforced dependency direction — no new project references needed; changes stay within existing layers
- [x] Deterministic state transitions and JSON contract validation are test-covered — `PromptText` is a passive data field; truncation logic is pure and testable; no state transitions affected
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale — `PromptText` serializes inside existing `PayloadJson` blob on `Sessions` table; no new store, no schema migration
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices — best-effort failure logs a warning via existing Serilog logger
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths — prompt capture success/failure logged at Information/Warning level
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes — uses existing Serilog configuration

**Gate Result**: PASS — no violations. No complexity tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/001-prompt-viewer-tab/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
DreamGenClone.Web/
├── Domain/
│   └── RolePlay/
│       └── RolePlayInteraction.cs          # ADD: PromptText property
├── Application/
│   └── RolePlay/
│       ├── RolePlayContinuationService.cs  # MODIFY: set PromptText on interaction creation
│       ├── RolePlayEngineService.cs        # MODIFY: set PromptText on multi-actor paths
│       └── InteractionRetryService.cs       # MODIFY: set PromptText on retry paths
└── Components/
    └── Pages/
        └── RolePlayWorkspace.razor         # MODIFY: add LLM Prompt tab + SetPromptTab method

DreamGenClone.Tests/
└── RolePlay/
    └── PromptTextTruncationTests.cs        # ADD: unit tests for truncation logic
```

**Structure Decision**: No new projects or directories. The feature touches 3 existing layers (Domain, Application, Web) within the existing .NET solution structure. All changes are additive (new property, new tab) or modifications to existing files (setting `PromptText` at interaction creation sites). The truncation helper is a pure function testable without database or UI.

## Complexity Tracking

No Constitution Check violations. No complexity tracking entries needed.
