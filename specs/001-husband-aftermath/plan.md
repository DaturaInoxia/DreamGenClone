# Implementation Plan: Wife-Husband Aftermath Closure

**Branch**: `001-husband-aftermath` | **Date**: 2026-07-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-husband-aftermath/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add a new `TimeSkipPhase.AftermathCoupleInteraction = 3` enum value and a `[Aftermath:husband-contrast]` theme marker that, when present in any non-Reset phase, inserts a closure turn after an encounter boundary is detected: the wife gets dressed, returns to the ordinary setting, interacts with her husband, and acts normal — the contrast between the secret encounter and ordinary performance is the narrative point. The state machine flow becomes:

- Multi-encounter + aftermath in Climax: `None → CloseScene → AftermathCoupleInteraction → AdvanceTime → None`
- Aftermath only (no multi-encounter), non-Reset phase: `AftermathCoupleInteraction → None`
- Aftermath + multi-encounter in a non-Climax phase: `AftermathCoupleInteraction → None` (AdvanceTime leg stays Climax-locked)
- Themes without the aftermath marker: unchanged (existing `CloseScene → AdvanceTime → None` for multi-encounter in Climax, natural pacing otherwise)

Technical approach (Option C from research — single inference call, branched consequence): extend the existing `TimeSkipPhase` enum (no DB migration for the phase column itself — value `3` fits the existing `INTEGER`); add one new `LastEncounterEvidenceSpan TEXT` column to `RolePlayV2AdaptiveStates` so the `HusbandAftermathInjector` can reference "what she just did" verbatim from the AI's own detection trace; relax the `TryDetectEncounterBoundaryAsync` phase/encounter gates to fire on either marker post-detection; insert a new state-machine branch in the overflow time-skip block; add a new `HusbandAftermathInjector` (priority 85) that emits the contrast directive; suppress `FinalDirectiveInjector`'s Fast Pacing HC during the aftermath leg only; restrict `ResolveSceneContinueActorsAsync` to wife + husband when aftermath is active, with explicit abort-on-missing-spouse (no silent fallback per repo no-fallback rule). Pure-unit tests mirror the 28 existing `MultiEncounterTimeSkipTests` patterns.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (`net9.0`; target framework shared by all projects in the solution)
**Primary Dependencies**: ASP.NET Core 9 (`Microsoft.NET.Sdk.Web`), Blazor Server, `Microsoft.Data.Sqlite 9.0.0`, Serilog stack (`Serilog.AspNetCore 9.0.0`, `Serilog.Sinks.Console 6.0.0`, `Serilog.Sinks.File 6.0.0`, `Serilog.Settings.Configuration 9.0.0`, enrichers Environment/Thread), xUnit 2.9.2 + coverlet 6.0.2.
**Storage**: SQLite — existing `RolePlayV2AdaptiveStates` table; new `LastEncounterEvidenceSpan TEXT` column (Phase A2). The `CurrentTimeSkipPhase` column survives the new enum value `3` unchanged (already `INTEGER NOT NULL DEFAULT 0`).
**Testing**: xUnit `MultiEncounterTimeSkipTests.cs` (28 existing tests, pure-unit, no DI) — extended with `AftermathHusbandContrastTests.cs` (~18 new tests).
**Target Platform**: Windows local-first Blazor Server runtime (single-user, local LLM orchestration).
**Project Type**: Web app (Blazor Server) with layered .NET solution (Domain / Application / Infrastructure / Web / Tests) + `artifacts/tmp/dbquery` console tool for SQLite inspection.
**Performance Goals**: No new perf targets. The aftermath leg adds one extra overflow interaction per encounter boundary (mirrors the existing CloseScene/AdvanceTime pattern). LLM detection fires once per boundary (Option C — single inference, branched consequence — preserves the existing one-call-per-detection invariant).
**Constraints**:
- Repo no-fallback rule (`.github/copilot-instructions.md`): missing spouse MUST abort explicitly with a diagnostic log; no silent default actors. Missing required `encounter-completed` semantic mapping MUST fail fast (FR-011).
- Repo roleplay-engine strict config contract: every RP behavior control (in this feature, the `[Aftermath:husband-contrast]` marker) MUST be UI-backed editable persisted data — satisfied because the marker lives in theme phase-guidance text, the existing editable surface.
- Wire-level enum extension MUST NOT break legacy `TimeSkipPending` back-compat path (existing `INTEGER`-cast fallback at `RolePlayStateRepository.cs:595` continues to work for value `3`).
**Scale/Scope**: 1 enum value, 1 new persisted field + DB column, 1 new marker helper, 1 detection refactor (gate relaxation), 1 state-machine branch, 1 new injector (priority 85), 1 actor-filter branch + spouse helper extraction, 1 Fast Pacing HC suppression, ~18 new unit tests. Small scope — single-feature, single sprint.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow) — feature reuses existing local LLM orchestration; no new cloud dependency.
- [x] Module boundaries and adapter seams are explicit and swappable — Domain enum + field extension (no boundary change); Infrastructure schema migration extends existing repository methods; Web/Application layer gains a new injector class implementing the existing `IPromptInjector` interface; no new seam required.
- [x] .NET layered architecture uses separate projects with enforced dependency direction — Domain → Application → Infrastructure → Web + Tests (the dependency direction already enforced in `DreamGenClone.sln` is preserved; no new project references).
- [x] Deterministic state transitions and JSON contract validation are test-covered — `TimeSkipPhase` transitions remain explicit enum assignments logged via Serilog (`MultiEncounterInstructionInjected` debug event gains the `AftermathCoupleInteraction` variant); `AftermathHusbandContrastTests.cs` pure-unit matrix covers each transition branch; no JSON contract surface added (the feature operates on already-validated `AdaptiveScenarioState` in-memory graph).
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale — `LastEncounterEvidenceSpan TEXT` column added to existing `RolePlayV2AdaptiveStates` table in `RolePlayStateRepository.EnsureSchemaAsync`; FR-013 explicitly states SQLite; no other persistence backend introduced.
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices — feature reuses the existing `_logger.Log*` pattern (structured templates with named placeholders like `{SessionId}`, `{Phase}`, `{Encounter}`); FR-014 mandates this for the new events `HusbandAftermathAbortedMissingSpouse`, `MultiEncounterInstructionInjected(AftermathCoupleInteraction)`, `AftermathHusbandContrast_AbortedMissingMapping`; also writes parallel `RolePlayDebugEventRecord` entries via the existing `_debugEventSink`.
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths — `RolePlayEngineService` emits Information-level logs for boundary advance, aftermath leg injection, and abort paths; `RolePlayStateRepository` continues its existing Information logs for schema migration; `HusbandAftermathInjector.BuildText` does not log (pure text assembly, consistent with other injectors); `_debugEventSink` records align with the existing diagnostic-panel surface for in-engine visibility.
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes — feature uses the existing `_logger.LogDebug("TryDetectEncounterBoundary: ...")` Verbose-level entry pattern (already configurable via `appsettings.json`'s Serilog minimum-level override); no new log-level hardcoding introduced.

**Pre-Phase 0 verdict**: All gates pass. No complexity tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/001-husband-aftermath/
├── plan.md                # This file
├── research.md            # Phase 0 — option C decision rationale, marker convention, spouse-resolution reuse, enum-value migration
├── data-model.md          # Phase 1 — TimeSkipPhase enum, LastEncounterEvidenceSpan field, state transitions
├── quickstart.md          # Phase 1 — sample theme config + manual verification recipe
├── contracts/
│   └── aftermarth-state-machine.md   # Phase 1 — the four state-machine flows (marker × Climax)
└── tasks.md               # Phase 2 (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/RolePlay/
└── AdaptiveScenarioState.cs             # §A1 — TimeSkipPhase.AftermathCoupleInteraction=3 + LastEncounterEvidenceSpan field

DreamGenClone.Infrastructure/RolePlay/
└── RolePlayStateRepository.cs           # §A2 — schema migration, INSERT/UPDATE/SELECT + parameter mapping for LastEncounterEvidenceSpan

DreamGenClone.Web/Application/RolePlay/
├── RolePlayAssistantPrompts.cs          # §B — IsAftermathHusbandContrast helper + EnsureEncounterCompletedMappingAsync widening
├── RolePlayEngineService.cs             # §C/D/F — TryDetectEncounterBoundaryAsync relaxation, overflow time-skip AftermathCoupleInteraction branch, ResolveSceneContinueActorsAsync filter, ResolveSpouseForAftermathAsync helper, HydrateV2State restore
└── Injectors/
    ├── HusbandAftermathInjector.cs      # §E (NEW) — priority 85, contrast directive text
    └── FinalDirectiveInjector.cs        # §E — suppress Fast Pacing HC during AftermathCoupleInteraction only

DreamGenClone.Web/
└── Program.cs                           # §E — register HusbandAftermathInjector in the IPromptInjector DI loop

DreamGenClone.Tests/RolePlay/
├── AftermathHusbandContrastTests.cs     # §G (NEW) — ~18 pure-unit tests mirroring MultiEncounterTimeSkipTests patterns
└── MultiEncounterTimeSkipTests.cs       # regression baseline (unchanged — 28 tests must still pass)

# Tooling (unchanged but used for verification)
artifacts/tmp/dbquery/
└── Program.cs                            # use `schema RolePlayV2AdaptiveStates` to verify the new column after bootstrap

# Backlog hygiene
specs/Planning/
├── backlog.md                           # §H — B-056 new → designed, B-054 annotation of subsumption
└── B-056-husband-aftermath.md           # §H — design plan (single source of truth)
```

**Structure Decision**: The existing layered .NET solution structure (Domain / Application / Infrastructure / Web / Tests) absorbs this feature without a new project. Every change lands in an existing assembly: enum + state in Domain, persistence migration in Infrastructure, engine + injector + DI in Web, tests in Tests. No new project, no new assembly, no new seam — the constitution's modularity principle II is preserved by extending existing modules at their natural extension points.

## Complexity Tracking

> None. All Constitution Check gates pass without exception. No violations to justify.
