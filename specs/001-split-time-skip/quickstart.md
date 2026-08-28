# Quickstart: Multi-Encounter Climax Time-Skip — Two-Turn Split

**Date**: 2026-06-24  
**Feature**: `001-split-time-skip`

## Overview

This feature splits the multi-encounter climax time-skip into two sequential Continue turns: first a close-scene turn, then an advance-time turn. Previously both instructions were combined into a single response.

## Where to Start

### 1. Domain Model (`DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`)

- Add `TimeSkipPhase` enum (values: `None=0, CloseScene=1, AdvanceTime=2`)
- Replace `public bool TimeSkipPending` with `public TimeSkipPhase CurrentTimeSkipPhase`
- Update `IsStateDirty` doc comment (line ~225) to reference `CurrentTimeSkipPhase`

### 2. Persistence (`DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs`)

- **Schema migration** (line ~1022): Add `CurrentTimeSkipPhase INTEGER NOT NULL DEFAULT 0` column + backfill
- **INSERT** (lines 233, 245): Add `CurrentTimeSkipPhase` to column list and VALUES; write `(int)state.CurrentTimeSkipPhase`
- **ON CONFLICT UPDATE** (line 280): Add `CurrentTimeSkipPhase = excluded.CurrentTimeSkipPhase`
- **Write param** (line 325): Replace `$timeSkipPending` write with `(int)state.CurrentTimeSkipPhase`, write 0 to retired column
- **SELECT** (line 520): Add `CurrentTimeSkipPhase` at ordinal 35
- **Read** (line 591): Read ordinal 35 with back-compat fallback to legacy ordinal 34

### 3. Engine Logic (`DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`)

Three change sites:

- **Overflow loop pre-gate** (lines ~1496-1542): Rewrite `timeSkipActive` logic to use `CurrentTimeSkipPhase` enum with phase-aware gating:
  - Reset `InteractionsInCurrentEncounter = 0` for both phases
  - CloseScene gates on `InteractionsInCurrentEncounter == 0` + all common gates
  - AdvanceTime gates on `phase == AdvanceTime` + common gates (no counter gate)
  - Set `IsStateDirty = true` on phase mutation

- **Per-actor prompt selection** (lines ~1557-1575): Split combined directive into two phase-specific directives:
  - CloseScene: `"Close the current encounter naturally."`
  - AdvanceTime: `"Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life."`
  - Add `CurrentTimeSkipPhase == TimeSkipPhase.None` guard to `isNewEncounterStart`

- **Pipeline-batch increment** (line ~4107-4109): Add `&& v2State.CurrentTimeSkipPhase == TimeSkipPhase.None` guard

- **`AlignPromptNarrativeStateWithV2Async`** (line ~4305+): Add comment documenting intentional non-sync of phase fields

- **`TryDetectEncounterBoundaryAsync`** (line ~4554): Set `CurrentTimeSkipPhase = CloseScene` instead of `TimeSkipPending = true`

### 4. Tests (`DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`)

- Update directive text assertions for split phrases
- Add phase-transition state machine tests
- Add pipeline-batch increment guard test
- Add `isNewEncounterStart` guard test

## Build & Verify

```powershell
dotnet build DreamGenClone.sln
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~MultiEncounterTimeSkip"
```

## Key Constants

| Constant | Value |
|----------|-------|
| User instruction window size | 3 interactions |
| Close-scene directive | `"Close the current encounter naturally."` |
| Advance-time directive | `"Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life."` |
| Prompt intent | `PromptIntent.Instruction` (one-shot, not persistent) |

## Back-Compat

- Legacy `TimeSkipPending = 1` rows are backfilled to `CurrentTimeSkipPhase = CloseScene` on migration
- Legacy `TimeSkipPending` column remains in schema (dead column, always written as 0)
- Read path falls back to legacy column if new column is 0 (for any rows missed by backfill)
