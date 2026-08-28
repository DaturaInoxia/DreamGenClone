# B-071: BUG — Instructions and character messages count as turns (double-count)

**State**: `designed`
**Priority**: high
**Scope**: small

---

## TL;DR

A single user-submitted instruction (`PromptIntent.Instruction`) currently increments **two** turn counters in one `SubmitPromptAsync` call:

1. **"1 to add the instruction"** — `StartTurnAsync` + `session.AdaptiveState.ObservedTurnCount++` at turn-start (line ~1082).
2. **"1 to run the instruction in a turn"** — `RunRolePlayV2PipelinesAsync` → `v2State.TurnCountInPhase = previous + 1` (line ~3737), plus `generatedSinceLastEval = 1` which also advances `TurnsInCurrentBeat` / `TurnsInCurrentEncounter`.

Instructions are **meta-actions** (directions to the model), not narrative progression. They should increment **no** turn counter and create **no** `RolePlayV2Turns` row. The fix makes instructions run the pipeline (so phase-floor enforcement, scenario backfill, encounter detection, and stat updates still fire) but with `incrementTurnCount: false`, so every turn counter stays flat.

**Related items**: B-044 (turns first-class, `implemented`), B-068 (pacing, `designed`), B-069 (story stall, `implemented`), 001-replace-interactions-turns, 001-opening-period.

---

## Discovery Summary (verified in code)

### The double-count in `SubmitPromptAsync`

| Step | Location | What happens |
|---|---|---|
| 1. Turn start | `RolePlayEngineService.cs` ~1075-1083 | `StartTurnAsync(...)` inserts a `RolePlayV2Turns` row (`TurnKind = "SubmitPrompt"`) **unconditionally**, then `session.AdaptiveState.ObservedTurnCount++` **unconditionally** |
| 2. Steer branch | ~1196-1224 | `/steer` instruction early-returns — pipeline NOT run; `CompleteTurnAsync` still called (line ~1224) |
| 3. Command detection | ~1235-1237 | `nextPhaseCommandRequested`, `explicitClimaxCompletionRequested` |
| 4. Pipeline | ~1300 (climax), ~1334 (main) | `RunRolePlayV2PipelinesAsync` runs → `TurnCountInPhase++` (line ~3737) and `generatedSinceLastEval = 1` → `TurnsInCurrentBeat` / `TurnsInCurrentEncounter` also advance |
| 5. Turn complete | ~1377 | `CompleteTurnAsync` called **unconditionally** |

So for a **plain instruction** (the common case), one submission produces:

- 1 × `RolePlayV2Turns` row
- +1 `ObservedTurnCount`
- +1 `TurnCountInPhase`
- +1 `TurnsInCurrentBeat` (if beat active)
- +1 `TurnsInCurrentEncounter` (if encounter active)

### Behavior by instruction variant (today)

| Variant | `StartTurnAsync` + `ObservedTurnCount++` | Pipeline runs? | `TurnCountInPhase++` | Net turn count |
|---|---|---|---|---|
| Steer (`/steer`) | ✅ | ❌ (early return) | ❌ | **1** (ObservedTurnCount only, not persisted — see persistence gap below) |
| Plain instruction | ✅ | ✅ | ✅ | **2** (double-count) |
| `/nextphase` | ✅ | ✅ | ✅ | **2** (double-count) |
| `/completeclimax` | ✅ | ✅ | ✅ | **2** (double-count) |
| Message / Narrative (SendButton) | ✅ | ✅ | ✅ | **2** — correct (real turn) |
| Message (PlusButton, character) | ✅ | ✅ | ✅ | **2** — correct (real character action turn) |

### Persistence gap on the steer path (pre-existing)

The steer path early-returns **before** `RunRolePlayV2PipelinesAsync`, so `SaveAdaptiveStateAsync` (which runs at the end of the pipeline, line ~4937) is **never called**. The `ObservedTurnCount++` from step 1 is therefore only in memory. If the session is reloaded from the DB before the next real turn, the increment is silently lost. The B-071 fix removes the increment for instructions entirely, which also resolves this gap.

### Where the counters are persisted / retrieved

| Counter | Stored in | Written by | Read by |
|---|---|---|---|
| `ObservedTurnCount` | `RolePlayV2ThemeTrackerMeta` | `ReplaceThemeTrackerMetaAsync` (via `SaveAdaptiveStateAsync`) | `LoadThemeTrackerMetaAsync` (line ~774) |
| `TurnCountInPhase` | `RolePlayV2AdaptiveStates` | `SaveAdaptiveStateAsync` (line ~4937) | `LoadAdaptiveStateAsync` (line ~633) |
| Turns (rows) | `RolePlayV2Turns` | `StartTurnAsync` | `LoadTurnsAsync` |

`AdaptiveState` is `[JsonIgnore]` on the session blob — it lives **only** in V2 tables. Instruction interactions are persisted separately via `QueueRolePlaySessionSave` → session blob (interactions list), which is unaffected by this fix.

### Downstream consumers of the counters (why the double-count matters)

- **`ObservedTurnCount`** drives: Opening→BuildUp transition (`<= OpeningPeriodTurnCount = 3`), theme observation window (`<= SelectionMinimumTurns`), OtherMan overflow exclusion, persona rotation in overflow (`% 2 == 0`), opening-period direction in `ScenarioGuidanceSlot`.
- **`TurnCountInPhase`** drives: BuildUp→Committed narrative gate (`NarrativeGateMetricKeys.TurnsSinceCommitment`), decision-point creation (`% 3 == 0` in `DecisionPointService`), phase-transition thresholds.

Inflating both with instructions causes premature Opening→BuildUp, premature theme selection, premature gate evaluation, and spurious decision points.

---

## Design Decisions

1. **All user-submitted instructions are non-turns.** `PromptIntent.Instruction` (plain, `/steer`, `/nextphase`, `/completeclimax`) creates **no** `RolePlayV2Turns` row and increments **no** turn counter. Rationale: consistent with the backlog title; phase commands reset `TurnCountInPhase` on transition anyway; `manualPhaseAdvanceTarget` bypasses gates; and it keeps the `persistedTurn` null-guard logic simple (one rule for all instructions).

   - **Note / decision to confirm during implementation**: `/completeclimax` generates real multi-actor finish-move narrative. With `incrementTurnCount: false` and `generatedSinceLastEval = 0`, the finish-move does not advance the Climax beat cursor / `TurnsInCurrentBeat` / `TurnsInCurrentEncounter`. This is consistent with "instructions are not turns", but it should be manually verified that the climax conclusion still reads correctly without beat-cursor advancement.

2. **Instructions still run the pipeline** (plain, `/nextphase`, `/completeclimax`). This preserves phase-floor enforcement, scenario backfill, encounter detection, and stat updates — the behaviors tests `ActivePhaseFloor_PreventsBackslide` and `BuildUpBackfillsActiveScenario_WhenMissing` depend on. Only the turn-count increment is suppressed, via a new `incrementTurnCount` parameter.

3. **`generatedSinceLastEval` becomes 0 for non-counted turns.** It is coupled to the increment (line ~3738). Setting it to 0 for instructions stops `TurnsInCurrentBeat` and `TurnsInCurrentEncounter` from advancing too.

4. **`persistedTurn` becomes nullable** (`RolePlayTurn?`). `CompleteTurnAsync` calls are guarded with `if (persistedTurn is not null)`.

5. **Message/Narrative/Continue/ContinueAs/AddInteraction are untouched.** They are real turns and keep `StartTurnAsync`, `ObservedTurnCount++`, and `incrementTurnCount: true`.

---

## Fix Plan

### File 1: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

**Change A — `SubmitPromptAsync`: only start a turn for non-instruction intents (~lines 1075-1083)**

```csharp
var isInstruction = submission.Intent == PromptIntent.Instruction;
RolePlayTurn? persistedTurn = null;
if (!isInstruction)
{
    persistedTurn = await _stateRepository.StartTurnAsync(
        session.Id,
        "SubmitPrompt",
        submission.SubmittedVia.ToString(),
        initiatedByActorName,
        null,
        cancellationToken);
    session.AdaptiveState.ObservedTurnCount++;
}
await EnsureOpeningToBuildUpTransition(session, cancellationToken);
```

**Change B — steer early-return: guard `CompleteTurnAsync` (~line 1224)**

```csharp
if (persistedTurn is not null)
{
    await _stateRepository.CompleteTurnAsync(
        session.Id,
        persistedTurn.TurnId,
        outputInteractionIds,
        succeeded: true,
        cancellationToken: cancellationToken);
}
return interaction;
```

**Change C — main-path `CompleteTurnAsync`: guard (~line 1377)**

```csharp
if (persistedTurn is not null)
{
    await _stateRepository.CompleteTurnAsync(
        session.Id,
        persistedTurn.TurnId,
        outputInteractionIds,
        succeeded: true,
        cancellationToken: cancellationToken);
}
```

**Change D — `RunRolePlayV2PipelinesAsync` call sites in `SubmitPromptAsync` (~1300 and ~1334): pass `incrementTurnCount: !isInstruction`**

```csharp
await RunRolePlayV2PipelinesAsync(
    session,
    DecisionTrigger.InteractionStart,
    cancellationToken,
    explicitClimaxCompletionRequested: true,
    manualPhaseAdvanceTarget,
    incrementTurnCount: !isInstruction);
```

and

```csharp
await RunRolePlayV2PipelinesAsync(
    session,
    DecisionTrigger.InteractionStart,
    cancellationToken,
    explicitClimaxCompletionRequested,
    manualPhaseAdvanceTarget,
    incrementTurnCount: !isInstruction);
```

**Change E — `RunRolePlayV2PipelinesAsync` signature (~line 3697): add `bool incrementTurnCount = true`**

```csharp
private async Task RunRolePlayV2PipelinesAsync(
    RolePlaySession session,
    DecisionTrigger trigger,
    CancellationToken cancellationToken,
    bool explicitClimaxCompletionRequested = false,
    DreamGenClone.Domain.RolePlay.NarrativePhase? manualPhaseAdvanceTarget = null,
    bool incrementTurnCount = true)
```

**Change F — guard the turn-count increment block (~lines 3736-3738)**

```csharp
// B-044: Turns is a first-class stored unit. Increment by exactly 1 per turn
// (one adaptive pipeline evaluation per turn call), independent of how many
// interactions were generated in batch. Instructions (PromptIntent.Instruction)
// are meta-actions, NOT narrative turns — incrementTurnCount: false keeps every
// turn counter flat for them (B-071).
var previousPhaseTurnCount = Math.Max(0, v2State.TurnCountInPhase);
var generatedSinceLastEval = incrementTurnCount ? 1 : 0;
v2State.TurnCountInPhase = previousPhaseTurnCount + generatedSinceLastEval;
```

**Change G — other `RunRolePlayV2PipelinesAsync` call sites** (`AddInteractionAsync` ~850, `ContinueAsync` ~957, `ContinueAsAsync` ~1787): leave on the default `incrementTurnCount: true`. No change needed.

**Files NOT changed**: `RolePlayContinuationService.cs`, `RolePlayAdaptiveStateService.cs`, `DecisionPointService.cs`, `ScenarioSelectionService.cs`, `RolePlayStateRepository.cs`, `RolePlayWorkspace.razor`. The counters stay consistent because the increment sites are the only change.

### State save/retrieval checklist (verified against the fix)

| Concern | Status after fix |
|---|---|
| `ObservedTurnCount` for instructions | No longer incremented; stays at the last real-turn value; persisted correctly on the next pipeline save (and steer no longer mutates it in memory) |
| `TurnCountInPhase` for instructions | No longer incremented (guarded by `incrementTurnCount`) |
| `RolePlayV2Turns` rows | No row created for instructions (no `StartTurnAsync`) |
| Instruction interactions | Still added to `session.Interactions` and persisted via `QueueRolePlaySessionSave` (session blob) — unchanged |
| Real turns (Message/Narrative/Continue/ContinueAs) | Identical behavior — `StartTurnAsync`, `ObservedTurnCount++`, `incrementTurnCount: true` all preserved |
| `EnsureOpeningToBuildUpTransition` | Still invoked; now only real turns push `ObservedTurnCount` past `OpeningPeriodTurnCount`, so opening period lasts for real turns only — intended |
| Theme observation window | `ObservedTurnCount <= SelectionMinimumTurns` now only counts real turns — intended |

### Scope boundaries (what this fix does NOT touch)

- **`ContinueAsAsync` turn-index usages** (lines ~1673 / ~1762) and **`CompleteTurnAsync`** (line ~1808): these are in the real-turn path (`ContinueAsAsync`) where `persistedTurn` is always non-null. The backlog's original line references conflated these with `SubmitPromptAsync`; they must NOT be guarded/changed.
- **`AddInteractionAsync` / `ContinueAsync` `CompleteTurnAsync`** (lines ~897 / ~1007): real turns; unchanged.
- **Engine-injected instruction interactions** (e.g. `MultiEncounterTimeSkip`, created with `GeneratedByCommand` inside an already-started turn): these do not call `StartTurnAsync` and are not part of this bug; unchanged.

---

## Regression Analysis

| Existing test | Uses instruction? | Depends on pipeline for instructions? | Impact |
|---|---|---|---|
| `SubmitPromptAsync_ActivePhaseFloor_PreventsBackslide` | ✅ plain ("continue naturally") | ✅ pipeline must run to enforce floor | **PASSES** — pipeline still runs (Change D/F keep it), test asserts `CurrentPhase`/`PhaseOverrideFloor`, not `TurnCountInPhase` |
| `SubmitPromptAsync_BuildUpBackfillsActiveScenario_WhenMissing` | ✅ plain | ✅ pipeline must run to backfill scenario | **PASSES** — pipeline still runs, test asserts `ActiveScenarioId`, not `TurnCountInPhase` |
| `SubmitPromptAsync_Steer_DoesNotProgressPhaseState` | ✅ `/steer` | ❌ early-return | **PASSES** — `CompleteTurnAsync` now guarded (null), `TurnCountInPhase` stays 7 as asserted; test does not assert `ObservedTurnCount` |
| `RolePlayInstructionFlowTests` | ✅ PlusButton instruction | ❌ | **PASSES** — interaction creation unchanged |
| `RolePlayIntentRoutingTests.SubmitPromptAsync_Instruction_BypassesContinuationService` | ✅ instruction | ❌ | **PASSES** — continuation bypass unchanged |
| `RolePlayBehaviorModeSubmitTests` | ❌ Narrative/Message | n/a | **PASSES** — untouched |
| `RolePlayIntentRoutingTests` (Message/Narrative) | ❌ | n/a | **PASSES** — real-turn path untouched |

### New tests to add

1. `SubmitPromptAsync_Instruction_DoesNotIncrementObservedTurnCount` — submit plain instruction; assert `LoadAdaptiveStateAsync(...).ObservedTurnCount` unchanged.
2. `SubmitPromptAsync_Instruction_DoesNotIncrementTurnCountInPhase` — submit plain instruction; assert `TurnCountInPhase` unchanged.
3. `SubmitPromptAsync_Instruction_DoesNotCreateTurnRow` — submit plain instruction; assert `LoadTurnsAsync(sessionId)` has no new `TurnKind = "SubmitPrompt"` row.
4. `SubmitPromptAsync_Instruction_InteractionStillPersisted` — submit instruction; assert interaction appears in reloaded session `Interactions`.
5. `SubmitPromptAsync_Message_StillIncrementsCounters` — submit `Message`; assert `ObservedTurnCount` +1 and `TurnCountInPhase` +1 (guards against over-suppression).

### Manual verification

1. Create session → submit 1 instruction → verify `ObservedTurnCount == 0` and `TurnCountInPhase == 0` in DB (`RolePlayV2ThemeTrackerMeta`, `RolePlayV2AdaptiveStates`), and no new row in `RolePlayV2Turns`.
2. Submit a real Message → verify both counters become 1 and one turn row exists.
3. During Opening, submit 3 instructions only → verify phase stays `Opening` (does not transition to `BuildUp`).
4. Verify an instruction's text appears in the session transcript after reload (session blob persistence intact).

---

## Verification Protocol

- `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore` — 0 errors, 0 warnings.
- `dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore` — 0 errors.
- Run the `RolePlay` test namespace; all existing tests pass (especially the four listed above) plus the five new ones.
- Confirm exactly one active decision path for each changed counter (no fallback branch): `ObservedTurnCount` increments only at the four real-turn `StartTurnAsync` sites; `TurnCountInPhase` increments only via the pipeline with `incrementTurnCount: true`.
