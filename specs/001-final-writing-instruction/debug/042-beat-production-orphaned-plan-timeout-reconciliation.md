# 042 - Beat production orphaned plan after durable job terminal failure (timeout reconciliation)

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59` · Interaction `d85cbd67-53a1-47b1-b5c5-40c34a6da180` · Turn `212f07a4001d4a71b7424b0802f0ae2d`
- **Catalogue**: `fccc6dc8-fc1a-4bd2-841e-860a748d47f3` (v3 Complete, z-ai/glm-4.7)
- **Date**: session debug following `041-beat-production-audio-ownership-and-pass-progress.md`
- **Git**: branch `development`, HEAD `deba25d Beat fix`

## Report

Beat b4 plan `e6214dca-ca0a-40c6-a026-dffe7201ce9d` was stuck at **Processing**
forever with an eternal spinner in the Studio. The durable job backing it
(`fe048976-c9eb-42d5-b3b0-2fb64a124fc2`) was already terminal **Failed** with
`structured_text_timeout` at 04:23:28, `AttemptCount=3 / MaxAttempts=3`,
`RetryDelaysSeconds=[5,30]`. The user observed: "it tried 3 times and failed, it
is supposed to return failure details now" — i.e. after exhausting all attempts a
beat plan must transition to **Failed** and surface the failure details + Retry,
not remain Processing.

By contrast a b1 plan (`30c43f80-acc5-426e-8d1b-ffcb6610ef1d`) failed cleanly
(OpenRouter HTTP 400 / AtlasCloud "invalid request params") and surfaced
correctly — the orphan happened specifically on the executor-watchdog path.

## Analysis

1. **Multi-pass v3 run killed by whole-run operation watchdog.** Attempt
   `a1c68cc6-0547-4482-8f26-4bd0a517572c` pass trace showed structure pass
   Complete (~105s), soundscape Complete (~113s), spoken Processing when killed.
   `TextAnalysisDurableJobExecutor`'s whole-run operation watchdog =
   `ProviderTimeoutSeconds` (240s). Each of the 3 durable attempts re-ran the
   handler from scratch, and the watchdog fired at exactly 240s per attempt —
   killing a healthy multi-pass run mid-passes (follows from debug 041's
   decomposition of the 4-pass v3 pipeline).

2. **Orphaned plan/attempt on terminal watchdog failure.** When the executor
   watchdog permanently fails a job (final attempt), it marks only the
   `DurableBackgroundJobs` row Failed and fire-and-forgets the handler
   (`ObserveHandlerCompletionAsync`), so the handler's own `FailAttemptAsync`
   never runs → plan/attempt left Processing forever. Handler catch order also
   contributes: `catch (OperationCanceledException) when (cancellationToken
   .IsCancellationRequested) { throw; }` fires before the
   `catch (TaskCanceledException)` / `FailAttemptAsync` path.

3. **Studio never reloaded the beat pipeline plan.** `RefreshPollingStateAsync`
   did not reload the catalogue/production plan, so even a clean terminal state
   would not surface live without a manual page reload.

## Plan (approved)

1. Scope the executor operation watchdog for multi-pass handlers (multiply by
   declared provider pass count) so healthy v3 runs are not killed at a
   single-pass timeout.
2. Reconcile orphaned Processing plans in the pipeline service: when a plan is
   Pending/Processing, has an attempt, and its durable job is terminal Failed
   with error info → `TryFailAttemptAsync` (compare-and-set guarded, idempotent)
   → failure details + Retry surface.
3. Reload the beat pipeline state during Studio polling when any beat work is
   in flight, so terminal states appear live without a manual refresh.
4. Cover with focused tests.

## Resolution — files changed

1. **`DreamGenClone.Application/Processing/IDurableJobOperationBudget.cs`**
   (NEW): `interface IDurableJobOperationBudget { int OperationTimeoutMultiplier
   { get; } }`.
2. **`DreamGenClone.Web/Application/BackgroundJobs/TextAnalysisDurableJobExecutor.cs`**:
   in the `hasStructuredTextTimeout` block multiply the operation watchdog by
   the single matching handler's declared multiplier (default 1 when the handler
   does not implement the interface).
3. **`DreamGenClone.Web/Application/RolePlay/SceneBeatProductionPlanJobHandler.cs`**:
   implements `IDurableJobOperationBudget`; multiplier =
   `SceneBeatProductionContract.ProviderPassCount` (4 passes: structure, spoken,
   soundscape, assembly).
4. **`DreamGenClone.Web/Application/RolePlay/SceneBeatProductionContract.cs`**:
   `public const int ProviderPassCount = 4;`.
5. **`DreamGenClone.Web/Application/RolePlay/SceneBeatProductionPipelineService.cs`**:
   `GetCurrentStatusAsync` reconciles orphaned plans (job terminal Failed + error
   present → `TryFailAttemptAsync`, then re-read and return updated state).
6. **`DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`**:
   `RefreshPollingStateAsync` reloads the catalogue when `HasInFlightBeatPipelineWork()`
   is true (new helper covering catalogue/production plan/moment set/moment
   enrichment Pending or Processing).
7. **`DreamGenClone.Tests/Processing/TextAnalysisDurableJobExecutorTests.cs`**:
   new test — declared budget scales watchdog past a single provider timeout
   (BudgetHandler multiplier 4; expects Complete, no retry, no fail).
8. **`DreamGenClone.Tests/RolePlay/SceneBeatProductionPipelineServiceTests.cs`**:
   new test — Processing plan with terminal Failed job reconciles to Failed and
   is idempotent on a second read.

## Validated

- Web build (`dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore`):
  0 errors / 0 warnings.
- Focused tests (SceneBeatProduction + TextAnalysisDurableJobExecutor): **56
  passed / 0 failed** (includes the 2 new tests).
- RolePlay + Processing suite excluding 4 pre-existing failing classes: **1476
  passed / 0 failed**.
- Pre-existing full-suite failures (NOT caused by this change; files untouched by
  this session, last modified at `e486a23 "Workflow in progess"`):
  `SceneImageServiceJobTests.EnqueueRenderAsync_*` (8, fail at
  `SceneImageService.ResolveRenderModelForDispatchAsync` line 853 — stale test
  fixture passes `null` model-resolution service),
  `AssetStudioUiContractTests.Operations_HaveDedicatedRoutesAndReusableComponents`,
  `ProductionMediaRepositoryTests.CompilationService_UsesExactQualifiedCellAndPersistsCanonicalRequest`,
  `ReferenceStrategyResolverTests.GraphStrategy_IsPossibleWhenDeclaredAndQualified`.
- Live acceptance: webapp running on :5177 with the new build; orphaned plan
  `e6214dca` heals to Failed automatically on the next Studio status read via the
  reconciliation. Full live RP run acceptance still pending.
