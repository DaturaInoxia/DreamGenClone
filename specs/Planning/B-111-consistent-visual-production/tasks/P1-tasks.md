# P1 — Run engine & provider-class execution — Task List

**Phase goal:** the Run — a staged, provider-class-aware batch that pays one cold start per endpoint
(the first request warms the worker; the rest ride it), keeps one-offs interactive via priority, and
reports honest state — plus the refusal-outcome model and SFW-clamp removal, and the Runs UI.
**Absorbs:** the B-102 remainder.
**Gate:** `plan.md` → P1 exit gate. **Contracts:** C4 (Run), C7 (Runs UI), and refusal (C4-12→14).

> ✅ **Fork confirmed by user (2026-09-05): Option A — extend `ProductionWorkload`.**
> ✅ **No warm-up (user 2026-09-05):** the serverless worker cold-starts on the **first request**;
> the existing `ProviderTimeoutSeconds` handling absorbs the cold-start wait. No sentinel job, no
> keep-alive, no active-worker raise. The one-cold-start-per-Run benefit still holds naturally —
> grouped jobs flow to the warming/warm worker within its idle window.

---

## Grounded reality (verified 2026-09-05) — what already exists vs what's missing

| Capability | State in code | Evidence |
|---|---|---|
| Durable jobs: lanes / lease / claim / retry / **cancel** / recovery / worker pump | **DONE** | `DurableBackgroundJob.cs` (lanes, states), `DurableBackgroundJobRepository.cs` (`TryClaimNextAsync`, `TryRenewLeaseAsync`, `TryCancelAsync`, `RecoverExpiredLeasesAsync`, SQLite DDL), `TextAnalysisDurableWorker.cs` (pumps all 4 lanes incl. `ImageRender`/`ImageEdit`) |
| 3 serverless clients (submit + poll) | **exist** | `RunPodServerlessImageClient/EditingClient/IdentityClient.cs` — `POST /run`, hard-coded **5s** poll to `model.ProviderTimeoutSeconds` |
| Protocol routing | **DONE** | `ImageGenerationClientDispatcher` / `ImageEditingClientDispatcher` / `IdentityConditionedImageClientDispatcher` switch on `model.ImageProtocol`, fail-fast on unknown |
| Provider serverless config fields | **exist but UNUSED** | `Provider.cs`: `LifecycleStrategyIdentifier`, `ReadinessPath`, `ReadinessSuccessContractJson`, `TransitionTimeoutSeconds`, `TransitionMarginSeconds`, `ShutdownDrainPolicyJson`, `MaximumActiveRequests`, `QueueCapacity`. Persisted (`ProviderRepository.SaveAsync`), UI-editable (`ModelManager.razor`) |
| Multi-item orchestration aggregate | **exists** | `IProductionWorkloadService` — `ProductionWorkload`/`Item`/`Attempt`, `ProductionDispatchGroup` (groups by `CompatibilityKey` + `Endpoint`), `CreateDraftAsync`/`SubmitAsync`/`ReconcileAsync`; adapters `RunPodProductionDispatchAdapter` (+ Together) |
| Warm-up / keep-alive / cold-start accounting | **MISSING** | none in clients or workload service |
| Priority (`lowPriority`) so one-off beats a Run | **MISSING** | not in payload |
| `batch_size` > 1 in one job | **MISSING** | one image per job |
| Serverless `CancelAsync` (`POST /cancel/{jobId}`) | **MISSING** | clients have no cancel; RunPod API supports it; durable `TryCancelAsync` exists |
| Webhook / adaptive poll | **MISSING** | hard-coded 5s |
| Honest cold/warming/warm state read | **MISSING** | `CheckImageModelHealthAsync` hits `/health` but contract unused; `ReadinessPath` unused |
| Idle scale-down surfacing (F7.6) | **MISSING** | none |
| Refusal-outcome model (C4-12→14) | **MISSING** | only hard adult-content guards exist |
| SFW clamp | present; **P1 removes it** per P0 `clamp-removal-map.md` |
| Runs UI (`RunTray` + Runs page) | **MISSING** | no Runs surface exists |

---

## P1-T0 — Design note (fork + no-warm-up decisions) ✅ DECIDED

**Decided by user 2026-09-05:** Option A (extend `ProductionWorkload`) + **no warm-up**.

**Deliverable of T0:** `p1-run-design.md` recording: (1) the Run *is* an extended
`ProductionWorkload`; (2) the lifecycle mapping `Staged→Warming→Draining→Complete/Aborted` onto
`ProductionWorkload`'s existing states — note **`Warming` collapses to "first request in flight; worker
cold-starting"** since there is no explicit warm step; (3) grouping by (endpoint, class); (4) that
cold-start is absorbed by `ProviderTimeoutSeconds`, not by a warm-up mechanism. **Coordinator writes
this note; then T1 dispatches.**

---

## Tasks (Option A, no warm-up)

> Each carries the standing constraints in `tasks/README.md`. Dispatch = `GPT-5.6 Luna (copilot)`
> unless noted. Sequencing respects that several clients share files (serialize edits to a client).

### P1-T1 — Provider-class execution policy (wire the used-but-unwired Provider fields)
Add a `ProviderExecutionClass` resolution (Hosted-API / Dedicated-Pod / Self-built-Serverless) derived
from `ImageProtocol`, and a policy object that reads the relevant `Provider` fields into runtime
behaviour: `MaximumActiveRequests` / `QueueCapacity` (concurrency), `ReadinessPath` +
`ReadinessSuccessContractJson` (health/state read only). **No warm-up:** the transition/lifecycle
fields (`LifecycleStrategyIdentifier`, `TransitionTimeoutSeconds`, `TransitionMarginSeconds`,
`ShutdownDrainPolicyJson`) are **not** used to trigger warming in P1 — leave them unwired (or
display-only). **Data-driven, no hard-codes** (FR-C4-03). Fail fast on missing required serverless
config for a serverless provider.

### P1-T2 — Serverless client hardening (the 3 clients)
Add to `RunPodServerlessImageClient/EditingClient/IdentityClient`: `CancelAsync` (`POST /cancel/{jobId}`),
malformed-output handling (missing `output.images`, base64 failure → explicit error, no fabrication),
optional `batch_size` in the request, a `lowPriority` flag in the payload, and webhook-or-adaptive
poll replacing the fixed 5s. Cold-start wait stays governed by `ProviderTimeoutSeconds` (no warm-up).
**Serialize edits per file.** (FR-C4-06/07/09)

### P1-T3 — Group by (endpoint, class) + submit-and-drain — ✅ ALREADY SATISFIED (verified 2026-09-05)
**No new code.** The Run (PATH B = `ProductionWorkload`, per fork A) already does this:
`ProductionWorkloadService` (lines ~410-434) builds `ProductionDispatchGroup` keyed by
`(CompatibilityKey, Endpoint)`; `RunPodProductionDispatchAdapter.SubmitAsync` submits each attempt in
the group to `/run`; `PollAsync` captures **`delayTime` + `executionTime`** (cold-start evidence);
`CancelAsync` exists. With no warm-up, submit-and-drain is exactly what `SubmitAsync`/`ReconcileAsync`
already do. **T3 becomes a verification item in T10, not an implementation task.**

> ### 🔁 GROUNDING CORRECTION (2026-09-05) — two dispatch paths, most of P1 already exists
> A deeper trace found **two live paths**, and the Run (fork A) is the already-wired PATH B:
> - **PATH A** (interactive one-off): Studio "Render" → durable job → `SceneImageRenderingJobHandler`
>   → `RunPodServerlessImageClient`. **Hardened by T1/T2.** This is the primary Studio render path.
> - **PATH B** (the Run): `IProductionWorkloadService` → `RunPodProductionDispatchAdapter`, **already
>   wired to `ProductionWorkspace.razor`** (embedded in `SceneImageStudio.razor` ~L802), used today for
>   batch/asset/LoRA workflows. Grouping, submit, poll-with-delayTime, and cancel already exist.
>
> **Net effect:** P1's remaining real work is small and per-path:
> | Feature | PATH A | PATH B (Run) | Where |
> |---|---|---|---|
> | cancel + malformed-output | ✅ T2 | ✅ existing `CancelAsync` (+ T2b editing/identity clients) | done |
> | cold-start timing | not captured (minor) | ✅ `delayTime` captured | PATH B done |
> | **lowPriority** | ✅ T2a | ❌ needs `ProductionDispatchPolicy` field → adapter (T4) | T4 |
> | **refusal-outcome model** | throws on empty only | ⚠️ stores output unvalidated → `ProductionReconciliationService.ApplyResultAsync` (T6) | T6 both paths |
> | **SFW clamp removal** | `SceneImageRenderingJobHandler` L99-107 + compiler sites | n/a (no clamp) | T7 PATH A + compilers |
> | **Runs UI** | n/a | ✅ `ProductionWorkspace.razor` EXISTS — assess vs C7, enhance not rebuild | T8 |
> | staging | n/a | ✅ ProductionWorkspace already stages | T9 mostly exists |
>
> **Revised remaining P1 = T4 (lowPriority on Run) + T6 (refusal) + T7 (clamp removal) + T5 (honest
> state display) + T8 (assess/enhance `ProductionWorkspace` against C7) + T9 (verify staging) + T10
> (gate).** Not "build a Run engine."

### P1-T4 — Priority lane — PATH B (the Run) needs it; PATH A done
PATH A `lowPriority` is done (T2a). Add a `bool LowPriority` to `ProductionDispatchPolicy`
(`IProductionWorkloadService.cs`), plumb it through, and emit `payload["policy"]["lowPriority"]` in
`RunPodProductionDispatchAdapter.SubmitAsync` (parallel to how it builds the workflow payload). A
one-off (PATH A, not low-priority) then stays interactive while a Run (PATH B, low-priority) drains.
(FR-C4-05, A4)

### P1-T5 — Honest endpoint state (display) + idle scale-down surfacing
PATH B already captures `delayTime`/`executionTime`; surface cold/warming/warm state in the UI in
backend vocabulary — **informational** ("cold — first image ~Ns"), not driving warm-up. Optionally
capture a coarse first-vs-subsequent timing on PATH A. Surface the idle scale-down hazard
(3d→2, 7d→0). (FR-C4-08/11, F7.6)

### P1-T6 — Refusal-outcome model (manual re-pick; NO auto-advance)
**Simplified by user decision 2026-09-05: the app never picks a model — the user does.** Detect the
deterministic refusal modes inline (policy/HTTP error and empty output), classify them as a
**refusal** (a distinct, user-legible failure carrying model + endpoint + mode), record an audit
signal (debug event) against `(model, endpoint)`, and ensure the failure is **surfaced clearly** so
the user can select a different model and retry. **No ordered-preference data, no reorder UI, no
automatic model-advance, no strategy change.** Attach on both paths: PATH A
`SceneImageRenderingJobHandler` (already throws on empty — classify + surface) and PATH B
`ProductionReconciliationService.ApplyResultAsync` (currently stores output unvalidated — add the
same classification). Silent sanitisation is deferred to the P4 scoring gate (not inline). (A15/A17)

### P1-T7 — Remove the SFW clamp (execute the P0 map) — PATH A + compiler sites
Apply `clamp-removal-map.md`: delete the 30 REMOVE sites (the `SceneImageRenderingJobHandler` L99-107
suffix append — **PATH A only; PATH B has no clamp**, `ResolveRatingTag(phase,policy)` safe path,
SDXL/Pony `BuildSystemPrompt` SFW branches, `SfwClampSuffix` members, asserting tests); **keep** the
15 KEEP sites (`ImageContentPolicy` capability signal). Lands with T6 (its replacement), per G2.

### P1-T8 — Runs UI — assess & enhance the EXISTING `ProductionWorkspace.razor` (do not rebuild)
The Run UI already exists: `ProductionWorkspace.razor` (embedded in `SceneImageStudio.razor` ~L802)
with submit/reconcile. **Assess it against C7 (`ui/RunTray.md`)** and enhance where it falls short:
extract/introduce the reusable `RunTray` component, virtualize the job list (FR-C7-04), show honest
cold/warming/warm + staged/draining state in backend vocabulary (FR-C7-05), abort-with-confirm
(FR-C7-09), non-blocking (FR-C7-08). **Enhance, don't rebuild** — and do not create a second Runs
surface. **Razor rules apply.**

### P1-T9 — Staging entry points — verify/extend (mostly exists)
`ProductionWorkspace` already stages workloads. Verify the staging entry points and add any missing
embed of `RunTray` in Studio / Reference Bootstrap (P2 consumes) / Asset Manager. Reuse the existing
`CreateDraftAsync` staging path; do not duplicate it.

### P1-T10 — Tests, round-trip, evidence, gate
Contract/concurrency/persistence tests for the Run; one normal + one BigLust application round trip;
refusal-advance test (A15), silent-sanitisation-caught test (A16), exhausted-order fail-fast (A17);
cold-start-count / cost-vs-baseline evidence; `tools/e2e/` LLM-free Runs-screen flow; full C# build +
suite green; then the P1 exit-gate checklist in `plan.md`.

---

## Resolved by the T0 decisions (2026-09-05)
1. **Fork:** Option A — extend `ProductionWorkload`. ✅
2. **`ProductionWorkload` vs B-100 coupling:** accepted — it is an execution layer (compiled attempts
   + endpoints), not story semantics; B-111 hosts its execution here. ✅
3. **Warm-up mechanism:** none. Cold-start on first request, absorbed by `ProviderTimeoutSeconds`. ✅
