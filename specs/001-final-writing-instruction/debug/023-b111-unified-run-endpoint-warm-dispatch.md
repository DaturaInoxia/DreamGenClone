# 023 - B-111 unified Run and endpoint-warm dispatch

## Report

The Studio create, normal-edit, Identity, and Finish actions must use one persisted production Run/queue flow. Hosted providers should flow immediately after staging. Serverless work must remain staged when the configured endpoint is cold, unreachable, or its state cannot be proven; it must flow immediately when the endpoint reports an active warmed or draining worker. An explicit Start action must submit intentionally staged serverless work. No provider path may silently bypass the Run/queue owner.

Session and interaction identifiers were not supplied. The affected visible surfaces are Studio Compose, Identity, Finish, and the production Run view.

## Analysis

`SceneImageService` currently persists a `SceneImageRecord`, then either calls the render/edit handlers directly for hosted and dedicated providers or enqueues generic background work for `ComfyUiServerless`. This gives the user two execution paths rather than one Run flow.

`ProductionWorkloadService` owns a persisted Run lifecycle, but its items require `ProductionIntentSnapshot` plus `CompiledMediaRequest`; Studio image records use a separate prompt/render/edit format. Bridging after rendering would not provide queue ownership before execution.

The generic durable queue owns Studio handler execution, but its statuses begin at `Queued`; it cannot express deliberate serverless staging. `TextAnalysisDurableWorker` claims queued work immediately.

`ProviderExecutionPolicy` requires `ReadinessPath` for a serverless provider, but no submit-time code reads that path. `RunPodServerlessImageClient.CheckImageModelHealthAsync` calls `/health` and reduces the response to a reachability boolean, so it cannot distinguish cold, warming, warm, or draining workers. The live Model Manager query on 2026-09-06 found empty `ReadinessPath`, `ReadinessSuccessContractJson`, `MaximumActiveRequests`, and `QueueCapacity` for both `RunPod Serverless BigLust` and `RunPod Qwen Image Edit`; automatic warm dispatch is therefore not currently provable.

Authoritative B-111 references consulted: `specs/Planning/B-111-consistent-visual-production/tasks/P1-tasks.md` and `tasks/p1-run-design.md`. The prior direct-dispatch correction is recorded in debug record 022 and is superseded by this unified-owner correction.

## Plan

1. Extend the persisted Studio job path with a `Staged` state and explicit activation so no serverless job can be claimed before a Run starts it.
2. Add a structured RunPod serverless endpoint-state probe that reads the configured readiness contract and reports actual worker state. Missing or unmatched configuration remains non-ready with a concrete diagnostic.
3. Replace `SceneImageService` direct provider dispatch with a single admission path: persist the scene image and durable production job, then activate immediately only for hosted/dedicated work or a serverless endpoint whose live state is proven active. Keep cold/unknown serverless work staged.
4. Add a focused Runs screen that lists staged and active Studio work, displays the actual endpoint state, and exposes Start only where staging requires deliberate submission.
5. Build the Web project after each implementation change. Automated tests remain disabled by the user for this debugging session.

Blast radius: `DreamGenClone.Domain/Processing`, durable persistence and worker execution, Model Manager provider-readiness services, `SceneImageService`, Program dependency injection, and the Studio/Runs Razor surfaces. No RP prompt or gate behavior changes are planned.

## Resolution

- `SceneImageService` now admits every Studio render, normal edit, Identity edit, and Finish edit to the persisted `DurableBackgroundJobs` owner. The prior direct hosted/dedicated handler invocation has been removed.
- `SceneImageRenderingJobHandler` and `SceneImageEditingJobHandler` now implement `IDurableBackgroundJobHandler`, preserving their existing image execution, storage, and audit behavior under the shared durable worker.
- Durable jobs now support `Staged`. The worker continues to claim only `Queued` and retry-scheduled work. `TryActivateAsync` is an atomic persisted `Staged -> Queued` transition.
- Hosted and dedicated Studio work is admitted as `Queued`; serverless Studio work is admitted as `Staged`, because the live provider configuration cannot prove worker warmth.
- `ProductionDashboard.razor` now exposes a Start button only for a `Staged` durable job. The action calls the persisted activation transition, then reloads the queue.
- Serverless automatic activation is intentionally not claimed as resolved: the live providers have no configured readiness path or success contract, and the current `/health` Boolean probe does not preserve worker-state data. A subsequent change must implement a structured configured worker-state contract before changing serverless admission from fail-closed staging to auto-flow.

## Validated

- [ ] Pending user verification of the Production Dashboard: hosted work appears and flows through the durable queue; cold serverless work appears as Staged and Start activates it; configured worker-state readiness is still required before verified-warm serverless auto-flow can be validated.
- Web build: `dotnet build DreamGenClone.Web\DreamGenClone.csproj --no-restore --nologo` completed on 2026-09-06 with 0 errors (119 warnings). Automated tests were not run per user instruction.
