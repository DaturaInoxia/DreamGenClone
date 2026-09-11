# B-118 Tasks — Pose Studio

**Execution rule:** complete in order unless marked `[P]`; every task ends with focused tests green.
Fix forward only. Read `plan.md`, the program map, the prompt-compiler standards and Razor instructions
before touching their matching files.

**Boundary:** B-118 owns pose authoring, extraction and the `PosePreset` library. It defines but does
not activate the render-request contract; B-117 owns the real Apply render. No batch/sweep action.

## 0. Verify before writing code

- [ ] **B118-000a** Confirm the COCO keypoint JSON and skeleton PNG shape in the git-tracked pose
  pack; record categories, resolutions, missing-keypoint cases and a stable known-good fixture set.
- [ ] **B118-000b** Confirm the local ComfyUI DWPose node/input/output contract and the existing
  image-provider/model configuration path. Record the exact configured provider/profile source;
  missing configuration must fail fast and no endpoint/node name may be guessed.
- [ ] **B118-000c** Confirm B-120's derived-asset boundary and B-117's `PoseControlSource` contract.
  Record one owner for skeleton bytes and prove no second pose store is required.

## A. Pose library and persistence

- [ ] **B118-001** Add `PosePreset` domain types: id, name, category, COCO-18 keypoint JSON,
  skeleton/thumbnail storage refs, known-good state, provenance, version/status and timestamps.
- [ ] **B118-002** Add repository/schema operations for create, read, search, version/supersede and
  delete-in-use. Missing/corrupt keypoint data fails explicitly.
- [ ] **B118-003** Add an idempotent importer for the git-tracked 472-pose pack. Preserve source
  provenance; re-running neither duplicates rows nor overwrites user-edited metadata.
- [ ] **B118-004 [P]** Test round-trip, search/category filtering, versioning, delete guards,
  idempotent import and malformed/missing source failures.

## B. DWPose extraction

- [ ] **B118-005** Add the configured local-ComfyUI extraction request and client:
  `LoadImage → DWPreprocessor → SaveImage`, returning keypoint JSON and skeleton PNG.
- [ ] **B118-006** Enforce the head-keypoint rule: nose, neck and both shoulders must meet the
  configured confidence floor. Reject with exact missing joints; never silently store a faceless pose.
- [ ] **B118-007** Persist extraction provenance including source asset/upload hash, configured
  provider/profile, node/workflow version and parameters.
- [ ] **B118-008 [P]** Test workflow shape, provider/configuration fail-fast cases, output parsing,
  head-keypoint rejection and provenance round-trip.

## C. 2D editor and save

- [ ] **B118-009** Build the COCO-18 body-joint canvas editor with drag, explicit mirror, undo and
  reset. Hands/face remain non-editable. Preserve fixed canvas dimensions across state changes.
- [ ] **B118-010** Save edited/extracted poses as new versioned presets with regenerated skeleton
  PNG/thumbnail and immutable keypoint provenance; do not mutate a library source in place.
- [ ] **B118-011 [P]** Test coordinate round-trip, mirror, undo/reset, save/reload and source-version
  immutability; run Razor diagnostics and desktop/mobile layout checks.

## D. Browse and Apply contract

- [ ] **B118-012** Add browse/search/category UI with skeleton previews and known-good badges.
- [ ] **B118-013** Define the B-117 request handoff as
  `PoseControlSource(PosePresetId) + ModelId + ControlStrength + ExplicitMirrorTransform`. Validate
  one pose, one model and one requested render; the contract contains no collection/sweep fields.
- [ ] **B118-014** Surface Apply through that contract. Before B-117 is present, show an explicit
  unavailable capability state; never substitute a text-only render. B-117 owns activation and live
  render acceptance.
- [ ] **B118-015 [P]** Test filtering, selection, request round-trip, unavailable-capability state,
  no fallback and absence of batch/sweep actions.

## E. Validation and evidence

- [ ] **B118-016** Run focused tests and the affected test project; record exact green counts.
- [ ] **B118-017** Live proof: import/search a library pose, extract one from an image, reject a
  missing-head extraction, edit/save/reload a preset, and produce a valid B-117 request. Record ids
  and screenshots; no direct DB writes or hand-run extraction script.
- [ ] **B118-018** Record source proofs for one pose store, configured extraction only, no render
  implementation in B-118, no fallback and no batch/sweep path.

## Definition of done

B-118 is complete when browse/extract/edit/save work in-app and the typed one-render handoff is
ready. Real Apply rendering is deliberately accepted in B-117 and is not a B-118 completion blocker.