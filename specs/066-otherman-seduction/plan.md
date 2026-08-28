# Implementation Plan: OtherMan Seduction Archetype

**Branch**: `066-otherman-seduction` | **Date**: 2026-08-11 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/066-otherman-seduction/spec.md`

## Summary

Add 8 genre-grounded seduction archetypes (Charmer, Competent, Confidante, Tease, Protector, Dominant, Mysterious, Situational) that control *how* the OtherMan character seduces in roleplay sessions. Archetypes are defined in code as a static catalog (like `SteerRoleIntentCatalog`), assigned per-character on the `Character` entity (zero-to-many, stored in scenario JSON in SQLite), and injected into continuation prompts via the existing `CharacterDataSlot` (Slot 5, Zone B) — appended to the character's role intent. The `SteerRoleIntentCatalog` OtherMan TOWARDS intent is updated with research-backed seduction patterns to serve as the role-level fallback. This feature is complementary to B-077 (gap-aware steering): B-077 defines *what outcome* to steer toward, this feature defines *how* the OtherMan behaves to get there.

**Priorities**: P1 = catalog + character data model + updated fallback; P2 = continuation prompt injection; P3 = scenario editor UI.

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: ASP.NET Core (Blazor Server), Microsoft.Extensions.Logging, Serilog, Microsoft.Data.Sqlite
**Storage**: SQLite (default per Constitution VIII). `SeductionArchetypes` list on `Character` is stored as a JSON array within the scenario's character JSON blob in SQLite (same pattern as `LocationAffinities`, `BaseStats`).
**Testing**: xUnit + FluentAssertions (`DreamGenClone.Tests` project). New tests in `DreamGenClone.Tests/RolePlay/Prompts/` and `DreamGenClone.Tests/StoryAnalysis/`.
**Target Platform**: Local Windows desktop runtime (Constitution I — local-first, private).
**Project Type**: Layered .NET 9 web application (Web host + Application + Domain + Infrastructure + Tests).
**Performance Goals**: Prompt build time delta must be negligible — archetype guidance text is a short pre-computed string appended to existing character role intent output. No new I/O or computation per turn beyond string lookup.
**Constraints**: No hardcoded runtime defaults for any RP behavior (repo Hard Rule). Archetype guidance only applies when `character.Role == "OtherMan"`. The `SteerRoleIntentCatalog` OtherMan TOWARDS entry is the single, unambiguous fallback path (FR-006). No duplicate source-selection logic across services.
**Scale/Scope**: Adds 1 static catalog class (~8 entries × ~150 chars each), 1 property on `Character` (`List<string> SeductionArchetypes`), ~10-line change to `CharacterDataSlot.AppendCharacterRoleIntents()`, updated `SteerRoleIntentCatalog` OtherMan TOWARDS text, and a multi-select UI component in the scenario editor character settings. No new project, no new table, no new slot.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **Local-first runtime preserved** — Archetype catalog is code-defined, prompt injection is local string building. No cloud dependency introduced.
- [x] **Module boundaries and adapter seams are explicit and swappable** — `SeductionArchetypeCatalog` is a new Domain-layer static catalog (same seam as `SteerRoleIntentCatalog`). Prompt injection extends `CharacterDataSlot.WriteAsync()` without introducing new interfaces.
- [x] **.NET layered architecture uses separate projects with enforced dependency direction** — Catalog in `DreamGenClone.Domain/StoryAnalysis/` (Domain layer, no dependencies). Character property in `DreamGenClone.Web/Domain/Scenarios/Character.cs` (Web Domain). Prompt injection in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/CharacterDataSlot.cs` (Application layer). No dependency-direction violations.
- [x] **Deterministic state transitions and JSON contract validation are test-covered** — Archetype catalog lookup is a pure function of `(archetypeId)` returning deterministic prose text. CharacterDataSlot output is deterministic given same archetype list + context. Both unit-testable without live model calls.
- [x] **Persistence uses SQLite by default** — `SeductionArchetypes` list persisted within existing scenario JSON character blob in SQLite. No new table needed; no alternate store introduced.
- [x] **Serilog is the primary logging framework with .NET 9 structured logging best practices** — CharacterDataSlot already emits structured Debug logs. Archetype guidance emission adds `SeductionArchetypes` count to existing log context.
- [x] **Logging coverage exists across layers/components/services with Information logs for major call paths** — Archetype guidance injection is a sub-path of the existing CharacterDataSlot (which logs per build). No new major call path.
- [x] **Log levels are externally configurable, including Verbose diagnostics without code changes** — Archetype-specific logging uses existing slot-level Debug/Trace levels.

**Gate Result**: PASS — no violations. No complexity tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/066-otherman-seduction/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output — resolves design decisions
├── data-model.md        # Phase 1 output — entities, fields, validation
├── quickstart.md        # Phase 1 output — build/run/test/verify commands
├── contracts/           # Phase 1 output
│   └── seduction-archetype-catalog-contract.md  # Catalog shape + injection contract
└── tasks.md             # Phase 2 output (/speckit.tasks command — NOT created here)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/StoryAnalysis/
├── SeductionArchetypeCatalog.cs   # NEW: 8 archetypes as static catalog (domain records + static class)
└── SteerRoleIntentCatalog.cs      # MODIFIED: updated OtherMan TOWARDS intent text

DreamGenClone.Web/Domain/Scenarios/
└── Character.cs                   # MODIFIED: +SeductionArchetypes List<string> property

DreamGenClone.Web/Application/RolePlay/Prompts/Slots/
└── CharacterDataSlot.cs           # MODIFIED: AppendCharacterRoleIntents() appends archetype guidance for OtherMan

DreamGenClone.Web/Components/Pages/
└── ScenarioEditor.razor           # MODIFIED: P3 — add archetype multi-select in character settings panel

DreamGenClone.Tests/
├── StoryAnalysis/
│   └── SeductionArchetypeCatalogTests.cs  # NEW: verify all 8 entries, lookup, idempotency
└── RolePlay/Prompts/
    └── CharacterDataSlotTests.cs          # MODIFIED: add archetype injection test cases
```

**Structure Decision**: Layered .NET 9 solution per Constitution II. The `SeductionArchetypeCatalog` is a pure domain concept (no dependencies, no I/O) → lives in `DreamGenClone.Domain/StoryAnalysis/` alongside the existing `SteerRoleIntentCatalog`. The `Character.SeductionArchetypes` property is a scenario domain entity → lives in `DreamGenClone.Web/Domain/Scenarios/Character.cs`. Prompt injection modification is Application-layer orchestration → lives in `CharacterDataSlot.cs`. No new project needed. This mirrors the exact layering of the existing role-intent + character-data pattern.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — table intentionally empty.
