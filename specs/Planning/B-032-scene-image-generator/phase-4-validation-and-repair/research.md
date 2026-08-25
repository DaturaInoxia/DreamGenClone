# Phase 4 Research - Validation, Repair, and Continuity Anchors

**Date:** 2026-08-24
**Status:** Complete for implementation planning

## Problem

Controlled inputs improve adherence but do not prove that pixels satisfy identity, cast, spatial,
relationship, anatomy, or continuity constraints. Manual inspection alone does not scale, while a
single vision-model verdict is too opaque to authorize autonomous repair.

## Lessons From Prior Proofs

- Fixed prompts/seeds do not guarantee cast, geometry, ownership, or contact.
- OpenPose can cause limb motion without satisfying semantic contact.
- Masked Juggernaut inpainting preserved unmasked pixels but repeatedly failed hand/contact
  topology and ownership.
- Qwen source editing preserved composition well on six covered non-explicit cases, but adult
  editing and exact-contact correction are not proven.
- Validation must preserve both the defect claim and the evidence used to make it.

## Validator Classes

### Deterministic validators

Use when the answer follows from persisted artifacts:

- file integrity, dimensions, checksum, status, version, and provenance;
- plan/shot/control compatibility and staleness;
- expected actor-region existence, overlap, and semantic keys;
- requested model/workflow/config values;
- repair-attempt limits and state transitions.

### Vision validators

Use schema-bound model output for pixel-level questions:

- expected cast/actor presence;
- identity and wardrobe ownership;
- landmark/object presence and ownership;
- visible spatial relationships;
- anatomy defects;
- contact-intent appearance;
- continuity against approved anchors.

Vision findings are evidence-bearing observations, not ground truth. Persist model/config, prompt
schema version, confidence, evidence region, rationale, and raw response.

### Human review

Required for approval, low-confidence required findings, validator disagreement, exact-contact
claims, adult-content validation until separately proven, and exhausted repair policy.

## Repair Taxonomy

| Finding | Candidate action | Initial automation |
|---|---|---|
| Missing/corrupt/stale artifact | Recompile or fail configuration | Deterministic, allowed. |
| Global scene/cast/layout failure | Return to visual plan/blocking or rerender | User decision. |
| Identity/wardrobe ownership failure | Revisit region assignment/identity strengths; rerender | User decision until proven. |
| Local non-explicit semantic defect | Qwen edit with bounded mask/instruction | Eligible only after a frozen proof. |
| Exact hand/contact defect | Review/reblock; editor only after dedicated proof | Never assumed from old proofs. |
| Anatomy defect outside local proven class | Rerender or review | User decision. |
| Continuity mismatch | Select/update approved anchor or recompile plan | User decision. |

## Bounded Loop Decision

Each validation policy declares eligible finding codes, action, maximum attempts, minimum
confidence, escalation action, and whether human approval is required before execution. Attempts
are persisted before enqueue. A failure cannot recurse or consume an unrecorded retry. Default
values are forbidden; an incomplete policy cannot run.

## Continuity Anchors

An approved frame may become an anchor for exact character, location, wardrobe, object, or shot
facts. Anchors reference an immutable image checksum and source plan/shot/identity versions.
They can be supplied to later validators and explicit source-image edits, but never silently mutate
new story evidence. Supersession is explicit and historical provenance remains readable.

## Evaluation

Freeze a labeled corpus including valid frames and one known violation for each target finding
code. Measure precision/recall per code rather than one aggregate score. Automatic repair requires
high precision for its eligible code, zero out-of-mask unacceptable changes on the proof set, and
successful termination at the configured bound. Until that evidence exists, ship report/review
without auto-execution.

## Sources

- Local Phase 0 OpenPose and inpainting proof ledgers.
- Local Qwen simple-people proof and integrated source-image editor.
- Qwen Image Edit 2511 project materials for multi-image and multi-person editing capability.
- Existing scene-image provenance, job, debug-event, and storage patterns.
