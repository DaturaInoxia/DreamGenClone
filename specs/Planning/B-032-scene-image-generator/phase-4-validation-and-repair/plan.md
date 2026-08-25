# Phase 4 Implementation Plan - Validation, Repair, and Anchors

## Summary

Build a report-first validation pipeline, explicit review/approval UI, policy-driven repair planner,
transactionally bounded repair jobs, and versioned continuity anchors. Automatic repair remains
disabled per finding code until its frozen proof passes.

The POC starts with manual acceptance and configured candidate sets rather than requiring a new
vision service. Qwen semantic repair is user-directed and creates a review-required child. Vision
ranking and automatic anatomy decisions remain later slices.

## Change Surface

### Domain and Application

- Add candidate sets/decisions, policies, runs, findings, overrides, repairs, approvals, anchors,
  and usages.
- Add repository, validator, planner, approval, and anchor resolver abstractions.
- Add strict schema DTOs and effective-finding calculation.

### Infrastructure

- Add additive SQLite schema, indexes, repositories, and transactional attempt reservation.
- Reuse configured text/vision provider abstractions where image input and strict schema are
  supported; otherwise add a narrow vision validation client.
- Reuse Qwen editor only for explicitly proven repair classes.

### Web

- Add validation policy fields to Model Manager or a dedicated UI-backed policy page.
- Add resolver, prompt builder/parser, validation and repair handlers, orchestration, and debug
  events.
- Add Studio report overlay/list, override/review, repair, approval, and anchor controls.
- Add a stable side-by-side candidate gallery with explicit accept/reject state and prevent any
  unaccepted image from entering downstream workflows.

### Tests

- Policy/schema/resolver no-fallback tests.
- Deterministic validator and corrupted/stale input tests.
- Vision parse/raw-response/error tests.
- Effective finding/override tests.
- Transactional repair-bound concurrency tests.
- Handler idempotency/termination/provenance tests.
- Approval checksum and anchor scope/version tests.
- Browser review workflow and frozen validation/repair corpus.

## Slices

1. Manual quality gate, acceptance persistence, and downstream eligibility enforcement.
2. Configured candidate sets, distinct seed provenance, and side-by-side selection.
3. User-directed Qwen child repair with bounds, lineage, and mandatory re-review.
4. Policy/report persistence, deterministic validators, and advisory lightweight signals.
5. Configured vision validator and labeled-corpus report when a deployable service is proven.
6. Per-code repair proofs and selective automation.
7. Continuity anchors and end-to-end acceptance.

## Safety and Trust Boundaries

- A validator observation cannot mutate source plans.
- A generated image is never approved automatically merely because a job completed.
- A validation pass is not approval, and a repair child never inherits approval.
- Rejected candidate siblings remain immutable and auditable.
- Unknown and low-confidence results remain visible.
- Repairs produce descendants, never overwrite source images.
- Repair exhaustion is a terminal review state, not permission to switch strategies.
- Adult-content and exact-contact auto-repair require their own future accepted proofs.

## Blast Radius

This phase touches shared model invocation and background job infrastructure but remains within the
scene-image subsystem. Concurrency around attempt reservation is the highest data risk. Vision
false positives are the highest product risk; use labeled per-code evaluation and default each
unproven action to review-only persisted policy.

The current production plus Qwen runtimes consume almost all of the approximately 50 GB pod. Do
not make another large local evaluator a POC dependency. Any future evaluator needs an explicit
deployment and capacity decision, with no automatic provider or runtime substitution.
