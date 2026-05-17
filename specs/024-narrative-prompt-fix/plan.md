# Implementation Plan: B-024 Narrative Prompt Fix

**Branch**: `024-narrative-prompt-fix` | **Date**: 2026-05-14 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/024-narrative-prompt-fix/spec.md`

## Summary

Eight targeted fixes in the narrative prompt pipeline: (1) remove the forced Atmospheric intensity override entirely — narrative intensity now follows the session's resolved intensity profile like all other character interactions; (2) route all narrative paths through the validation pipeline via new `ContinueNarrativeAsync`; (3) exclude quoted text from first-person leak detection; (4) add character interiority to the retry trigger; (5) make correction prompts violation-specific; (6) strengthen both writing instructions to enumerate required omniscient description categories — spatial layout, character positions, physical sensations and sounds, and for intimate scenes: bodies, contact, movement; (7) harden dialogue suppression to zero-default and lower the Climax quoted-block retry threshold to 1; (8) sanitize location names before prompt injection to strip display subtitles (e.g. `"Trailer — Shared Private Space"` → `"Trailer"`). All changes confined to `RolePlayContinuationService.cs`, `IRolePlayContinuationService.cs`, `RolePlayEngineService.cs`, and the narrative validation tests.

## Technical Context

**Language/Version**: C# 13 / .NET 9  
**Primary Dependencies**: ASP.NET Core Blazor, Entity Framework Core (SQLite), Serilog, xUnit  
**Storage**: SQLite (unchanged — no schema changes for this feature)  
**Testing**: xUnit (`dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj`)  
**Target Platform**: Windows local-first web app  
**Project Type**: Web application (Blazor Server)  
**Performance Goals**: No additional latency targets — validation retry budget stays at 1  
**Constraints**: No streaming in `ContinueNarrativeAsync` (retry and streaming are incompatible)  
**Scale/Scope**: 4 source files, ~10 code sites changed, ~10 new test cases

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow) — no change to model invocation paths
- [x] Module boundaries and adapter seams are explicit and swappable — new `ContinueNarrativeAsync` is on the `IRolePlayContinuationService` interface, not a concrete type
- [x] .NET layered architecture uses separate projects with enforced dependency direction — changes in `DreamGenClone.Web` application layer only
- [x] Deterministic state transitions and JSON contract validation are test-covered — new tests added for each fix
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale — no persistence changes
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices — existing logging patterns preserved
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths — `ContinueNarrativeAsync` will emit the same debug events as `ContinueBatchAsync` narrative path
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes — no new log level dependencies introduced

**No violations. No complexity justification required.**

## Project Structure

### Documentation (this feature)

```text
specs/024-narrative-prompt-fix/
├── plan.md              # This file
├── spec.md              # Feature spec — 5 requirements
├── research.md          # Root cause analysis for all 5 issues
├── data-model.md        # Interface change + logic changes (no new entities)
├── quickstart.md        # How to verify the fix
└── tasks.md             # Phase 2 output (created by /speckit.tasks — not yet created)
```

### Source Code (affected files)

```text
DreamGenClone.Web/
├── Application/
│   └── RolePlay/
│       ├── IRolePlayContinuationService.cs     # +ContinueNarrativeAsync (REQ-2)
│       ├── RolePlayContinuationService.cs       # REQ-1, REQ-2, REQ-3, REQ-4, REQ-5
│       └── RolePlayEngineService.cs             # REQ-2: call ContinueNarrativeAsync
└── (no Razor/UI changes)

DreamGenClone.Tests/
└── RolePlay/
    └── RolePlayContinuationNarrativeValidationTests.cs  # New tests for all 5 fixes
```

**Structure Decision**: No new projects. All changes in the existing `DreamGenClone.Web` web project (application layer) and `DreamGenClone.Tests`.

## Complexity Tracking

*No Constitution Check violations — section left intentionally empty.*
