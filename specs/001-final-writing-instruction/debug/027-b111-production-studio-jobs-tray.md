# B-111 Production Studio Jobs Tray

## Report

Production Studio could not show or manage the durable job that was stuck. The existing embedded `RunTray` read `ProductionWorkload`, but Studio image Compose/Identity/Finish work uses `DurableBackgroundJob`, leaving the actual queue invisible and unable to start, inspect, or cancel from the production workflow.

## Analysis

`IDurableBackgroundJobQueue` already provides persisted `TryActivateAsync` and `TryCancelAsync`. `DurableBackgroundJob` retains job type, lane, status, attempts, retries, leases, errors, timestamps, and payload. Studio image render/edit job payloads contain the exact image record ID; prompt payloads contain a prompt record ID that resolves to a production group.

The B-111 P1-T8 plan and `ui/RunTray.md` require a reused, summary-first, bounded job surface with drill-down and confirmed abort. The existing `RunTray` was a disconnected placeholder for a separate aggregate, so it was enhanced rather than introducing another queue UI loop.

## Plan

1. Rework the shared `RunTray` to scope durable jobs to one production group.
2. Surface status counts, bounded jobs, drill-down details, the persisted image model/provider target, Start for staged jobs, and confirmed Cancel for every nonterminal queue state.
3. Make Production Studio switch between the existing Production POV workbench and the reused Jobs tray.
4. Add source-contract coverage and validate diagnostics/build.

## Resolution

- `Components/Shared/RunTray.razor` now correlates durable jobs to a production group using persisted prompt/image IDs, displays at most 100 matching jobs from a bounded 200-record query, shows status counts, persisted image model/provider targets, and details, starts staged jobs, and confirms cancellation for staged, queued, processing, or retry-scheduled jobs.
- `Components/Pages/SceneImageStudio.razor` now has `Production POV` and `Jobs` tabs. The POV workbench remains intact; Jobs hosts the shared tray for the selected POV's exact production group.
- `DurableBackgroundJobRepository.TryCancelAsync` now permits the valid pre-activation transition `Staged -> Cancelled` while retaining status-guarded cancellation.
- `SceneImageStudioUiContractTests.cs` and `DurableBackgroundJobRepositoryTests.cs` now assert shared tray reuse, bounded query/display, persisted target disclosure, staged cancellation, confirmation, and details.

## Validated

- [x] Razor diagnostics for `SceneImageStudio.razor` and `RunTray.razor` reported no errors.
- [x] `dotnet build DreamGenClone.Web\\DreamGenClone.csproj --no-restore --nologo -c Release` succeeded with no errors (152 existing warnings).
- [!] Debug build reached C# and Razor compilation but could not copy its output because the active `.NET Host` process (PID 18600) held `bin/Debug/net9.0/DreamGenClone.dll`; the process was not stopped.
- [x] Editor diagnostics reported no errors for the Studio page, shared tray, or new Studio UI contract test.
- [x] A subsequent Release Web build after staged cancellation and target-disclosure changes succeeded with no errors (152 existing warnings).
- [!] The test suite was not run because it remains disabled. A test-project build is additionally blocked by unrelated existing `DreamGenClone.CorpusRunner/FrozenAdapters.cs`: `RecordingDurableQueue` does not implement the existing `IDurableBackgroundJobQueue.TryActivateAsync` member.
- [ ] Pending user verification with a selected Production POV containing a queued job.