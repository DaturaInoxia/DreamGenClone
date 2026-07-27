# Full Simplification — V2 Tables as Single Source of Truth

**Created:** 2026-07-26
**Status:** Planned — not yet implemented
**Parent:** Option B (partially implemented — `[JsonIgnore]` on AdaptiveState, V2 load in LoadRolePlaySessionAsync)
**Goal:** Eliminate the in-memory `Sessions` cache and the `PayloadJson` adaptive state copy. V2 tables are the ONLY store for adaptive state. UI reads from DB. No merge logic. No cache invalidation. No intermediate saves.

---

## Current Architecture (3 sources of truth)

```
┌─────────────────────────────────────────────────────────────┐
│ IN-MEMORY CACHE (Sessions dictionary)                        │
│  - RolePlaySession object held in memory between requests    │
│  - AdaptiveState is a live mutable object on the session     │
│  - Pipeline mutates it, then saves to V2 + blob              │
│  - InvalidateSessionCache removes it → forces DB reload      │
└─────────────────────────────────────────────────────────────┘
         ↕ (sync/merge/repair)
┌─────────────────────────────────────────────────────────────┐
│ PayloadJson blob (Sessions table)                            │
│  - Full session serialized as JSON                           │
│  - AdaptiveState was [JsonIgnore]'d in Option B             │
│  - Interactions, metadata, settings live here               │
│  - Debounced autosave (1s timer) + explicit FlushAsync       │
└─────────────────────────────────────────────────────────────┘
         ↕ (HydrateV2State merge, NormalizeRolePlaySession)
┌─────────────────────────────────────────────────────────────┐
│ V2 TABLES (RolePlayV2AdaptiveStates, ThemeScores, etc.)      │
│  - Authoritative adaptive state                              │
│  - Written by SaveAdaptiveStateAsync (pipeline)             │
│  - Written by SaveAdaptiveStateSemanticFieldsAsync (job)     │
│  - Read by LoadAdaptiveStateAsync                            │
└─────────────────────────────────────────────────────────────┘
```

**Every bug** (TurnCountInPhase resets, phase flip-flops, ActiveScenarioId nulling, stale cache) is caused by these three stores disagreeing.

---

## Target Architecture (1 source of truth)

```
┌─────────────────────────────────────────────────────────────┐
│ V2 TABLES — ONLY store for adaptive state                    │
│  - Pipeline writes: SaveAdaptiveStateAsync (once per turn)  │
│  - Semantic job writes: SaveAdaptiveStateSemanticFieldsAsync│
│  - All reads: LoadAdaptiveStateAsync                         │
│  - No in-memory cache. No blob copy. No merge.               │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ PayloadJson blob — ONLY for session metadata + interactions  │
│  - Interactions, title, settings, persona, scenarioId        │
│  - NO adaptive state (already [JsonIgnore]'d)               │
│  - Saved once per turn (after pipeline completes)            │
└─────────────────────────────────────────────────────────────┘
```

**Flow per turn:**
1. Load session from DB (blob for interactions/metadata + V2 for adaptive state)
2. Generate interactions (in-memory, not persisted yet)
3. Run pipeline (mutate adaptive state in memory)
4. Save V2 state once (SaveAdaptiveStateAsync)
5. Save session blob once (SaveRolePlaySessionAsync)
6. Queue semantic jobs (they write to V2 independently)

**UI flow:**
1. Load V2 state from DB
2. Display it
3. If user changes a setting, save to DB, reload from DB

---

## Implementation Plan

### Phase 1: Remove In-Memory Sessions Cache

**File:** `RolePlayEngineService.cs`

- Remove `private static readonly ConcurrentDictionary<string, RolePlaySession> Sessions`
- `GetSessionAsync` → always calls `_sessionService.LoadRolePlaySessionAsync` (which loads blob + V2)
- Remove `InvalidateSessionCache` method and all call sites
- Remove `EnsurePersistedSessionsLoadedAsync` (no cache to populate)

**Impact:** Every `GetSessionAsync` call hits the DB. This is correct — the DB is the source of truth. Performance impact is negligible (SQLite local, <1ms per query).

**Call sites to update:**
- `GetSessionAsync` — remove cache lookup, always load from DB
- `CreateRolePlaySessionAsync` — no longer adds to cache
- `OpenSessionAsync` — no longer adds to cache
- `SaveSessionAsync` — no longer updates cache, just saves to DB
- `InvalidateSessionCache` — delete, remove all call sites (SemanticInteractionAnalysisJobHandler, any others)

### Phase 2: Eliminate All Merge/Sync Logic

**File:** `RolePlayEngineService.cs`

Remove these methods entirely (they exist only to reconcile stale cache with V2):

| Method | Why it exists | Action |
|--------|--------------|--------|
| `HydrateV2State` | Merges blob state with V2 state | **Delete** — session.AdaptiveState IS V2 state |
| `SyncSessionAdaptiveStateFromV2` | Copies V2 state back to session | **Delete** — same reference |
| `SyncThemeTrackerFromV2State` | No-op already | **Delete** |
| `AlignPromptNarrativeStateWithV2Async` | Repairs stale session from V2 | **Delete** — session is always loaded from V2 |
| `NormalizeRolePlaySession` (in SessionService) | Repairs ActiveScenarioId from theme scores | **Delete** — V2 is authoritative |

**Pipeline change:**
```csharp
// Before (current):
var previousV2State = await _stateRepository.LoadAdaptiveStateAsync(session.Id, cancellationToken);
var v2State = HydrateV2State(session, previousV2State);
// ... pipeline mutates v2State ...
await _stateRepository.SaveAdaptiveStateAsync(v2State, cancellationToken);
SyncSessionAdaptiveStateFromV2(session, v2State);

// After:
// session.AdaptiveState was loaded from V2 by LoadRolePlaySessionAsync.
// Pipeline mutates session.AdaptiveState directly.
// ... pipeline runs ...
await _stateRepository.SaveAdaptiveStateAsync(session.AdaptiveState, cancellationToken);
// Done. No merge, no sync.
```

### Phase 3: Remove Intermediate Saves

**File:** `RolePlayEngineService.cs`

Remove ALL `SaveAdaptiveStateAsync` calls EXCEPT the single one at the end of `RunRolePlayV2PipelinesAsync`:

| Location | Current | Action |
|----------|---------|--------|
| `EnsureOpeningToBuildUpTransition` | Saves immediately after phase change | **Remove** — pipeline saves at end |
| Overflow time-skip (3 calls) | Already removed in current session | ✅ Done |
| `ContinueAsAsync` dirty save | `if (IsStateDirty) SaveAdaptiveStateAsync` | **Remove** — pipeline save is sufficient |
| `SubmitPromptAsync` dirty save | Same pattern | **Remove** |
| `RebuildAdaptiveStateInternalAsync` | Saves after rebuild | **Keep** — rebuild is a standalone operation |
| `RunRolePlayV2PipelinesAsync` final | Saves after pipeline | **Keep** — this is THE save |

### Phase 4: Simplify RebuildAdaptiveStateAsync

**File:** `RolePlayEngineService.cs`

Current: Wipes state, replays all interactions, reseeds from scenario.
This is expensive and dangerous.

New approach:
```csharp
private async Task RebuildAdaptiveStateInternalAsync(RolePlaySession session, CancellationToken cancellationToken)
{
    // Load current V2 state — preserve all pipeline fields.
    var currentState = await _stateRepository.LoadAdaptiveStateAsync(session.Id, cancellationToken)
        ?? session.AdaptiveState;

    // Re-seed theme scores from scenario (theme profile may have changed).
    if (!string.IsNullOrWhiteSpace(session.ScenarioId))
    {
        var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
        if (scenario is not null)
        {
            await SeedAdaptiveStateFromScenarioAsync(session, scenario, cancellationToken);
            await SeedRuntimeEncounterStatsAsync(session, scenario, cancellationToken);
        }
    }

    // Restore pipeline fields that SeedAdaptiveStateFromScenarioAsync may have overwritten.
    session.AdaptiveState.CurrentPhase = currentState.CurrentPhase;
    session.AdaptiveState.TurnCountInPhase = currentState.TurnCountInPhase;
    session.AdaptiveState.ActiveScenarioId = currentState.ActiveScenarioId;
    session.AdaptiveState.ThemeSelectionRule = currentState.ThemeSelectionRule;
    session.AdaptiveState.ObservedTurnCount = currentState.ObservedTurnCount;

    // Save to V2.
    await _stateRepository.SaveAdaptiveStateAsync(session.AdaptiveState, cancellationToken);
}
```

No interaction replay. No wiping state. Just re-seed theme scores and preserve everything else.

### Phase 5: UI Reads from DB Only

**File:** `RolePlayWorkspace.razor`

- `HydrateV2AdaptiveStateAsync` already loads from V2 — keep this.
- The adaptive panel displays `_v2State` (from V2) — not `_session.AdaptiveState` (in-memory).
- Remove all `_session.AdaptiveState.*` reads in the UI — replace with `_v2State.*`.
- Settings changes (theme profile, steering profile, etc.) call `RebuildAdaptiveStateAsync` then `HydrateV2AdaptiveStateAsync` — already correct after Phase 4.

### Phase 6: Remove Debounced Autosave for Adaptive State

**File:** `AutoSaveCoordinator.cs`, `RolePlayEngineService.cs`

The debounced autosave (`QueueRolePlaySessionSave`) saves the session blob. This is fine for interactions/metadata. But it should NEVER be the path that saves adaptive state.

After Option B, `SaveRolePlaySessionAsync` no longer serializes `AdaptiveState` (it's `[JsonIgnore]`). So the debounced save only writes the blob — no adaptive state. This is already correct.

**No changes needed** — just verify that no code path calls `SaveAdaptiveStateAsync` through the debounced autosave.

### Phase 7: Audit and Remove Dead Code

After Phases 1-6, remove:

| Dead code | Why it's dead |
|-----------|--------------|
| `Sessions` dictionary | Cache removed |
| `InvalidateSessionCache` | No cache to invalidate |
| `EnsurePersistedSessionsLoadedAsync` | No cache to populate |
| `HydrateV2State` | No merge needed |
| `SyncSessionAdaptiveStateFromV2` | Same reference |
| `SyncThemeTrackerFromV2State` | Was already no-op |
| `AlignPromptNarrativeStateWithV2Async` | Session always loaded from V2 |
| `NormalizeRolePlaySession` repair logic | V2 is authoritative |
| `ResolveScenarioIdFromState` | Only used by Normalize repair |
| `IsStateDirty` checks | No dirty saves |
| `DIAG:DirtySave` log | Diagnostic, no longer needed |

### Phase 8: Update Tests

- Remove test doubles that mock `Sessions` cache
- `LoadRolePlaySessionAsync` mock must return V2 state
- `GetSessionAsync` mock must call `LoadRolePlaySessionAsync`
- Remove tests for `HydrateV2State`, `NormalizeRolePlaySession`, `InvalidateSessionCache`
- Add tests for: single save per turn, UI reads from V2, rebuild preserves phase

### Phase 9: Verify

1. **Build:** Zero errors
2. **Unit tests:** All pass
3. **Manual — Continue:** Create session → Continue 10 turns → verify TurnCountInPhase increments monotonically in V2 → verify BuildUp→Committed transition
4. **Manual — Autocomplete:** Create session → Autocomplete 20 turns → verify same
5. **Manual — Adaptive panel:** Mid-session, click adaptive tab → verify phase doesn't change → verify V2 state matches display
6. **Manual — Phase transitions:** Verify Opening→BuildUp→Committed→Approaching→Climax→Reset all work
7. **DB inspection:** After each turn, `SELECT CurrentPhase, TurnCountInPhase FROM RolePlayV2AdaptiveStates` — verify monotonic increment, no resets

---

## Files Changed

| File | Change |
|------|--------|
| `RolePlayEngineService.cs` | Remove Sessions cache, HydrateV2State, SyncSessionAdaptiveStateFromV2, AlignPromptNarrativeStateWithV2Async, InvalidateSessionCache, EnsurePersistedSessionsLoadedAsync, intermediate saves, dirty saves. Simplify RebuildAdaptiveStateInternalAsync. |
| `SessionService.cs` | Remove NormalizeRolePlaySession repair logic, ResolveScenarioIdFromState. LoadRolePlaySessionAsync already loads V2 (from Option B). |
| `RolePlayWorkspace.razor` | Replace `_session.AdaptiveState.*` reads with `_v2State.*` in adaptive panel. |
| `SemanticInteractionAnalysisJobHandler.cs` | Remove InvalidateSessionCache call. |
| `RolePlayAutoCompleteService.cs` | GetSessionAsync now always loads from DB — no cache to worry about. |
| `DreamGenClone.Tests/RolePlay/**/*.cs` | Update test doubles, remove dead tests. |

---

## Rollback Plan

All changes are forward-only code edits. If regressions appear:
1. Re-add `Sessions` dictionary
2. Re-add `HydrateV2State` merge logic
3. Re-add `NormalizeRolePlaySession` repair
4. V2 tables remain intact — no data loss

---

## Effort Estimate

| Phase | Hours |
|-------|-------|
| 1: Remove cache | 1 |
| 2: Remove merge logic | 1 |
| 3: Remove intermediate saves | 0.5 |
| 4: Simplify rebuild | 0.5 |
| 5: UI reads from DB | 1 |
| 6: Verify autosave | 0.5 |
| 7: Remove dead code | 1 |
| 8: Update tests | 2 |
| 9: Verify | 1 |
| **Total** | **~8 hours** |

---

## What This Fixes

- TurnCountInPhase resets between turns ✅
- Phase flip-flops (Approaching↔Climax) ✅
- ActiveScenarioId getting nulled ✅
- Adaptive panel corrupting state ✅
- Autocomplete vs Continue behavior differences ✅
- Stale cache after semantic job ✅
- Every race condition caused by dual/triple source ✅
