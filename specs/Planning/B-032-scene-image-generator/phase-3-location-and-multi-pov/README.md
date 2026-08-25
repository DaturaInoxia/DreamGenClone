# Phase 3 - Location And Multi-POV

**Status:** Planned
**Epic:** B-032 Scene Image Generator
**Prerequisite (2026-08-25 B-097 re-open):** ControlNet OpenPose + Depth conditioning is established
in Phase 1B/2; camera-specific pose/depth controls derive from the canonical visual plan.

## Goal

Keep a detailed location, important object placement, and one frozen beat consistent while rendering multiple camera viewpoints.

## Implementation Package

- [`research.md`](research.md) - editor/control alternatives and the shared-scene decision.
- [`spec.md`](spec.md) - location, plan, blocking, shot, and control requirements.
- [`data-model.md`](data-model.md) - versioned aggregates, coordinate contract, and staleness rules.
- [`contracts.md`](contracts.md) - Three.js interop, control compiler, renderer, and browser proof.
- [`plan.md`](plan.md) - layered implementation slices and risk boundaries.
- [`tasks.md`](tasks.md) - dependency-ordered implementation ledger.

## Delivery

- Add persisted `LocationVisualProfile` records for reusable scenario locations.
- Store approved location references, canonical objects, spatial anchors, lighting variants, layout/depth references, provenance, and checksums.
- Introduce the camera-independent `SceneVisualPlan` as the source of truth for cast, wardrobe, blocking, relationships, objects, lighting, mood, and content boundary.
- Add `SceneShotPlan` records compiled from one frozen visual plan.
- Generate camera-specific pose, depth, semantic, and character-region controls. Add edge/canny
	only after a frozen proof demonstrates measurable value.
- Derive omniscient and character POV shots by changing the camera/framing, not by reinterpreting the beat.
- Use Three.js for the first integrated blocking editor while persisting engine-neutral scene data;
	Blender remains an optional later compiler, not a prerequisite.

## Exit Gate

Multiple approved POV shots preserve the same cast assignments, wardrobe, required relationships, object anchors, and location identity unless the visual plan is explicitly versioned.
