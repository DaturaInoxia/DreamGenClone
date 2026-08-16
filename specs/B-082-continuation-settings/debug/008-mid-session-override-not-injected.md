# 008 — Mid-Session Override Change Not Injected (Beat Style=Single after Clear all)

**Created:** 2026-08-14
**Feature:** B-082 continuation-settings override — changing options mid-session.
**Status:** Root cause identified — mid-session override change does not reach the prompt.

## Report

Session `318d77a2-d72b-436a-8171-a1fb3176d867` (NTR Open World). After P1/P2/P3 (pacing set at session start, all injected correctly), I changed the override **mid-session**: clicked **Clear all** then **Beat Style = Single**, then generated a turn (idx 12-15, BuildUp).

- DB `Sessions.PayloadJson.continuationOverride` = `{"beatScope": 0, "pacing": null, ...}` → **saved correctly**.
- But the generated prompts (idx 12-15) contain **no** `Scene Direction Override` block and **no** `Beat Style` line.

## Analysis

The pacing override (P1-P3) was set **before the first turn** and injected correctly. The Beat Style override was changed **after 4 turns** and did NOT inject.

Two prompt paths (both read `session.ContinuationOverride` at build time, `RolePlayContinuationService.cs:675`):
- **Pacing** → `ContinuationOverrideResolver.ApplySceneDirection` → `FinalInstructionSlot` (works).
- **Beat Style/Time Shift/Granularity/Scene Presence** → `context.Override` → `ContinuationOverrideSlot` (registered in `Program.cs:170`; `ShouldWrite` = `context.Override is not null && HasUnconsumedDimensionOverride`).

`ContinuationOverrideSlot` is correctly registered and its `ShouldWrite` would be true for `beatScope: 0`. Yet the block is absent → **`context.Override` was null/stale at prompt build time**, even though the DB row has `beatScope: 0`.

## Root cause

**Mid-session override changes are persisted to the DB but do not update the in-memory `session.ContinuationOverride` used by the next prompt build.** Pacing only appeared to work because it was set before the session's first turn (fresh load carried the override). The "change options mid-session" use case — which is exactly what the test matrix now requires — is broken: an override set after the session has been running does not reach subsequent prompts.

This is consistent with the F5/phase-stamp finding (in-memory session diverges from DB): the Blazor circuit holds a `RolePlaySession` whose `ContinuationOverride` is stale relative to the saved payload.

## Impact

Every "change option mid-session → generate turn → verify" test case is affected: the injected text may reflect the OLD override (or none), not the newly-saved one.

## Resolution

None yet (change control). Candidate direction (needs user approval): after the popup "Done" saves the override, also update the in-memory `session.ContinuationOverride` (or reload the session) so the next prompt build sees the new value. The popup's save path (`RolePlayWorkspace.razor` "Done" handler) persists to DB but likely does not write back to the live session object.

## Validated

- [x] `beatScope: 0` saved to DB after "Clear all" + "Single".
- [x] Prompts idx 12-15 have no `Scene Direction Override` / `Beat Style` block.
- [x] `ContinuationOverrideSlot` is registered and would fire if `context.Override` were populated.
- [x] Root cause = stale in-memory `session.ContinuationOverride` on mid-session change.
- [ ] Fix (pending approval).
