# Phase 4 Research - Validation, Repair, and Continuity Anchors

**Date:** 2026-08-25
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

## 2026-08-25 Anatomy Remediation Research Decision

There is no proven one-click ComfyUI component that detects every malformed body or extra limb and
then repairs it without review. Current tools solve narrower parts of the problem:

- negative prompts reduce defect frequency but do not validate anatomy;
- OpenPose/DWPose extract expected whole-body keypoints and Pose ControlNet can constrain a new
  generation, but pose estimation is not semantic anatomy validation;
- ADetailer/Impact Detailer detect configured object classes such as faces, hands, or people and
  repaint those regions, but do not know that a detected hand belongs to an impossible third arm;
- SAM and detector masks localize a user-selected region but do not decide whether that region is
  anatomically invalid;
- LaMa can erase a masked protrusion and reconstruct background texture, but it does not reliably
  reconstruct shoulder, torso, clothing, ownership, or contact topology;
- masked diffusion inpainting can rebuild a selected region but may change identity, clothing,
  pose, adjacent people, or relationship geometry;
- Qwen Image Edit 2511 is the strongest already-deployed candidate for a semantic instruction such
  as removing an extra arm and reconstructing the torso. Its improved character consistency,
  multi-person consistency, reduced drift, and geometric reasoning make it suitable for a bounded
  repair proof, not automatic acceptance;
- a vision-language evaluator can rank or report likely defects, but remains fallible and requires
  per-code labeled evaluation. The installed `qwen_2.5_vl_7b_fp8_scaled.safetensors` is Qwen edit
  conditioning inside ComfyUI, not a general structured-output validation endpoint.

For the one-pod POC, the reliable baseline is therefore bounded candidate generation and explicit
human selection. Generate a small configured batch, initially three candidates with distinct
seeds; review all candidates; accept one valid result; optionally create one or more Qwen repair
children from an otherwise valuable candidate; and review every child again. The candidate count,
repair variant count, and bounds must be persisted UI-backed settings rather than runtime defaults.

This is not random-seed searching hidden from the user. Every candidate is a first-class render
attempt with its seed and provenance, and the selection or rejection is an explicit review record.
Only an explicitly accepted image may feed continuity anchors or later phases. Generation
completion, file integrity, and validation pass are each distinct from acceptance.

### Staged delivery decision

1. Implement manual review/acceptance and downstream blocking first.
2. Add configured candidate-set generation and side-by-side selection.
3. Add manual Qwen semantic repair as a derived-image operation with lineage and re-review.
4. Evaluate lightweight pose/person-count signals only as advisory findings.
5. Add an automated vision validator or automatic repair eligibility only after a deployable
   service passes the frozen labeled corpus for the exact finding codes it will influence.

The current approximately 50 GB pod is effectively full with the production ComfyUI and isolated
Qwen runtime. Phase 4 must not require another large validator runtime for the POC. A future
validator may run sequentially after model unload, use a remote configured provider, or follow a
volume increase, but no hidden resource fallback is allowed.

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

For anatomy specifically, prefer candidate selection over repair when another candidate already
satisfies the scene. Use Qwen repair for a localized defect in an otherwise valuable image. Return
to candidate generation or structural planning when the body layout, cast, pose, or interaction is
fundamentally wrong. Every repair creates a child and must never overwrite its source.

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

The initial anatomy corpus must include correct controls as well as extra arms/legs, fused or
missing limbs, malformed hands/feet, ambiguous occlusion, multiple interacting people, and false
positive traps such as reflections or background people. Candidate-selection evidence should
record acceptance rate by batch size and render cost. Qwen repair evidence should separately score
target correction, identity/wardrobe preservation, cast stability, relationship preservation, and
unintended changes outside the target region.

## Sources

- Local Phase 0 OpenPose and inpainting proof ledgers.
- Local Qwen simple-people proof and integrated source-image editor.
- Qwen Image Edit 2511 project materials for multi-image and multi-person editing capability.
- Existing scene-image provenance, job, debug-event, and storage patterns.
- [ComfyUI ControlNet](https://docs.comfy.org/tutorials/controlnet/controlnet) and
  [two-pass Pose ControlNet](https://docs.comfy.org/tutorials/controlnet/pose-controlnet-2-pass):
  pose conditions improve structural control but do not validate generated anatomy.
- [DWPose](https://github.com/IDEA-Research/DWPose) and
  [ComfyUI ControlNet Auxiliary Preprocessors](https://github.com/Fannovel16/comfyui_controlnet_aux):
  whole-body keypoint estimation and OpenPose-format evidence.
- [ADetailer](https://github.com/Bing-su/adetailer),
  [ComfyUI Impact Pack](https://github.com/ltdrdata/ComfyUI-Impact-Pack), and
  [Impact Subpack](https://github.com/ltdrdata/ComfyUI-Impact-Subpack): detector-driven face, hand,
  and person masking, regional detailing, candidate picking, and manual SAM masks.
- [LaMa](https://github.com/advimman/lama) and the
  [ComfyUI LaMa remover](https://github.com/Layer-norm/comfyui-lama-remover): mask-driven object
  removal/background completion rather than semantic anatomy reconstruction.
- [Qwen Image Edit 2511](https://github.com/QwenLM/Qwen-Image) and the
  [ComfyUI native workflow](https://docs.comfy.org/tutorials/image/qwen/qwen-image-edit-2511):
  semantic/appearance editing, improved character and multi-person consistency, reduced drift, and
  stronger geometric reasoning; no guarantee of anatomy validation or deterministic repair.
