# Implementation Plan: Finishing Move System – Catalog and Matrix Redesign

**Branch**: `007-finishing-move-catalog` | **Date**: 2026-05-05 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/007-finishing-move-catalog/spec.md`

## Summary

Add five band-aware reference catalog tables (Locations, Facial Types, Receptivity Levels, His Control Levels, Transition Actions) to the finishing move system. The existing `RPFinishingMoveMatrixRows` table is migrated: the `DominanceBand` column is renamed to `OtherManDominanceBand` and band ranges are updated to the three-tier standard (`0-29`/`30-59`/`60-100`). Runtime prompt assembly (climax phase) queries both the matched matrix row and all five catalogs filtered by the session's current adaptive stat bands, emitting a labeled prompt section for each eligible catalog type. All five catalogs are seeded automatically on first run. The UI gains five new management tabs in Theme Profiles.

## Technical Context

**Language/Version**: C# 13 / .NET 9  
**Primary Dependencies**: Microsoft.Data.Sqlite, Blazor Server, Serilog  
**Storage**: SQLite (project default; no exception)  
**Testing**: xUnit with NSubstitute (existing test project `DreamGenClone.Tests`)  
**Target Platform**: Windows desktop (local server)  
**Project Type**: Blazor Server web application (multi-project .NET solution)  
**Performance Goals**: Catalog tables are small (tens of entries); in-memory filtering is sufficient  
**Constraints**: All catalog filtering in-memory (matching existing matrix pattern); no cloud dependencies  
**Scale/Scope**: 5 new tables, ~60 total seed entries, 15 new service methods, 5 new seed services, 5 new UI tabs

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction (Domain ← Application ← Infrastructure ← Web)
- [x] Deterministic state transitions and JSON contract validation are test-covered (seed services tested; band filtering is deterministic)
- [x] Persistence uses SQLite by default (no exception introduced)
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

**Post-design re-check**: All gates still pass. No new cross-layer dependencies or external services introduced. The five new catalog entities follow the same Domain → Application interface → Infrastructure implementation → Web prompt assembly path as the existing matrix.

## Project Structure

### Documentation (this feature)

```text
specs/007-finishing-move-catalog/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── IRPThemeService-additions.md   # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (affected files)

```text
DreamGenClone.Domain/
└── RolePlay/
    └── RPThemeModels.cs                          # +5 new model classes; RPFinishingMoveMatrixRow.DominanceBand renamed

DreamGenClone.Application/
└── RolePlay/
    └── IRPThemeService.cs                        # +15 new method signatures

DreamGenClone.Infrastructure/
└── RolePlay/
    ├── RPThemeService.cs                         # +15 CRUD implementations; migration; schema DDL; updated matrix methods
    ├── FinishingMoveMatrixSeedService.cs         # Band range update; column rename
    ├── RPFinishLocationSeedService.cs            # NEW — ≥15 entries with category and band eligibility
    ├── RPFinishFacialTypeSeedService.cs          # NEW — ≥6 entries with physical cues
    ├── RPFinishReceptivityLevelSeedService.cs    # NEW — exactly 8 named levels
    ├── RPFinishHisControlLevelSeedService.cs     # NEW — exactly 3 control levels
    └── RPFinishTransitionActionSeedService.cs    # NEW — ≥6 transition actions

DreamGenClone.Web/
├── Program.cs                                    # +5 seed service registrations
└── Components/Pages/
    ├── ThemeProfiles.razor                       # +5 new tabs; matrix tab header/binding update
    └── RolePlayWorkspace.razor                   # MatchesFinishingMoveRow update; MatchesBandEligibility helper; 5 catalog prompt sections

DreamGenClone.Tests/
└── RolePlay/
    ├── FinishingMoveMatrixSeedServiceTests.cs    # Update for renamed column + new band ranges
    ├── RPFinishingMoveMatrixServiceTests.cs      # Update for renamed column
    ├── RPFinishLocationSeedServiceTests.cs       # NEW
    ├── RPFinishFacialTypeSeedServiceTests.cs     # NEW
    ├── RPFinishReceptivityLevelSeedServiceTests.cs # NEW
    ├── RPFinishHisControlLevelSeedServiceTests.cs # NEW
    └── RPFinishTransitionActionSeedServiceTests.cs # NEW
```

**Structure Decision**: Multi-project .NET solution (existing layout unchanged). New files follow `DreamGenClone.Infrastructure/RolePlay/` placement convention for seed services. No new projects introduced.
