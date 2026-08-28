# Root Cause Analysis: Time-Skip State Race Condition

**Date**: 2026-07-04
**Author**: Implementation analysis from B-056 debugging session

## Symptom

Encounter boundary detection fires successfully (`EncounterBoundaryAdvanced` debug event with "1 -> 2") but the persisted state never advances — `CurrentEncounterNumber` stays at 1, `CurrentTimeSkipPhase` stays at `None`. The encounter never ends and the model keeps generating indefinitely regardless of pacing markers.

## Root Cause

Every turn executes this sequence at multiple call sites (lines 838–839, 946–947, 1289, 1757 of `RolePlayEngineService.cs`):

```
1. UpdateStateAndDetectEncounterAsync(session, interaction, ct)
   → detection fires → mutates session.AdaptiveState in memory
   → sets CurrentEncounterNumber=2, CurrentTimeSkipPhase=CloseScene, IsStateDirty=true

2. RunRolePlayV2PipelinesAsync(session, ...)
   → LoadAdaptiveStateAsync(session.Id)       ← reads STALE DB (encounter=1, phase=None)
   → HydrateV2State(session, previousState)   ← COPIES DB values over in-memory state
   → session.AdaptiveState is now BACK to encounter=1, phase=None
   → detection's in-memory mutations are LOST

3. Turn ends → finally block checks IsStateDirty
   → SaveAdaptiveStateAsync(session.AdaptiveState)
   → saves encounter=1, phase=None to DB
   → DB stays at encounter=1 forever
```

The detection fires correctly every time, but `HydrateV2State` unconditionally overwrites the freshly-mutated fields with stale DB values before the save runs. `HydrateV2State` copies `CurrentEncounterNumber`, `CurrentTimeSkipPhase`, `InteractionsInCurrentEncounter`, and `LastEncounterEvidenceSpan` from the DB snapshot — even when detection just advanced them in the same turn.

## Why it keeps recurring

`HydrateV2State` serves **two conflicting purposes**:

1. **Session reload** (app restart / new session load): DB IS authoritative — in-memory is empty/default
2. **Mid-turn pipeline** (same turn as detection): in-memory IS authoritative — DB is stale (last turn's snapshot)

Every attempted fix adds conditional logic ("if in-memory is newer, keep it") that introduces new edge cases:
- `CurrentTimeSkipPhase != None` → fails when phase cycles back to `None` but encounter number is bumped
- `CurrentEncounterNumber > previous` → fails when detection resets encounter to 0 on phase exit
- `IsStateDirty` → not set on `InteractionsInCurrentEncounter++` (separate gap)
- Timestamp comparison → adds complexity, still races if save is debounced

None of these conditionals can reliably distinguish "mid-turn detection just fired" from "fresh app restart" because both present the same surface values (in-memory `None`/`0`, DB `None`/`0`).

## Fix Options

### Option A: Single source of truth — DB only

Remove in-memory mutation entirely. Detection writes directly to DB. Overflow injection reads from DB. No `HydrateV2State` overwrite needed.

**Changes:**
- `TryDetectEncounterBoundaryAsync`: after mutating state, immediately `SaveAdaptiveStateAsync` (synchronous, not deferred)
- `RunRolePlayV2PipelinesAsync`: `HydrateV2State` becomes pure DB→memory sync (no conditional logic)
- `IsStateDirty` for time-skip fields: removed (DB is always current)
- Overflow injection: reads `session.AdaptiveState` which was just loaded from DB

**Pros:** One copy. No race. No conditional logic. Impossible to desync.
**Cons:** One extra synchronous DB write per detection (rare — once per encounter boundary, not per interaction).

---

### Option B: Remove time-skip fields from HydrateV2State restore

Never restore time-skip fields from DB in `HydrateV2State`. They live only in memory. On app restart they reset to `None`/`0`.

**Changes:**
- `HydrateV2State`: delete the 4 lines that copy `CurrentTimeSkipPhase`, `CurrentEncounterNumber`, `InteractionsInCurrentEncounter`, `LastEncounterEvidenceSpan`
- `SaveAdaptiveStateAsync`: still writes them (for diagnostic panel visibility)
- On app restart: time-skip resets to default — user replays the encounter boundary

**Pros:** Simplest code change. No race.
**Cons:** App restart mid-transition loses state. Acceptable for single-user local app.

---

### Option C: Reorder calls — pipeline before detection

Swap the call order at the 4 sites: `RunRolePlayV2PipelinesAsync` first, then `UpdateStateAndDetectEncounterAsync`.

**Changes:**
- Lines 838–839, 946–947, 1289, 1757: swap `UpdateStateAndDetectEncounterAsync` and `RunRolePlayV2PipelinesAsync`
- `HydrateV2State`: revert to unconditional DB restore (no conditional logic)
- Nothing between hydration and detection reads time-skip state expecting it to be already advanced

**Pros:** 4-line change. No new DB writes. No conditional logic. Natural ordering (hydrate DB first, then mutate).
**Cons:** Detection runs later in the turn — must verify no intervening code reads time-skip fields expecting pre-mutation values.

---

### Option D: Persist immediately after detection

Keep current call order, but make `TryDetectEncounterBoundaryAsync` save to DB synchronously after mutation.

**Changes:**
- `TryDetectEncounterBoundaryAsync`: after mutating state and setting `IsStateDirty = true`, call `SaveAdaptiveStateAsync` immediately (before returning)
- `HydrateV2State`: revert to unconditional DB restore — the DB is always current
- Remove `IsStateDirty` gating for time-skip fields (detection saves synchronously, not deferred)

**Pros:** DB always authoritative and current. `HydrateV2State` is simple unconditional restore. No race.
**Cons:** One extra DB write per detection.

## How to Reproduce

1. Start a roleplay session with a theme that has `[ClimaxMode:multi-encounter]` in Climax phase guidance
2. Play through a sexual encounter until the model produces encounter-completion keywords
3. Observe `EncounterBoundaryAdvanced` debug event with "1 -> 2"
4. Check `RolePlayV2AdaptiveStates`: `CurrentEncounterNumber` is still 1, `CurrentTimeSkipPhase` is still 0
5. The encounter never ends regardless of pacing markers

## Affected Files

- `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — call order at lines 838–839, 946–947, 1289, 1757; `HydrateV2State` at line 4237; `TryDetectEncounterBoundaryAsync` at line 4636
- `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` — `IsStateDirty` contract docstring
