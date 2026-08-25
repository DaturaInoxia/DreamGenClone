# Phase 4 - Validation And Repair

**Status:** Planned
**Epic:** B-032 Scene Image Generator

## Goal

Make controlled rendering auditable and recoverable without hiding failures behind random-seed searches.

## Delivery

- Add `SceneValidationReport` with constraint-level pass/fail, confidence, evidence, and repair recommendation.
- Validate cast count, identity, wardrobe, required and forbidden relationships, blocking, POV, object anchors, location, anatomy, and image integrity.
- Add bounded local repair using Qwen semantic editing, masked inpainting, or detail repair where appropriate.
- Route structural failures back to blocking/control compilation instead of attempting arbitrary rerenders.
- Persist complete render provenance: workflow, checkpoint, adapters, controls, masks, prompts, seed, sampler, settings, and parent plan IDs.
- Add `ApprovedSceneFrame` continuity anchors for future beats, POVs, characters, and locations.
- Enforce UI-backed retry/repair bounds and fail explicitly when required controls or identity assets are unavailable.

## Exit Gate

Every controlled render has an auditable validation result, repairs stop at the configured bound, unresolved constraints are exposed, and approved frames can be reused as continuity anchors.
