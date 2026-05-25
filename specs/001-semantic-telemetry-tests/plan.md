# Implementation Plan: Semantic Telemetry and Event-Driven Evidence

**Branch**: `001-semantic-telemetry-tests` | **Date**: 2026-05-18 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-semantic-telemetry-tests/spec.md`

## Summary

Add additive semantic evidence processing and debug telemetry to RP V2 so each interaction exposes semantic events, confidence, and applied/capped/suppressed deltas while preserving strict no-fallback behavior. Processing remains latest-interaction-only and evidence-only; invalid semantic payload/config/confidence fails semantic processing for that interaction with explicit diagnostics and zero semantic delta application.

## Technical Context

**Language/Version**: C# on .NET 9  
**Primary Dependencies**: ASP.NET Core/Blazor host, existing RolePlay services, Serilog, SQLite persistence layer  
**Storage**: SQLite (default policy, no new store)  
**Testing**: xUnit via `dotnet test` in `DreamGenClone.Tests`  
**Target Platform**: Windows local runtime (web app + local services)  
**Project Type**: Layered .NET web application (`Web`, `Application`, `Domain`, `Infrastructure`, `Tests`)  
**Performance Goals**: Keep semantic evaluation bounded to one interaction; no perceptible regression in continue/selection flow latency  
**Constraints**: No fallback/default semantic paths, deterministic evidence updates, explicit diagnostics, blocked theme lock invariants  
**Scale/Scope**: RP V2 semantic evidence path, debug telemetry output, and RolePlay test suites for mapping, guardrails, and ordering/fit outcomes

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Design Gate

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [x] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

### Post-Design Recheck

- [x] Design artifacts keep one canonical semantic configuration source and no fallback branch
- [x] Data model defines deterministic constraints for confidence validation, cap/cooldown, and lock enforcement
- [x] Contracts require explicit diagnostics on semantic-step failure and zero semantic delta application
- [x] Quickstart verification includes unit/integration/regression and manual debug evidence checks

## Project Structure

### Documentation (this feature)

```text
specs/001-semantic-telemetry-tests/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── service-contracts.md
└── tasks.md  (generated later by /speckit.tasks)
```

### Source Code (repository root)

```text
DreamGenClone.Web/
├── Application/RolePlay/
├── Domain/RolePlay/
└── Components/Pages/RolePlay*.razor

DreamGenClone.Infrastructure/
└── RolePlay/

DreamGenClone.Domain/
└── RolePlay/

DreamGenClone.Tests/
└── RolePlay/
```

**Structure Decision**: Use the existing layered .NET repository structure and implement semantic telemetry/evidence behavior in RolePlay services and models, with verification in RolePlay tests and debug surface checks.

## Phase 0: Research Output

Research completed in [research.md](./research.md) with decisions for:

- Canonical semantic configuration source and no-fallback enforcement.
- Latest-interaction-only semantic scope and evidence-only updates.
- Out-of-range confidence fail-fast handling with zero semantic deltas.
- Telemetry shape and constraint enforcement order.
- Required test coverage matrix and release gate checks.

## Phase 1: Design and Contracts Output

Design artifacts completed:

- Data model: [data-model.md](./data-model.md)
- Service contracts: [contracts/service-contracts.md](./contracts/service-contracts.md)
- Implementation/verification flow: [quickstart.md](./quickstart.md)

Planned implementation touchpoints (aligned to spec):

- `RolePlayAdaptiveStateService` semantic evidence computation and guardrails.
- `RolePlayEngineService` pipeline integration and diagnostics emission.
- `ScenarioSelectionService` ordering/candidate fit consumption of finalized evidence snapshot.
- `RPThemeModels`, `RolePlayAdaptiveState`, `RPThemeService` model/config and mapping enforcement.
- `RolePlayWorkspace.razor` debug telemetry rendering for semantic event trace and deltas.
- RolePlay test suites for mapping, fail-fast, cooldown/cap, corruption progression, lock regression, and end-to-end ranking/fit behavior.

## Phase 2 Preview (for /speckit.tasks)

Task generation should create slices in this order:

1. Contract/model updates for semantic telemetry and diagnostics reason codes.
2. Semantic mapping/confidence validation and no-fallback source resolution.
3. Cap/cooldown/lock constrained delta application.
4. Pipeline integration into ranking and candidate fit using one finalized snapshot.
5. Debug workspace telemetry rendering updates.
6. Unit/integration/regression/end-to-end tests mapped to SC-001..SC-006.
7. Final verification evidence: one active decision path, no fallback branches, explicit failure on missing/invalid config.

## Verification Mapping

- **SC-001** -> Debug telemetry tests + manual workspace trace output validation.
- **SC-002** -> Unit tests for invalid payload, unknown event, missing config, out-of-range confidence fail-fast behavior.
- **SC-003** -> Cap/cooldown repeated-adjacent-turn suppression tests.
- **SC-004** -> Blocked theme lock regression tests proving locked zero evidence.
- **SC-005** -> End-to-end ordering and candidate fit behavior change tests driven by semantic evidence.
- **SC-006** -> Manual debug runbook in quickstart with evidence checklist.

## Complexity Tracking

No constitution violations identified; no exception table required.
