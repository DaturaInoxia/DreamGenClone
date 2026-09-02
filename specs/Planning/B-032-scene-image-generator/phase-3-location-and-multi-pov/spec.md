# Phase 3 Specification - Location Continuity And Multi-POV Production

**Status:** Ready after the Phase 2 production exit gate
**Depends on:** Qualified Phase 2 cells, approved character assets, B-100 frozen Moments
**Architecture:** [`../production-architecture.md`](../production-architecture.md)
**Evidence:** [`../provider-evidence-matrix.md`](../provider-evidence-matrix.md)

## Goal

Prepare and render coherent shot families from one approved, camera-independent Moment
interpretation while preserving location identity/state, cast and wardrobe ownership, blocking,
screen direction, landmarks, props, lighting, and spatial relationships across viewpoints.

## Non-Goals

- General-purpose 3D modeling, physics, animation, or cloth simulation.
- Claiming exact contact/anatomy from pose or depth controls alone.
- Mandatory Blender installation.
- Reinterpreting story/world state independently for each camera.
- Migrating pre-production sessions or retaining old one-off generation modes.
- B-101 timeline placement/publication or future audio/video rendering.

## User Stories

### US3.1 - Curate reusable location states

The user approves a reusable location profile and explicit state variants with coordinate frame,
dimensions, stable landmarks, references, materials, lighting/time/weather, exclusions, and
provenance.

### US3.2 - Review one canonical visual plan

The user resolves a frozen Moment to exact location-state, character, body, and wardrobe versions.
The system proposes typed facts, while the user reviews any inferred facts before approval.

### US3.3 - Block actors, props, and cameras

The user edits engine-neutral actor/prop transforms, joints, anchors, visibility, camera rigs, and
screen direction in a full-bleed Three.js workspace with versioned undo/redo and save-as-version.

### US3.4 - Prepare a shot family

The user creates wide, medium, close/reaction, over-the-shoulder, reverse, and character-POV shots
against one approved visual plan, then compiles deterministic previews and controls.

### US3.5 - Dispatch and compare a workload

The user prepares compatible shot items together, reviews readiness/cost, submits one workload, and
compares attempts in shot-family context before approval.

### US3.6 - Hand approved images to presentation

Each approved derivative exposes immutable Moment/shot/attempt lineage and placement metadata to
B-101 without transferring production ownership.

## Functional Requirements

### Locations And Visual Plans

- **FR3-001:** Persist immutable location-profile versions with stable location/landmark keys,
  coordinate convention, dimensions, approved references, provenance, consent/license, and use scope.
- **FR3-002:** Persist location-state variants separately for lighting/time/weather/dressing changes;
  a variant never mutates the base profile.
- **FR3-003:** Persist and validate one right-handed, centimeter, Y-up coordinate convention with
  positive dimensions/scales and normalized rotations.
- **FR3-004:** A visual plan references exact B-100 Moment/fact versions, location-state version,
  character identity/body/wardrobe versions, and content-policy snapshot.
- **FR3-005:** Every actor, object, landmark, anchor, and relationship has a stable semantic key
  unique in the plan.
- **FR3-006:** Every plan fact records canonical, inferred-reviewed, or `UserAuthored` provenance.
- **FR3-007:** Relationships use typed subject/predicate/object/anchor, parameters, importance, and
  validation mode; prose is not their only representation.
- **FR3-008:** Any schema-bound inference produces a draft with confidence/evidence. It cannot
  overwrite approved facts or emit model-native media prompts.
- **FR3-009:** Approval freezes a visual-plan version. World-state changes create a successor and
  make dependent shots/controls/workload items stale.

### Blocking And Shots

- **FR3-010:** Persist engine-neutral transforms, skeleton/joints, actor regions, prop bounds,
  relationship anchors, and camera rigs; do not persist Three.js object serialization.
- **FR3-011:** The editor supports selection, translate/rotate, joint adjustment, visibility,
  camera orbit/pan/dolly, undo/redo, validation overlays, and save-as-version.
- **FR3-012:** Each shot references exactly one approved visual-plan version and stable shot key.
- **FR3-013:** Persist shot purpose/type, subject priority, POV owner, camera intrinsics/extrinsics,
  aspect/output, crop/headroom, focus/depth, visible/occluded keys, screen direction, and movement intent.
- **FR3-014:** Creating another viewpoint creates a shot version, never another interpretation of
  the Moment or visual plan.
- **FR3-015:** A camera-only change creates a new shot version and invalidates only that shot's
  controls/items; world-state changes invalidate all dependent shots.
- **FR3-016:** Shot-family invariants explicitly name required cast/location/wardrobe/landmark/prop,
  relative-position, lighting, and screen-direction facts shared by every member.
- **FR3-017:** A character POV changes camera/framing/visibility only; it does not silently add,
  remove, or relocate canonical facts.

### Controls, Compilation, And Qualification

- **FR3-018:** Control compilation is deterministic from exact visual-plan, shot, compiler,
  renderer, profile, and source-asset versions.
- **FR3-019:** Compilers may emit preview, depth, pose, actor-region, semantic-region, edge, normal,
  or segmentation controls only when the exact model/profile cell documents and qualifies them.
- **FR3-020:** Every control asset has dimensions, semantic owner/key, preprocessing, checksum,
  compiler/input hash, and an immutable manifest.
- **FR3-021:** Missing, stale, overlapping, dimensionally invalid, or unqualified required controls
  fail readiness before dispatch.
- **FR3-022:** Visible controlled actors have explicit ownership bindings. Region/control guidance
  never substitutes for identity/body/wardrobe references.
- **FR3-023:** The deterministic model-family compiler combines frozen shot facts, qualified Phase 2
  character bindings, location references, and qualified spatial controls without LLM polishing.
- **FR3-024:** No compiler, resolver, or dispatcher changes model, provider, operation, controls,
  references, shot type, or quality goal when configuration/capability is absent.
- **FR3-025:** Spatial qualification is cell-based by model/workflow/compiler, shot type, actor count,
  location/reference layout, control tuple, identity tuple, and composition class.
- **FR3-026:** Fixed seeds and attractive outputs are insufficient; causal control evidence and
  invariant/shot-specific scores are required.

### Workloads, Review, And Handoff

- **FR3-027:** A shot family can be prepared as one durable workload using the Phase 2 workload,
  item, attempt, reconciliation, and approval state machines.
- **FR3-028:** Readiness reports stale/missing plans, shots, controls, assets, profiles, endpoints,
  policy, output count, compatible dispatch groups, dependencies, and cost estimate/range.
- **FR3-029:** Compatible items may share a dispatch/warm window; incompatibility splits explicit
  groups rather than silently changing requests.
- **FR3-030:** Every attempt retains exact plan/shot/control/location/character/compiler/profile/
  workflow/provider lineage and owned output checksum.
- **FR3-031:** Production Studio preserves selected Moment, visual plan, shot family, shot, attempt,
  inspector, and queue context during navigation and refresh.
- **FR3-032:** The UI shows invariant violations across a shot family separately from shot-specific
  failures and supports side-by-side/overlay comparison.
- **FR3-033:** Approval remains per derivative; family readiness requires every required shot to
  have one approved derivative satisfying the same invariant set.
- **FR3-034:** Approved derivatives expose B-101 placement contracts keyed by exact Moment and shot;
  B-101 does not mutate production attempts or lineage.
- **FR3-035:** Asset Manager supports location profiles/states/references, control assets, manifests,
  previews, and approved derivatives through the shared Phase 2 asset contract.
- **FR3-036:** A new-session baseline is required. No backfill, dual-read/write, compatibility
  adapter, synthetic visual plan, or old-mode fallback is implemented.
- **FR3-037:** All mutations/jobs emit structured diagnostics with no secrets; retention guards
  protect assets referenced by plans, controls, attempts, approval, or B-101 placement.

## Acceptance Scenarios

1. A location profile cannot be approved without valid dimensions, provenance/use scope, and
   required landmark transforms.
2. A visual plan cannot approve with duplicate keys or unresolved character/location versions.
3. Reloading a blocking version restores equivalent engine-neutral actors, props, joints, and cameras.
4. Changing a landmark creates a successor plan and marks all dependent shot controls/items stale.
5. Changing only a medium camera leaves the wide-shot manifest current.
6. A visible actor with missing/overlapping required regions blocks workload readiness.
7. Wide, medium, reverse, and POV shots retain one invariant set and exact approved source versions.
8. A POV camera cannot introduce an actor/object absent from the approved visual plan.
9. An unqualified depth+pose+identity tuple blocks dispatch even if each component passed separately.
10. Recompiling unchanged inputs reproduces byte-equivalent canonical manifests and requests.
11. A mixed-capability shot family splits into explicit compatible dispatch groups with cost impact.
12. Restart after provider submission resumes the same attempts and captures transient outputs.
13. A rejected reverse shot can retry as a new attempt without changing approved wide/medium results.
14. Switching Moments and returning restores blocking, shot selection, attempts, and queue context.
15. B-101 receives one approved derivative placement contract and cannot overwrite its attempt.
16. Opening an older session provides create-new-session guidance and creates no Phase 3 records.

## Exit Gate

- At least one required wide/medium/reverse-or-OTS/character-POV family compiles from one approved
  visual plan and completes through durable workload dispatch/recovery/review/approval.
- Every shot passes cast identity/body/wardrobe ownership, location/landmark identity, required
  relationships, prop ownership, lighting/state, screen direction, and shot-specific camera facts.
- Every exposed control/identity/location capability tuple has a passing frozen local matrix cell.
- Controls and provider requests reproduce from exact version IDs and input/compiler hashes.
- No request downgrades or reinterprets the approved plan, shot, controls, provider, or operation.
- Asset Manager, Production Studio, Three.js editor, and B-101 handoff pass desktop/mobile,
  accessibility, state-recovery, and no-overlap checks.
- Affected tests, solution build, full suite, Razor diagnostics, browser matrix, provider smoke,
  restart recovery, security, retention, and cost evidence are recorded and pass.
