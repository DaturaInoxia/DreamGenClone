# Phase 3 Implementation Plan - Location and Multi-POV

## Summary

Add reusable location profiles, canonical visual-plan versions, a Three.js blocking editor,
multiple shot plans, deterministic control compilation, and a scene-controlled ComfyUI path.

## Change Surface

### Domain and Application

- Add location, plan, actor/object/relationship, shot, control, and manifest records.
- Add repository/storage/control-compiler abstractions and DTO validators.
- Extend controlled-render provenance with exact plan, shot, and manifest IDs.

### Infrastructure

- Add additive SQLite schema and repositories.
- Extend safe control-asset storage.
- Extend the selected conditioned ComfyUI workflow only after a fixed standalone proof.

### Web

- Add location-profile management UI.
- Add visual-plan compiler/orchestration and approval.
- Add `SceneBlockingEditor` Razor component, isolated JS module, and CSS.
- Add shot management, control compilation job, status UI, and scene-controlled render action.
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
6. Scene-controlled render orchestration and three-shot acceptance.

## Architectural Constraints

- No edits to RP continuation or prompt-slot behavior.
- One plan is the world-state source for all shots.
- Three.js stays behind DTO and compiler boundaries.
- Controls do not erase semantic relationship constraints.
- All runtime control values are persisted and UI backed.
- Missing controls fail; no downgrade to identity-only or prompt-only.

## Blast Radius

The largest frontend risk is WebGL lifecycle and responsive layout. Keep the editor isolated and
dispose every resource. The largest backend risk is workflow complexity and mismatched control
dimensions; freeze a standalone workflow first and assert its JSON structure and asset hashes.
