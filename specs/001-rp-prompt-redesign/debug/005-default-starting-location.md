# Debug 005: Default Starting Location Not Used in Opening Prompt

**Date:** 2026-07-17
**Session:** `b472e733-8e75-4151-a997-28b7751e673d`
**Interaction:** `1defeaf3-877b-435c-9ea1-117a28be1e31`

## Report
First interaction prompt for a new session does not mention the scenario's configured `DefaultStartingLocationId`. Narrative should know where the characters are, but the prompt falls back to "Unknown location" and generic "Location Continuity" message. Makes no mention of the Campground or the Husband and Wife trailer.

## Analysis
Root cause: Both `SceneAnchorSlot` (slot 1) and `SceneLocationLockSlot` (slot 4) read from `session.AdaptiveState.CurrentSceneLocation`, which is `null` for brand-new sessions. The scenario's `DefaultStartingLocationId` was never consulted as a fallback.

`Scenario.DefaultStartingLocationId` is a location ID — needs to be resolved to a name via `scenario.Locations`.

## Plan
1. Add `DefaultStartingLocationName` field to `ResolvedScenarioData` record
2. Create `ResolveDefaultStartingLocationAsync` helper in `RolePlayContinuationService`
3. Update `SceneAnchorSlot` to use `context.Scenario.DefaultStartingLocationName` as fallback
4. Update `SceneLocationLockSlot` to use same fallback
5. Update test data (3 test files)

## Resolution
- `PromptBuildContext.cs`: Added `DefaultStartingLocationName` field to `ResolvedScenarioData`
- `RolePlayContinuationService.cs`: Added `ResolveDefaultStartingLocationAsync` method that:
  - Returns `null` if `CurrentSceneLocation` already set (engine takes priority)
  - Looks up `scenario.DefaultStartingLocationId` against `scenario.Locations`
  - Returns the matching location name
- `SceneAnchorSlot.cs`: Changed fallback from `"Unknown location"` to `context.Scenario.DefaultStartingLocationName ?? "Unknown location"`
- `SceneLocationLockSlot.cs`: Uses resolved location instead of generic "Location Continuity" fallback
- `PromptBuilderTests.cs`, `SlotContractTests.cs`: Added `DefaultStartingLocationName = null` to test data

## Validated
- [x] 2026-07-17 — Build 0 errors, 104 tests pass
- [x] User confirmed fixed with new session

