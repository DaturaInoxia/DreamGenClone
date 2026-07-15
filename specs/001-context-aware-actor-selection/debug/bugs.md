# Bug List: B-050 Context-Aware Actor Selection

**File**: `debug/bugs.md`
**Purpose**: Track implementation bugs found during testing
**Date**: 2026-07-14

---

## BUG-001: SQL backfill references nonexistent `RolePlayInteractions` table

**Severity**: 💥 Critical (blocks session creation)
**Date found**: 2026-07-14
**Status**: Fixed
**Fix commit**: Pending

### Symptom

```
[ERR] Failed to create role-play session from wizard
SQLite Error 1: 'no such table: RolePlayInteractions'
   at RolePlayStateRepository.EnsureAdaptiveStateSchemaAsync:line 1146
   at RolePlayEngineService.CreateSessionAsync:line 589
```

### Root Cause

The one-time `ActorName` backfill SQL in `RolePlayStateRepository.EnsureAdaptiveStateSchemaAsync` (L1133–1145) references `RolePlayInteractions` table. This table **does not exist** — interactions are stored as JSON inside `Sessions.PayloadJson`, not in a separate SQLite table. The backfill SQL runs inside the `HasColumnAsync` guard, so it fires on first load of any session after the `ActorName` column is added.

### Fix

Remove the broken SQL backfill. The `ActorName` column is correctly wired for **new events** via T020 (at `RolePlayAdaptiveStateService.cs:1036` and `RolePlayEngineService.cs:8080`). A proper C# backfill would need to deserialize session blobs — not worth the complexity for existing rows (they remain null, which is backward-compatible per the spec).

### Verification

- [ ] App starts without `SQLite Error 1` on session create
- [ ] New semantic events have `ActorName` populated (verify in debug logs or DB query)
- [ ] Existing events remain null — no crash

---

## BUG-002: `ActorSelection requires at least one candidate` when no NPCs are available

**Severity**: 💥 Critical (blocks first overflow continue)
**Date found**: 2026-07-14
**Status**: Fixed
**Fix commit**: Pending

### Symptom

```
[ERR] Submission tracker: background task failed for session:
ActorSelection requires at least one candidate.
   at ActorSelectionService.ValidateRequest:line 198
   at ActorSelectionService.SelectActorsAsync:line 37
   at RolePlayEngineService.ResolveSceneContinueActorsAsync:line 2457
   at RolePlayEngineService.ContinueAsAsync:line 1459
```

First turn click after session create always fails.

### Root Cause

The guard at the start of the new actor-selection pipeline was:
```csharp
if (availableByName.Count == 0 && !autoAllowedActors.Contains(ContinueAsActor.You))
```

In `TakeTurns` mode, `autoAllowedActors` includes `You`, so when no NPCs were available (e.g., session created with characters but `ResolveAvailableCharacters` filtered all out, or scenario had no characters), the guard skipped the fallback and fell through to `ActorSelectionService.SelectActorsAsync` with zero candidates — which throws.

### Fix

Changed guard to unconditional:
```csharp
if (availableByName.Count == 0) { skip NPC pipeline; fall through to persona rules; }
else { full scoring/pipeline/NPC mapping }
```

Also fixed a bad edit that duplicated the entire pipeline outside the `else` block.

### Verification

- [ ] First overflow click succeeds without the error
- [ ] When NPCs exist, the pipeline runs as before
- [ ] Persona insertion rules still apply regardless

---

## TODO-003: Add `DefaultStartingLocationId` dropdown in Scenario Details (fallback UI)

**Severity**: Low (engine fallback works, just no UI to set it)
**Date added**: 2026-07-14
**Status**: Done
**Fix commit**: 2026-07-15 reimplementation

### Description

`Scenario.DefaultStartingLocationId` is used as a fallback in `CreateSessionAsync` when no opening has a `LocationId` set. Added a dropdown in Scenario Details card in `ScenarioEditor.razor` (after Default Time of Day dropdown) bound to `CurrentScenario.DefaultStartingLocationId`. Options populated from `CurrentScenario.Locations`. Only shown when locations exist.

---

## TODO-004: Add Location section to RP Workspace Adaptive tab (before Character Stats)

**Severity**: Low (info display only — no engine impact)
**Date added**: 2026-07-14
**Status**: Done
**Fix commit**: 2026-07-15 reimplementation

### Description

Added a collapsible "Location" section in the Adaptive tab of `RolePlayWorkspace.razor` (just before Character Stats section). Shows:
- `CurrentSceneLocation` (display name or "(none)")
- `CurrentTimeOfDay` + Auto/Manual indicator badge
- Per-character location truth (`TrueLocation` per character from `_v2State.CharacterLocations`)
- Hidden indicator per character

---

## TODO-005: Add ResponsePriority UI in Workspace for per-character ordering

**Severity**: Medium (engine supports it; no UI to set it)
**Date added**: 2026-07-14
**Status**: Done
**Fix commit**: 2026-07-15 reimplementation (T058)

---

## BUG-003: LLM location detection oscillates on identical input

**Severity**: Medium (LLM behavior, not code bug)
**Date found**: 2026-07-14
**Status**: Fixed
**Fix commit**: 2026-07-15 reimplementation

### Symptom

Session `9188210c`: job ran at 21:10 (detected `Hiking Trails`, confidence 0.80) and 21:34 (detected `Trailer`, confidence 0.65) on the SAME `InteractionsLen=931`. LLM flipped between two valid locations on identical input.

### Root Cause

When interactions reference multiple locations, the LLM may pick either. The `previousLocation` tiebreaker in the system prompt isn't strong enough.

### Fix

Added to system prompt in `LocationDetectionService.cs`: "If multiple locations are plausibly referenced, prefer the most recently mentioned one. If the narrative describes a transition, prioritize the destination."

---

## BUG-004: Location detection fails on sessions with no NPC interactions

**Severity**: Medium
**Date found**: 2026-07-14
**Status**: Fixed
**Fix commit**: 2026-07-15 reimplementation

### Symptom

Session `fa458cae`: three failures `"requires at least one recent interaction"`. New sessions have no NPC/Custom interactions — only opening narrative (System type).

### Fix

Changed `ValidateRequest` in `LocationDetectionService.cs` to return `Success=false` with `ErrorMessage` instead of throwing when `RecentInteractions.Count == 0`. LLM is not called; previous location is preserved. Updated call site to check returned result.

---

## BUG-005: Per-character location applied uniformly regardless of narrative presence

**Severity**: Medium (data accuracy)
**Date found**: 2026-07-14
**Status**: Fixed
**Fix commit**: 2026-07-15 reimplementation

### Symptom

Adaptive panel shows all characters at the same location. `UpsertTrueLocation` applies to every character in `PerCharacterLocations` regardless of narrative reality.

### Fix

Changed `LocationDetectionJobHandler.cs` to skip `PerCharacterLocations` entries where the LLM returned null/empty. Characters with null entries are left at their existing `TrueLocation` instead of being overwritten with `previousLocation`.

---

## Bug Entry Template

## Bug Entry Template

```
## BUG-NNN: [Short title]

**Severity**: [Critical/High/Medium/Low]
**Date found**: [YYYY-MM-DD]
**Status**: [Open/Fixed/Workaround]
**Fix commit**: [Commit hash or "Pending"]

### Symptom

[Error message, stack trace, or behavioral description]

### Root Cause

[Analysis of what caused the bug]

### Fix

[Description of the fix applied]

### Verification

- [ ] Check item 1
- [ ] Check item 2
```
