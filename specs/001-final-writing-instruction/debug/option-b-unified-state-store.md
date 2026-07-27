# Option B — Eliminate Dual-Source Adaptive State

**Created:** 2026-07-26
**Status:** Planned — not yet implemented
**Parent spec:** `specs/001-final-writing-instruction/`
**Related debug records:** Autocomplete BuildUp→Committed transition failures across sessions `ff1e6791`, `1bffb4e2`

---

## Problem Statement

Adaptive state is persisted in **two independent stores** that can diverge:

1. **`Sessions.PayloadJson`** — the session blob, serialized via `SaveRolePlaySessionAsync` → `JsonSerializer.Serialize(session)`. Contains a full copy of `AdaptiveScenarioState` inside the session object. Written by the debounced `AutoSaveCoordinator` (1-second timer) and explicit `FlushAsync`.

2. **`RolePlayV2AdaptiveStates`** (V2 table) — written by `SaveAdaptiveStateAsync` (full save) and `SaveAdaptiveStateSemanticFieldsAsync` (semantic-only). Read by `LoadAdaptiveStateAsync` at the start of every pipeline run.

### The Race Condition

1. Turn N completes: Pipeline writes correct state to V2 table → `QueueRolePlaySessionSave` + `FlushAsync` writes session blob to `PayloadJson`.
2. Semantic job fires: Loads V2 state → applies deltas → writes semantic fields to V2 → calls `InvalidateSessionCache` → removes session from in-memory `Sessions` cache.
3. Turn N+1 starts: `GetSessionAsync` → cache miss → `LoadRolePlaySessionAsync` → deserializes `PayloadJson` → gets `session.AdaptiveState` from the blob.
4. Pipeline runs: `HydrateV2State(session, previousV2State)` merges blob state with V2 state — but they may disagree on `TurnCountInPhase`, `CurrentPhase`, `ActiveScenarioId`, etc.

### Symptoms

- `TurnCountInPhase` resets to 0 or 1 between turns (blob has stale value, V2 has correct value, merge logic doesn't always pick the right one)
- `EnsureOpeningToBuildUpTransition` re-fires because blob says `Opening` even though V2 says `BuildUp`
- `ActiveScenarioId` gets nulled because blob has stale `ThemeSelectionRule = "Observing"` even though V2 has `ActiveScenarioLock`
- Autocomplete is more affected than manual Continue because its 500ms (now 3s) delay between turns is shorter than the semantic job's completion time + the debounced save's 1-second window

### Current Patches (All Compensating for Dual-Source)

These code paths exist **only** because two stores can diverge:

| Patch | File | Purpose |
|-------|------|---------|
| `HydrateV2State` merge logic | `RolePlayEngineService.cs:4694` | Merges blob state with V2 state using `Math.Max` and conditional overrides |
| `EnsureOpeningToBuildUpTransition` V2 guard | `RolePlayEngineService.cs:35` | Checks V2 DB before transitioning, to avoid re-transitioning from stale blob |
| `NormalizeRolePlaySession` repair logic | `SessionService.cs:301` | Repairs `ActiveScenarioId` from theme scores when blob has stale null |
| `SyncSessionAdaptiveStateFromV2` | `RolePlayEngineService.cs:4981` | Copies V2 state back to session after pipeline run |
| `SaveAdaptiveStateSemanticFieldsAsync` | `RolePlayStateRepository.cs:218` | Semantic job writes only semantic columns to avoid clobbering pipeline fields |
| `InvalidateSessionCache` | `RolePlayEngineService.cs:559` | Forces reload from DB after semantic job, to avoid stale in-memory cache |

---

## Solution: V2 Table as Single Source of Truth

**Principle:** `AdaptiveScenarioState` lives **only** in the V2 tables. The session blob (`PayloadJson`) no longer serializes it. `LoadRolePlaySessionAsync` always loads V2 state from the V2 table via `LoadAdaptiveStateAsync`.

---

## Implementation Plan

### Phase 1: Stop Serializing AdaptiveState into PayloadJson

**File:** `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`

- Add `[JsonIgnore]` to the `AdaptiveState` property on `RolePlaySession`.
- The session blob will no longer contain adaptive state.

**Risk:** Any code that deserializes a session blob and immediately reads `session.AdaptiveState` without loading V2 state first will get a null/default `AdaptiveScenarioState`. This is caught in Phase 2.

### Phase 2: Load V2 State in LoadRolePlaySessionAsync

**File:** `DreamGenClone.Web/Application/Sessions/SessionService.cs`

- Inject `IRolePlayStateRepository` into `SessionService` (or use a lazy callback to avoid circular DI).
- In `LoadRolePlaySessionAsync`, after deserializing the session:
  ```csharp
  var v2State = await _stateRepository.LoadAdaptiveStateAsync(session.Id, cancellationToken);
  if (v2State is not null)
  {
      session.AdaptiveState = v2State;
  }
  else
  {
      // New session — no V2 state yet. Initialize default.
      session.AdaptiveState ??= new AdaptiveScenarioState();
      session.AdaptiveState.SessionId = session.Id;
  }
  ```
- Simplify `NormalizeRolePlaySession` — remove the `ActiveScenarioId` repair logic and the `ThemeSelectionRule` observation-window guard. V2 is authoritative; no repair needed.

**Risk:** `SessionService` currently has no dependency on `IRolePlayStateRepository`. Adding it may create a DI cycle. Alternative: pass `IRolePlayStateRepository` as a parameter to `LoadRolePlaySessionAsync`, or use a factory pattern. Check DI graph before implementing.

### Phase 3: Simplify HydrateV2State

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

- `HydrateV2State` currently merges blob state with V2 state. After Phase 1+2, `session.AdaptiveState` IS the V2 state (loaded in `LoadRolePlaySessionAsync`). No merge needed.
- Simplify to:
  ```csharp
  private static AdaptiveScenarioState HydrateV2State(
      RolePlaySession session,
      AdaptiveScenarioState? previousState)
  {
      var mapped = session.AdaptiveState;
      mapped.SyncCharacterSnapshots();
      return mapped;
  }
  ```
- Remove the `Math.Max` merge logic, the `ThemeSelectionRule` override, the `ActiveScenarioId` null-on-observing logic, and the `SelectedNarrativeGateProfileId` fallback.
- The `previousState` parameter can be removed entirely, or kept for logging only.

### Phase 4: Simplify EnsureOpeningToBuildUpTransition

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

- Remove the V2 DB check added as a patch. Since `session.AdaptiveState` is now always the V2 state (loaded in Phase 2), the in-memory phase is always correct.
- Revert to the simple version:
  ```csharp
  private async Task EnsureOpeningToBuildUpTransition(RolePlaySession session)
  {
      if (session.AdaptiveState.CurrentPhase == NarrativePhase.Opening
          && session.AdaptiveState.ObservedTurnCount > OpeningPeriodTurnCount)
      {
          session.AdaptiveState.CurrentPhase = NarrativePhase.BuildUp;
          session.AdaptiveState.TurnCountInPhase = 0;
          // No immediate save needed — pipeline will save V2 state at the end.
      }
  }
  ```
- Remove the `await _stateRepository.SaveAdaptiveStateAsync(...)` call inside this method. The pipeline's final save at line 4684 handles persistence.

### Phase 5: Simplify SyncSessionAdaptiveStateFromV2

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

- Currently: `session.AdaptiveState = v2State;` (assigns V2 state to session).
- After Phase 1+2: `session.AdaptiveState` IS `v2State` (same reference, since `HydrateV2State` returns `session.AdaptiveState`). The sync is a no-op.
- Keep the method as a no-op or remove it entirely and delete all call sites.

### Phase 6: Audit All session.AdaptiveState Access Points

Search for every code path that reads `session.AdaptiveState` and verify it's safe after the change:

| Code Path | File | Safe? |
|-----------|------|-------|
| `GetSessionAsync` → cache hit | `RolePlayEngineService.cs:589` | ✅ In-memory session has V2 state from prior load |
| `GetSessionAsync` → cache miss | `RolePlayEngineService.cs:593` | ✅ `LoadRolePlaySessionAsync` loads V2 (Phase 2) |
| `CreateRolePlaySessionAsync` | `RolePlayEngineService.cs:~525` | ✅ New session initializes `AdaptiveState` before first save |
| `OpenSessionAsync` | `RolePlayEngineService.cs:~605` | ✅ Calls `GetSessionAsync` |
| `SemanticInteractionAnalysisJobHandler` | `SemanticInteractionAnalysisJobHandler.cs:106` | ✅ Already loads V2 state explicitly (line 122) |
| `LocationDetectionJobHandler` | `LocationDetectionJobHandler.cs` | ⚠️ Check if it loads V2 state or relies on blob |
| Blazor components | `RolePlayWorkspace.razor` | ⚠️ Check if they read `session.AdaptiveState` directly |
| Session list / overview | `SessionService.cs:GetSessionsByTypeAsync` | ⚠️ Returns list items, not full sessions — check if AdaptiveState is accessed |

### Phase 7: Update SaveRolePlaySessionAsync

**File:** `DreamGenClone.Web/Application/Sessions/SessionService.cs`

- Remove `session.AdaptiveState.SyncCharacterSnapshots()` call (V2 save handles this).
- The `JsonSerializer.Serialize(session)` call will automatically skip `AdaptiveState` due to `[JsonIgnore]`.
- No other changes needed — the blob just gets smaller.

### Phase 8: Update Tests

**Files:** `DreamGenClone.Tests/RolePlay/**/*.cs`

- Any test that deserializes a session blob and reads `AdaptiveState` without loading V2 state will break.
- Test doubles that mock `IRolePlayStateRepository` need to return V2 state from `LoadAdaptiveStateAsync`.
- `AdaptiveScenarioStateV2RoundTripTests` — verify still passes.
- `RolePlaySessionLifecycleTests` — verify `LoadRolePlaySessionAsync` mock returns V2 state.

### Phase 9: Migration for Existing Sessions

**Risk:** Existing sessions in the DB have `AdaptiveState` serialized in `PayloadJson`. After the change, `LoadRolePlaySessionAsync` will ignore it and load from V2 tables. If V2 tables are missing for a session (e.g. very old sessions), the session will get a default `AdaptiveScenarioState`.

**Mitigation:** V2 tables are created for every session at creation time (`SaveAdaptiveStateAsync` is called in `CreateRolePlaySessionAsync`). Any session without V2 state is either very old or corrupt — a default state is acceptable.

**No migration script needed.** The V2 tables are already authoritative for all live sessions.

---

## Files Changed

| File | Change |
|------|--------|
| `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs` | `[JsonIgnore]` on `AdaptiveState` |
| `DreamGenClone.Web/Application/Sessions/SessionService.cs` | Load V2 state in `LoadRolePlaySessionAsync`, simplify `NormalizeRolePlaySession` |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Simplify `HydrateV2State`, simplify `EnsureOpeningToBuildUpTransition`, remove/simplify `SyncSessionAdaptiveStateFromV2` |
| `DreamGenClone.Tests/RolePlay/**/*.cs` | Update test doubles to return V2 state from `LoadAdaptiveStateAsync` |

---

## Verification Plan

1. **Build:** `dotnet build DreamGenClone.sln --no-restore` — zero errors
2. **Unit tests:** `dotnet test DreamGenClone.Tests --no-build --filter "FullyQualifiedName~RolePlay"` — all pass
3. **Manual test — Continue button:** Create new session → Continue through Opening → BuildUp → verify `TurnCountInPhase` increments monotonically → verify transition to Committed
4. **Manual test — Autocomplete:** Create new session → Autocomplete 20 turns → verify `TurnCountInPhase` increments monotonically → verify transition to Committed
5. **DB inspection:** After each turn, query `RolePlayV2AdaptiveStates` — verify `TurnCountInPhase` matches expected value
6. **Cache invalidation test:** Run autocomplete → verify semantic job's `InvalidateSessionCache` doesn't cause `TurnCountInPhase` to reset

---

## Rollback Plan

If Option B introduces regressions:
1. Remove `[JsonIgnore]` from `AdaptiveState`
2. Revert `LoadRolePlaySessionAsync` to not load V2 state
3. Revert `HydrateV2State` to the merge logic
4. The V2 tables remain intact — no data loss

---

## What This Does NOT Fix

- The semantic job's `InvalidateSessionCache` still fires — but it's now harmless because the reload always gets V2 state.
- The debounced `AutoSaveCoordinator` still exists — but it only saves the session blob (interactions, metadata), not adaptive state. No race on adaptive state.
- The `SaveAdaptiveStateSemanticFieldsAsync` method still exists — but it's no longer needed to protect pipeline fields from being clobbered. It could be simplified to a full `SaveAdaptiveStateAsync` call, but that's a separate cleanup.

---

## Effort Estimate

- Phase 1-5 (core changes): ~2 hours
- Phase 6 (audit): ~1 hour
- Phase 7-8 (save + tests): ~1 hour
- Phase 9 (migration): 0 (no migration needed)
- Verification: ~1 hour
- **Total: ~5 hours**
