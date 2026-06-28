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

> This Design section incorporates all findings from the "Code Review Findings" section
> at the end of this document. The original flawed pseudocode has been replaced with
> corrected logic. Each change site references the finding number (e.g., `[F1]`) that
> justifies it.

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

On `AdaptiveScenarioState` (`DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`):
- **Remove**: `public bool TimeSkipPending { get; set; }` (line 155)
- **Add**: `public TimeSkipPhase CurrentTimeSkipPhase { get; set; }`
- **Update**: the `IsStateDirty` XML doc comment (line 225) to reference
  `CurrentTimeSkipPhase` instead of `TimeSkipPending`.

### State Transitions

| Trigger | Old (bool) | New (enum) |
|---|---|---|
| Encounter boundary detected (`TryDetectEncounterBoundaryAsync`, line 4554) | `TimeSkipPending = true` | `CurrentTimeSkipPhase = CloseScene` |
| User instruction detected in window (either phase) | Keep `true` (retry next turn) | Keep `CloseScene` or `AdvanceTime` unchanged (retry next turn) |
| Overflow loop, CloseScene phase fires | `TimeSkipPending = false` | `CurrentTimeSkipPhase = AdvanceTime` |
| Overflow loop, AdvanceTime phase fires | N/A | `CurrentTimeSkipPhase = None` |

### Schema Migration — Strategy B (Additive + Back-Compat Read) `[F6]`

SQLite cannot easily drop/rename columns. Use an additive approach:

**Migration** (`RolePlayStateRepository.cs`, `EnsureAdaptiveStateSchemaAsync`, after line 1022):

```csharp
// New phase column (additive — does not touch legacy TimeSkipPending)
if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "CurrentTimeSkipPhase", cancellationToken))
{
    await using var add = connection.CreateCommand();
    add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CurrentTimeSkipPhase INTEGER NOT NULL DEFAULT 0";
    await add.ExecuteNonQueryAsync(cancellationToken);

    // Backfill: any row with legacy TimeSkipPending=1 becomes CloseScene (phase=1).
    // TimeSkipPending only ever meant "boundary detected, close-scene pending."
    await using var backfill = connection.CreateCommand();
    backfill.CommandText = "UPDATE RolePlayV2AdaptiveStates SET CurrentTimeSkipPhase = 1 WHERE TimeSkipPending = 1";
    await backfill.ExecuteNonQueryAsync(cancellationToken);
}
```

**Keep** the legacy `TimeSkipPending` column in the schema (do not drop — SQLite table
rebuild is risky). Stop writing to it after migration; it becomes a dead column.

**Write** (`SaveAdaptiveStateAsync`, replace line 325):
```csharp
command.Parameters.AddWithValue("$currentTimeSkipPhase", (int)state.CurrentTimeSkipPhase);
command.Parameters.AddWithValue("$timeSkipPending", 0); // retired — always write 0
```
Update the INSERT column list (line 233) and VALUES (line 245) to include
`CurrentTimeSkipPhase` alongside the retired `TimeSkipPending`.

**Read** (`LoadAdaptiveStateAsync`, replace line 591):
```csharp
// Back-compat: if CurrentTimeSkipPhase column exists and is non-zero, use it.
// Otherwise fall back to legacy TimeSkipPending (1 = CloseScene).
CurrentTimeSkipPhase = reader.IsDBNull(35)
    ? (reader.IsDBNull(34) ? TimeSkipPhase.None : (reader.GetInt32(34) != 0 ? TimeSkipPhase.CloseScene : TimeSkipPhase.None))
    : (TimeSkipPhase)reader.GetInt32(35)
```
This requires adding `CurrentTimeSkipPhase` to the SELECT column list (line 520) as
ordinal 35 (after `TimeSkipPending` at ordinal 34).

### Overflow Loop Logic — Corrected `[F1, F3, F5]`

**Location**: `RolePlayEngineService.cs`, lines 1496-1587.

The original plan's pseudocode had two flaws: (1) the `InteractionsInCurrentEncounter == 0`
gate was applied to both phases, but the AdvanceTime phase must not use it; (2) the
pipeline-batch increment (line 4107-4109) was not made phase-aware, causing
double-counting. The corrected logic:

```csharp
// ---- Before the overflow loop (replaces lines 1496-1542) ----

var isClimaxPhase = string.Equals(
    session.AdaptiveState.CurrentPhase.ToString(), "Climax",
    StringComparison.OrdinalIgnoreCase);

var timeSkipPhase = isClimaxPhase
    ? session.AdaptiveState.CurrentTimeSkipPhase
    : TimeSkipPhase.None;

// [F1] Reset the counter for BOTH phases so the per-interaction increment
// starts clean. The pipeline-batch increment (line 4109) is gated separately below.
if (timeSkipPhase != TimeSkipPhase.None)
{
    session.AdaptiveState.InteractionsInCurrentEncounter = 0;
}

// [F1] Gate logic — phase-specific:
//   CloseScene: requires InteractionsInCurrentEncounter == 0 (don't re-fire mid-encounter)
//   AdvanceTime: gates ONLY on phase != None + encounter > 1 + no user instruction
//                (the counter is unreliable here due to the CloseScene turn's increment)
var timeSkipShouldFire = timeSkipPhase != TimeSkipPhase.None
    && session.AdaptiveState.CurrentEncounterNumber > 1
    && !HasRecentUserInstruction(session, windowSize: 3)
    && (timeSkipPhase == TimeSkipPhase.AdvanceTime
        || session.AdaptiveState.InteractionsInCurrentEncounter == 0);

if (timeSkipShouldFire)
{
    // [F5] Mark dirty so the phase transition persists even if pipeline save is refactored
    session.AdaptiveState.IsStateDirty = true;

    if (timeSkipPhase == TimeSkipPhase.CloseScene)
    {
        // Phase transitions to AdvanceTime — will fire on the NEXT Continue turn
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
        // Log: "MultiEncounterTimeSkipCloseSceneInjected"
    }
    else // AdvanceTime
    {
        session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.None;
        // Log: "MultiEncounterTimeSkipAdvanceTimeInjected"
    }
}
else if (timeSkipPhase != TimeSkipPhase.None && HasRecentUserInstruction(session, windowSize: 3))
{
    // User instruction active — skip injection this turn, keep phase for retry
    // Log: "MultiEncounterTimeSkipSkippedDueToUserInstruction"
}
```

**Inside the overflow loop** (replaces lines 1557-1575) — the per-actor prompt selection:

```csharp
string promptText;
PromptIntent actorIntent = PromptIntent.Message;

if (isClimaxPhase)
{
    if (i == 0)
    {
        if (timeSkipShouldFire && timeSkipPhase == TimeSkipPhase.CloseScene)
        {
            promptText = "Close the current encounter naturally.";
            actorIntent = PromptIntent.Instruction;
        }
        else if (timeSkipShouldFire && timeSkipPhase == TimeSkipPhase.AdvanceTime)
        {
            promptText = "Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life.";
            actorIntent = PromptIntent.Instruction;
        }
        else
        {
            // [F3] isNewEncounterStart must NOT fire during an AdvanceTime retry
            // (when injection was skipped due to user instruction). The scene was
            // already closed by the CloseScene turn; "continue the scene" is wrong.
            var isNewEncounterStart = session.AdaptiveState.CurrentEncounterNumber > 0
                && session.AdaptiveState.InteractionsInCurrentEncounter == 0
                && session.AdaptiveState.CurrentTimeSkipPhase == TimeSkipPhase.None;
            promptText = isNewEncounterStart
                ? "Continue the scene naturally."
                : "Continue the current encounter naturally from where it left off.";
        }
    }
    else
    {
        promptText = "Describe this same moment from your character's perspective.";
    }
}
else
{
    promptText = i == 0
        ? "Continue the scene naturally with the next character response."
        : "Continue the conversation naturally, building on the previous response.";
}
```

### Pipeline-Batch Increment — Phase-Aware `[F1]`

**Location**: `RolePlayEngineService.cs`, `RunRolePlayV2PipelinesAsync`, lines 4107-4109.

The pipeline-batch increment `v2State.InteractionsInCurrentEncounter +=
generatedSinceLastEval` runs unconditionally for Climax turns. When a time-skip phase is
active, the per-interaction increment (in `UpdateStateAndDetectEncounterAsync`, line 2403)
already accounts for the generated interactions. The pipeline-batch add would double-count.

**Change** line 4107-4109 to:

```csharp
else if (isMultiEncounterClimax
         && finalPhase == NarrativePhase.Climax
         && priorPhase == NarrativePhase.Climax
         && generatedSinceLastEval > 0
         && v2State.CurrentTimeSkipPhase == TimeSkipPhase.None)  // [F1] skip during time-skip
{
    v2State.InteractionsInCurrentEncounter += generatedSinceLastEval;
}
```

### `AlignPromptNarrativeStateWithV2Async` — Do Not Sync Phase `[F4]`

**Location**: `RolePlayEngineService.cs`, line 4305+.

This method reloads state from the DB mid-overflow-loop (called at line 1584 before each
`ContinueAsync`). It currently does **not** sync `TimeSkipPending`,
`CurrentEncounterNumber`, or `InteractionsInCurrentEncounter` — this is correct by design
(these are in-memory turn-scoped values).

**Change**: Add an explicit code comment in `AlignPromptNarrativeStateWithV2Async`
documenting that `CurrentTimeSkipPhase`, `CurrentEncounterNumber`, and
`InteractionsInCurrentEncounter` are intentionally NOT synced from the DB snapshot here.
Do **not** add `CurrentTimeSkipPhase` to the field sync list.

```csharp
// [F4] Intentionally NOT synced from DB snapshot:
//   - CurrentTimeSkipPhase
//   - CurrentEncounterNumber
//   - InteractionsInCurrentEncounter
// These are in-memory turn-scoped values mutated by the overflow loop and
// TryDetectEncounterBoundaryAsync. Syncing them here would clobber the phase mid-loop
// (e.g., reset AdvanceTime back to CloseScene if the DB hasn't been persisted yet).
```

### Boundary Detection — Set CloseScene `[F5]`

**Location**: `RolePlayEngineService.cs`, `TryDetectEncounterBoundaryAsync`, line 4554.

Replace:
```csharp
state.TimeSkipPending = true;
```
With:
```csharp
state.CurrentTimeSkipPhase = TimeSkipPhase.CloseScene;
state.IsStateDirty = true; // already set on line 4556 — verify it remains
```

### `HydrateV2State` — No Change Needed (Verified Invariant) `[F2]`

`HydrateV2State` (line 4135) does not carry forward `TimeSkipPending`,
`CurrentEncounterNumber`, or `InteractionsInCurrentEncounter` from `previousState`. The
in-memory `session.AdaptiveState` values flow through correctly because `mapped =
session.AdaptiveState` (line 4138) and these fields are not overwritten.

**No code change.** But add a regression test (see Tests section) that asserts
`CurrentTimeSkipPhase` survives a full overflow-loop → pipeline → save → reload cycle.

## Files to Modify

| File | Lines | Change | Finding |
|---|---|---|---|
| `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` | 155, 225 | Replace `bool TimeSkipPending` with `TimeSkipPhase CurrentTimeSkipPhase`; update `IsStateDirty` doc comment | — |
| `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` | 233, 245, 280, 325 | Add `CurrentTimeSkipPhase` to INSERT columns/VALUES/ON CONFLICT; write `(int)state.CurrentTimeSkipPhase`; retire `TimeSkipPending` (always write 0) | F6 |
| `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` | 520, 591 | Add `CurrentTimeSkipPhase` to SELECT; read with back-compat fallback to legacy `TimeSkipPending` | F6 |
| `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` | 1018-1022 | Add `CurrentTimeSkipPhase` column migration + backfill from `TimeSkipPending` | F6 |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | 1496-1542 | Rewrite overflow loop pre-loop gate logic: phase-aware gate, reset counter for both phases, set `IsStateDirty` | F1, F5 |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | 1557-1575 | Rewrite per-actor prompt selection: split CloseScene/AdvanceTime directives; fix `isNewEncounterStart` phase check | F3 |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | 4107-4109 | Add `CurrentTimeSkipPhase == None` guard to pipeline-batch increment | F1 |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | 4305+ | Add comment in `AlignPromptNarrativeStateWithV2Async` documenting non-sync of phase fields | F4 |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | 4554 | Set `CurrentTimeSkipPhase = CloseScene` instead of `TimeSkipPending = true` | F5 |
| `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` | 17-32 | Update directive text assertions for split; add two-phase state-machine tests | F9 |
| `DreamGenClone.Tests/RolePlay/MultiEncounterClimaxTests.cs` | — | No `TimeSkipPending` references found (F8). Remove from modify list. | F8 |

## New vs. Replace Decision

**Decision: Option A (Replace Default)** — Change the existing
`[ClimaxMode:multi-encounter]` behavior to always use the two-phase split. Simpler
implementation, fewer code paths, and the two-phase behavior is strictly better for
narrative quality. All existing multi-encounter themes automatically get the new flow.

## Edge Cases

1. **User Instruction during CloseScene phase**: Skip keeps the phase at `CloseScene`.
   Retries next turn. Same pattern as current `TimeSkipPending` retry logic.

2. **User Instruction during AdvanceTime phase**: Skip keeps the phase at `AdvanceTime`.
   Retries next turn. Identical retry pattern.

3. **Session ends between Turn 1 and Turn 2**: `CurrentTimeSkipPhase = AdvanceTime`
   persists in DB. Harmless — next Continue picks it up and injects the advance-time
   instruction.

4. **User navigates away mid-transition**: State persists in DB. On return, the phase
   continues from wherever it left off.

5. **What if the close-scene generation already establishes ordinary life?**: Turn 2's
   advance-time instruction is still injected — the model will either confirm the
   transition or add a time-skip. The directive acts as guidance, not a forced action.

6. **Both phases blocked by user instruction across multiple turns**: `CurrentTimeSkipPhase`
   remains until injection succeeds. No timeout — the phase persists indefinitely until
   the engine can inject it. This matches the current "retry until success" semantics.

7. **`isNewEncounterStart` during AdvanceTime retry** `[F3]`: When AdvanceTime injection is
   skipped (user instruction active), the fallback prompt must NOT use
   `"Continue the scene naturally."` (the `isNewEncounterStart` branch). The phase check
   `CurrentTimeSkipPhase == TimeSkipPhase.None` prevents this.

8. **Pipeline-batch increment during time-skip** `[F1]`: When `CurrentTimeSkipPhase !=
   None`, the pipeline-batch `InteractionsInCurrentEncounter += generatedSinceLastEval` is
   skipped to prevent double-counting with the per-interaction increment.

## Implementation Steps

### Phase 1: State + Persistence (no behavior change)

**Goal**: Introduce the enum and persistence without changing runtime behavior. The system
still reads/writes the old way via back-compat.

1. **Add `TimeSkipPhase` enum** to `DreamGenClone.Domain/RolePlay/` (new file or in
   `AdaptiveScenarioState.cs`). Values: `None=0, CloseScene=1, AdvanceTime=2`.

2. **Add `CurrentTimeSkipPhase` property** to `AdaptiveScenarioState`. Keep
   `TimeSkipPending` temporarily (do not remove yet — Phase 3 removes it).

3. **Add schema migration** in `RolePlayStateRepository.EnsureAdaptiveStateSchemaAsync`:
   add `CurrentTimeSkipPhase INTEGER NOT NULL DEFAULT 0` column + backfill from
   `TimeSkipPending`.

4. **Update `SaveAdaptiveStateAsync`**: write both `CurrentTimeSkipPhase` (as int) and
   `TimeSkipPending` (as int, derived: `CurrentTimeSkipPhase != None`). This keeps old
   code paths that read `TimeSkipPending` working during the transition.

5. **Update `LoadAdaptiveStateAsync`**: read `CurrentTimeSkipPhase` with back-compat
   fallback to `TimeSkipPending`.

6. **Build + run existing tests.** All should pass — no behavior changed.

### Phase 2: Engine Logic (behavior change)

**Goal**: Switch the engine to the two-phase flow.

7. **Update `TryDetectEncounterBoundaryAsync`** (line 4554): set
   `CurrentTimeSkipPhase = CloseScene` instead of `TimeSkipPending = true`. Set
   `IsStateDirty = true`.

8. **Rewrite overflow loop pre-loop gate** (lines 1496-1542): phase-aware gate logic per
   the corrected pseudocode above. Reset `InteractionsInCurrentEncounter = 0` for both
   phases. Set `IsStateDirty = true` on phase mutation.

9. **Rewrite per-actor prompt selection** (lines 1557-1575): split CloseScene/AdvanceTime
   directives. Add `CurrentTimeSkipPhase == TimeSkipPhase.None` check to
   `isNewEncounterStart`.

10. **Add phase guard to pipeline-batch increment** (line 4107-4109): skip when
    `CurrentTimeSkipPhase != None`.

11. **Add comment to `AlignPromptNarrativeStateWithV2Async`** (line 4305+): document that
    phase fields are intentionally not synced from DB.

12. **Build.** Fix any compile errors from the `TimeSkipPending` → `CurrentTimeSkipPhase`
    rename.

### Phase 3: Cleanup + Tests

**Goal**: Remove the legacy field and add test coverage.

13. **Remove `TimeSkipPending`** from `AdaptiveScenarioState`. Update all remaining
    references (repository write should now only write `CurrentTimeSkipPhase`; the
    `TimeSkipPending` column stays in the schema as a dead column).

14. **Update `MultiEncounterTimeSkipTests.cs`**:
    - Replace combined-directive text assertions with per-phase assertions.
    - Add test: `CloseScene_Phase_Transitions_To_AdvanceTime`.
    - Add test: `AdvanceTime_Phase_Transitions_To_None`.
    - Add test: `UserInstruction_Skips_CloseScene_Keeps_Phase`.
    - Add test: `UserInstruction_Skips_AdvanceTime_Keeps_Phase`.
    - Add test: `isNewEncounterStart_False_During_AdvanceTime_Retry` `[F3]`.
    - Add test: `PipelineBatchIncrement_Skipped_During_TimeSkip` `[F1]`.
    - Add test: `CurrentTimeSkipPhase_Survives_Pipeline_Save_Reload` `[F2]`.

15. **Remove `MultiEncounterClimaxTests.cs`** from the modify list — no
    `TimeSkipPending` references exist there `[F8]`.

16. **Build + run all tests.** Verify no regressions.

### Verification Checklist

- [ ] `CurrentTimeSkipPhase` persists across turn boundaries (DB round-trip).
- [ ] CloseScene injection fires on Turn 1, transitions to AdvanceTime.
- [ ] AdvanceTime injection fires on Turn 2, transitions to None.
- [ ] `InteractionsInCurrentEncounter` is not double-counted during time-skip turns.
- [ ] `isNewEncounterStart` does not fire during AdvanceTime retry.
- [ ] User instruction skip works for both phases.
- [ ] `AlignPromptNarrativeStateWithV2Async` does not clobber the phase mid-loop.
- [ ] Legacy `TimeSkipPending = 1` rows are backfilled to `CurrentTimeSkipPhase = CloseScene`.
- [ ] `IsStateDirty` is set on every phase mutation.
- [ ] No hardcoded fallback for `CurrentTimeSkipPhase` (per repo no-fallback rules).

## Backlog Reference

**B-051**: Multi-encounter Climax time-skip — split into two-turn close-scene + advance-time instructions.

---

# Code Review Findings (2026-06-24)

This section documents issues found by cross-referencing the proposed plan against the
actual codebase. Each finding includes the file/line evidence, severity, and a recommended
mitigation. The plan as written is **not safe to implement verbatim** — several issues
below would cause bugs or regressions.

## Finding 1 — CRITICAL: `InteractionsInCurrentEncounter` double-increment will break the `AdvanceTime` gate

### Evidence

The plan's "Important: InteractionsInCurrentEncounter Gate" section acknowledges that
`InteractionsInCurrentEncounter` will be ≥ 1 on Turn 2 (AdvanceTime) because the CloseScene
generation increments it. The plan proposes two alternative fixes but does not commit to one,
and **both proposed fixes are wrong** because they misdiagnose the increment source.

The counter is incremented in **two** places, not one:

1. **Per-interaction** in `UpdateStateAndDetectEncounterAsync` (`RolePlayEngineService.cs:2400-2404`):
   ```csharp
   if (session.AdaptiveState.CurrentEncounterNumber > 0
       && session.AdaptiveState.CurrentPhase == NarrativePhase.Climax)
   {
       session.AdaptiveState.InteractionsInCurrentEncounter++;
   }
   ```
   This fires for **every** interaction generated in the overflow loop (including the
   CloseScene instruction interaction), because `UpdateStateAndDetectEncounterAsync` is
   called at the end of each iteration (line 1587).

2. **Pipeline-batch** in `RunRolePlayV2PipelinesAsync` (`RolePlayEngineService.cs:4107-4109`):
   ```csharp
   else if (isMultiEncounterClimax && finalPhase == Climax && priorPhase == Climax && generatedSinceLastEval > 0)
   {
       v2State.InteractionsInCurrentEncounter += generatedSinceLastEval;
   }
   ```
   This runs **after** the overflow loop (line 1697) and adds `generatedSinceLastEval`
   (the count of Npc/Custom/System interactions created since `LastEvaluationUtc`).

### Why the plan's fixes fail

- **Plan fix A** ("Don't gate on `InteractionsInCurrentEncounter` for AdvanceTime; gate
  primarily on `CurrentTimeSkipPhase != None`"): This works for the gate itself, but the
  `InteractionsInCurrentEncounter` value is still corrupted by the double-increment. The
  `minIxns = 4` gate in `TryDetectEncounterBoundaryAsync` (line 4537) and the
  `isNewEncounterStart` prompt branch (line 1568) both depend on this counter being
  accurate. After a CloseScene+AdvanceTime cycle the counter will be inflated by the
  pipeline-batch add, causing the next encounter's `minIxns` gate to pass too early and
  `isNewEncounterStart` to misfire.

- **Plan fix B** ("extend the `TimeSkipPending` reset logic that forces
  `InteractionsInCurrentEncounter = 0` when pending"): The current reset at line 1499-1501
  only fires **before** the overflow loop. On Turn 2 (AdvanceTime), the reset would set the
  counter to 0, but then the per-interaction increment (source 1) and the pipeline-batch
  add (source 2) would both re-increment it. The pipeline-batch add is the real problem:
  it runs unconditionally for any Climax turn with `generatedSinceLastEval > 0` and has no
  awareness of the time-skip phase.

### Recommended fix

The pipeline-batch increment (source 2) must be made phase-aware. When
`CurrentTimeSkipPhase != None`, the pipeline-batch add must be skipped (the per-interaction
increment already accounts for the generated interactions). Concretely, change line 4107-4109
to:

```csharp
else if (isMultiEncounterClimax && finalPhase == Climax && priorPhase == Climax
         && generatedSinceLastEval > 0
         && v2State.CurrentTimeSkipPhase == TimeSkipPhase.None)
{
    v2State.InteractionsInCurrentEncounter += generatedSinceLastEval;
}
```

Additionally, the pre-overflow reset (line 1499-1501) must be extended to reset the counter
for **both** phases, and the `AdvanceTime` gate must NOT use
`InteractionsInCurrentEncounter == 0` (use `CurrentTimeSkipPhase == AdvanceTime` as the
primary gate). The `CloseScene` gate keeps `InteractionsInCurrentEncounter == 0` to prevent
re-firing mid-encounter.

**Severity**: Critical — without this, the AdvanceTime phase either never fires (gate fails)
or the encounter counter drifts, breaking boundary detection for all subsequent encounters.

## Finding 2 — CRITICAL: `HydrateV2State` does not carry forward `TimeSkipPending`/`CurrentTimeSkipPhase`, so the phase is lost when `RunRolePlayV2PipelinesAsync` runs

### Evidence

The overflow loop (lines 1496-1587) runs **first** and mutates
`session.AdaptiveState.CurrentTimeSkipPhase` (e.g., `CloseScene → AdvanceTime`). Then
`RunRolePlayV2PipelinesAsync` (line 1697) runs and calls `HydrateV2State(session,
previousState)` (line 4131), where `previousState` is the state loaded from the DB at the
**start** of the pipeline (before the overflow loop's mutation).

`HydrateV2State` (`RolePlayEngineService.cs:4135-4354`) selectively carries forward fields
from `previousState` into `mapped`. It carries forward `CurrentBeatCode`,
`TurnsInCurrentBeat`, `ThemeMachineSnapshot`, `CharacterSnapshots`, `PhaseOverride*`, etc.
— but it does **NOT** carry forward `TimeSkipPending`, `CurrentEncounterNumber`, or
`InteractionsInCurrentEncounter`.

This means:
- The `mapped` state returned by `HydrateV2State` retains the `session.AdaptiveState`
  values for these fields (since `mapped = session.AdaptiveState` at line 4138 and these
  fields are not overwritten). **This is currently correct by accident** — the in-memory
  `session.AdaptiveState` already has the overflow loop's mutations.
- However, the pipeline then does `v2State.InteractionsInCurrentEncounter +=
  generatedSinceLastEval` (line 4109) on the hydrated state, and at line 4121 calls
  `SaveAdaptiveStateAsync(v2State, ...)` then `SyncSessionAdaptiveStateFromV2(session,
  v2State)` (line 4122), which **replaces** `session.AdaptiveState` with `v2State`.

### Impact on the two-phase plan

For the **current** bool implementation this is benign because `TimeSkipPending` is set to
`false` by the overflow loop before the pipeline runs, and the pipeline doesn't touch it.

For the **two-phase** implementation, this becomes a hazard:
- If the overflow loop sets `CurrentTimeSkipPhase = AdvanceTime` (Turn 1 CloseScene fired),
  the pipeline's `SaveAdaptiveStateAsync` will persist `AdvanceTime` — **correct**.
- But if the overflow loop **skipped** injection (user instruction active) and left
  `CurrentTimeSkipPhase = CloseScene`, the pipeline still persists `CloseScene` — **correct**.
- The risk is the `InteractionsInCurrentEncounter` mutation at line 4109 (see Finding 1)
  which runs on the same `v2State` object that gets persisted and synced back.

### Recommended fix

No change needed to `HydrateV2State` for the phase field itself (the in-memory value flows
through correctly). But the implementer MUST verify that the pipeline-batch increment
(Finding 1) does not corrupt the counter that the next turn's overflow loop gates on. Add an
explicit regression test that asserts `CurrentTimeSkipPhase` survives a full
overflow-loop → pipeline → save → reload cycle.

**Severity**: Critical (latent) — works today by accident; the two-phase change must not
introduce a code path where the pipeline overwrites the phase.

## Finding 3 — HIGH: `isNewEncounterStart` prompt branch (line 1568) will misfire during the AdvanceTime turn

### Evidence

```csharp
var isNewEncounterStart = session.AdaptiveState.CurrentEncounterNumber > 0
    && session.AdaptiveState.InteractionsInCurrentEncounter == 0;
promptText = isNewEncounterStart
    ? "Continue the scene naturally."
    : "Continue the current encounter naturally from where it left off.";
```

This branch fires for the **non-time-skip** first actor (i.e., `timeSkipActive == false`).
On the AdvanceTime turn, if the gate is restructured per Finding 1 so that `timeSkipActive`
is true, this branch is skipped — good. But if the AdvanceTime injection is **skipped** due
to a user instruction (retry case), `timeSkipActive == false` and this branch fires. With
`InteractionsInCurrentEncounter` reset to 0 (per the pre-overflow reset), `isNewEncounterStart
== true`, producing "Continue the scene naturally." — which is reasonable but semantically
wrong: the scene was already closed by the CloseScene turn, and the model should be advancing
time, not "continuing the scene."

### Recommended fix

When `CurrentTimeSkipPhase == AdvanceTime` and injection is skipped (user instruction
active), the fallback prompt should not use the `isNewEncounterStart` branch. Add a phase
check:

```csharp
var isNewEncounterStart = session.AdaptiveState.CurrentEncounterNumber > 0
    && session.AdaptiveState.InteractionsInCurrentEncounter == 0
    && session.AdaptiveState.CurrentTimeSkipPhase == TimeSkipPhase.None;
```

**Severity**: High — produces narratively wrong prompt text in the retry path.

## Finding 4 — HIGH: `AlignPromptNarrativeStateWithV2Async` (line 1584) reloads state from DB mid-overflow-loop and may clobber the phase

### Evidence

Inside the overflow loop, before each `ContinueAsync` call (line 1584):
```csharp
await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
```

`AlignPromptNarrativeStateWithV2Async` (line 4305) calls
`_stateRepository.LoadAdaptiveStateAsync(session.Id, ...)` and overwrites several
`session.AdaptiveState` fields from the DB snapshot: `ActiveVariantId`, `CurrentPhase`,
`ActiveScenarioId`, `PhaseOverride*`, `InteractionsSinceCommitment`,
`InteractionsInApproaching`, `ThemeMachineSnapshot`, `CharacterSnapshots`,
`EncounterSummaries`.

It does **NOT** overwrite `TimeSkipPending`, `CurrentEncounterNumber`, or
`InteractionsInCurrentEncounter` — so the in-memory phase value survives this reload.
**This is correct today.**

### Impact on the two-phase plan

The same accidental correctness applies to `CurrentTimeSkipPhase`. However, the implementer
must be aware that `AlignPromptNarrativeStateWithV2Async` is called **inside** the overflow
loop (once per actor), so any future change that adds `CurrentTimeSkipPhase` to the
`AlignPromptNarrativeStateWithV2Async` sync list would clobber the phase mid-loop (e.g.,
reset `AdvanceTime` back to `CloseScene` if the DB hasn't been persisted yet).

### Recommended fix

Add a code comment in `AlignPromptNarrativeStateWithV2Async` explicitly noting that
`CurrentTimeSkipPhase`, `CurrentEncounterNumber`, and `InteractionsInCurrentEncounter` are
intentionally NOT synced from the DB snapshot here (they are in-memory turn-scoped state).
Do not add them to the sync list.

**Severity**: High (latent) — a future maintainer could easily introduce a regression.

## Finding 5 — MEDIUM: Deferred persistence (`IsStateDirty`) is not set when the overflow loop mutates the phase

### Evidence

`TryDetectEncounterBoundaryAsync` sets `state.IsStateDirty = true` (line 4556) when it
advances the encounter and sets `TimeSkipPending = true`. This ensures the boundary
detection is persisted at turn completion (lines 1701-1704).

However, the overflow loop's mutation of `TimeSkipPending = false` (line 1516) and the
proposed `CurrentTimeSkipPhase` transitions (`CloseScene → AdvanceTime`, `AdvanceTime →
None`) do **NOT** set `IsStateDirty = true`. They rely on the pipeline's
`SaveAdaptiveStateAsync` call (line 4121) to persist the state.

This works because `RunRolePlayV2PipelinesAsync` always calls `SaveAdaptiveStateAsync`
unconditionally (line 4121), and the post-pipeline `IsStateDirty` flush (lines 1701-1704)
is a backup. But it's fragile: if a future change makes the pipeline's save conditional,
the phase transition would be lost.

### Recommended fix

Set `session.AdaptiveState.IsStateDirty = true` whenever the overflow loop mutates
`CurrentTimeSkipPhase`. This makes the persistence intent explicit and survives any future
pipeline-save refactoring.

**Severity**: Medium — works today, fragile for future changes.

## Finding 6 — MEDIUM: Schema migration must handle the bool→enum transition for existing rows

### Evidence

The current schema migration (line 1018-1022):
```csharp
if (!await HasColumnAsync(connection, "RolePlayV2AdaptiveStates", "TimeSkipPending", cancellationToken))
{
    add.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN TimeSkipPending INTEGER NOT NULL DEFAULT 0";
}
```

The plan says "update schema migration" but does not specify the migration strategy. SQLite
does not support `ALTER COLUMN` or easily renaming columns. Existing rows have
`TimeSkipPending` as 0 or 1.

### Recommended fix

Two viable strategies:

**Strategy A (rename + reinterpret)**: Keep the `TimeSkipPending` column name but reinterpret
0 = `None`, 1 = `CloseScene`. Add a new `CurrentTimeSkipPhase` column (INTEGER, default 0)
and migrate: `UPDATE RolePlayV2AdaptiveStates SET CurrentTimeSkipPhase = TimeSkipPending
WHERE TimeSkipPending = 1`. Then drop `TimeSkipPending` (SQLite requires table rebuild to
drop a column). This is complex.

**Strategy B (additive, recommended)**: Add a new `CurrentTimeSkipPhase INTEGER NOT NULL
DEFAULT 0` column. On read, if `CurrentTimeSkipPhase == 0` AND legacy `TimeSkipPending ==
1`, treat as `CloseScene` (backward compat). On write, always write `CurrentTimeSkipPhase`
and write 0 to `TimeSkipPending` (effectively retiring it). Keep the `TimeSkipPending`
column in the schema for backward compat but stop reading it once all rows are migrated.

The plan must explicitly specify which strategy and include the migration SQL. The
`LoadAdaptiveStateAsync` read logic (line 591) and `SaveAdaptiveStateAsync` write logic
(line 325) must both be updated consistently.

**Severity**: Medium — existing in-flight sessions with `TimeSkipPending = true` would be
silently reset to `None` (losing the pending time-skip) if the migration is not handled.

## Finding 7 — MEDIUM: `HasRecentUserInstruction` window check may false-negative on the AdvanceTime turn

### Evidence

`HasRecentUserInstruction` (line 227-234) checks the last `windowSize` (3) interactions for
an `ActorName == "Instruction"` with null/empty `GeneratedByCommand`.

The CloseScene injection creates an interaction via `_continuationService.ContinueAsync`
with `actorIntent = PromptIntent.Instruction`. However, per
`RolePlayContinuationService.cs:258-274`, the resulting interaction has:
- `ActorName` = the actor's name (e.g., "NPC", "Becky"), **NOT** "Instruction"
- `GeneratedByCommand` = "Continue"
- `InteractionType` = Npc/Custom/User (based on actor), **NOT** System

So the CloseScene interaction does **NOT** satisfy `HasRecentUserInstruction` — **correct**.
The one-shot design holds for the two-phase split as well.

### Remaining concern

If the user types an Instruction on the turn between CloseScene and AdvanceTime, the
AdvanceTime injection is correctly skipped (retry next turn). But the CloseScene interaction
is now in the window. Since it has `ActorName != "Instruction"`, it doesn't trigger the
skip — **correct**. No issue here, but the plan should document this as a verified
invariant.

**Severity**: Medium (verification) — no bug, but the plan should document the invariant.

## Finding 8 — LOW: `MultiEncounterClimaxTests.cs` does not reference `TimeSkipPending`

### Evidence

A grep for `TimeSkipPending|TimeSkip|timeSkip` in
`DreamGenClone.Tests/RolePlay/MultiEncounterClimaxTests.cs` returned **no matches**. The
plan's "Files to Modify" table lists this file for "Update any tests referencing
`TimeSkipPending`", but there are none.

### Recommended fix

Remove `MultiEncounterClimaxTests.cs` from the files-to-modify list, or verify the grep
isn't missing partial matches. No code change needed in that file.

**Severity**: Low — plan inaccuracy.

## Finding 9 — LOW: `MultiEncounterTimeSkipTests.cs` tests assert on directive text that will change

### Evidence

`MultiEncounterTimeSkipTests.cs` lines 17-32 assert on the **combined** directive text:
```csharp
var directive = "Close the current encounter naturally. Then advance time to a new moment — ...";
Assert.Contains("Close the current encounter", directive, ...);
Assert.Contains("advance time", directive, ...);
```

After the split, no single directive contains both phrases. These tests will fail.

### Recommended fix

Update the tests to assert on each phase's directive separately:
- CloseScene directive: `Assert.Contains("Close the current encounter", ...)`
- AdvanceTime directive: `Assert.Contains("advance time", ...)`

Add new tests for the two-phase state machine transitions (CloseScene → AdvanceTime → None).

**Severity**: Low — test-only, but build will fail without updates.

## Finding 10 — LOW: Plan's pseudocode uses `session.AdaptiveState.CurrentTimeSkipPhase` but the field is on `AdaptiveScenarioState`

### Evidence

The plan's pseudocode references `session.AdaptiveState.CurrentTimeSkipPhase` correctly
(matching the existing `session.AdaptiveState.TimeSkipPending` pattern). This is consistent.
No issue — noting for completeness.

## Summary of Required Plan Updates

| # | Severity | Issue | Action |
|---|---|---|---|
| 1 | Critical | `InteractionsInCurrentEncounter` double-increment breaks AdvanceTime gate | Make pipeline-batch increment phase-aware; restructure gates |
| 2 | Critical | `HydrateV2State` does not carry forward phase (works by accident) | Add regression test; document invariant |
| 3 | High | `isNewEncounterStart` misfires during AdvanceTime retry | Add phase check to `isNewEncounterStart` |
| 4 | High | `AlignPromptNarrativeStateWithV2Async` could clobber phase | Add comment; do not sync phase from DB mid-loop |
| 5 | Medium | `IsStateDirty` not set on phase transition | Set `IsStateDirty = true` on phase mutation |
| 6 | Medium | Schema migration strategy unspecified | Specify Strategy B (additive column + back-compat read) |
| 7 | Medium | `HasRecentUserInstruction` invariant undocumented | Document the one-shot invariant |
| 8 | Low | `MultiEncounterClimaxTests.cs` has no `TimeSkipPending` refs | Remove from files-to-modify |
| 9 | Low | Existing tests assert combined directive text | Update tests for split directives |
| 10 | Low | Pseudocode field reference | No action needed |

## Additional Files to Modify (Beyond Plan)

The plan lists 5 files. Based on this review, the following must also be considered:

- **`DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs`** — already listed,
  but the migration logic (lines 1018-1022) and read logic (line 591) need the Strategy B
  back-compat path, not just a column rename.
- **`DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`** — already listed,
  but the changes are more extensive than the plan implies:
  - Line 4107-4109: pipeline-batch increment must be phase-aware (Finding 1)
  - Line 1568: `isNewEncounterStart` must check phase (Finding 3)
  - Line 4305+: `AlignPromptNarrativeStateWithV2Async` must NOT sync phase (Finding 4)
  - Lines 1516, 1530: set `IsStateDirty = true` on phase mutation (Finding 5)

## Verified Invariants (No Change Needed)

- The CloseScene/AdvanceTime injection interactions have `ActorName` = actor name (not
  "Instruction") and `GeneratedByCommand = "Continue"`, so `HasRecentUserInstruction` will
  not false-positive on them. The one-shot design is preserved.
- `HydrateV2State` does not overwrite `CurrentEncounterNumber`/`InteractionsInCurrentEncounter`/
  `TimeSkipPending` from `previousState`, so in-memory mutations survive the pipeline call.
- The pre-overflow reset (line 1499-1501) correctly fires before the gate check.
