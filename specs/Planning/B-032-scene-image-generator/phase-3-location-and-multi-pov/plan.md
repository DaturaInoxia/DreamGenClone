# Phase 3 Implementation Plan - Location and Multi-POV

## Summary

Add reusable location profiles/states, canonical visual-plan and shot-family versions, a Three.js
blocking editor, deterministic qualified controls, Phase 2 workload integration, family-aware
review/approval, and immutable B-101 placement handoff.

## Change Surface

### Domain and Application

- Add location, plan, actor/object/relationship, shot, control, and manifest records.
- Add repository/storage/control-compiler abstractions and DTO validators.
- Extend production intent/attempt provenance with exact location-state, invariant, plan, shot, and
	manifest IDs/hashes.

### Infrastructure

- Add additive SQLite schema and repositories.
- Extend safe control-asset storage.
- Integrate provider/model workflows only after exact combined identity/location/control cells pass.

### Web

- Add location-profile management UI.
- Add visual-plan compiler/orchestration and approval.
- Add `SceneBlockingEditor` Razor component, isolated JS module, and CSS.
- Add shot-family management, control compilation, workload preparation, status/comparison, review,
  and approval in the shared Production Studio.
- Add strict spatial-control Model Manager settings/resolver.

### Tests

- Serialization/coordinate/version/staleness tests.
- Repository and approval tests.
- JS interop DTO/cleanup tests where practical.
- Control pixel/hash/manifest tests.
- Workflow structure and no-fallback tests.
- Handler state/provenance tests.
- Browser screenshots/canvas checks and the three-shot manual matrix.

## Slices

1. Location profile persistence and curation.
2. Visual plan domain/compiler/versioning.
3. Three.js editor with primitives, actors, joints, cameras, and save/reload.
4. Shot plans and deterministic control export.
5. Standalone spatial-control proof and pinned workflow.
6. Shot-family workload orchestration, recovery, comparison, approval, and B-101 handoff.
7. Wide/medium/reverse-or-OTS/character-POV qualification and release evidence.

## Architectural Constraints

- No edits to RP continuation or prompt-slot behavior.
- One plan is the world-state source for all shots.
- Three.js stays behind DTO and compiler boundaries.
- Controls do not erase semantic relationship constraints.
- All runtime control values are persisted and UI backed.
- Missing controls fail; no downgrade to identity-only or prompt-only.
- New production sessions only; no backfill, synthetic plan, dual path, or compatibility mode.
- B-100 owns canonical Moment facts, B-032 owns production, B-102 owns transport/deployment, and
	B-101 owns placement/publication.

## Blast Radius

The largest frontend risk is WebGL lifecycle and responsive layout. Keep the editor isolated and
dispose every resource. The largest backend risk is workflow complexity and mismatched control
dimensions; freeze a standalone workflow first and assert its JSON structure and asset hashes.
