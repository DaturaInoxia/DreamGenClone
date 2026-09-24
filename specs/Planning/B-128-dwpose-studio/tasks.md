# B-128 Tasks — DWPose Studio

**Execution rule:** complete in order unless marked `[P]`; every task ends with focused tests green.
Fix forward only. Read `plan.md`, `docs/local-comfyui-model-manager-setup.md` and the Razor
instructions before touching their matching files.

**Boundary:** B-128 owns pose authoring, extraction, the library and the shared picker. It defines the
one-pose render request through B-124's shared primitive; B-117 owns the ControlNet render itself. No
batch/sweep action, no Python at runtime, no fallback path. **Authoring substrate (D6):** every
angle-producing tool projects a rigid 3D mannequin; 2D keypoint manipulation is never the source of an
angle. **Reuse posture (D7):** learn from the MIT editors, write our own code, never copy from the
GPL-3.0 port.

## 0. Verify before writing code

- [ ] **B128-000a** Confirm the exact OpenPose JSON shape in the git-tracked pack (body 54 values,
  hands 63 each, y grows down, confidence in slot 3), the category/resolution folder variance and the
  count of poses, then record a stable known-good fixture set of three poses.
- [ ] **B128-000b** Confirm the local ComfyUI `DWPreprocessor` node contract (input names, ckpts,
  output shape) and the exact configured provider/profile rows the app already uses. Missing
  configuration must fail fast; no endpoint or node name may be guessed.
- [ ] **B128-000c** Confirm the existing `PosePreset` schema (columns, ordinals) and B-124's shared
  create/edit primitive contract, so the library extension is additive and there is exactly one pose
  path to a render. Record one owner for skeleton bytes.
- [ ] **B128-000d** Confirm the render routes that honour a pose (SDXL/FLUX ControlNet vs Qwen-2.1
  skeleton-as-reference) and record which ones honour head and hand detail, for the UI's capability text.
- [ ] **B128-000e** **Spike — build vs reuse (D7).** Stand up the MIT reference editor
  (`ZhUyU1997/open-pose-editor`) locally and drive it through its `postMessage` API: `SetPose([18×xyz])`
  → `MakeImages` → confirm the returned skeleton actually changes with a commanded yaw. Record what
  embedding costs (second front-end stack, asset licence questions) against reimplementing on the same
  projection core, and settle D7 on that evidence. Record the decision and its numbers in the plan.
- [ ] **B128-000f** **Licence audit before any asset or dependency is added.** Verify the provenance of
  the reference editor's `models/hand.fbx` and `models/foot.fbx`; confirm the 3D body is procedurally
  generated and not a licensed mesh; record that no SMPL/SMPL-X/MANO/FLAME asset or code enters the
  build; record the licence of every model weight relied on (DWPose ONNX, face mesh, ControlNet).
  Anything unclear is refused, not assumed.

## A. Library model (P1)

- [ ] **B128-001** Add `PoseLibrary` (id, name, description, system flag, timestamps) and extend
  `PosePreset` with `LibraryId`, `Keywords`, `Version`/status and timestamps. Additive only; presence
  checked; no existing column is repurposed.
- [ ] **B128-002** Add repository operations: create library, list libraries, search poses by keyword
  across name/keywords/category/library, create new version, supersede, delete with in-use guard.
  An unknown library id and unparseable keypoints both fail explicitly.
- [ ] **B128-003 [P]** Tests: library create/search, keyword search across libraries, versioning,
  delete-in-use guard, malformed keypoints and unknown library failures.

## B. Seed importer (P2)

- [ ] **B128-004** Add the idempotent pack importer into a system library, preserving source
  provenance and rendering the skeleton with the C# renderer (no Python).
- [ ] **B128-005** Tag known-good only from recorded evidence (standing, squatting, kneeling with feet
  down); everything else is explicitly unverified rather than assumed good.
- [ ] **B128-006 [P]** Tests: import twice leaves the row count unchanged, edited metadata survives a
  re-run, missing/corrupt source files fail explicitly, provenance round-trips.

## C. Pose-math core (P3)

- [ ] **B128-007** Add the keypoint model and OpenPose JSON reader/writer (body, hands, confidence
  slots) with explicit failures on the wrong value count or a missing frame.
- [ ] **B128-008** Add the skeleton renderer reproducing the audited output conventions, including the
  **pair predicate** for a limb line (both endpoints above the floor) and no head silhouette.
- [ ] **B128-009 [P]** Tests: round-trip byte-stable parse/serialize, the pair predicate rejects a
  half-present limb, a rendered fixture is stable, and the head-keypoint validation rejects a faceless
  map naming the missing joints.

## C2. 3D projection core (P3, D6 — the authoring substrate)

- [ ] **B128-009a** Add the rigid-mannequin joint model: a procedurally defined skeleton with explicit
  parent/child bones and bone lengths, in 3D, with **no third-party mesh and no SMPL-family asset**.
  Every dimension is configured; nothing is a hidden constant.
- [ ] **B128-009b** Add the projection: rotate the pose (or the camera) about the axis and angle asked
  for, then project orthographically/perspective to 2D with pinned focal length and camera distance,
  emitting the COCO-18 keypoints in the canvas convention (y grows down). Reuse one core for the body
  and the head rather than two implementations.
- [ ] **B128-009c** Add the 5° stepping on top of the projection: discrete yaw/pitch/roll increments
  with pinned axis conventions, so a button press is a declared rotation, never an ad-hoc nudge.
- [ ] **B128-009d** Confirm the prior-art distinction is honoured: the projection's joint depths come
  from the rigid skeleton, **never** from a monocular estimate — the mediapipe world-landmark path is
  not reused here.
- [ ] **B128-009e [P]** Tests: rotation composition (5° × n equals the closed-form n·5°), axis
  conventions asserted per direction, projection round-trips at zero rotation, bone lengths invariant
  under projection, and a missing projection parameter fails fast.

## D. Body authoring — Tool A (P5 + P3)

- [ ] **B128-010** Add the standing-pose generator (front / 3⁄4 / profile targets) on the C2 projection
  core, and the 5° rotate buttons driving the declared rotation (D6). Parameterless defaults are
  refused, not invented.
- [ ] **B128-011** Keep the 2D foreshortening transform only as an optional preview affordance, and
  prove it cannot become a saved source of an angle: the save path accepts a projected skeleton only,
  and a preview state is never persisted as accepted (D2).
- [ ] **B128-012** Add the verification path: render a plate in the target view, DWPose-extract it, and
  store the comparison against the projected skeleton as evidence. Marking an angle known-good requires
  a recorded measurement (D2, B128-017).
- [ ] **B128-012a [P]** Tests: 5° steps compose to the expected total, the rotation is exactly
  invertible at the same step size, a preview cannot be saved as an angle, an unmeasured angle cannot
  be marked known-good, and a missing projection parameter fails fast.

## E. Head authoring — Tool B (P5 + P3)

- [ ] **B128-013** Add the 3D head proxy on the **same C2 projection core**: canonical 3D head
  keypoints seeded from the Apache-2.0 MediaPipe canonical face mesh, a defined origin anchored to the
  neck, yaw/pitch/roll in degrees, projection parameters pinned in configuration.
- [ ] **B128-014** Wire the 5° buttons (left/right yaw, up/down pitch) and the full-range controls
  (profile through lying-looking-up to looking-down), with independent roll; the head stays attached to
  the neck's 2D position.
- [ ] **B128-015 [P]** Tests: 5° accumulation, full-range extremes, roll independence, neck attachment
  preserved, missing projection configuration fails fast, and the capability text names the routes that
  honour a rotated head.

## F. Acceptance probe (P6)

- [ ] **B128-016** Add the git-tracked `tools/` probe: render a pose+angle on a target model,
  re-extract the result's skeleton, and score **joint geometry** (never raster IoU). Register it in
  `tools/README.md` with a pinned requirements file.
- [ ] **B128-017** Measure the projection against the probe: for each angle family (front, 3⁄4,
  profile, and the head extremes) compare the **projected** skeleton to the **DWPose-extracted** one
  and record the per-joint agreement as numbers. That table — not an assumption — is what licenses
  marking an angle known-good and is what the UI reads.
- [ ] **B128-018 [P]** Tests: the probe refuses to mark known-good without a measurement, and the
  recorded agreement table is read by the UI rather than hardcoded twice.

## G. Full-body IK editor — Tool C (P5 + P3)

- [ ] **B128-019** Add the 2D IK solver over the COCO-18 chains with bone lengths measured from the
  source pose and joint limits from configuration; bone lengths are invariant. Follow the reference
  editor's behaviour (joint rotation plus explicit wrist/ankle **target points** that solve the limb);
  write our own implementation and never copy from the GPL-3.0 port.
- [ ] **B128-020** Add the canvas editor with drag, root handling, contact pinning, undo, reset and
  mirror. Hands and face are not editable.
- [ ] **B128-020a** Confirm the authored pose leaves the editor as a **projected** skeleton through the
  same C2 core used by Tools A and B — one geometry path, no editor-only representation.
- [ ] **B128-021 [P]** Tests: bone lengths preserved under drag, IK reaches the hands-at-sides →
  handshake case, contact pinning holds, undo/reset return to the exact source keypoints, and joint
  limits are enforced.

## H. Shared UI and integration (P5)

- [ ] **B128-022** Add the searchable picker component (keyword search, library and category filters,
  skeleton previews, known-good and provenance badges, create-new-library action).
- [ ] **B128-023** Open the same component from Composition Composer, the body views panel, scene
  compose, the image edit workspace, the character studio and the asset-manager face/body surfaces.
  One component, one pose path — no per-surface pose code.
- [ ] **B128-024** Apply the picked/authored pose through B-124's shared primitive, with the explicit
  unavailable-capability state when the selected model cannot carry a pose; never a silent text-only
  render and never an automatic substitute.
- [ ] **B128-025 [P]** Tests: picker filters, create-library-then-search, one-path assertions for each
  surface, unavailable-capability state, and the absence of batch/sweep fields.

## I. Validation and evidence

- [ ] **B128-026** Run focused tests for the touched areas and record exact green counts.
- [ ] **B128-027** Live proof in-app: search a library pose, create a library, extract a pose from an
  image, reject a faceless one, rotate a body pose, drive a head pose, drag to a handshake, and produce
  a render that uses the pose. Record ids and screenshots; no direct DB writes.
- [ ] **B128-028** Record source proofs for one pose store, one render path, one geometry path (the C2
  projection core serves Tools A, B and C), configured parameters only, no fallback, no Python at
  runtime and no batch/sweep action.
- [ ] **B128-029** Close the two gating records: the licence audit (B128-000f) with every weight and
  asset named and cleared, and the build-vs-reuse spike (B128-000e) with its measured evidence and the
  resulting decision written into the plan.

## Definition of done

B-128 is complete when the library is searchable and extensible, extraction and all three authoring
tools work in-app on the one 3D projection core, every angle marked known-good carries a recorded
measurement, and every listed surface picks or authors a pose through the single shared path into a
render — with the licence audit (B128-000f) and the build-vs-reuse decision (B128-000e) both closed.
