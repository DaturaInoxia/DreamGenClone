# Debug 009: Actor availability gated on location match despite line-of-sight

**Date:** 2026-07-18
**Session:** e971b6ba-f561-4e18-95df-f0a9e2628e15

## Report

Ken (persona, oblivious husband) was not being selected for overflow interactions despite being an active participant — he was watching the scene at the Other Man's Trailer from his own trailer, with line of sight. Becky referenced him in her interactions. But `availableForSelection.Count == 2` instead of the expected 3+.

## Analysis

`ResolveAvailableCharacters` at line 2297:
```csharp
var isAvailable = bestStatus switch
{
    AffinityStatus.Excluded => false,
    AffinityStatus.Required => true,
    _ => inScene   // ← Ken: no affinity → falls here → inScene = false
};
```

Ken has no location affinity configured, so `bestStatus = None`, falling to `_ => inScene`. `IsActorInScene("Ken")` returns `false` because Ken's `TrueLocation` (Husband and Wife Trailer) ≠ `CurrentSceneLocation` (The Other Man's Trailer). Even though Ken has line-of-sight and is actively watching, the location mismatch gates him out completely.

The `AvailableForSelection` filter then drops Ken:
```csharp
var availableForSelection = availableCharacters
    .Where(c => c.IsAvailable && ...)  // Ken: IsAvailable = false → filtered out
```

Result: only Dean (Required affinity) and Becky (inScene=true) pass. Ken excluded.

The location ping-pong between Husband/Wife Trailer and Other Man's Trailer (addressed by commenting out Slots 1/4/7/11) would reduce the frequency of this issue, but the gate itself is wrong — a character with line-of-sight or being referenced by others should not be disqualified by location mismatch alone.

## Plan

**File:** `RolePlayEngineService.cs` line 2297
**Change:** `_ => inScene` → `_ => true`

Only `Excluded` affinity blocks availability. `ScoreActorForAutoSelection` already weights `IsInScene` (line 2313: `if (character.IsInScene) score += ScoreLocationMatch;`) — out-of-scene characters are ordered lower but not blocked entirely.

## Resolution

- [x] Changed `ResolveAvailableCharacters` line 2297: `_ => true`
- [x] Build pending
- [ ] User confirmed fixed (pending)

## Validated

[ ] pending — requires fresh session test
