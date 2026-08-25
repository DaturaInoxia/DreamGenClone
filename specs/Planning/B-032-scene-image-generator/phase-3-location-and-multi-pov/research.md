# Phase 3 Research - Location Continuity and Multi-POV

**Date:** 2026-08-24
**Status:** Complete for implementation planning

## Problem

Generating each camera view from prose independently changes room layout, actor position, object
ownership, wardrobe, and relationships. A fixed seed does not solve this because camera text
changes the denoising problem. Phase 3 therefore needs one frozen, camera-independent scene model
from which every shot is compiled.

## Evidence

- Prompt-only tests varied cast and geometry across seeds and views.
- OpenPose causally controls macro pose but cannot encode semantic contact or actor ownership.
- Inpainting preserved unmasked pixels but failed exact-contact semantics.
- Identity control from Phase 2 requires per-actor region ownership and should be reused by shots.
- Three.js exposes scene graphs, cameras, skinned meshes, depth materials, render targets, object
  transforms, serialization, and glTF loaders in the browser.
- Blender supports headless rendering and stronger offline geometry tooling, but adds a separate
  installation, process boundary, scripting API, and deployment requirement.

## Decision

Build the initial blocker in Three.js within the Blazor UI. Persist a renderer-neutral model:
centimeter-based coordinates, transforms, primitive/asset references, stable semantic IDs,
skeleton joints, relationships, and cameras. Three.js is an editor and preview/compiler, not the
database format.

Generate at least these per-shot controls:

- actor region masks for Phase 2 identity assignment;
- depth map for major spatial layout;
- pose/skeleton image for macro body pose where useful;
- semantic/object masks for validation and later repair;
- optional line/canny output only after a proof demonstrates value.

Do not claim exact-contact control. Store desired relationship constraints separately so Phase 4
can validate them.

## Location Representation

A location profile combines:

- semantic description and aliases;
- stable dimensions and coordinate frame;
- structural surfaces and openings;
- landmark/prop instances with stable IDs and transforms;
- visual materials, palette, lighting intent, and approved reference assets;
- exclusion rules;
- approved version and provenance.

Reusable location facts must be distinguished from one-scene temporary objects.

## Multi-POV Strategy

1. Freeze one `SceneVisualPlan` version from the selected beat, identity packs, and location profile.
2. Block all actors, objects, and relationships once in world space.
3. Define several `SceneShotPlan` cameras that reference that version.
4. Compile controls from the same world state for each camera.
5. Render without asking an LLM to reinterpret cast/layout per shot.
6. Compare shots for invariant facts and record discrepancies in Phase 4.

## Alternatives Rejected

| Alternative | Reason |
|---|---|
| Independent prompt per POV | Reinterprets the scene and cannot prove shared state. |
| OpenPose-only scene model | Lacks room/object geometry, ownership, camera-independent relationships, and exact contact. |
| Blender-first editor | Powerful but creates avoidable deployment and authoring friction for the first integrated slice. |
| Opaque Three.js JSON persistence | Couples domain data to library internals and makes version migration/audit difficult. |
| AI-generated blocking with no review | Converts model guesses into authoritative geometry without evidence. |

## Proof Matrix

Freeze one indoor location with at least four landmarks, two adult recurring actors, one portable
prop, one non-contact relationship, and one contact-intent relationship. Create wide, medium, and
reverse/side cameras. All three shots must preserve cast, actor identity/wardrobe ownership,
left/right and front/behind relationships as visible, landmark identity/layout, portable-prop
ownership, and time/lighting intent. Contact is scored but is not an automatic gate until Phase 4
defines the validator/repair policy.

## Sources Consulted

- `https://threejs.org/docs/`
- `https://threejs.org/manual/`
- `https://docs.blender.org/manual/en/latest/advanced/command_line/render.html`
- `https://github.com/lllyasviel/ControlNet`
- Local Phase 0 control and inpainting proofs
