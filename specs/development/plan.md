# Implementation Plan: B-042 — Unify Character Stats Profiles with Encounter Behavior Profiles

**Branch**: `development` | **Date**: 2026-05-27 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/development/spec.md`

## Summary

Retire the two separate character profile systems (`BaseStatProfile` and `HusbandAwarenessProfile`) and replace them with a single unified `CharacterProfile` entity that carries both canonical character stats (Desire, Restraint, Tension, Connection, Dominance, Loyalty, SelfRespect) and role-specific encounter behavioral dimensions. Dimensions are defined in a code-side `BehavioralDimensionCatalog` with 4 tier levels per dimension producing LLM directive text. All UI surfaces (profile CRUD, session creation, workspace adaptive panel) are updated in one pass. The RP continuation prompt receives behavioral frames from every character with a bound profile, not just the husband. 25 unified seeded archetypes replace the previous two archetype sets.

## Technical Context

**Language/Version**: C# / .NET 9  
**Primary Dependencies**: Blazor Server, Microsoft.Data.Sqlite, Serilog, xUnit  
**Storage**: SQLite (`DreamGenClone.Web/data/dreamgenclone.dev.db`) — all new tables and migrations in `SqlitePersistence.cs`  
**Testing**: xUnit (`DreamGenClone.Tests`) — unit tests for dimension catalog, frame generation, session state migration, and service CRUD  
**Target Platform**: Windows local desktop app (Blazor Server self-hosted)  
**Project Type**: Web application (Blazor Server)  
**Performance Goals**: No new performance requirements; profile CRUD and live preview must feel responsive (< 50ms for UI state updates, which is synchronous in Blazor)  
**Constraints**: No external network calls for profile/dimension logic; all operations are local SQLite reads/writes  
**Scale/Scope**: ~25 seed profiles per table, ~3 roles, ~6–7 dimensions per role; no pagination needed

## Constitution Check

*Pre-design GATE: all items pass.*

- [x] Local-first runtime preserved — all new services operate on local SQLite; no cloud dependency introduced
- [x] Module boundaries and adapter seams are explicit and swappable — `ICharacterProfileService` interface in Application; `ISqlitePersistence` extension methods for new table operations; `IBehavioralFrameGenerator` interface for prompt injection
- [x] .NET layered architecture uses separate projects with enforced dependency direction — `BehavioralDimensionCatalog` in Domain; `ICharacterProfileService` in Application; `CharacterProfileService` + `CharacterBehavioralFrameGenerator` in Infrastructure; UI updates in Web
- [x] Deterministic state transitions and JSON contract validation are test-covered — existing session round-trip tests updated; new tests for dimension catalog tier resolution and frame generation
- [x] Persistence uses SQLite — new `CharacterProfiles` table; `AdaptiveScenarioState.CharacterEncounterProfileIds` serialized as JSON column in `RolePlayV2AdaptiveStates`
- [x] Serilog is the primary logging framework — all new services use Serilog `ILogger<T>`; no `Console.WriteLine` or `Debug.WriteLine`
- [x] Logging coverage exists across layers — Information logs at service entry/exit for CRUD operations and frame generation; Warning logs for missing profiles
- [x] Log levels are externally configurable — no hardcoded log level checks; standard Serilog configuration via `appsettings.json`

*Post-design re-check: passes — design artifacts confirm no violations.*

## Project Structure

### Documentation (this feature)

```text
specs/development/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — resolved unknowns
├── data-model.md        # Phase 1 — entity design and migration plan
├── quickstart.md        # Phase 1 — implementation order and gotchas
├── contracts/           # Phase 1 — interface and contract definitions
│   ├── ICharacterProfileService.md
│   ├── IBehavioralFrameGenerator.md
│   └── ScenarioGuidanceContracts.md
└── tasks.md             # Phase 2 output (speckit.tasks — NOT created by speckit.plan)
```

### Source Code (affected files by layer)

```text
DreamGenClone.Domain/
├── RolePlay/
│   └── AdaptiveScenarioState.cs          # MODIFY: HusbandAwarenessProfileId → CharacterEncounterProfileIds
├── StoryAnalysis/
│   ├── CharacterProfile.cs               # NEW: unified entity
│   ├── BehavioralDimensionCatalog.cs     # NEW: static dimension + tier text definitions
│   ├── BaseStatProfile.cs                # RETIRE (keep for migration compatibility, mark Obsolete)
│   └── HusbandAwarenessProfile.cs        # RETIRE (keep for migration compatibility, mark Obsolete)

DreamGenClone.Application/
├── RolePlay/
│   └── RolePlayContracts.cs              # MODIFY: ScenarioGuidanceRequest + ScenarioGuidanceOutput
├── StoryAnalysis/
│   ├── Abstractions/
│   │   ├── ICharacterProfileService.cs   # NEW: unified CRUD interface
│   │   └── IBehavioralFrameGenerator.cs  # NEW: frame generation interface
│   └── Models/
│       └── ScenarioEngineContracts.cs    # MODIFY: HusbandAwarenessProfileId → CharacterEncounterProfileIds

DreamGenClone.Infrastructure/
├── RolePlay/
│   ├── RolePlayEngineService.cs          # MODIFY: write CharacterEncounterProfileIds at session creation
│   ├── ScenarioGuidanceGenerator.cs      # MODIFY: BuildCharacterBehavioralFramesAsync replaces husband-only method
│   └── SemanticInteractionAnalysisJobHandler.cs  # MODIFY: preserve CharacterEncounterProfileIds
├── StoryAnalysis/
│   ├── CharacterProfileService.cs        # NEW: replaces BaseStatProfileService + HusbandAwarenessProfileService
│   ├── CharacterBehavioralFrameGenerator.cs  # NEW: generates frame text from BehavioralDimensionCatalog
│   ├── ScenarioGuidanceContextFactory.cs # MODIFY: CharacterEncounterProfileIds flow
│   ├── BaseStatProfileService.cs         # RETIRE (keep briefly during migration)
│   └── HusbandAwarenessProfileService.cs # RETIRE (keep briefly during migration)
└── Persistence/
    └── SqlitePersistence.cs              # MODIFY: CharacterProfiles table, migration logic, ISqlitePersistence extension

DreamGenClone.Web/
├── Application/RolePlay/
│   ├── RolePlayContinuationService.cs    # MODIFY: multi-character frame injection
│   └── RolePlayAssistantPrompts.cs       # MODIFY: multi-character early frame injection
├── Components/Pages/
│   ├── ThemeProfiles.razor               # MODIFY: replace two tabs with unified "character-profiles" tab
│   └── RolePlayCreate.razor              # MODIFY: single profile picker per character
├── Domain/RolePlay/
│   └── RolePlayWorkspace.razor           # MODIFY: per-character profile switcher in adaptive panel
└── Program.cs                            # MODIFY: DI registrations

DreamGenClone.Tests/
├── RolePlay/
│   ├── AdaptiveScenarioStateV2RoundTripTests.cs   # MODIFY: update HusbandAwarenessProfileId refs
│   ├── ScenarioGuidanceGeneratorTests.cs          # MODIFY: multi-character frame tests
│   ├── SessionThemeSelectionsTests.cs             # MODIFY: update assertion
│   └── RolePlaySessionBaseStatInitializationTests.cs  # MODIFY: use CharacterProfile
└── StoryAnalysis/
    └── BehavioralDimensionCatalogTests.cs         # NEW: tier resolution + frame text tests
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
