# Debug 004: Opening→BuildUp Phase Transition Not Persisting

**Date:** 2026-07-17
**Session:** `0aad7fd1-5c7e-4e19-a79d-516af1658987`

## Report
New session failing to transition from Opening to BuildUp at turn 4. User suspected state mismatch from database to in-memory (state getting wiped out on reload).

## Analysis
DB inspection confirmed:
- **V2 state** (`RolePlayV2AdaptiveStates`): `CurrentPhase = Opening` — never updated
- **Payload** (`Sessions.PayloadJson`): `phase = 1 (BuildUp)`, `ObservedTurnCount = 4` — transition happened in memory

Root cause: `EnsureOpeningToBuildUpTransition` was a `void` method that updated `session.AdaptiveState.CurrentPhase` in memory but NEVER persisted to the V2 state via `_stateRepository.SaveAdaptiveStateAsync`. The transition was lost whenever the app restarted or the session reloaded from DB.

Note: `RolePlayEngineService.cs` was NOT modified by any of the prompt refactor changes — this was a pre-existing issue that surfaced during testing.

## Plan
1. Make `EnsureOpeningToBuildUpTransition` async — call `_stateRepository.SaveAdaptiveStateAsync` immediately after phase change
2. Update all 4 call sites to `await` the async method

## Resolution
- Changed `private void EnsureOpeningToBuildUpTransition` → `private async Task EnsureOpeningToBuildUpTransition`
- Added `await _stateRepository.SaveAdaptiveStateAsync(session.AdaptiveState, CancellationToken.None)` after phase change
- Updated all 4 call sites: `EnsureOpeningToBuildUpTransition(session)` → `await EnsureOpeningToBuildUpTransition(session)` via PowerShell regex replacement
- Files: `RolePlayEngineService.cs` (method signature + persistence call)

## Validated
- [x] 2026-07-17 — Build 0 errors, 104 tests pass
- [x] 2026-07-17 — User confirmed: "I have validated transitions from opening to buildup to committed working now."

