# Implementation Plan: Wife Resistance & Cheating Motivation Gap

**Branch**: `001-wife-resistance-motivation` | **Date**: 2026-06-07 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-wife-resistance-motivation/spec.md`

## Summary

Add a real resistance counterweight to the Wife character so "we shouldn't" is
not just flavour text — the Wife genuinely resists escalation when her
configured resistance band says she should. A new **ResistanceProfile**
(mirroring the existing WillingnessProfile domain/persistence/service/UI
pattern) maps a target stat (Loyalty, by default) to resistance directive bands.
A **motivation score** computed from four profile-level inputs (Husband
Attentiveness, IntimacyAvailability; Wife SelfRespect; OtherMan
PersistencePastLimits) inflates the effective stat value before band lookup —
so marital neglect, sexual frustration, low self-worth, and persistent pursuit
all nudge the Wife toward a more permissive band, but never override her
Loyalty-anchored baseline.

Four new behavioral dimensions follow the existing code-defined catalog pattern
and flow through the CharacterBehavioralFrameGenerator into per-character HARD
CONSTRAINT prompt lines. Escalation guidance is made target-aware so it stops
pushing past a firm-resistance band and stops referencing the legacy "Tension"
stat. No new canonical stats. Existing sessions purged at cutover. All
thresholds from persisted ResistanceProfile bands.

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: ASP.NET Core Blazor Server, EF Core via SQLite, Serilog
**Storage**: SQLite — new `StatResistanceProfiles` table + new `SelectedResistanceProfileId` column on `RolePlayV2AdaptiveStates`
**Testing**: xUnit — existing test project `DreamGenClone.Tests`
**Target Platform**: Windows (local-first desktop web app)
**Project Type**: .NET layered web application (Web → Application → Domain; Infrastructure)
**Performance Goals**: Motivation score computation < 1ms (O(1) arithmetic + band lookup); no measurable impact on prompt-build latency
**Constraints**: No new canonical stats (FR-011); mirror WillingnessProfile pattern exactly; session purge at cutover (FR-010); no hardcoded RP thresholds (FR-012)
**Scale/Scope**: 1 new SQLite table, 1 new column, 4 new domain classes, 2 new behavioral dimensions × 2 roles (4 total), 3 new service interface+impl pairs, 1 new UI tab, ~10 files modified

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **Local-first runtime** — All resistance/motivation logic executes on the local machine. No cloud dependency.
- [x] **Module boundaries and adapter seams** — New types follow existing Domain/Application/Infrastructure/Web project boundaries. Dependency direction enforced by project references.
- [x] **.NET layered architecture** — Separate projects with explicit references. No new projects needed.
- [x] **Deterministic state transitions and JSON contract validation** — Motivation score formula is pure arithmetic. ResistanceProfile thresholds JSON-validated on save. Band lookup is O(1). All testable without live LLM.
- [x] **SQLite persistence** — `StatResistanceProfiles` table follows existing `StatWillingnessProfiles` schema pattern. Column on `RolePlayV2AdaptiveStates`.
- [x] **Serilog structured logging** — FR-014: Information-level logs on profile save/load and resistance directive resolution.
- [x] **Logging coverage across layers** — Profile service (Infrastructure), scenario guidance (Infrastructure), facade (Web) all emit Information logs.
- [x] **Log levels configurable** — FR-015: via appsettings.json without code changes.

## Project Structure

### Documentation (this feature)

```text
specs/001-wife-resistance-motivation/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── resistance-profile-api.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
└── StoryAnalysis/
    ├── StatResistanceProfile.cs          # NEW
    ├── BehavioralDimensionCatalog.cs     # MODIFY — 4 new dimensions
    ├── CharacterStatTextCatalog.cs       # MODIFY — new stat text entries (if needed)
    └── StatToDimensionMappings.cs        # MODIFY — drift rules for new dims
└── RolePlay/
    └── AdaptiveScenarioState.cs          # MODIFY — SelectedResistanceProfileId

DreamGenClone.Application/
└── StoryAnalysis/
    └── IStatResistanceProfileService.cs  # NEW

DreamGenClone.Infrastructure/
├── StoryAnalysis/
│   └── StatResistanceProfileService.cs   # NEW
├── Persistence/
│   ├── ISqlitePersistence.cs             # MODIFY — 5 new method signatures
│   └── SqlitePersistence.cs              # MODIFY — CREATE TABLE + UPSERT + loaders
└── RolePlay/
    ├── ScenarioGuidanceGenerator.cs      # MODIFY — BuildResistanceInterpretationAsync
    └── RolePlayStateRepository.cs        # MODIFY — column + save/load ordinal

DreamGenClone.Web/
├── Application/
│   ├── StoryAnalysis/
│   │   └── StoryAnalysisFacade.cs        # MODIFY — resistance passthrough methods
│   └── RolePlay/
│       ├── RolePlayContinuationService.cs # MODIFY — target-aware escalation, inject resistance directive
│       └── RolePlayAssistantPrompts.cs   # MODIFY — AppendResistanceDirective
├── Components/
│   └── Pages/
│       └── ThemeProfiles.razor           # MODIFY — new "Resistance" tab + @code
└── Program.cs                            # MODIFY — DI registration

DreamGenClone.Tests/
└── RolePlay/
    └── ResistanceProfileTests.cs         # NEW
```

**Structure Decision**: Single .NET 9 layered solution. Feature follows existing project boundaries exactly. No new projects. New domain types live alongside their willingness counterparts in `DreamGenClone.Domain/StoryAnalysis`. New service follows the `I*Service` → `*Service` → `Facade` passthrough pattern. UI tab clones the existing Willingness tab in `ThemeProfiles.razor`.

## Complexity Tracking

No violations. All constitution gates pass. Feature mirrors existing patterns exactly — zero architectural deviations.
