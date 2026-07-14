# Implementation Plan: Context-Aware Actor Selection

**Branch**: `001-context-aware-actor-selection` | **Date**: 2026-07-14 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-context-aware-actor-selection/spec.md`

## Summary

Fix the disabled location detection by replacing the regex-based synchronous detector with an LLM-driven, background-enqueued location detection job (no regex fallback). Build on the resulting `CurrentSceneLocation` to gate character availability (Required/Preferred/Excluded affinities, optional TimeOfDay restriction, multiple entries per character-location allowed). Track time-of-day via keyword detection with manual override. Replace the existing recency-only sort in `ResolveSceneContinueActorsAsync` with a deterministic scoring base path (always runs) and an optional LLM reordering step (`RolePlayActorSelection`) gated by a composite context-change fingerprint. Add per-character turn overrides (auto-participate, response priority 0–100 additive boost, preferred position hint) and a configurable batch size (already on `RolePlaySession.SceneContinueBatchSize`). Extend `SemanticEventRecord` with an `ActorName` column and run a one-time idempotent C# startup backfill via a `RolePlayInteractions.ActorName` JOIN.

Technical approach follows the existing `SemanticEventInferenceService` + `SemanticBackgroundJobQueue` + `IBackgroundJobHandler` infrastructure. No-fallback compliance: `ModelResolutionException` → `Success = false`, prior state preserved and logged.

## Technical Context

**Language/Version**: C# 12 / .NET 9 (Blazor Server + interactive server components)
**Primary Dependencies**: Blazor Server (`DreamGenClone.Web`), SQLite via `Microsoft.Data.Sqlite`, Serilog, `IOptions<T>` configuration, existing `SemanticBackgroundJobQueue`/`IBackgroundJobHandler` infrastructure, existing `ICompletionClient` + `IModelResolutionService` model boundary
**Storage**: SQLite (`dreamgenclone.dev.db`) — feature persists via the already-idempotent `EnsureAdaptiveStateSchemaAsync` migration pattern in `RolePlayStateRepository.cs`
**Testing**: xUnit (`DreamGenClone.Tests`) — new tests under `DreamGenClone.Tests/RolePlay/` for location detection, time-of-day, availability resolver, scoring, actor selection, overrides, backfill
**Target Platform**: Windows local desktop runtime (single-user Blazor Server)
**Project Type**: Web application (Blazor Server) with layered .NET solution (Web/Application/Domain/Infrastructure)
**Performance Goals**: AI actor selection ≤ 5s for ≤ 10 candidates (SC-005); scoring-only path ≤ 200 ms (SC-006); background location detection MUST NOT block foreground interaction generation
**Constraints**: No silent fallback paths (repo no-fallback rules); all scoring weights are internal `const` (not user-tunable RP behavior); no regex location-detection path remains; one-turn lag on location detection is accepted (eventually consistent)
**Scale/Scope**: Typical scenarios: 5 characters × 3 locations; up to 10 candidates per selection call; one session per local user

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow) — LLM calls via OpenAI-compatible `ICompletionClient`; local providers (Ollama/LM Studio) supported; location/actor AI failure does not block core story generation (scoring base path or unchanged state)
- [x] Module boundaries and adapter seams are explicit and swappable — new services `ILocationDetectionService` / `IActorSelectionService` in `DreamGenClone.Web/Application/RolePlay/`; new domain types in `DreamGenClone.Domain/RolePlay/`; new job handler registered via `IBackgroundJobHandler`; LLM access via existing `ICompletionClient`/`IModelResolutionService` boundary
- [x] .NET layered architecture uses separate projects with enforced dependency direction — domain enums/types land in `DreamGenClone.Domain`; persistence migrations stay in `DreamGenClone.Infrastructure`; orchestration in `DreamGenClone.Web/Application`; UI in `DreamGenClone.Web/Components`
- [x] Deterministic state transitions and JSON contract validation are test-covered — scoring is deterministic; AI JSON responses validated before mutation; cache fingerprint deterministic; backfill idempotency test-covered
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale — all new persisted fields use SQLite via existing `RolePlayStateRepository` migration pattern (FR-016)
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices — new services emit `Information`-level logs for REQUEST/RESPONSE/PARSED/FAILED transitions with `SessionId`/`Function`/`ElapsedMs`/`Source` enrichment; `LogDebug` for cache hits and skips (FR-010 / FR-017 / FR-018)
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths — location detection job completion, actor selection source (AI/Cache/Scoring/Fallback), availability resolution, cache invalidation events
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes — uses existing Serilog configuration; `Debug` events surface via existing `RolePlayDebugEventSink` infrastructure

## Project Structure

### Documentation (this feature)

```text
specs/001-context-aware-actor-selection/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── ILocationDetectionService.md
│   └── IActorSelectionService.md
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
└── RolePlay/
    ├── TimeOfDay.cs                              # NEW — enum
    ├── AdaptiveStateV2Records.cs                 # MODIFY — add ActorName to SemanticEventRecord
    └── AdaptiveScenarioState.cs                  # MODIFY — add CurrentTimeOfDay, TimeOfDayManuallySet
└── ModelManager/
    └── AppFunction.cs                            # MODIFY — add RolePlayLocationDetection, RolePlayActorSelection

DreamGenClone.Application/
└── RolePlay/Abstractions/
    ├── ILocationDetectionService.cs               # NEW — interface contract
    └── IActorSelectionService.cs                  # NEW — interface contract

DreamGenClone.Web/
└── Domain/RolePlay/
    └── CharacterTurnOverride.cs                   # NEW — override model + PreferredTurnPosition
└── Domain/Scenarios/
    ├── Character.cs                               # MODIFY — add LocationAffinities
    ├── CharacterLocationAffinity.cs               # NEW — affinity model + AffinityType
    └── Scenario.cs                                # MODIFY — add DefaultTimeOfDay
└── Domain/RolePlay/RolePlaySession.cs             # MODIFY — add CharacterTurnOverrides; transient cache fields
└── Application/RolePlay/
    ├── LocationDetectionService.cs                # NEW — implementation
    ├── ActorSelectionService.cs                   # NEW — implementation
    ├── Models/LocationDetectionModels.cs          # NEW — request/response DTOs
    ├── Models/ActorSelectionModels.cs             # NEW — request/response DTOs
    ├── LocationDetectionJobHandler.cs             # NEW — IBackgroundJobHandler
    ├── RolePlayEngineService.cs                   # MODIFY — replace DetectSceneLocationSignalAsync;
    │                                                add DetectTimeOfDayAsync, ResolveAvailableCharacters,
    │                                                ScoreActorForAutoSelection, BuildNarrativeSummary;
    │                                                rewrite sort in ResolveSceneContinueActorsAsync;
    │                                                remove MatchScenarioLocation/MatchGenericLocation/
    │                                                ContainsWholeWord/GenericLocationNames; update CreateSessionAsync
    │                                                + SeedFromScenarioAsync seeding
    └── BackgroundJobs/BackgroundJobTypes.cs       # MODIFY — add LocationDetection constant

DreamGenClone.Infrastructure/
└── RolePlay/RolePlayStateRepository.cs            # MODIFY — new ALTER TABLE columns (CurrentTimeOfDay,
                                                     TimeOfDayManuallySet, ActorName) + load/save wiring
                                                     + one-time ActorName backfill migration

DreamGenClone.Web/
└── Components/Pages/
    ├── ScenarioEditor.razor                       # MODIFY — affinity editor + default time-of-day
    └── RolePlayWorkspace.razor                    # MODIFY — time-of-day display/override + per-char overrides
└── Program.cs                                      # MODIFY — DI: ILocationDetectionService,
                                                     IActorSelectionService, LocationDetectionJobHandler,
                                                     hosted backfill migration
└── appsettings.json, appsettings.Development.json  # MODIFY — EnableLocationServices: true

DreamGenClone.Tests/
└── RolePlay/
    ├── LocationDetectionServiceTests.cs            # NEW
    ├── ActorSelectionServiceTests.cs               # NEW
    ├── TimeOfDayDetectionTests.cs                  # NEW
    ├── CharacterAvailabilityResolverTests.cs       # NEW
    ├── ActorScoringTests.cs                        # NEW
    ├── SemanticEventActorNameBackfillTests.cs      # NEW
    └── ResolveSceneContinueActorsIntegrationTests.cs # NEW — open-world flow
```

**Structure Decision**: Reuses the established layered .NET solution structure. New domain types land in `DreamGenClone.Domain/RolePlay/`; new application services in `DreamGenClone.Web/Application/RolePlay/`; new persistence wiring in `DreamGenClone.Infrastructure/RolePlay/`. No new projects are introduced. The two LLM-backed services (`ILocationDetectionService`, `IActorSelectionService`) mirror the existing `ISemanticEventInferenceService` arrangement — interface in `DreamGenClone.Application/RolePlay/Abstractions/`, implementation in `DreamGenClone.Web/Application/RolePlay/`.

## Complexity Tracking

No Constitution Check violations require justification. The feature stays within the existing module boundaries and persistence framework.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
