# Contracts: Multi-Encounter Climax Time-Skip — Two-Turn Split

**Date**: 2026-06-24  
**Feature**: `001-split-time-skip`

## Contract Status: No New External Interfaces

This feature is a **purely internal behavior change** to the existing multi-encounter climax time-skip injection logic. No new public APIs, REST endpoints, CLI commands, UI contracts, or external interfaces are introduced.

## Modified Internal Contracts

The following existing internal contracts are modified:

### 1. AdaptiveScenarioState Data Contract

The `AdaptiveScenarioState` C# class contract changes:

- **Removed**: `bool TimeSkipPending { get; set; }`
- **Added**: `TimeSkipPhase CurrentTimeSkipPhase { get; set; }`
- **Added**: `enum TimeSkipPhase { None = 0, CloseScene = 1, AdvanceTime = 2 }`

Consumers that read `TimeSkipPending` must be updated (only `RolePlayEngineService` and `RolePlayStateRepository`).

### 2. SQLite Schema Contract (RolePlayV2AdaptiveStates)

- **Added**: `CurrentTimeSkipPhase INTEGER NOT NULL DEFAULT 0` column
- **Retained (dead)**: `TimeSkipPending INTEGER NOT NULL DEFAULT 0` column
- **Backfill**: Rows with `TimeSkipPending = 1` receive `CurrentTimeSkipPhase = 1` (CloseScene)

### 3. Directive Text Contract

- **Before**: Single combined directive text
- **After**: Two separate directive texts:
  - CloseScene: `"Close the current encounter naturally."`
  - AdvanceTime: `"Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life."`

## No New Dependencies

No new NuGet packages, project references, or external dependencies are introduced.
