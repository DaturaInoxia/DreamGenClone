# Phase 4 - Validation And Repair

**Status:** Planned
**Epic:** B-032 Scene Image Generator

## Goal

Make controlled rendering auditable and recoverable through bounded candidate selection, explicit
acceptance, and provenance-preserving repair without hiding failures behind random-seed searches.

## POC Delivery Decision (2026-08-25)

- First ship a persisted manual quality gate: generated images require review and only an explicitly
	accepted image can feed later phases or continuity anchors.
- Generate a configured small candidate set, initially three distinct seeds, and present it for
	side-by-side accept/reject selection. The count is UI-backed persisted configuration, not a
	hardcoded runtime default.
- Allow an otherwise valuable candidate to create one or more bounded Qwen semantic-edit children;
	every child returns to review and never overwrites or inherits acceptance from its parent.
- Treat pose estimators, person/hand detectors, detailers, SAM, and LaMa as prevention,
	localization, or repair tools, not authoritative anatomy validators.
- Defer automated anatomy decisions and automatic repair until a configured evaluator passes the
	frozen per-code corpus. The current one-pod POC must not require another large model runtime.

## Implementation Package

- [`research.md`](research.md) - validator classes, repair taxonomy, bounds, and anchor evidence.
- [`spec.md`](spec.md) - report, review, repair, approval, and anchor requirements.
- [`data-model.md`](data-model.md) - policies, findings, overrides, attempts, approvals, and anchors.
- [`contracts.md`](contracts.md) - validators, schema, policy resolver, reservation, and jobs.
- [`plan.md`](plan.md) - report-first slices, trust boundaries, and risks.
- [`tasks.md`](tasks.md) - dependency-ordered implementation ledger.

## Delivery

- Add `SceneValidationReport` with constraint-level pass/fail, confidence, evidence, and repair recommendation.
- Add candidate-set provenance and immutable accept/reject decisions; keep render completion,
  validation result, and user acceptance as separate states.
- Validate cast count, identity, wardrobe, required and forbidden relationships, blocking, POV, object anchors, location, anatomy, and image integrity.
- Add bounded local repair only for finding/action pairs that pass a dedicated frozen proof. Qwen is
	the current semantic-editor candidate; rejected Juggernaut inpainting is not a default route.
- Route structural failures back to blocking/control compilation instead of attempting arbitrary rerenders.
- Persist complete render provenance: workflow, checkpoint, adapters, controls, masks, prompts, seed, sampler, settings, and parent plan IDs.
- Add explicit user-approved `ApprovedSceneFrame` continuity anchors for future beats, POVs,
  characters, and locations.
- Enforce UI-backed retry/repair bounds and fail explicitly when required controls or identity assets are unavailable.

## Exit Gate

Every controlled render has an auditable review/validation result, only accepted frames are
downstream-eligible, candidate and repair bounds are enforced, unresolved constraints are exposed,
and approved frames can be reused as continuity anchors. Automated validator and repair behavior is
enabled only for exact finding/action pairs with accepted proof evidence.
