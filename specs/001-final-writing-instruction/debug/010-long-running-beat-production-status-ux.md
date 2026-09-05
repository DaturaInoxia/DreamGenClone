# 010 — Long-running Beat Production has no usable status UX

**Report**

- Session: `4f2eec18-b190-4beb-ad35-8d520ae5c800`
- Original Beat Catalogue interaction: `7c354447-289e-440c-b3af-a85a5dfdd72d`
- Symptom: Beat Catalogue / Beat Production remained on a spinner for several minutes, with no elapsed time, attempt state, heartbeat, provider, or meaningful cancellation confirmation.
- Evidence: B1 version 2 completed successfully after `273061 ms` (about 4.55 minutes). A separate stale lease was owned by dead process `29892` until its 15-minute expiry, making restart recovery appear hung. The current development run was later owned by process `9348`.

**Analysis**

- The structured provider client and durable executor now have an operation watchdog and retry classification, so the provider path is bounded and recoverable.
- `SceneImageStudio.razor` polls the durable plan but renders `Pending` and `Processing` as the same spinner. It does not load the persisted current attempt or durable job metadata.
- The plan repository already persists `CreatedUtc`, `StartedUtc`, `UpdatedUtc`, provider/model, and terminal errors. The attempt persists attempt number, status, duration, output size, and validation code. The durable job persists execution attempt count, status, lease expiry, and updated time.
- Root cause: state is persisted but the user-facing status boundary exposes only the coarse plan status. This is a product observability defect, not evidence that the configured timeout is ineffective.

**Plan**

- Add a read-only Beat Production status contract returned by the existing pipeline service, combining the current plan, current attempt, and durable job.
- Update `SceneImageStudio.razor` to refresh and display queue/provider phase, attempt count, elapsed time, last durable update, lease expiry, provider/model, response size, and retry/error details while preserving the existing cancel operation.
- Keep cancellation authoritative through the existing queue cancellation plus plan/attempt compare-and-set transition; show the cancelled terminal state after reload.
- Add focused service coverage for the status projection and run the affected RolePlay tests plus a Web build.

**Resolution**

- Added `SceneBeatProductionStatus` and `GetCurrentStatusAsync` to expose the persisted plan, analysis attempt, and durable job as one read model.
- Updated `SceneImageStudio.razor` to poll and render queued/provider-running state, elapsed time, attempt/retry information, durable heartbeat, lease expiry, provider/model, and terminal status while retaining cancellation.
- Added `GetLatestAsync` as a separate repository path that includes cancelled plans. Status reads use it, while active enqueue decisions continue using `GetCurrentAsync`, which excludes cancelled and superseded plans.
- Added regression coverage proving a cancelled plan and cancelled attempt remain visible through a subsequent status read.

**Validated**

- [x] Focused `SceneBeatProductionPipelineServiceTests`: 3 passed.
- [x] Web project build: succeeded with 0 errors.
- [x] Test project build: succeeded with 0 errors.
- [ ] Fresh Studio runtime acceptance and user confirmation pending.
