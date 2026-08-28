# Implementation Plan: B-041 — Session Memory Context (Intimate Encounter History Injection)

**Branch**: `001-session-memory-context` | **Date**: 2026-05-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-session-memory-context/spec.md`

## Summary

Characters in ongoing RP sessions lose memory of prior arcs as interactions scroll out of the LLM context window. This feature adds per-character encounter summaries that capture what physically happened in each arc (kissing, touching, oral sex, intercourse with positions, finishing move) and injects them as a structured "Session Memory" block into every continuation prompt. Template summaries are generated synchronously at each phase transition; an async LLM job enriches ArcCompletion rows with first-person prose after Climax→Reset. Prompt injection is capped by `MaxMilestonesToInject` (default 5) for current-arc milestones and `MaxArcCompletionsToInject` (default 10) for prior-arc completions.

## Technical Context

## Technical Context

**Language/Version**: C# 12 / .NET 9
**Primary Dependencies**: Blazor Server (DreamGenClone.Web), SQLite via Microsoft.Data.Sqlite, Serilog, IOptions<T> configuration pattern, IBackgroundJobHandler infrastructure
**Storage**: SQLite — new `RolePlayV2EncounterSummaries` table + additive `Sessions` column
**Testing**: xUnit (DreamGenClone.Tests); unit tests for template generation, injection filtering, and job dedup
**Target Platform**: Local Windows desktop web app (localhost Blazor Server)
**Project Type**: Desktop web application — layered .NET solution (Domain / Application / Infrastructure / Web)
**Performance Goals**: Template summary generation MUST complete within existing phase transition latency budget (no measurable delta); LLM arc-completion job targets ≤30 s under normal model load
**Constraints**: Fire-and-forget job MUST NOT block session continuation; all persistence MUST be SQLite; no cloud dependencies
**Scale/Scope**: Single-user local session; sessions may accumulate 10–20 arcs; memory block capped by config to prevent token blowout

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
  - Template summaries are fully synchronous + local; LLM job uses the existing local model manager (same as semantic analysis)
- [x] Module boundaries and adapter seams are explicit and swappable
  - `IEncounterSummaryService` defined in Application layer; implementation in Infrastructure; no cross-layer leakage
- [x] .NET layered architecture uses separate projects with enforced dependency direction
  - Domain ← Application ← Infrastructure ← Web; new types follow existing project placement
- [x] Deterministic state transitions and JSON contract validation are test-covered
  - Template generation is deterministic; injection filtering is deterministic; both are unit-testable without LLM
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
  - All persistence is SQLite; `RolePlayV2EncounterSummaries` table and Sessions migration follow existing `IF NOT EXISTS` + `PRAGMA` guard pattern
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
  - FR-014/FR-015/FR-016 explicitly require Serilog structured logging; job handler and service log at Information/Warning
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
  - Engine hook logs transition + summary write; job handler logs start/complete/retry/abandon
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes
  - `RolePlayMemoryOptions.EnableLlmSummaryEnhancement` + all log levels in `appsettings.json`

**Result**: All 8 gates PASS. No violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/001-session-memory-context/
├── plan.md              ← this file
├── spec.md              ← feature specification
├── research.md          ← Phase 0 resolved unknowns
├── data-model.md        ← Phase 1 entity + DB schema
├── quickstart.md        ← Phase 1 implementation order
├── contracts/
│   ├── IEncounterSummaryService.md
│   └── EncounterSummaryJobPayload.md
└── tasks.md             ← Phase 2 output (/speckit.tasks — not yet generated)
```

### Source Code (additive changes only)

```text
DreamGenClone.Domain/
└── RolePlay/
    ├── EncounterSummaryRecord.cs              ← NEW (entity + enum)
    └── AdaptiveScenarioState.cs               ← ADDITIVE

DreamGenClone.Application/
└── RolePlay/
    ├── Abstractions/
    │   └── IEncounterSummaryService.cs        ← NEW
    └── EncounterSummaryJobPayload.cs          ← NEW

DreamGenClone.Web/
├── Application/
│   ├── BackgroundJobs/
│   │   └── BackgroundJobTypes.cs             ← ADDITIVE (new constant)
│   └── RolePlay/
│       ├── RolePlayEngineService.cs           ← ADDITIVE (transition hook)
│       └── RolePlayContinuationService.cs    ← ADDITIVE (Session Memory injection)
├── Domain/
│   └── RolePlay/
│       └── RolePlaySession.cs                ← ADDITIVE
├── Components/Pages/
│   └── [session creation page]               ← ADDITIVE (MaxMilestonesToInject field)
├── Program.cs                                ← ADDITIVE (DI registrations)
└── appsettings.Development.json              ← ADDITIVE (RolePlayMemory section)

DreamGenClone.Infrastructure/
├── Configuration/
│   └── RolePlayMemoryOptions.cs              ← NEW
├── RolePlay/
│   ├── EncounterSummaryService.cs            ← NEW
│   └── EncounterSummaryJobHandler.cs         ← NEW
└── Persistence/
    ├── SqlitePersistence.cs                  ← ADDITIVE (table + migration)
    └── RolePlayStateRepository.cs            ← ADDITIVE (load/save methods)

DreamGenClone.Tests/
└── RolePlay/
    ├── EncounterSummaryServiceTests.cs       ← NEW
    └── SessionMemoryInjectionTests.cs        ← NEW
```

---

## Phase 0: Research

*Status: COMPLETE — see [research.md](research.md)*

All NEEDS CLARIFICATION items resolved:

| # | Unknown | Decision |
|---|---|---|
| R1 | Phase transition hook location | After `SaveTransitionEventAsync` ~L2892 in `RolePlayEngineService` |
| R2 | Async job pattern | Follow `SemanticInteractionAnalysisJobHandler` exactly; one job per arc; retry once ~5 s then abandon |
| R3 | Prompt injection pattern | `StringBuilder.AppendLine`; after Recent Interaction History; `InjectSessionMemoryBlock` helper |
| R4 | Template summary format | Structured template for PhaseMilestone; placeholder prose for ArcCompletion pre-LLM |
| R5 | Arc interaction loading | New `LoadArcInteractionsAsync(sessionId, cycleIndex)`; last 30 interactions for token budget |
| R6 | Settings pattern | `RolePlayMemoryOptions` matching `RolePlayFeatureFlagsOptions`; `MaxMilestonesToInject`=5, `MaxArcCompletionsToInject`=10 |
| R7 | SQLite migration | `CREATE TABLE IF NOT EXISTS` in `EnsureAdaptiveStateSchemaAsync`; `PRAGMA table_info` guard for Sessions column |

---

## Phase 1: Design & Contracts

*Status: COMPLETE — see [data-model.md](data-model.md), [quickstart.md](quickstart.md), [contracts/](contracts/)*

### Entity Summary

**`EncounterSummaryRecord`** — one row per character per phase transition
- `Id`, `SessionId`, `CharacterId`, `SummaryType` (PhaseMilestone | ArcCompletion)
- `CycleIndex`, `FromPhase`, `ToPhase`, `OccurredUtc`, `InteractionCountInPhase`
- `SceneLocation?`, `ActiveThemeId?`, `FinishingMoveId?` (ArcCompletion only), `PositionIdsJson?` (ArcCompletion only)
- `CharacterStatsSnapshotJson`, `TemplateSummary`, `LlmSummary?`, `LlmEnhancedUtc?`
- Computed: `ActiveSummary → LlmSummary ?? TemplateSummary`

**`RolePlayMemoryOptions`** — config: `MaxMilestonesToInject`=5, `MaxArcCompletionsToInject`=10, `EnableLlmSummaryEnhancement`=true

**`EncounterSummaryJobPayload`** — `{ SessionId, CycleIndex }` — one job per arc transition

### New DB Artifacts

- `RolePlayV2EncounterSummaries` table (17 columns; index on `(SessionId, OccurredUtc DESC)`)
- `Sessions.MaxMilestonesToInject INTEGER NULL` additive column

### Interface Contracts

| Contract | Location |
|---|---|
| `IEncounterSummaryService` | [contracts/IEncounterSummaryService.md](contracts/IEncounterSummaryService.md) |
| `EncounterSummaryJobPayload` | [contracts/EncounterSummaryJobPayload.md](contracts/EncounterSummaryJobPayload.md) |

---

## Implementation Phases

### Phase 1 — Domain Layer

**Goal**: New entity and enum; additive state model changes. No external dependencies.

1. `DreamGenClone.Domain/RolePlay/EncounterSummaryRecord.cs` — `EncounterSummaryType` enum + `EncounterSummaryRecord` class with all properties; computed `ActiveSummary`, `IsEnhanced`
2. `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` — add `public List<EncounterSummaryRecord> EncounterSummaries { get; set; } = [];`
3. `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs` — add `public int? MaxMilestonesToInject { get; set; }`

**Build check**: Domain project 0 errors.

---

### Phase 2 — Configuration

**Goal**: `RolePlayMemoryOptions` config class.

1. `DreamGenClone.Infrastructure/Configuration/RolePlayMemoryOptions.cs` — sealed class; `SectionName = "RolePlayMemory"`, `MaxMilestonesToInject = 5`, `MaxArcCompletionsToInject = 10`, `EnableLlmSummaryEnhancement = true`

**Build check**: Infrastructure project 0 errors.

---

### Phase 3 — Application Contracts

**Goal**: Interface, job payload, and new `BackgroundJobTypes` constant.

1. `DreamGenClone.Application/RolePlay/Abstractions/IEncounterSummaryService.cs` — per [contracts/IEncounterSummaryService.md](contracts/IEncounterSummaryService.md)
2. `DreamGenClone.Application/RolePlay/EncounterSummaryJobPayload.cs` — `{ SessionId, CycleIndex }`
3. `DreamGenClone.Web/Application/BackgroundJobs/BackgroundJobTypes.cs` — add `public const string EncounterSummaryEnhancement = "encounter-summary-enhancement";`

**Build check**: Application + Web 0 errors.

---

### Phase 4 — Persistence: Schema + CRUD

**Goal**: Table creation, Sessions migration, repository save and load methods.

1. `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
   - Add `CREATE TABLE IF NOT EXISTS RolePlayV2EncounterSummaries` in `EnsureAdaptiveStateSchemaAsync`
   - Add `ALTER TABLE Sessions ADD COLUMN MaxMilestonesToInject INTEGER NULL` with `PRAGMA table_info` guard
2. `DreamGenClone.Infrastructure/Persistence/RolePlayStateRepository.cs`
   - `SaveEncounterSummaryAsync(EncounterSummaryRecord, CancellationToken)`
   - `UpdateLlmSummaryAsync(string id, string llmSummary, DateTime enhancedUtc, CancellationToken)`
   - `LoadEncounterSummariesAsync(string sessionId, int maxMilestones, int maxArcCompletions, int currentCycleIndex, CancellationToken)` — two queries: ArcCompletion DESC LIMIT M + PhaseMilestone for current arc DESC LIMIT N; merged in chronological order
   - `LoadArcInteractionsAsync(string sessionId, int cycleIndex, int limit, CancellationToken)` — query `RolePlayInteractions` for the arc; check `CycleIndex` column exists with PRAGMA guard
   - Call `LoadEncounterSummariesAsync` from `LoadStateAsync` to populate `AdaptiveScenarioState.EncounterSummaries`

**Build check**: Infrastructure 0 errors; table present in dev DB after first run.

---

### Phase 5 — `EncounterSummaryService` (template generation)

**Goal**: Synchronous template summary generator and save orchestration.

1. `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryService.cs` — implements `IEncounterSummaryService`
   - `GenerateTemplatesAsync`: builds one `EncounterSummaryRecord` per character from `v2State.CharacterSnapshots`; writes `TemplateSummary` from structured template; guard: if snapshots empty, produce minimal stat-free summary (no throw)
   - `SaveAsync`: delegates to `RolePlayStateRepository.SaveEncounterSummaryAsync`
   - `UpdateLlmSummaryAsync`: delegates to repository

**Build check**: Infrastructure 0 errors.

---

### Phase 6 — `EncounterSummaryJobHandler` (LLM enrichment)

**Goal**: Fire-and-forget job enriching ArcCompletion rows with per-character prose.

1. `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryJobHandler.cs` — implements `IBackgroundJobHandler`
   - `JobType = BackgroundJobTypes.EncounterSummaryEnhancement`
   - Deserializes `EncounterSummaryJobPayload { SessionId, CycleIndex }`
   - Loads arc interactions via `LoadArcInteractionsAsync` (last 30)
   - Loads all `ArcCompletion` rows for `(SessionId, CycleIndex)` to get character list
   - Single LLM call (same inference service as semantic analysis) generating per-character prose for all characters
   - Parses per-character prose; calls `UpdateLlmSummaryAsync` per character row
   - **Retry**: on LLM exception, wait ~5 s, retry once; on second failure log `Warning` and return (no throw)

**Build check**: Infrastructure 0 errors.

---

### Phase 7 — Engine Hook

**Goal**: Wire template generation and job enqueue into `RolePlayEngineService`.

1. `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
   - Inject `IEncounterSummaryService`, `IOptions<RolePlayMemoryOptions>`
   - After `SaveTransitionEventAsync` (~L2892): call `GenerateTemplatesAsync` → `SaveAsync` per record → append each to `v2State.EncounterSummaries`
   - If Climax→Reset AND `EnableLlmSummaryEnhancement`: enqueue `EncounterSummaryEnhancement` job with dedup key `$"enc-summary:{session.SessionId}:{v2State.CycleIndex}"`

**Build check**: Web 0 errors.

---

### Phase 8 — Prompt Injection

**Goal**: Inject "Session Memory" block into continuation prompts.

1. `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`
   - Inject `IOptions<RolePlayMemoryOptions>`
   - In `BuildPromptAsync`: after Recent Interaction History block, call `InjectSessionMemoryBlock`
   - `InjectSessionMemoryBlock(sb, summaries, effectiveMilestones, effectiveArcCompletions)`:
     - Take most-recent M `ArcCompletion` rows → reverse to chronological → render `[Arc N Complete — {CharName}]` + `ActiveSummary`
     - Take most-recent N `PhaseMilestone` rows for current `CycleIndex` → reverse to chronological → render `[{FromPhase} → {ToPhase} — {CharName}]` + `ActiveSummary`
     - Omit entire block if both filtered lists are empty
   - `effectiveMilestones = session.MaxMilestonesToInject ?? _memoryOptions.Value.MaxMilestonesToInject`
   - `effectiveArcCompletions = _memoryOptions.Value.MaxArcCompletionsToInject`

**Build check**: Web 0 errors; block appears in prompt log after first arc completion.

---

### Phase 9 — Registration, Config, Session Creation UI

**Goal**: Wire DI, appsettings, and per-session override field.

1. `DreamGenClone.Web/Program.cs`
   - `builder.Services.Configure<RolePlayMemoryOptions>(builder.Configuration.GetSection(RolePlayMemoryOptions.SectionName))`
   - `builder.Services.AddScoped<IEncounterSummaryService, EncounterSummaryService>()`
   - `builder.Services.AddScoped<IBackgroundJobHandler, EncounterSummaryJobHandler>()`
2. `DreamGenClone.Web/appsettings.Development.json` — add `"RolePlayMemory": { "MaxMilestonesToInject": 5, "MaxArcCompletionsToInject": 10, "EnableLlmSummaryEnhancement": true }`
3. Session creation Razor page — add optional `MaxMilestonesToInject` numeric input (nullable int); write to `session.MaxMilestonesToInject` on create

**Build check**: Full solution 0 errors; app starts.

---

### Phase 10 — Tests

**Goal**: Unit tests covering template generation, injection filtering, and SC-005/SC-006.

1. `DreamGenClone.Tests/RolePlay/EncounterSummaryServiceTests.cs`
   - `GenerateTemplate_PhaseMilestone_ContainsAllFields`
   - `GenerateTemplate_EmptyCharacterSnapshots_ProducesMinimalSummaryWithoutThrowing`
   - `GenerateTemplate_ArcCompletion_ContainsBeatCodeAndStats`
2. `DreamGenClone.Tests/RolePlay/SessionMemoryInjectionTests.cs`
   - `InjectBlock_NoSummaries_BlockOmitted`
   - `InjectBlock_MaxMilestonesEnforced` (set to 2, verify 2 injected from 4 available)
   - `InjectBlock_MaxArcCompletionsEnforced` (set to 3, verify 3 injected from 5 arcs)
   - `InjectBlock_PerSessionOverrideUsedWhenPresent`
   - `InjectBlock_ArcCompletionsRenderedBeforeMilestones`
   - `InjectBlock_LlmSummaryPreferredOverTemplate`

**Build check**: All tests pass; SC-005 and SC-006 satisfied.

---

## Dependency Map

```
Phase 1 (Domain)
├── Phase 2 (Config)
└── Phase 3 (Contracts)
     └── Phase 4 (Persistence)
          └── Phase 5 (EncounterSummaryService)
               ├── Phase 6 (JobHandler)
               └── Phase 7 (Engine Hook)
                    └── Phase 8 (Prompt Injection)
                         └── Phase 9 (Registration + Config + UI)
                              └── Phase 10 (Tests)
```

---

## Key Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| `RolePlayInteractions.CycleIndex` column may not exist | Medium | Check column with `PRAGMA table_info` before querying; `LoadArcInteractionsAsync` handles missing column gracefully |
| `BackgroundJobTypes` file location | Low | Search for `SemanticInteractionAnalysis` constant at implementation time |
| LLM arc-completion prompt token budget exceeded | Medium | Cap arc interactions at last 30; if response parse fails for a character, skip that row and log Warning |
| Session creation Razor page identity | Low | Search existing session creation flow; reference `quickstart.md` step 12 |
| Per-session override UI conflict with existing form | Low | Field is optional (nullable int); no validation unless value ≤ 0 |

---

## Post-Phase 1 Constitution Re-check

All 8 constitution gates re-verified after Phase 1 design:

1. ✅ Local-first: LLM job uses same local model manager as semantic analysis; no cloud dependency introduced
2. ✅ Module boundaries: `IEncounterSummaryService` seam is explicit; job handler is swappable
3. ✅ Layered architecture: all new types placed in correct projects following Domain → Application → Infrastructure → Web dependency direction
4. ✅ Deterministic transitions: template generation is pure function of structured data; injection filtering is deterministic; both fully unit-testable
5. ✅ SQLite default: all new persistence is SQLite with standard `IF NOT EXISTS` + `PRAGMA` guard patterns
6. ✅ Serilog: all new code logs via injected `ILogger<T>` with structured message templates
7. ✅ Logging coverage: engine hook, service, and job handler log at Information for major paths; Warning for retry/abandon
8. ✅ Configurable log levels: feature behavior gated via `RolePlayMemoryOptions`; all log thresholds in `appsettings.json`
