# 019 — Encounter Boundary Over-Detection (Too Many Encounter Memories)

## Report

Session `42b79db3` logged **7 `EncounterBoundaryAdvanced` events** within ~15 minutes, producing **28 `EncounterCompletion` summary records** (4 per boundary). The user reported "too many encounter memories being logged."

Evidence: consecutive boundaries described the **same continuing scene**:
- 02:53:49 (2→3): "Dean came in my mouth and I swallowed and he came again"
- 02:54:58 (3→4): "He came in my mouth... third pulse overflowed... my own orgasm crashed" — 69s later, same oral act
- 03:05:34 (6→7): "He came with a groan... pulse after pulse... swallowing"

## Analysis

Root cause: `TryDetectEncounterBoundaryAsync` did **not** check `IsEncounterActive` before firing. After a boundary fires it sets `IsEncounterActive = false`, but the next interaction could fire **another boundary without a fresh start detection**.

In a multi-participant climax (Becky performing oral on Dean → Ken → Sam), the LLM detected `encounter-completed` on every orgasm reference. With the theme's `ConfidenceMin=0.5` and observed confidences 0.92–1.0, each orgasm was accepted, producing one boundary per orgasm and flooding EncounterCompletion memory.

Contributing factor: the boundary detection's existing guards (Reset phase, services, ActiveScenarioId, CurrentTimeSkipPhase, IsCharacterHavingSex) did not include an active-encounter requirement.

## Plan

Add an `IsEncounterActive` guard to `TryDetectEncounterBoundaryAsync` so a boundary can only fire when an encounter is currently active. This enforces: start → active → boundary → inactive → (fresh start) → ...

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

## Resolution

Added after the FR-008 re-entry guard in `TryDetectEncounterBoundaryAsync`:

```csharp
if (!state.IsEncounterActive)
{
    _logger.LogDebug(
        "TryDetectEncounterBoundary: skipped — no active encounter (IsEncounterActive=false) for SessionId={SessionId}",
        session.Id);
    return;
}
```

Safety: multi-encounter Climax sets `IsEncounterActive=true` at Climax entry; non-multi themes set it true in `TryDetectEncounterStartAsync`. Both pass the guard.

## Validated

- [x] Build: `dotnet build DreamGenClone.Web --no-restore` — 0 errors
- [x] Tests: 81/83 `Encounter*` + `MultiEncounter*` tests pass (2 pre-existing `EncounterSummaryServiceTests` template-format failures unrelated)
- [ ] Verified in live session: pending
