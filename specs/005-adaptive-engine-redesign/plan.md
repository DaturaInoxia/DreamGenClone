# Implementation Plan: Adaptive Engine Redesign

**Branch**: `005-adaptive-engine-redesign` | **Date**: 2026-04-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/005-adaptive-engine-redesign/spec.md`

## Summary

Redesign the adaptive engine to replace hardcoded theme scoring with a data-driven Theme Catalog persisted in SQLite. Introduce four configurable profile layers (Tone, Style, Theme, Catalog) that feed into a scenario-seeding pipeline, profile-driven style resolution, and per-interaction affinity scoring. Rename stats (Arousal→Desire, Inhibition→Restraint, Trust→Connection, Agency→Dominance) and RankingProfile→ThemeProfile across the full stack.

## Technical Context

**Language/Version**: C# / .NET 9 / Blazor Server
**Primary Dependencies**: Microsoft.Data.Sqlite, Serilog, System.Text.Json
**Storage**: SQLite (via SqlitePersistence.cs — single file, direct ADO.NET, no ORM)
**Testing**: xUnit + FluentAssertions
**Target Platform**: Windows desktop (local-first, single-user)
**Project Type**: Blazor Server web application (modular layered architecture)
**Performance Goals**: Interactive UI response; keyword scoring is lightweight (string.Contains per keyword, capped at 12 per theme)
**Constraints**: Local-first single-user; no cloud dependency. All state persisted in SQLite
**Scale/Scope**: Single user, ~10 theme catalog entries, ~5 profile types, ~10 scenarios, sessions with ~50 interactions

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [x] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default — no exceptions in this feature
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

**Notes**: This feature adds no new external dependencies, no cloud services, no new persistence backends. All changes operate within existing module boundaries. New ThemeCatalogService follows the same SqlitePersistence pattern as existing services.

## Project Structure

### Documentation (this feature)

```text
specs/005-adaptive-engine-redesign/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
├── StoryAnalysis/
│   ├── RankingProfile.cs          → rename to ThemeProfile.cs
│   ├── RankingCriterion.cs        → rename ThemePreference class (file may also rename)
│   └── ThemeCatalogEntry.cs       ← NEW domain model
│
DreamGenClone.Application/
├── StoryAnalysis/
│   ├── AdaptiveStatCatalog.cs     → stat renames + legacy mapping extension
│   ├── IRolePlayAdaptiveStateService.cs → extended interface (SeedFromScenarioAsync)
│   └── IThemeCatalogService.cs    ← NEW interface
│
DreamGenClone.Infrastructure/
├── StoryAnalysis/
│   └── ThemeCatalogService.cs     ← NEW service implementation
├── Persistence/
│   └── SqlitePersistence.cs       → new ThemeCatalog table, RankingProfiles→ThemeProfiles migration,
│                                    StyleProfiles column additions, new persistence methods
│
DreamGenClone.Web/
├── Application/RolePlay/
│   ├── RolePlayAdaptiveStateService.cs  → remove hardcoded catalog, inject IThemeCatalogService,
│   │                                      SeedFromScenarioAsync, affinity/stat scoring
│   ├── RolePlayStyleResolver.cs         → profile-driven escalation, MustHave/HardDealBreaker gates
│   ├── RolePlayEngineService.cs         → inject new deps, call seed, thread profiles
│   ├── RolePlayContinuationService.cs   → rename references
│   ├── RolePlayAssistantService.cs      → context line rename
│   └── RolePlayAssistantPrompts.cs      → terminology updates
├── Application/Scenarios/
│   └── ScenarioAssistantPrompts.cs      → seeding guidance
├── Domain/RolePlay/
│   ├── RolePlaySession.cs               → SelectedRankingProfileId → SelectedThemeProfileId
│   └── RolePlayAdaptiveState.cs         → add Blocked + SuppressedHitCount to ThemeTrackerItem
├── Domain/Scenarios/
│   └── Scenario.cs                      → DefaultRankingProfileId → DefaultThemeProfileId
├── Components/
│   ├── RankingProfiles.razor            → rename to ThemeProfiles.razor, add Theme Catalog tab
│   ├── RolePlayWorkspace.razor          → rename references, SuppressedHitCount in debug panel
│   └── ScenarioEditor.razor             → rename references
│
DreamGenClone.Tests/
├── StoryAnalysis/
│   ├── ThemeCatalogServiceTests.cs      ← NEW
│   └── ScenarioSeedAdaptiveStateTests.cs ← NEW
│   └── StyleProfileAffinityTests.cs     ← NEW
│   └── StyleResolverProfileDrivenTests.cs ← NEW
├── RolePlay/
│   ├── RolePlayAdaptiveStateServiceTests.cs → update stat names, mock IThemeCatalogService
│   ├── RolePlayAdaptiveProfilesTests.cs     → rename references
│   └── RolePlaySessionBaseStatInitializationTests.cs → update stat names
```

**Structure Decision**: No new projects. All changes fit within the existing 5-project layered architecture (Domain, Application, Infrastructure, Web, Tests). ThemeCatalogEntry goes in Domain, IThemeCatalogService in Application, ThemeCatalogService in Infrastructure — matching the existing pattern for StyleProfile, ToneProfile, etc.

## Complexity Tracking

No constitution violations. No complexity justifications needed.
