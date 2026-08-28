# Phase 4 Specification - Validation, Repair, and Continuity Anchors

**Status:** POC manual-gate slice ready for review; automated validation remains gated by proof
**Depends on:** Frozen visual plans, shots, controls, identities, and render attempts

## Goal

Produce auditable constraint findings for scene images, support explicit approval, execute only
proven and configured bounded repairs, support bounded candidate selection, and reuse approved
frames as continuity evidence.

## Non-Goals

- Claiming objective truth from one vision model.
- Unbounded autonomous regeneration.
- Hidden prompt/model/control fallback.
- Replacing source story evidence with image-derived facts.
- Assuming adult or exact-contact editing is proven.

## User Stories

### US4.1 - Validate a render

The user runs a configured policy. Deterministic and vision validators produce a structured report
against exact visual-plan and shot constraints.

For the POC, a render receives a validation run and may proceed to human review when no proven
configured vision validator exists. The run records unavailable optional automation and never
represents it as a pass.

### US4.1A - Select from bounded candidates

The user requests a configured candidate count, compares render attempts with distinct persisted
seeds, and accepts or rejects each candidate without losing its provenance.

### US4.2 - Review findings

The user sees each expected constraint, status, confidence, evidence region, rationale, validator
provenance, and suggested action, then confirms/overrides findings with a reason.

### US4.3 - Repair safely

For an eligible proven finding, the user approves or policy-authorizes one repair attempt. The
derived image is revalidated and the loop stops at success, configured bound, or escalation.

### US4.4 - Approve and anchor a frame

The user approves a frame and may bind it as a scoped continuity anchor. Later scenes can explicitly
reference the anchor for validation or editing.

## Functional Requirements

- **FR4-001:** Persist named validation-policy versions; all models, thresholds, finding rules,
  repair actions, and bounds are required persisted values.
- **FR4-002:** A validation run references an immutable render attempt, plan, shot, manifest, policy,
  and expected-constraint snapshot.
- **FR4-003:** Deterministic validation runs before any model call and can fail the run when inputs
  are corrupt, stale, or incompatible.
- **FR4-004:** Vision validation uses a strict schema and one resolved configured model.
- **FR4-005:** Persist raw model output, parsed findings, parse errors, model/provider/config, and
  prompt schema version.
- **FR4-006:** Findings use stable codes and identify the subject/object/relationship when relevant.
- **FR4-007:** Findings include status, confidence, evidence regions, rationale, and suggested action.
- **FR4-008:** Unknown/unscored is distinct from pass; it cannot satisfy a required constraint.
- **FR4-009:** Required low-confidence findings and validator conflicts require human review.
- **FR4-010:** User overrides are immutable records containing actor, timestamp, decision, reason,
  and original finding.
- **FR4-011:** Approval is an explicit immutable record; completion of generation is not approval.
- **FR4-012:** Repair eligibility comes only from the exact validation-policy version.
- **FR4-013:** Every repair creates a derived render attempt linked to source image, report, finding,
  policy, action, instruction, optional mask, and attempt ordinal.
- **FR4-014:** Attempt count is reserved transactionally before enqueue and never exceeds the policy.
- **FR4-015:** A repaired output must receive a new validation run before approval or another repair.
- **FR4-016:** Qwen repair is limited to proven content/finding classes; no untested adult or
  exact-contact automatic use.
- **FR4-017:** Structural findings route to plan/shot review rather than repeated local editing.
- **FR4-018:** Anchor creation requires an approved image with a verified checksum and complete
  source provenance.
- **FR4-019:** Anchor use is explicit, scoped, versioned, and recorded in downstream provenance.
- **FR4-020:** Superseding an anchor never rewrites prior images or reports.
- **FR4-021:** UI exposes pending/running/review/approved/failed/exhausted states and actionable
  errors.
- **FR4-022:** Debug events never include binary data or secrets.
- **FR4-023:** Candidate count and seed-allocation policy are required persisted UI-backed values;
  no runtime candidate-count fallback exists.
- **FR4-024:** Every candidate is an independent immutable render attempt in one candidate set with
  its own seed and complete provenance.
- **FR4-025:** Candidate accept/reject decisions are immutable and include reviewer, timestamp, and
  optional reason; selecting one candidate does not delete siblings.
- **FR4-026:** `Complete`, validation `Passed`, and user `Accepted` are distinct states. Only an
  accepted image with a checksum-guarded `ApprovedSceneFrame` is eligible for later phases,
  publication-oriented source edits, or continuity anchors. Acceptance records the candidate
  decision and approved frame atomically; there is no second approval authority.
- **FR4-027:** Manual Qwen repair creates a child render linked to its source and instruction. The
  child starts unaccepted and requires review even when the parent was accepted.
- **FR4-028:** Pose, person/hand detection, Detailer, SAM, and LaMa results are advisory or
  user-directed evidence; none independently establishes anatomy validity.
- **FR4-029:** An automated anatomy finding may influence rejection, ranking, or repair eligibility
  only after the exact evaluator/config/schema version passes the frozen per-code corpus.
- **FR4-030:** The one-pod POC cannot require a second large validator runtime. Any later local or
  remote validator is explicitly configured and missing capacity fails visibly without fallback.

## Finding Vocabulary

Initial codes: `InputIntegrity`, `CastCount`, `ActorMissing`, `ActorUnexpected`, `IdentityMismatch`,
`IdentitySwapped`, `WardrobeMismatch`, `LandmarkMissing`, `ObjectOwnership`, `SpatialRelationship`,
`ContactIntent`, `Anatomy`, `ControlNonCompliance`, `ContinuityMismatch`, and `ValidatorUncertain`.

Extend through persisted/schema-versioned codes; do not use ad hoc free text for decision logic.

## Acceptance Scenarios

1. A stale control manifest produces deterministic failure and no vision-model call.
2. Invalid vision JSON persists the raw response and failed parse diagnostic; it does not pass.
3. An unscored required constraint leaves the report in review-required state.
4. A configured maximum of two repairs cannot enqueue a third under concurrent requests.
5. A repaired image has new provenance and does not overwrite the source file/record.
6. A structural finding cannot invoke a local edit policy.
7. A valid report can be approved, and an approved image can become a scoped anchor.
8. An anchor supersession leaves prior provenance resolvable.
9. A completed but unaccepted candidate is blocked from every downstream image-consumption path.
10. A configured three-candidate request creates three independently traceable render attempts and
  preserves rejected siblings after one is accepted.
11. A Qwen repair child is unaccepted and cannot inherit its parent's approval.
12. With no configured proven vision evaluator, the validation run records automation as
  unavailable, manual review remains available, and the system does not emit a synthetic pass.

## Exit Gate

- The labeled validation corpus has per-code precision/recall and disagreement results.
- Every automatically eligible repair code has a separate proof and bounded termination evidence.
- At least one scene completes render -> validate -> review/repair -> revalidate -> approve -> anchor.
- At least one scene completes candidate set -> compare -> reject siblings -> accept -> downstream
  eligibility, preserving every candidate's provenance.
- Attempt bounds survive concurrency tests.
- No validation or repair fallback exists.
- Narrow tests, build, full suite, and manual/browser matrix pass.
