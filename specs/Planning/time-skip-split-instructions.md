# Multi-Encounter Climax Time-Skip: Split Into Two-Turn Instructions

**Date**: 2026-06-24
**Status**: Draft
**Related**: `specs/001-fix-climax-timeskip/` (existing implementation)

## Problem Statement

When `[ClimaxMode:multi-encounter]` detects an encounter boundary, the time-skip system currently injects a **single combined instruction** in one turn:

```
Instruction: "Close the current encounter naturally.
              Then advance time to a new moment — a different day or time,
              a new context, a new circumstance. Establish ordinary life."
```

This asks the model to do two things in one response: (1) close out the current scene, and (2) jump to a new scene. In practice the model can handle this, but separating these into **two distinct turns** would produce more natural scene transitions — the close-out gets full attention, and the new scene starts fresh in its own turn without the model trying to compress both actions into one response.

## Proposed Change

Split the single combined instruction into **two sequential phases**, each applied on a separate continuation turn:

```
Boundary detected → Phase = CloseScene

Turn 1 (CloseScene):
  └─ Instruction: "Close the current encounter naturally."
  └─ Phase → AdvanceTime

Turn 2 (AdvanceTime):
  └─ Instruction: "Advance time to a new moment — a different day or time,
                   a new context, a new circumstance. Establish ordinary life."
  └─ Phase → None (back to normal flow)
```

## Current Architecture (Baseline)

The existing implementation (`specs/001-fix-climax-timeskip/`) already fixed three bugs:

1. **One-shot injection** — time-skip is injected as `PromptIntent.Instruction` on the first overflow actor, not as a persistent `RolePlayInteraction` (prevents looping)
2. **No encounter number** — directive text is encounter-number-agnostic
3. **User instruction priority** — skips injection if a user-typed Instruction is in the last 3 interactions, retries next turn

**Key state field**: `AdaptiveScenarioState.TimeSkipPending` (bool) — set `true` on boundary detection, cleared `false` after the single combined instruction is injected.

**Injection site**: `RolePlayEngineService.cs` overflow loop (~line 1496-1575).

## Design

### State Change

Replace `bool TimeSkipPending` with a two-phase enum:

```csharp
public enum TimeSkipPhase
{
    None = 0,
    CloseScene = 1,
    AdvanceTime = 2
}
```

On `AdaptiveScenarioState`:
- **Remove**: `public bool TimeSkipPending { get; set; }`
- **Add**: `public TimeSkipPhase CurrentTimeSkipPhase { get; set; }`

### State Transitions

| Trigger | Old (bool) | New (enum) |
|---|---|---|
| Encounter boundary detected | `TimeSkipPending = true` | `CurrentTimeSkipPhase = CloseScene` |
| User instruction detected in window | Keep `true` (retry next turn) | Keep `CloseScene` or `AdvanceTime` unchanged (retry next turn) |
| Overflow loop, CloseScene phase fires | `TimeSkipPending = false` | `CurrentTimeSkipPhase = AdvanceTime` |
| Overflow loop, AdvanceTime phase fires | N/A | `CurrentTimeSkipPhase = None` |

### Overflow Loop Logic (RolePlayEngineService.cs)

```csharp
// Before the overflow loop:
var timeSkipPhase = isClimaxPhase
    ? session.AdaptiveState.CurrentTimeSkipPhase
    : TimeSkipPhase.None;

if (timeSkipPhase == TimeSkipPhase.CloseScene || timeSkipPhase == TimeSkipPhase.AdvanceTime)
{
    // Reset InteractionsInCurrentEncounter so the counter gate works
    // (same as current TimeSkipPending reset logic)
    session.AdaptiveState.InteractionsInCurrentEncounter = 0;
}

// Determine if injection fires this turn (respect user-instruction skip)
var timeSkipShouldFire = timeSkipPhase != TimeSkipPhase.None
    && session.AdaptiveState.CurrentEncounterNumber > 1
    && session.AdaptiveState.InteractionsInCurrentEncounter == 0
    && !HasRecentUserInstruction(session, windowSize: 3);

if (timeSkipShouldFire)
{
    // Inject into first actor
    if (timeSkipPhase == TimeSkipPhase.CloseScene)
    {
        promptText = "Close the current encounter naturally.";
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        // Log: "MultiEncounterTimeSkipCloseSceneInjected"
    }
    else // AdvanceTime
    {
        promptText = "Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life.";
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.None;
        // Log: "MultiEncounterTimeSkipAdvanceTimeInjected"
    }
    actorIntent = PromptIntent.Instruction;
}
else if (timeSkipPhase != TimeSkipPhase.None && HasRecentUserInstruction(session, windowSize: 3))
{
    // User instruction active — skip injection this turn, keep phase for retry
    // Log: "MultiEncounterTimeSkipSkippedDueToUserInstruction"
}
```

### Important: InteractionsInCurrentEncounter Gate

The current code uses `InteractionsInCurrentEncounter == 0` as a gate. This works for the one-shot case, but when splitting across two turns:

- **Turn 1 (CloseScene)**: `InteractionsInCurrentEncounter == 0` → passes gate, injection fires, `CurrentTimeSkipPhase → AdvanceTime`
- **Turn 2 (AdvanceTime)**: `InteractionsInCurrentEncounter` will be ≥ 1 (incremented by the CloseScene generation) — **would fail the gate**

**Fix**: Don't gate on `InteractionsInCurrentEncounter` for the `AdvanceTime` phase. Instead, gate primarily on `CurrentTimeSkipPhase != None`. The `InteractionsInCurrentEncounter == 0` gate only applies to `CloseScene` (ensuring we don't re-fire close-scene mid-encounter).

Alternatively, the `TimeSkipPending` reset logic (line ~1498-1501) that forces `InteractionsInCurrentEncounter = 0` when `TimeSkipPending` is true already handles this — extend it to reset when `CurrentTimeSkipPhase` is non-None.

## Files to Modify

| File | Change |
|---|---|
| `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` | Replace `bool TimeSkipPending` with `TimeSkipPhase CurrentTimeSkipPhase` enum |
| `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` | Update persistence (read/write INTEGER 0/1/2 instead of 0/1), update schema migration |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Two-phase logic in overflow loop; boundary detection sets `CloseScene` instead of `true` |
| `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` | Update existing tests; add tests for two-phase flow |
| `DreamGenClone.Tests/RolePlay/MultiEncounterClimaxTests.cs` | Update any tests referencing `TimeSkipPending` |

## New vs. Replace Decision

Two options for introducing this feature:

### Option A: Replace Default (Breaking)
Change the existing `[ClimaxMode:multi-encounter]` behavior to always use the two-phase split. Simpler implementation. All existing multi-encounter themes automatically get the new flow.

### Option B: Opt-In Marker
Add a new tag like `[TimeSkipStyle:split]` or a `[ClimaxMode:multi-encounter-split]` so existing sessions keep the current combined behavior and new themes can opt in. More flexible but adds marker parsing and mutual-exclusion validation.

**Recommendation**: Option A (replace default) — simpler, fewer code paths, and the two-phase behavior is strictly better for narrative quality.

## Edge Cases

1. **User Instruction during CloseScene phase**: Skip keeps the phase at `CloseScene`. Retries next turn. Same pattern as current `TimeSkipPending` retry logic.

2. **User Instruction during AdvanceTime phase**: Skip keeps the phase at `AdvanceTime`. Retries next turn. Identical retry pattern.

3. **Session ends between Turn 1 and Turn 2**: `CurrentTimeSkipPhase = AdvanceTime` persists in DB. Harmless — next Continue picks it up and injects the advance-time instruction.

4. **User navigates away mid-transition**: State persists in DB. On return, the phase continues from wherever it left off.

5. **What if the close-scene generation already establishes ordinary life?**: Turn 2's advance-time instruction is still injected — the model will either confirm the transition or add a time-skip. The directive acts as guidance, not a forced action. If the scene is already post-time-skip, the model will generally produce an appropriate continuation.

6. **Both phases blocked by user instruction across multiple turns**: `TimeSkipPhase` remains until injection succeeds. No timeout — the phase persists indefinitely until the engine can inject it. This matches the current "retry until success" semantics.

## Backlog Reference

**B-051**: Multi-encounter Climax time-skip — split into two-turn close-scene + advance-time instructions.
