# Phase 4 - Validation And Repair

**Status:** Planned
**Epic:** B-032 Scene Image Generator

## Goal

Make controlled rendering auditable and recoverable without hiding failures behind random-seed searches.

## Implementation Package

- [`research.md`](research.md) - validator classes, repair taxonomy, bounds, and anchor evidence.
- [`spec.md`](spec.md) - report, review, repair, approval, and anchor requirements.
- [`data-model.md`](data-model.md) - policies, findings, overrides, attempts, approvals, and anchors.
- [`contracts.md`](contracts.md) - validators, schema, policy resolver, reservation, and jobs.
- [`plan.md`](plan.md) - report-first slices, trust boundaries, and risks.
- [`tasks.md`](tasks.md) - dependency-ordered implementation ledger.

## Delivery

- Add `SceneValidationReport` with constraint-level pass/fail, confidence, evidence, and repair recommendation.
- Validate cast count, identity, wardrobe, required and forbidden relationships, blocking, POV, object anchors, location, anatomy, and image integrity.
- Add bounded local repair only for finding/action pairs that pass a dedicated frozen proof. Qwen is
	the current semantic-editor candidate; rejected Juggernaut inpainting is not a default route.
- Route structural failures back to blocking/control compilation instead of attempting arbitrary rerenders.
- Persist complete render provenance: workflow, checkpoint, adapters, controls, masks, prompts, seed, sampler, settings, and parent plan IDs.
- Add explicit user-approved `ApprovedSceneFrame` continuity anchors for future beats, POVs,
  characters, and locations.
- Enforce UI-backed retry/repair bounds and fail explicitly when required controls or identity assets are unavailable.

## Exit Gate

Every controlled render has an auditable validation result, repairs stop at the configured bound, unresolved constraints are exposed, and approved frames can be reused as continuity anchors.
