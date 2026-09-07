# P1-T0 — Run Engine Design Note

**Date:** 2026-09-05 · **Decisions by:** user · **Status:** decided, dispatch-ready

This note records the two load-bearing P1 decisions and the concrete mapping, so every P1 task builds
on the same foundation.

---

## Decision 1 — The Run *is* the existing `ProductionWorkload`, extended (Fork Option A)

B-111's "Production Run" is **not** a new aggregate. It is the existing `ProductionWorkload`
(`DreamGenClone.Domain/RolePlay/ProductionMediaModels.cs`, `IProductionWorkloadService`) with added
*execution behaviour*. Building a parallel `Run` would duplicate an existing aggregate and violate G4
(one concern, one owner).

**Why it fits (verified):**
- `ProductionWorkload` already groups work by `(CompatibilityKey, Endpoint)` via `ProductionDispatchGroup`.
- It already has `CreateDraftAsync` → `SubmitAsync` → `ReconcileAsync`.
- It already has dispatch adapters (`RunPodProductionDispatchAdapter`, `TogetherProductionDispatchAdapter`).
- Its status enum already covers the full lifecycle (below) — **no new states needed**.

**Coupling check (resolved):** `ProductionWorkload` is an *execution* layer — it takes compiled
attempts + endpoints, not B-100 story/Moment semantics. B-111 hosting execution here does not pull in
B-100 semantics. Accepted.

### Lifecycle mapping (B-111 spec → existing `ProductionWorkloadStatus`)

| B-111 Run state (spec C4-01) | Existing `ProductionWorkloadStatus` | Notes |
|---|---|---|
| `Staged` | `Draft` / `Validating` / `Ready` | items staged, not yet submitted |
| `Warming` | *(collapses — see Decision 2)* | no explicit warming state; first request cold-starts inside `Queued`/`Running` |
| `Draining` | `Queued` / `Running` / `PartiallyComplete` | group's jobs flowing through the worker |
| `Complete` | `Complete` | |
| `Aborted` | `Cancelled` | |
| (failure) | `Failed` / `Blocked` | |

Item- and attempt-level lifecycles (`ProductionWorkloadItemStatus`, `ProductionAttemptStatus`) are
already rich (`Submitted`/`Running`/`Succeeded`/`Failed`/`Cancelled`/`Indeterminate`) and are reused
as-is. **P1 adds no new status enums.**

---

## Decision 2 — No warm-up

The serverless worker **cold-starts on the first request** of a group. There is **no** sentinel job,
no keep-alive, and no active-worker raise. The cold-start wait is absorbed by the already-configured
`Provider.TimeoutSeconds` (`ProviderTimeoutSeconds` in the clients).

**Consequences:**
- The one-cold-start-per-Run benefit still holds **naturally**: grouped jobs flow to the
  warming/warm worker within its idle window; only the first pays the boot cost.
- The transition/lifecycle `Provider` fields (`LifecycleStrategyIdentifier`,
  `TransitionTimeoutSeconds`, `TransitionMarginSeconds`, `ShutdownDrainPolicyJson`) are **not wired to
  trigger warming** in P1. They remain persisted and may be surfaced display-only.
- Endpoint state (cold/warming/warm) is read and shown **for honest UI only** (P1-T5), not to drive a
  warm-up.
- The `Warming` Run state is therefore informational at most; the aggregate does not need it as a
  distinct persisted status.

---

## What P1 actually adds (behaviour, not structure)

Onto the existing `ProductionWorkload` + serverless clients + durable-job system:

1. **Provider-class execution policy** (`ProviderExecutionClass` from `ImageProtocol`) reading
   `MaximumActiveRequests`/`QueueCapacity`/`ReadinessPath` — data-driven, fail-fast (P1-T1).
2. **Serverless client hardening**: `CancelAsync`, malformed-output handling, `batch_size`,
   `lowPriority`, adaptive-poll/webhook (P1-T2).
3. **Group + submit-and-drain, no warm-up** (P1-T3).
4. **Priority lane** so one-offs stay interactive (P1-T4).
5. **Honest state display + idle scale-down surfacing** (P1-T5).
6. **Refusal-outcome model** + **SFW-clamp removal** (P1-T6/T7).
7. **Runs UI** (`RunTray` + Runs page) + **staging entry points** (P1-T8/T9).
8. **Tests + round-trip + evidence + gate** (P1-T10).

**Net effect of the two decisions:** P1 adds **zero new aggregates and zero new status enums** — it is
execution behaviour + refusal + clamp-removal + UI on top of an existing spine.
