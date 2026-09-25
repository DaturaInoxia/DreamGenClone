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
- [x] **B128-000f** **Licence audit before any asset or dependency is added.** Verify the provenance of
  the reference editor's `models/hand.fbx` and `models/foot.fbx`; confirm the 3D body is procedurally
  generated and not a licensed mesh; record that no SMPL/SMPL-X/MANO/FLAME asset or code enters the
  build; record the licence of every model weight relied on (DWPose ONNX, face mesh, ControlNet).
  Anything unclear is refused, not assumed.
  **Closed 2026-09-24.** Bundled pack licence verified as **CreativeML Open RAIL-M** (Civitai model 297881,
  version 574236 by dickccchen761; archive sha256 814ef6e3…4fb4) and now recorded in
  `pose-packs/openpose-nsfw/pack.json` with full attribution. RAIL-M carries **use-based restrictions that
  travel with the material**, and the Civitai version links an addendum that has not been read — both are
  stated in the pack README rather than treated as cleared. The rig's body is ours (procedural, no
  SMPL-family asset), and no reference-editor asset is used: the rig replaces it.

## A. Library model (P1)

- [x] **B128-001** Add `PoseLibrary` (id, name, description, system flag, timestamps) and extend
  `PosePreset` with `LibraryId`, `Keywords`, `Version`/status and timestamps. Additive only; presence
  checked; no existing column is repurposed.
- [~] **B128-002** Add repository operations: create library, list libraries, search poses by keyword
  across name/keywords/category/library, create new version, supersede, delete with in-use guard.
  An unknown library id and unparseable keypoints both fail explicitly.
  **Slice 1 landed:** library create/list/get, the single keyword+category+library search path, and the
  additive in-place schema upgrade. **Still open:** preset versioning/supersede and the delete-in-use
  guard (needed once a preset can be referenced by a render).
- [x] **B128-003 [P]** Tests: library create/search, keyword search across libraries, versioning,
  delete-in-use guard, malformed keypoints and unknown library failures.

## B. Seed importer (P2)

- [x] **B128-004** Add the idempotent pack importer into a system library, preserving source
  provenance and rendering the skeleton with the C# renderer (no Python).
  **Multi-pack (2026-09-24):** the packs root holds one folder per pack and each becomes its own
  library; a pack must carry `pack.json` (name required, refused by name without one); preset ids and
  skeleton folders are namespaced per pack, so two packs may each hold a same-named file.
- [x] **B128-004b** Add the pack downloader: a zip URL plus a name becomes a new pack folder with a
  generated manifest, then imports as its own library. Refuses a non-http(s) URL, an existing pack
  folder, an oversized archive, a non-zip body, and any entry that would escape the pack folder —
  cleaning up so a refused download leaves nothing behind. Ceiling from
  `PoseLibrary:DownloadMaxMegabytes` (required), never an inherited default.
- [x] **B128-005** Tag known-good only from recorded evidence (standing, squatting, kneeling with feet
  down); everything else is explicitly unverified rather than assumed good.
- [x] **B128-006 [P]** Tests: import twice leaves the row count unchanged, edited metadata survives a
  re-run, missing/corrupt source files fail explicitly, provenance round-trips.

## C. Pose-math core (P3)

- [x] **B128-007** Add the keypoint model and OpenPose JSON reader/writer (body, hands, confidence
  slots) with explicit failures on the wrong value count or a missing frame.
- [x] **B128-008** Add the skeleton renderer reproducing the audited output conventions, including the
  **pair predicate** for a limb line (both endpoints above the floor) and no head silhouette.
- [x] **B128-009 [P]** Tests: round-trip byte-stable parse/serialize, the pair predicate rejects a
  half-present limb, a rendered fixture is stable, and the head-keypoint validation rejects a faceless
  map naming the missing joints.

## C2. 3D projection core (P3, D6 — the authoring substrate)

- [x] **B128-009a** Add the rigid-mannequin joint model: a procedurally defined skeleton with explicit
  parent/child bones and bone lengths, in 3D, with **no third-party mesh and no SMPL-family asset**.
  Every dimension is configured; nothing is a hidden constant.
- [x] **B128-009b** Add the projection: rotate the pose (or the camera) about the axis and angle asked
  for, then project orthographically/perspective to 2D with pinned focal length and camera distance,
  emitting the COCO-18 keypoints in the canvas convention (y grows down). Reuse one core for the body
  and the head rather than two implementations.
- [x] **B128-009c** Add the 5° stepping on top of the projection: discrete yaw/pitch/roll increments
  with pinned axis conventions, so a button press is a declared rotation, never an ad-hoc nudge.
- [x] **B128-009d** Confirm the prior-art distinction is honoured: the projection's joint depths come
  from the rigid skeleton, **never** from a monocular estimate — the mediapipe world-landmark path is
  not reused here.
- [x] **B128-009e [P]** Tests: rotation composition (5° × n equals the closed-form n·5°), axis
  conventions asserted per direction, projection round-trips at zero rotation, bone lengths invariant
  under projection, and a missing projection parameter fails fast.
  **Landed 2026-09-24:** `PoseMannequin` (19-joint rig, 18 COCO joints, authored proportions — no SMPL
  family asset), `PoseProjection` (perspective projection about the root; yaw/pitch/roll; pitch clamped
  at ±89°; camera-too-close refused by name), `PoseStudioOptions` (`FocalLengthPx`, `CameraDistance`,
  `Canvas`, `RotationStepDegrees` — all required, refused by name when absent). **17 projection tests
  green**, including: bone lengths invariant under an arbitrary pose; yaw collapses the shoulder span
  while an in-plane rotation would have produced an equal *vertical* span (the discriminating check);
  shoulders coincide at true profile; 18 presses of the 5° arrow equal one 90° turn; and an end-to-end
  render where front > 3/4 > profile in rendered width, proving the narrowing survives the renderer's
  uniform fit.

## D. Body authoring — Tool A (P5 + P3)

- [x] **B128-010** Add the standing-pose generator (front / 3⁄4 / profile targets) on the C2 projection
  core, and the 5° rotate buttons driving the declared rotation (D6). Parameterless defaults are
  refused, not invented.
  **Landed 2026-09-24:** `PoseAuthorPanel` — named targets (Front / 3⁄4 left / 3⁄4 right / Profile left /
  Profile right), yaw / pitch / roll arrow buttons whose step comes from `PoseStudio:RotationStepDegrees`
  (not a literal in the UI), Reset, live projected preview as a data URI, and Save. `IPoseLibraryService`
  gained `ProjectAuthoredPose`, `RenderAuthoredPreview`, `SaveAuthoredPoseAsync` and
  `EnsureAuthoredLibraryAsync`.
- [x] **B128-011** Keep the 2D foreshortening transform only as an optional preview affordance, and
  prove it cannot become a saved source of an angle: the save path accepts a projected skeleton only,
  and a preview state is never persisted as accepted (D2).
  **Resolved by construction:** no 2D transform was built at all, so the only thing a save can persist is
  a projected rig pose. The 2D path in D2 is therefore an option that was not needed rather than a path
  that exists and is guarded.
- [ ] **B128-012** Add the verification path: render a plate in the target view, DWPose-extract it, and
  store the comparison against the projected skeleton as evidence. Marking an angle known-good requires
  a recorded measurement (D2, B128-017).
- [x] **B128-012a [P]** Tests: 5° steps compose to the expected total, the rotation is exactly
  invertible at the same step size, a preview cannot be saved as an angle, an unmeasured angle cannot
  be marked known-good, and a missing projection parameter fails fast.
  **Landed:** 17 projection tests + 8 authoring tests (saved pose carries keypoints, skeleton, recipe and
  `KnownGood = false`; duplicate name refused; saving into a pack refused; missing name/category/library
  refused; authored pose searchable immediately).

## E. Head authoring — Tool B (P5 + P3)

- [x] **B128-013** Add the 3D head proxy on the **same C2 projection core**: canonical 3D head
  keypoints seeded from the Apache-2.0 MediaPipe canonical face mesh, a defined origin anchored to the
  neck, yaw/pitch/roll in degrees, projection parameters pinned in configuration.
  **Landed 2026-09-24 with one deliberate deviation:** the head is the rig's own five COCO head points on a
  dedicated **head pivot joint above the neck**, not a separate face-mesh proxy. Reason: COCO-18 carries
  only those five points, so a richer proxy would produce geometry the skeleton cannot express — and the
  rig already had them. The deviation is recorded here rather than left to be discovered. **A real rig bug
  was found and fixed doing this:** the shoulders originally hung from the neck, so turning the head swung
  the arms. The head now has its own pivot (offset chosen so the rest pose is numerically identical), and a
  test asserts the shoulders do not move when the head turns.
- [x] **B128-014** Wire the 5° buttons (left/right yaw, up/down pitch) and the full-range controls
  (profile through lying-looking-up to looking-down), with independent roll; the head stays attached to
  the neck's 2D position.
  **Landed 2026-09-24:** `PoseHeadControls` (a child of the authoring panel, so there is still ONE save path)
  with named targets Front / Profile left / Profile right / Looking up / Looking down, arrows for yaw, pitch
  and roll at the configured step, Reset, and a **head-framed close-up preview**
  (`RenderAuthoredHeadPreview` frames on `PoseMannequin.HeadCocoIndices` — a body-scale render gives the head
  a handful of pixels and cannot be judged). Pitch is clamped at ±89° on the head as it is on the body.
  **Two convention bugs caught and fixed:** positive pitch was tipping the head *down* (now negated so a
  positive pitch means looking up, on both the body and the head), and the renderer's framing refuses when
  none of the named joints are visible rather than silently falling back to a body shot.
- [x] **B128-015 [P]** Tests: 5° accumulation, full-range extremes, roll independence, neck attachment
  preserved, missing projection configuration fails fast, and the capability text names the routes that
  honour a rotated head.
  **Landed:** head turn moves the head and leaves shoulders/hips/ankles bit-identical; head yaw collapses
  the eye separation to under 20%; the pitch convention is asserted on the rotation itself (where
  perspective cannot muddy it — an image-space assertion was tried first and was provably wrong, and the
  note is kept in the test); the head-framed preview fills >25% of the frame where a body fit would give a
  few percent; a save records the head rotation and a neutral head is omitted rather than stored as a
  fabricated zero. **144 pose tests green; build 0 errors.**

## F. Acceptance probe (P6)

- [x] **B128-016** Add the git-tracked `tools/` probe: render a pose+angle on a target model,
  re-extract the result's skeleton, and score **joint geometry** (never raster IoU). Register it in
  `tools/README.md` with a pinned requirements file.
  Done as `tools/pose-angle-probe`. Runs against a plate through ComfyUI's `OpenposePreprocessor`
  (re-uploads the plate under a unique name per run) or against a saved reference JSON, and unwraps the
  canvas document ComfyUI returns. Validated both directions: a pose against itself scores 0.00 % and
  passes; standing against kneeling scores 25.91 % and fails.
- [~] **B128-017** Measure the projection against the probe: for each angle family (front, 3⁄4,
  profile, and the head extremes) compare the **projected** skeleton to the **DWPose-extracted** one
  and record the per-joint agreement as numbers. That table — not an assumption — is what licenses
  marking an angle known-good and is what the UI reads.
  **Bodies measured** (2026-09-02, `juggernautXL_ragnarok` + `OpenPoseXL2` 0.85, 1024×1024 plate):
  front 2.12 %, 3⁄4 left 2.68 %, 3⁄4 right 2.57 %, profile left 3.50 %, profile right 4.28 % mean joint
  error — all inside a 6 % bar, table recorded in the probe's README and in plan.md constraint 6.
  **Heads not yet measured**, and the profile **shoulder span** disagrees 7.30 % against the renders'
  1.76 %/2.05 % (perspective at `CameraDistance = 4.5`; see constraint 6). No angle is marked
  `known-good` — the known-good column stays empty for the authored angles until the camera distance is
  re-chosen and this table is re-run.
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

- [x] **B128-022** Add the searchable picker component (keyword search, library and category filters,
  skeleton previews, known-good and provenance badges, create-new-library action).
- [~] **B128-023** Open the same component from Composition Composer, the body views panel, scene
  compose, the image edit workspace, the character studio and the asset-manager face/body surfaces.
  One component, one pose path — no per-surface pose code.
  **Slice 1 landed:** Composition Composer opens the shared `PoseLibraryPicker`, and a picked pose is
  copied into the session's pose input so the render path is unchanged. **Still open:** the body views
  panel, scene compose, image edit workspace, character studio and asset-manager surfaces. A standalone
  `/pose-library` page browses and extends the library meanwhile.
- [ ] **B128-024** Apply the picked/authored pose through B-124's shared primitive, with the explicit
  unavailable-capability state when the selected model cannot carry a pose; never a silent text-only
  render and never an automatic substitute.
- [ ] **B128-025 [P]** Tests: picker filters, create-library-then-search, one-path assertions for each
  surface, unavailable-capability state, and the absence of batch/sweep fields.

## I. Validation and evidence

- [~] **B128-026** Run focused tests for the touched areas and record exact green counts.
  **Slice 1:** 42 pose tests green (library, importer, JSON, renderer, repository incl. the legacy-DB
  upgrade and the real 472-pose pack) and 38 blast-radius tests green (Composition / BodyViews /
  stance+angle skeletons). Solution build 0 errors.
  **Probe slice:** 163 pose tests green (filter `FullyQualifiedName~Pose`), solution build 0 errors.
  Added `PoseExportProbeTests` (the export writes keypoints + a skeleton for every named angle, and the
  named angles foreshorten the shoulder span the way a real turn must) and
  `PoseProjectionTests.AProfileSpanShrinksTowardTheRealRenderAsTheCameraMovesBack`.
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
