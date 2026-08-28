# Implementation Plan: RP Prompt Redesign

**Branch**: `001-rp-prompt-redesign` | **Date**: 2026-07-17 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-rp-prompt-redesign/spec.md`
**Design Reference**: [specs/Planning/rp-prompt-improvement-plan.md](../Planning/rp-prompt-improvement-plan.md)

## Summary

Replace the ~1,400-line procedural `BuildPromptAsync` method in `RolePlayContinuationService` with a composable, slot-based template architecture. The new architecture defines exactly 17 ordered prompt slots distributed across three attention zones (Primacy/Context/Recency), with actor-aware content filtering, configurable character-level token budget enforcement, deduplication of all directive categories, tiered history compression, Narrative as a first-class prompt variant, and a World State slot ready for B-062. All compression thresholds, `MaxPromptChars`, and phase Rule-of-Thumb text become UI-backed persisted configuration with fail-fast diagnostics — no hardcoded runtime defaults.

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: ASP.NET Core (Blazor Server), Microsoft.Extensions.Logging, Serilog, Microsoft.Data.Sqlite, Microsoft.Extensions.Options
**Storage**: SQLite (default per Constitution VIII) — `Sessions` table (PayloadJson blob) + dedicated V2 tables (`RolePlayV2EncounterSummaries`, etc.). New session-level config columns added via idempotent `ALTER TABLE` migrations.
**Testing**: xUnit + FluentAssertions (`DreamGenClone.Tests` project). Existing RP test suite has 70+ test files in `DreamGenClone.Tests/RolePlay/`.
**Target Platform**: Local Windows desktop runtime (Constitution I — local-first, private).
**Project Type**: Layered .NET 9 web application (Web host + Application + Domain + Infrastructure + Tests).
**Performance Goals**: Prompt build time must not increase by more than 20% vs. current baseline (SC-007). Target prompt size <= 35,000 chars (SC-006), >=30% reduction vs. ~50K baseline (SC-001).
**Constraints**: No hardcoded runtime defaults for any RP behavior control (repo Hard Rule). All thresholds UI-backed persisted config with fail-fast. No fallback branches. Each slot independently unit-testable (FR-036, SC-008). 17-slot architecture is frozen by spec contract.
**Scale/Scope**: Replaces one ~1,400-line method + 13 coordinator injectors + inline duplicate blocks. Adds ~17 slot implementations, 1 builder, 1 context record, 1 actor-profile resolver, 5 actor-profile types, 1 budget enforcer, 1 trim orchestrator, schema migrations for `MaxPromptChars` + compression thresholds + phase Rule-of-Thumb config, and enrichment-prompt rewrite for encounter memory.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **Local-first runtime preserved** — All prompt building executes locally; no cloud dependency introduced. The only existing cloud touchpoint (LLM completion call) is unchanged.
- [x] **Module boundaries and adapter seams are explicit and swappable** — `IPromptSlot` is a new explicit seam; each slot is independently swappable. `RolePlayPromptBuilder` replaces the monolithic method without crossing existing project boundaries.
- [x] **.NET layered architecture uses separate projects with enforced dependency direction** — Slots live in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/` (Application layer). Domain types (`PromptZone`, `ActorProfileKind`) live in `DreamGenClone.Domain/RolePlay/`. Infrastructure config (`RolePlayPromptOptions`) lives in `DreamGenClone.Infrastructure/Configuration/`. No dependency-direction violations.
- [x] **Deterministic state transitions and JSON contract validation are test-covered** — Each slot is a pure function of `PromptBuildContext` (FR-036, SC-008). Budget enforcement is deterministic given the same input. New contracts documented in `/contracts/`.
- [x] **Persistence uses SQLite by default** — New config (`MaxPromptChars`, compression thresholds, phase Rule-of-Thumb) persisted to SQLite via `Sessions` columns + a new `PhaseRuleOfThumb` config table. No alternate store introduced.
- [x] **Serilog is the primary logging framework with .NET 9 structured logging best practices** — Builder and slots emit structured Serilog logs (FR-037, FR-030) with `SessionId`, `Actor`, `Phase`, `Chars`, `SlotsFired` properties.
- [x] **Logging coverage exists across layers/components/services with Information logs for major call paths** — `RolePlayPromptBuilder.BuildAsync` logs at Information per build; trim warnings at Warning; slot-level diagnostics at Debug.
- [x] **Log levels are externally configurable, including Verbose diagnostics without code changes** — Slot-level diagnostics use `_logger.LogDebug`/`LogTrace`; appsettings already controls Serilog minimum levels.

**Gate Result**: PASS — no violations. No complexity tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/001-rp-prompt-redesign/
├── plan.md              # This file
├── research.md          # Phase 0 output — resolves all NEEDS CLARIFICATION
├── data-model.md        # Phase 1 output — entities, fields, validation, state
├── quickstart.md        # Phase 1 output — build/run/test/verify commands
├── contracts/
│   ├── prompt-slot-contract.md       # IPromptSlot contract + 17-slot registry
│   ├── prompt-build-context.md       # PromptBuildContext shape consumed by every slot
│   ├── actor-profile-contract.md     # 5 actor profiles + resolution rules
│   ├── token-budget-contract.md      # MaxPromptChars + trim priority order
│   └── encounter-enrichment-contract.md  # 6-dimension enrichment prompt I/O
└── tasks.md             # Phase 2 output (/speckit.tasks command — NOT created here)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/RolePlay/
├── PromptZone.cs                    # enum { A, B, C }
├── ActorProfileKind.cs              # enum { Player, NpcPresent, NpcNonPresent, Narrative, Custom }
├── PromptSlotId.cs                  # enum (17 slots + WorldState conditional sub-slot)
└── PromptVariant.cs                 # enum { Character, Narrative }

DreamGenClone.Infrastructure/Configuration/
└── RolePlayPromptOptions.cs         # MaxPromptChars recommended value, compression threshold keys
                                       (actual values are session-scoped or phase-scoped persisted config)

DreamGenClone.Web/Application/RolePlay/Prompts/
├── IPromptSlot.cs                   # slot contract
├── PromptBuildContext.cs            # immutable record consumed by every slot
├── ActorProfile.cs                  # resolved profile record
├── ActorProfileResolver.cs          # ContinueAsActor + PromptIntent → ActorProfile
├── PromptBudgetEnforcer.cs          # MaxPromptChars enforcement + trim priority
├── RolePlayPromptBuilder.cs         # orchestrates 17 slots in zone/order
├── Slots/
│   ├── SceneAnchorSlot.cs           # Slot 1  (Zone A)
│   ├── ActorAssignmentSlot.cs       # Slot 2  (Zone A)
│   ├── TurnContextSlot.cs           # Slot 3  (Zone A)
│   ├── SceneLocationLockSlot.cs     # Slot 4  (Zone A)
│   ├── WorldStateSlot.cs            # Slot 4a (Zone A, conditional — B-062)
│   ├── CharacterDataSlot.cs         # Slot 5  (Zone B, trimmable)
│   ├── ScenarioContextSlot.cs       # Slot 6  (Zone B, trimmable)
│   ├── CurrentLocationSlot.cs       # Slot 7  (Zone B, trimmable)
│   ├── WritingStyleSlot.cs          # Slot 8  (Zone B, last-resort trim)
│   ├── InteractionHistorySlot.cs    # Slot 9  (Zone B, trimmable — tiered)
│   ├── SessionMemorySlot.cs         # Slot 10 (Zone B, trimmable — 3-tier)
│   ├── SceneContinuityAnchorSlot.cs # Slot 11 (Zone B, low trim)
│   ├── ThemeContractSlot.cs         # Slot 12 (Zone C)
│   ├── BehavioralFramesSlot.cs      # Slot 13 (Zone C, non-present trimmable)
│   ├── ScenarioGuidanceSlot.cs      # Slot 14 (Zone C, low trim)
│   ├── IntensityPacingSlot.cs       # Slot 15 (Zone C)
│   ├── UserDirectionSlot.cs         # Slot 16 (Zone C, conditional)
│   └── FinalInstructionSlot.cs      # Slot 17 (Zone C)
└── Legacy/
    └── (deleted) BuildPromptAsync path in RolePlayContinuationService.cs

DreamGenClone.Web/Application/RolePlay/
├── RolePlayContinuationService.cs   # refactored — delegates to RolePlayPromptBuilder
├── EncounterSummaryJobHandler.cs    # rewritten enrichment prompt (6 dimensions)
└── RolePlayEngineService.cs         # TryDetectEncounterBoundaryAsync — secondary signals

DreamGenClone.Web/Domain/RolePlay/
└── RolePlaySession.cs               # +MaxPromptChars, +ContextWindowTurns, +compression threshold fields

DreamGenClone.Infrastructure/Persistence/
└── SqlitePersistence.cs             # idempotent ALTER TABLE migrations for new columns
                                       + new PhaseRuleOfThumb config table

DreamGenClone.Web/Program.cs         # DI: register IPromptSlot implementations + RolePlayPromptBuilder

DreamGenClone.Tests/RolePlay/Prompts/
├── SlotContractTests.cs             # one test per slot (FR-036, SC-008)
├── PromptBuilderTests.cs            # end-to-end build, budget, dedup, ordering
├── ActorProfileResolverTests.cs     # 5 profiles × variant matrix
├── PromptBudgetEnforcerTests.cs     # trim priority, never-trim invariants, fail-fast
├── EncounterEnrichmentPromptTests.cs # 6-dimension capture
└── LegacyRemovalTests.cs            # asserts no residual BuildPromptAsync code path
```

**Structure Decision**: Layered .NET 9 solution per Constitution II. New prompt code lives in `DreamGenClone.Web/Application/RolePlay/Prompts/` (Application layer) because it depends on `RolePlaySession`, `ScenarioService`, `RPThemeService`, and other Application-layer services. Pure domain enums (`PromptZone`, `ActorProfileKind`, `PromptSlotId`, `PromptVariant`) live in `DreamGenClone.Domain/RolePlay/`. Config options live in `DreamGenClone.Infrastructure/Configuration/`. This preserves the existing dependency direction (Web -> Application -> Domain <- Infrastructure) without introducing a new project.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations — table intentionally empty.
