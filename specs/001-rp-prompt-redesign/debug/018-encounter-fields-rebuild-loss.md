# 018 — Encounter Tracking Fields Lost During State Rebuild

## Report

Encounter start and end detection stopped working. Session `5ba08ad1` showed evidence that encounter boundaries had fired (debug events: `EncounterBoundaryAdvanced 0→1` twice, `GlobalEncounterCount=1`) but `IsEncounterActive` remained `false` and `CurrentEncounterNumber` was `0`, with `CurrentTimeSkipPhase` stuck at `CloseScene`.

The session was permanently deadlocked: start detection blocked by `CurrentTimeSkipPhase != None`, boundary detection blocked by same guard, and time-skip overflow blocked by `CurrentEncounterNumber <= 0`.

## Analysis

Root cause: `RebuildAdaptiveStateInternalAsync` (line 3003) creates a fresh `AdaptiveScenarioState` but did **not** restore encounter tracking fields. The method saved/restored only `CurrentPhase`, `TurnCountInPhase`, `ActiveScenarioId`, and `CharacterEncounterProfileIds`. All encounter fields defaulted to `0`/`None`/`false` on the new state object, and then `SaveAdaptiveStateAsync` at line 3057 persisted those zeros to the DB, silently destroying the encounter state.

Fields lost on rebuild (all default to zero):
- `GlobalEncounterCount`
- `CurrentEncounterNumber`
- `TurnsInCurrentEncounter`
- `CurrentTimeSkipPhase`
- `CurrentEncounterStartInteractionIndex`

This affected any session where `RebuildAdaptiveStateAsync` was invoked (manual UI operation), and could also contribute to the deadlock when encounter detection had fired but was then wiped by a rebuild.

## Plan

Add save/restore of the 5 encounter tracking fields in `RebuildAdaptiveStateInternalAsync`, at both positions (after initial state creation and after the interaction replay loop), matching the existing pattern for `CurrentPhase`/`TurnCountInPhase`/`ActiveScenarioId`.

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

## Resolution

Added preservation and restoration of the 5 encounter tracking fields:

```csharp
// ---- Preserve universal encounter tracking fields (not phase-gated) ----
var savedGlobalEncounterCount = existingState.GlobalEncounterCount;
var savedCurrentEncounterNumber = existingState.CurrentEncounterNumber;
var savedTurnsInCurrentEncounter = existingState.TurnsInCurrentEncounter;
var savedCurrentTimeSkipPhase = existingState.CurrentTimeSkipPhase;
var savedCurrentEncounterStartInteractionIndex = existingState.CurrentEncounterStartInteractionIndex;
```

Restored at both positions (after `new AdaptiveScenarioState()` and after the replay loop).

No changes to `SaveAdaptiveStateAsync`, `LoadAdaptiveStateAsync`, or any other state persistence behavior.

## Validated

- [x] Build: `dotnet build DreamGenClone.Web --no-restore` — 0 errors
- [x] Tests: 53/53 `MultiEncounter*` tests pass
- [x] Tests: 81/83 `Encounter*` tests pass (2 pre-existing failures in `EncounterSummaryServiceTests` unrelated)
- [ ] Verified fixed in live session: pending
