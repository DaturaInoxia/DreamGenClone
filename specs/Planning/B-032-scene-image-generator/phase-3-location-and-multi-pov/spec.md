# Phase 3 Specification - Location Continuity and Multi-POV

**Status:** Ready after Phase 2 exit gate
**Depends on:** Approved identity conditioning profile and packs

## Goal

Create multiple camera views from one persisted scene interpretation while preserving location,
cast, identity assignment, wardrobe, object ownership, and spatial relationships.

## Non-Goals

- General-purpose 3D modeling.
- Physically accurate animation or cloth simulation.
- Automatic exact-contact guarantee.
- Mandatory Blender installation.
- Final automated validation and repair, delivered in Phase 4.

## User Stories

### US3.1 - Curate a location profile

The user defines a reusable location coordinate frame, dimensions, landmarks, visual references,
materials, lighting intent, and exclusions, then approves an immutable version.

### US3.2 - Compile a canonical visual plan

The user selects a beat, location version, and identity-pack versions. The system creates a draft
camera-independent plan with evidence-linked actors, wardrobe, objects, relationships, and anchors.

### US3.3 - Block and review the scene

The user adjusts actors, props, joints, and relationship anchors in a Three.js editor. Changes save
as an engine-neutral version and do not alter story evidence.

### US3.4 - Author multiple shots

The user creates cameras against one frozen plan, previews crop/visibility, and compiles per-shot
depth, pose, actor-region, and semantic controls.

### US3.5 - Render a frozen scene

The system renders each shot with the same plan version, identities, wardrobe, and location facts,
and persists complete control provenance.

## Functional Requirements

- **FR3-001:** Persist immutable location-profile versions with stable landmark IDs and safe
  reference assets.
- **FR3-002:** Persist one unit/axis convention and validate dimensions/transforms.
- **FR3-003:** A visual plan references exact beat, location, and identity-pack versions.
- **FR3-004:** Every actor and object has a stable semantic key unique in the plan.
- **FR3-005:** Every plan fact records evidence source or `UserAuthored` provenance.
- **FR3-006:** Relationships use typed subject/predicate/object plus importance and validation mode;
  prose is not the only representation.
- **FR3-007:** The visual-plan compiler may propose drafts but never silently overwrite approved
  plan values.
- **FR3-008:** The blocker persists engine-neutral transforms and joints, not Three.js serialization.
- **FR3-009:** The editor supports selection, translation, rotation, visibility, camera orbit/pan,
  joint adjustment, undo/redo, and save-as-version.
- **FR3-010:** Each shot references exactly one frozen visual-plan version.
- **FR3-011:** Camera intrinsics/extrinsics, aspect ratio, crop intent, and visible actor/object keys
  are persisted.
- **FR3-012:** Control compilation is deterministic from plan version, shot version, compiler
  version, and asset versions.
- **FR3-013:** Every controlled shot has distinct non-overlapping actor regions for visible
  identity-controlled actors.
- **FR3-014:** Control assets have checksums and a manifest; missing controls fail before render.
- **FR3-015:** Controlled rendering uses Phase 2 identity assignments plus the configured spatial
  control profile; no prompt-only fallback.
- **FR3-016:** Creating another POV creates a shot, not another visual plan.
- **FR3-017:** Changes to world state create a new plan version and make prior controls stale.
- **FR3-018:** Changes to a camera create a new shot version and invalidate only that shot's controls.
- **FR3-019:** The Studio exposes plan, shot, control-compile, and render statuses/errors.
- **FR3-020:** All generated images retain source plan/shot/control manifest provenance.

## Acceptance Scenarios

1. A location profile cannot be approved without valid dimensions and required landmark transforms.
2. A visual plan cannot compile with duplicate semantic keys or unresolved actor identity packs.
3. Reloading a saved blocking version restores equivalent engine-neutral transforms and cameras.
4. Changing a landmark creates a new plan version and marks every prior shot/control stale.
5. Changing only the medium-shot camera leaves the wide-shot control manifest current.
6. A visible actor with no valid region prevents controlled render submission.
7. Three shots from one frozen plan reference the same cast/location/version facts.
8. Existing prompt-only and identity-only modes remain independently available and labeled.

## Exit Gate

- Wide, medium, and reverse/side shots compile from one frozen plan.
- All three pass manual checks for cast, identity/wardrobe ownership, major spatial relationships,
  location landmarks, prop ownership, and lighting intent.
- Controls are reproducible by input/compiler hashes.
- No scene-controlled request downgrades to another mode.
- Automated tests, build, full suite, and browser matrix pass.
