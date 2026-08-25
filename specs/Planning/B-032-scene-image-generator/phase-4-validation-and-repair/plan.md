# Phase 4 Implementation Plan - Validation, Repair, and Anchors

## Summary

Build a report-first validation pipeline, explicit review/approval UI, policy-driven repair planner,
transactionally bounded repair jobs, and versioned continuity anchors. Automatic repair remains
disabled per finding code until its frozen proof passes.

## Change Surface

### Domain and Application

- Add policies, runs, findings, overrides, repairs, approvals, anchors, and usages.
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

1. Policy and report persistence plus deterministic validators.
2. Configured vision validator and labeled-corpus report.
3. Review, overrides, and approval.
4. Repair policy/planner and transactional bounds, initially review-only.
5. Per-code Qwen/rerender proofs and selective automation.
6. Continuity anchors and end-to-end acceptance.

## Safety and Trust Boundaries

- A validator observation cannot mutate source plans.
- A generated image is never approved automatically merely because a job completed.
- Unknown and low-confidence results remain visible.
- Repairs produce descendants, never overwrite source images.
- Repair exhaustion is a terminal review state, not permission to switch strategies.
- Adult-content and exact-contact auto-repair require their own future accepted proofs.

## Blast Radius

This phase touches shared model invocation and background job infrastructure but remains within the
scene-image subsystem. Concurrency around attempt reservation is the highest data risk. Vision
false positives are the highest product risk; use labeled per-code evaluation and default each
unproven action to review-only persisted policy.
