# Phase 3 - Location And Multi-POV

**Status:** Designed and ready after the Phase 2 production exit gate
**Epic:** B-032 Scene Image Generator
**Baseline:** New production sessions with qualified Phase 2 character cells
**Architecture:** [`../production-architecture.md`](../production-architecture.md)
**Evidence:** [`../provider-evidence-matrix.md`](../provider-evidence-matrix.md)

## Goal

Prepare and approve coherent shot families from one frozen Moment/visual plan while preserving
location state, cast, wardrobe, blocking, landmarks, props, lighting, and screen direction.

## Implementation Package

- [`research.md`](research.md) - editor/control alternatives and the shared-scene decision.
- [`spec.md`](spec.md) - location, plan, blocking, shot, and control requirements.
- [`data-model.md`](data-model.md) - versioned aggregates, coordinate contract, and staleness rules.
- [`contracts.md`](contracts.md) - Three.js interop, control compiler, renderer, and browser proof.
- [`plan.md`](plan.md) - layered implementation slices and risk boundaries.
- [`pose-editor-plan.md`](pose-editor-plan.md) - DWPose import, 3D rig editing, IK, control export,
	and acceptance plan.
- [`tasks.md`](tasks.md) - dependency-ordered implementation ledger.

## Delivery

- Add persisted `LocationVisualProfile` records for reusable scenario locations.
- Store approved location references, canonical objects, spatial anchors, lighting variants, layout/depth references, provenance, and checksums.
- Introduce the camera-independent `SceneVisualPlan` as the source of truth for cast, wardrobe, blocking, relationships, objects, lighting, mood, and content boundary.
- Add `SceneShotPlan` records compiled from one frozen visual plan.
- Generate camera-specific pose, depth, semantic, and character-region controls. Add edge/canny
	only after a frozen proof demonstrates measurable value.
- Derive omniscient and character POV shots by changing the camera/framing, not by reinterpreting the beat.
- Prepare compatible shots as durable workloads and compare attempts in family context.
- Expose approved derivatives to B-101 through immutable placement contracts.
- Use Three.js for the first integrated blocking editor while persisting engine-neutral scene data;
	Blender remains an optional later compiler, not a prerequisite.

## Exit Gate

A wide/medium/reverse-or-OTS/character-POV family passes exact combined capability cells and the
application workload/recovery/review/approval path while preserving all required invariants.
