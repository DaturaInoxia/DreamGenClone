# Phase 4 Tasks - Validation, Repair, and Anchors

**Prerequisite:** Phase 3 exit gate is recorded.

## A. Policies and Persistence

- [ ] P4-001 Add validation policy/run/finding/override enums and records.
- [ ] P4-002 Add repair/approval/anchor records and exact-version relationships.
- [ ] P4-003 Add repositories, additive SQLite schema/indexes, and transactional repair-attempt
  reservation.
- [ ] P4-004 Add policy approval validation requiring all thresholds, model IDs, actions, and bounds.
- [ ] P4-005 [P] Add migration, repository, state, and concurrent reservation tests.

## B. Deterministic Validation

- [ ] P4-006 Define canonical expected-constraint snapshot compilation from plan/shot/manifest.
- [ ] P4-007 Implement integrity, version, staleness, actor-region, configuration, and provenance
  validators.
- [ ] P4-008 Add validation service/job records, payload, dedupe, monotonic states, and diagnostics.
- [ ] P4-009 Ensure deterministic invalid-input findings prevent a vision call according to policy.
- [ ] P4-010 [P] Add valid/corrupt/stale/missing fixture tests.

## C. Vision Validation Proof

- [ ] P4-011 Freeze a labeled corpus containing valid examples and known violations for every
  initial finding code, including unknown/occluded cases.
- [ ] P4-012 Add explicit validation-model configuration and one strict resolver with image-input,
  schema, content-policy, and threshold validation.
- [ ] P4-013 Implement compact prompt/schema v1, strict parser, raw-response persistence, and
  unknown-code/key rejection.
- [ ] P4-014 Run the corpus, calculate per-code precision/recall/conflicts, and record unsupported
  codes as human-only.
- [ ] P4-015 Integrate only codes meeting the documented report-quality threshold; do not enable
  repair merely because reporting passes.
- [ ] P4-016 [P] Add schema, parse failure, model failure, raw evidence, and resolver tests.

## D. Review and Approval

- [ ] P4-017 Read Razor instructions and full Studio context before editing.
- [ ] P4-018 Add report summary, finding list/overlay, confidence/evidence/rationale, and validator
  provenance UI.
- [ ] P4-019 Add immutable confirm/override actions with required reasons and effective-status view.
- [ ] P4-020 Add approval service with unresolved-constraint and disk-checksum guards.
- [ ] P4-021 [P] Add effective finding, approval idempotency/checksum, Razor diagnostics, and browser
  workflow tests.

## E. Bounded Repair

- [ ] P4-022 Implement repair planner outcomes and policy-only eligibility/action selection.
- [ ] P4-023 Add review-only repair proposals for all candidate codes before any automatic execution.
- [ ] P4-024 For each proposed automatic code, freeze defects/masks/instructions/seeds, prove the
  configured Qwen or rerender action, score target correction and unintended changes, and record a
  separate decision.
- [ ] P4-025 Enable only accepted code/action pairs in an approved policy; adult and exact-contact
  remain disabled unless separately proven.
- [ ] P4-026 Add repair job, derived render attempt, revalidation enqueue, status UI, and provenance.
- [ ] P4-027 Enforce transactional attempt bounds; exhausted state requires review and cannot recurse.
- [ ] P4-028 [P] Add ineligible/eligible/concurrent/exhausted, handler idempotency, source immutability,
  and revalidation tests.

## F. Continuity Anchors

- [ ] P4-029 Implement approved-frame and anchor creation, scope, crop, revocation, supersession, and
  exact-version resolver.
- [ ] P4-030 Add anchor selection to later visual-plan validation and explicit source-image edit
  requests with persisted usage.
- [ ] P4-031 Add approved-frame/anchor UI and provenance navigation.
- [ ] P4-032 [P] Add approval prerequisite, checksum, scope compatibility, exact version, and
  historical provenance tests.

## G. Exit Evidence

- [ ] P4-033 Execute one full render -> validate -> review/repair -> revalidate -> approve -> anchor
  workflow and preserve the report.
- [ ] P4-034 Reuse the anchor in one later scene and verify that source story/plan evidence remains
  unchanged.
- [ ] P4-035 Run affected tests, solution build, full suite, browser matrix, and record the final
  B-032 acceptance decision.
