# Implementation Plan: Final Writing Instruction Consolidation

**Branch**: `001-final-writing-instruction` | **Date**: 2026-07-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-final-writing-instruction/spec.md`

## Summary

Consolidate all writing direction (Prose Style, Voice, Tone, Heat Level, Pacing, POV, immersion, word targets, action directives) into Slot 17 (Final Instruction) as the authoritative writing instruction. Slots 8 (WritingStyle) and 15 (IntensityPacing) become purely contextual/structural. Slot 12 (ThemeContract) stops emitting phase guidance. Phase guidance is renamed to "Scene Direction" and positioned relative to the Writing Instruction block in an order determined by research and validated through integration testing (manual checklist + automated scoring + subjective review). Prompt-facing labels are renamed to writer-standard terms (Prose Style, Heat Level, Voice, Scene Direction, Tone). The SteeringProfile data model gains configurable ImmersionDirective, ActionDirective, and per-variant WordTarget ranges (Character: WordTargetMin/Max; Narrative: NarrativeWordTargetMin/Max — intentionally longer). The NarrativeSettings data model gains separate Tone, Register, and Focus fields with legacy NarrativeTone retained for backward compatibility. The Atmospheric ToneProfile is moved to StyleProfiles (data migration). Sensual and Emotional ToneProfile descriptions are cleaned to heat-level-only language via a model-generated cleanup spec. UI editing of the new fields is in scope and sequenced last for dedicated-agent implementation. No existing-session migration — only new RP sessions are supported after the feature goes live.

## Technical Context

**Language/Version**: C# 12 / .NET 9
**Primary Dependencies**: ASP.NET Core Blazor (Web host), Microsoft.Data.Sqlite (persistence), Serilog (logging), xUnit (testing)
**Storage**: SQLite (`DreamGenClone.Web/data/dreamgenclone.dev.db`) — `StyleProfiles`, `ToneProfiles`, `Scenarios` (PayloadJson) tables
**Testing**: xUnit + FluentAssertions; contract tests in `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs`
**Target Platform**: Local Windows machine (local-first runtime per Constitution I)
**Project Type**: Layered .NET 9 web application (Web / Application / Domain / Infrastructure projects with enforced dependency direction)
**Performance Goals**: Prompt build < 500ms; no measurable latency regression from consolidation
**Constraints**: Hard Rule — no hardcoded fallbacks for RP engine values; fail-fast on missing required config; 17-slot architecture frozen per 001-rp-prompt-redesign spec contract
**Scale/Scope**: Single-user local app; ~6 ToneProfiles, ~1 StyleProfile (Sultry) currently; ~1 target scenario (135a9237) for narrative tone decomposition

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow) — feature is pure prompt-assembly + local DB schema/data changes; no cloud dependency introduced
- [x] Module boundaries and adapter seams are explicit and swappable — changes confined to existing `Web/Application/RolePlay/Prompts/Slots/*` + `Web/Domain/Scenarios/*` + `Domain/StoryAnalysis/*`; no new projects; existing 4-project layered architecture preserved
- [x] .NET layered architecture uses separate projects with enforced dependency direction — SteeringProfile (Domain), NarrativeSettings (Web/Domain), Slots (Web/Application); dependency direction unchanged
- [x] Deterministic state transitions and JSON contract validation are test-covered — SlotContractTests.cs updated for new expected strings; fail-fast errors for missing SteeringProfile fields are unit-testable
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale — StyleProfiles table gains columns via ALTER TABLE; Scenarios PayloadJson gains new NarrativeSettings fields (no schema change — JSON payload); ToneProfiles row removed for Atmospheric
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices — existing FinalInstructionSlot/IntensityPacingSlot/WritingStyleSlot already use `ILogger<T>`; new fail-fast paths emit structured errors
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths — slot WriteAsync methods already log at Debug; fail-fast paths log at Error/Critical
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes — existing appsettings configuration unchanged

**Gate Result**: PASS — no violations. No Complexity Tracking entries needed.

**Post-Design Re-check (Phase 1 complete)**: PASS — design adds fields to existing Domain/Web types (`SteeringProfile`, `NarrativeSettings`, `PromptBuildContext`, `ResolvedWritingStyleData`), extends the existing `StyleProfiles` SQLite table via ALTER TABLE, adds a new `StyleProfiles` row for Atmospheric, and updates slot output in existing slot classes. No new projects, no cloud dependency, no non-SQLite storage, Serilog logging preserved, deterministic fail-fast paths (FR-006) are unit-testable via `SlotContractTests.cs`. No new violations introduced.

## Project Structure

### Documentation (this feature)

```text
specs/001-final-writing-instruction/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   ├── slot-17-output-contract.md
│   └── terminology-mapping.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
└── StoryAnalysis/
    └── SteeringProfile.cs          # Phase 1: add 6 new fields

DreamGenClone.Web/
├── Domain/
│   └── Scenarios/
│       └── NarrativeSettings.cs    # Phase 1: add Tone, Register, Focus
├── Application/
│   └── RolePlay/
│       └── Prompts/
│           ├── PromptBuildContext.cs        # Phase 3: add ResolvedNarrativeToneData, extend ResolvedWritingStyleData
│           ├── RolePlayPromptBuilder.cs     # Phase 3: resolve new fields with fail-fast
│           └── Slots/
│               ├── WritingStyleSlot.cs       # Phase 3: remove writing direction
│               ├── IntensityPacingSlot.cs    # Phase 3: remove heat/pacing; keep positions
│               ├── ThemeContractSlot.cs      # Phase 3: confirm phase guidance removed
│               └── FinalInstructionSlot.cs   # Phase 3: consolidated 9-component output
├── Components/
│   └── Pages/
│       └── [Style Profile management page]   # Phase 4 (UI): add new field editors
│       └── [Scenario narrative settings]     # Phase 4 (UI): add Tone/Register/Focus editors
└── data/
    └── dreamgenclone.dev.db                  # Phases 1-2: schema + data migrations

DreamGenClone.Tests/
└── RolePlay/
    └── Prompts/
        └── SlotContractTests.cs              # Phase 3: update expected strings

artifacts/tmp/dbquery/
└── queries/                                  # SQL files for data inspection/migration
```

**Structure Decision**: Existing 4-project layered .NET 9 architecture (Domain, Application, Infrastructure, Web) with enforced dependency direction. No new projects. Changes confined to Domain (SteeringProfile), Web/Domain (NarrativeSettings), Web/Application (Slots, PromptBuildContext, Builder), Web/Components (UI pages), and Tests. SQLite DB at `DreamGenClone.Web/data/dreamgenclone.dev.db`. The `artifacts/tmp/dbquery` console project is used for schema migrations and data inspection.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution Check violations. No complexity tracking entries needed.
