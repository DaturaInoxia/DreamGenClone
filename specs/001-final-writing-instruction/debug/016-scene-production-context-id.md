# Debug 016: Scene Production Context ID

## Report

The durable Scene Production workload did not use the exact role-play session ID as its scene production context ID. The affected session was `4f2eec18-b190-4beb-ad35-8d520ae5c800`; the Studio bootstrap created a `ProductionIntentSnapshot` from the selected Moment.

## Analysis

`ProductionStudioService.CreateInitialRevisionAsync` copies `ProductionIntentSnapshot.ContextId` into `ProductionWorkload.ContextId`. The Studio bootstrap in `SceneImageStudio.razor` assigned `ContextId = _selectedMoment.MomentId`, while assigning the session separately to `SessionId`. This made the workload context ID the Moment ID rather than the exact session ID.

The durable model and existing tests use the session ID as the `SceneMoment` context ID. Moment identity remains available through `MomentId` and the lineage fields in the intent and context snapshot.

## Plan

Update `SceneImageStudio.razor` so Scene Production sets `ProductionIntentSnapshot.ContextId` to `sessionId`. Add a regression assertion for the durable workload context ID, then run focused production tests, a clean Web build, and inspect the persisted record with the repository DB query tool.

## Resolution

Updated `SceneImageStudio.razor` so `ProductionIntentSnapshot.ContextId` is populated from
the exact route/session parameter `sessionId`. Added a regression assertion in
`ProductionWorkloadServiceTests` that the durable workload preserves the session context ID.

## Validated

[x] Focused production tests: 36 passed, 0 failed.
[x] Web project rebuilt successfully with 0 errors.
[x] Live DB inspection confirmed no durable workload had been persisted by the failed pre-fix attempt.
[ ] Browser confirmation pending: the available session Studio links currently resolve to an
interaction that the Studio reports as missing, independently of this context-ID fix.
