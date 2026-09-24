# B-128 — DWPose Studio (pose authoring, extraction, library and shared picker)

**State:** `planned`. **Priority:** high. **Scope:** large.
**Supersedes and absorbs B-118** (`specs/Planning/B-118-pose-studio/`) — B-118's scope is a subset of
this item; nothing was built from the B-118 row and it must not be built from in parallel.
**Consumes:** B-124's shared create/edit primitive (pose as a first-class parameter) and the
`PosePreset` store it landed; B-117's `PoseControlSource` render route; the existing
`IStancePoseSkeletonProvider` / `BodyAngleSkeletonProvider` reads.
**Does not own:** ControlNet render wiring (B-117), derived-asset cataloguing (B-120), multi-subject
composition (B-126), LoRA cells (B-123).

## Purpose

One DWPose component, shared by every surface that needs a pose: search the library, extract a pose
from an image, author a new pose (body angle, head angle, or joint-by-joint), save it into a library,
and hand it to a render. Today the app has a `PosePreset` table and seven static skeleton PNGs
produced by hand-run Python; there is no in-app authoring, no extraction, no library grouping, no
search, and no shared picker.

## Operator decisions (locked 2026-09-23)

| # | Decision |
|---|---|
| D1 | Tracked as a **new item B-128** that supersedes and absorbs B-118. |
| D2 | **REVISED 2026-09-23** (was: "2D preview + DWPose-annotated plate"). Body 5° rotation is produced by **posing a rigid 3D mannequin and projecting it to 2D** — deterministic, exact, instant and free. The 2D foreshortening transform is demoted to an optional preview and the DWPose plate-extract is demoted to the **verification probe** that proves the projection, not the only source of truth. |
| D3 | Head rotation uses a **3D head proxy model projected to 2D** (real yaw/pitch/roll math), not 2D nudging of five points. Confirmed — and it shares the **same projection core** as D2, so body and head are one mechanism. |
| D4 | The drag editor edits **COCO-18 body joints plus contact points** only. Hands and face are not editable there. |
| D5 | First slice = library + search + seed import + shared picker opened from the Composition Composer. |
| D6 | **The 3D mannequin projection is the authoring substrate** for every angle-producing tool (body yaw, head pose, joint drag). 2D keypoint manipulation is never the source of an angle. |
| D7 | **Tool C is build-vs-reuse.** Build our own shell (library, search, apply, provenance) and adopt the 3D authoring core from the MIT reference editor — either embedded or reimplemented. Which of the two is settled by an evidence spike (B128-000e), with the recommendation below. |

## Measured constraints this design must respect (do not re-litigate)

1. **Naive rotation of 2D keypoints is a dead end for body yaw.** Recorded in `BodyAngleSkeletons.cs`
   and `specs/image-generator-tests/qwen-21-native-reference/RUNBOOK.md`: in-plane rotation only
   *tilts* a figure; world-3D rotation shears it (~27 cm of horizontal shear across a 1.30 m body,
   from a 0.49 m ear-to-ankle monocular depth gradient); flat-cutout rotation only *narrows* it. The
   shipped `angle-34-*` and `angle-profile-*` skeletons exist because a plate was rendered **in** that
   view and DWPose extracted it.
   **D6 does not contradict this measurement — it is a different mechanism, and the difference must
   be stated precisely:** the rejected result came from **MediaPipe monocular world landmarks**, i.e. an
   *estimated per-joint depth* that is not mutually consistent (hence the shear). A 3D mannequin's
   joint depths are **synthetic, rigid and mutually consistent**, and the angle comes from a real
   camera projection of one coherent skeleton. The prior rejection therefore does not transfer, but it
   also does not license assuming the projection is correct: B128-017 measures the projection against a
   DWPose-extracted plate and records the agreement as numbers before any angle is marked known-good.
2. **The skeleton is an angle input, not a proportion source.** The accepted body reference supplies
   build; the skeleton supplies limb layout.
3. **A limb line needs BOTH endpoints above the confidence floor.** A lenient "at least one keypoint"
   audit produced a false 100% pass rate — never use it.
4. **OpenPoseXL2 does not reliably honour hands or face.** A rotated head therefore only pays off on
   the Qwen-2.1 skeleton-as-reference route (proven 2026-09-23) or for face-angle plates. This must be
   stated in the UI, not discovered in a render.
5. **Raster IoU is invalid as a pose-adherence score** (established in B-123's scoping). Adherence is
   measured with joint geometry.

## Prior art and reuse (researched 2026-09-23)

This capability has been built before, and one project is most of Tools A and C. Licence status was
checked because this repo has blocked work on licensing before (B-114).

| Project | Licence | What it establishes |
|---|---|---|
| `ZhUyU1997/open-pose-editor` (959★) | **MIT** | A 3D mannequin editor in Three.js: joint rotation by mouse, hand **and** foot editing, height/weight/limb-length parameters, undo via a command pattern, save/load scene, and **depth/normal/canny export**. Its body is built **procedurally in code** (`BodyControlor.CreateBones` / `CreateLink2`), so the body carries no third-party mesh licence. Embeddable as an iframe driven by `postMessage {cmd:'openpose-3d', method, type:'call'|'return', payload}` with a real API: `SetPose([18×xyz])`, `SetBlazePose`, `MakeImages` → `{pose, depth, normal, canny}`, `OnlyHand`, `OutputWidth`/`OutputHeight`. **Its pose library is random-pick only** (`GetRandomPose`, no search) — see the gap below. **Verify the provenance of `models/hand.fbx` and `models/foot.fbx` before reusing either file.** |
| `huchenlei/sd-webui-openpose-editor` (795★) | **MIT** | A 2D keypoint drag editor: joint drag, add/remove person, add a default hand, visibility toggle that removes a limb segment, and group select → rotate/scale/skew. It states explicitly that **face editing is not supported because 70 keypoints is too many to adjust by hand** — independent confirmation of D4. |
| `Fannovel16/comfyui_controlnet_aux` + `IDEA-Research/DWPose` | Apache-2.0 | The extraction route this repo already uses, and **ONNX weights exist** (`yolox_l.onnx`, `dw-ll_ucoco_384.onnx`) so extraction can run in **C# through ONNX Runtime with no Python** — letterbox preprocessing and SimCC decoding must be written in C#. |
| MediaPipe `canonical_face_model.obj` / `face_model_with_iris.obj` | Apache-2.0 | A licence-clean **canonical 3D face mesh** — the substrate for the D3 head proxy. |
| `cleardusk/3DDFA_V2` | MIT (code) | Fits a real 3D face and reports yaw/pitch/roll. Trained on 300W-LP and shipping a 3DMM basis, so the **weights need a licence review** before they travel with the app. |

**Licence blockers.** SMPL, SMPL-X, SMPL+H, MANO and FLAME are **non-commercial research licences only** (commercial use requires a licence from MPI). `open-mmlab/mmhuman3d` is Apache-2.0 code, but every body model it supports is SMPL-family — so the 3D body route is **not** built on SMPL. `huchenlei/ComfyUI-openpose-editor` is **GPL-3.0**: learn from it, never copy from it.

**What nobody has done, and we therefore build:** keyword search over a pose library, user-created
libraries, and the app integration. The one shipped library is random-pick. That is the easy part —
SQLite plus the 472-pose pack already in this repo.

**Recommendation adopted (D7):** build our own shell (library, search, apply, provenance) on the 3D
mannequin projection core, reusing the MIT editor either embedded or as a documented reference. The
spike decides which, on evidence, not preference.

**Bonus synergy.** The same 3D scene emits **depth, normal and canny** maps. Those are exactly what
B-119 route C1 and B-120 need for multi-character layout — one authoring tool, three consumers.

## Parts

| Part | What it is |
|---|---|
| **P1 — Library model** | `PoseLibrary` (id, name, description, system/seeded flag, timestamps) + `PosePreset.LibraryId`, `Keywords`, `Provenance`, `KnownGood`. Category stays (pose class: standing / kneeling / …) and is a different axis from Library (collection). |
| **P2 — Seed importer** | Idempotent import of the git-tracked 472-pose OpenPose pack into a system library, with provenance preserved, known-good tagging for the verified stances, and skeletons rendered by the C# renderer. Re-running never duplicates rows and never overwrites user-edited metadata. |
| **P3 — Pose-math core (C#)** | The C# equivalent of the proof scripts: OpenPose JSON parse/serialize (y grows down, confidence in slot 3), skeleton PNG rendering, the **3D projection core** (rigid mannequin → 2D with a real camera projection), the 3D head proxy on the same core, and the 2D IK solver. No Python at runtime. |
| **P4 — DWPose extract client** | `LoadImage → DWPreprocessor → SaveImage` against the configured local ComfyUI; returns keypoints + skeleton + provenance (source hash, provider/profile, node/workflow version, parameters). Head-keypoint rule enforced. |
| **P5 — Shared UI** | One embeddable component family: search/browse/preview/pick, plus the three authoring tools (body rotate, head rotate, joint drag). Opened from Composition Composer, body views, scene compose, image edit workspace, character studio and the asset-manager face/body surfaces. |
| **P6 — Acceptance probe** | A git-tracked tool under `tools/` that renders a pose+angle on a target model, re-extracts the result's skeleton and compares **joint geometry**. `KnownGood` may only be set from a measured pass. |

## Tool A — body pose + rotation

Generate a standing pose at front / 3⁄4 / profile; `←` and `→` rotate in 5° steps; save and use.

- **Accepted route (D6):** pose the **rigid 3D mannequin** and project it. Rotating the mannequin (or
  its camera) about the body's vertical axis by yaw $\theta$ and projecting each joint is exact,
  instant and costs no GPU:

  $$\begin{bmatrix}x'\\z'\end{bmatrix}=\begin{bmatrix}\cos\theta&-\sin\theta\\ \sin\theta&\cos\theta\end{bmatrix}\begin{bmatrix}x\\z\end{bmatrix},\qquad u=f\frac{x'}{d-z'},\quad v=f\frac{y}{d-z'}$$

  The perspective term is what produces honest foreshortening and narrowing, and joint depth stays
  mutually consistent because it comes from one rigid skeleton.
- **Optional preview:** the 2D foreshortening transform, for a keystroke cheap enough to feel instant.
  It is never the source of a saved angle and is never silently promoted (D2).
- **Verification (probe):** render a plate in the target view and DWPose-extract it, then compare the
  result's joint geometry against the projected skeleton (B128-017). Costs one GPU render per angle and
  runs **once per angle family**, not per pose. Its job is to prove the projection, and it is what
  licenses marking an angle known-good.
- **Gate:** no angle is marked known-good until the probe has recorded the agreement as numbers.

## Tool B — head / face pose

Start from a full front face or any saved head pose; `left` / `right` / `up` / `down` move it 5° per
press; full-range rotation covers head-on profile → lying-down looking up, and the inverse (looking
straight down), with left/right as well.

- The geometry is a **3D head proxy** on the **same projection core as Tool A** (D3): a canonical 3D
  head keypoint set — seeded from the Apache-2.0 MediaPipe canonical face mesh — with a defined origin
  anchored to the neck, rotated by yaw/pitch/roll and projected to 2D. The head stays attached to the
  neck's 2D position, and the projection parameters are pinned in configuration and fail fast when
  missing.
- Roll is a first-class control, not a side effect of yaw.
- Honest limits, stated in the UI rather than discovered in a render: COCO-18 carries only five head
  points, and OpenPoseXL2 does not honour the face, so a rotated head only lands on the Qwen-2.1
  skeleton-as-reference route or on a face plate.

## Tool C — full-body IK editor

Drag arms, legs and contact points; standing hands-at-sides → handshake must be reachable by dragging
a hand.

- 2D IK over the COCO-18 chains (shoulder→elbow→wrist, hip→knee→ankle) with **bone lengths measured
  from the source pose** and joint-angle limits from configuration. Bone lengths never stretch.
- The root (pelvis) is fixed unless explicitly dragged; a dragged wrist solves elbow then shoulder.
- Contact points: wrists, ankles and a ground plane may be pinned so a contact pose keeps contact.
- Undo, reset and explicit mirror. Hands and face are not editable here (D4).
- **Precedent to follow:** the MIT reference editor does exactly this — joints are posed by rotation,
  and wrists/ankles have explicit **target points** (`left_wrist_target`, `right_ankle_target` …) that
  solve the limb, with a move/free mode and a command-pattern undo stack. Study its behaviour; write
  our own code (never copy from the GPL-3.0 port).

## Seams

- **Feeds the render:** a pose is applied through B-124's shared primitive as one pose + one model +
  one strength — there is no second pose path and no batch/sweep action.
- **Feeds B-117/B-126/B-123:** those consume the library; they do not own it.
- **B-120 boundary:** B-120 owns the catalogued, versioned derived-asset record; B-128 owns the
  authoring store. A pose used in a render may also be catalogued by B-120 — the stores do not merge.
- **Feeds B-119 / B-120 with structure maps (new).** The 3D authoring scene can emit **depth, normal
  and canny** alongside the skeleton. That is the route-C1 / derived-asset input, so the depth and
  canny requirements are satisfied from the authoring tool rather than by extracting them from a photo.

## Non-goals

- No ControlNet render wiring (B-117) and no multi-subject composition (B-126).
- No 3D rig / Three.js editor.
- No batch or sweep action — one pose, one model, one render.
- No second pose store, no Python at runtime, and no silent substitution of an angle or a pose.

## Acceptance (the wireframe test)

1. Searching the library by keyword returns poses from more than one library, with skeleton previews,
   and a new library created in the UI is immediately searchable and usable.
2. Importing the seeded pack twice leaves the row count unchanged and does not overwrite edited metadata.
3. Extracting a pose from an image stores keypoints, skeleton and provenance; an image with no head
   keypoints is rejected naming the missing joints.
4. Tool A: rotating a body pose 5° at a time rotates the projected 3D mannequin and shows the result
   live; a saved pose is only marked known-good in the angle families the probe has measured.
5. Tool B: a head pose can be driven from front to full profile to looking-up and to looking-down, with
   the head still attached to the neck, roll applied independently, and both body and head produced by
   the one projection core.
6. Tool C: dragging a wrist from hands-at-sides produces a handshake with bone lengths preserved and
   the contact point held.
7. Composition Composer (and each other surface) opens the same component, and the picked or authored
   pose reaches the render through the shared primitive.

## Risks

- **The 3D projection's agreement with a real render is measured, not assumed.** B128-017 compares the
  projected skeleton against a DWPose-extracted plate and records the numbers; until then no angle is
  known-good. If the projection drifts at extreme angles, the measured table says so instead of the
  operator discovering it in an image.
- **SMPL-family licences are non-commercial.** The 3D route must be built on a procedurally generated
  or Apache/MIT-clean body, never on SMPL/SMPL-X/MANO/FLAME. Any third-party mesh added later —
  including the reference editor's `hand.fbx` / `foot.fbx` — needs its provenance verified first.
- **Head detail at full-body scale is small.** The head tool's real payoff is close-up and portrait
  renders; a full-body skeleton gives the head a handful of pixels.
- **Reuse carries a copy-left trap.** The MIT editors may be learned from and reimplemented with
  attribution; the GPL-3.0 ComfyUI port may not be copied.
- **Two stores could drift** (authoring vs derived asset). The boundary above is the guard.
- **Build-vs-reuse is a real fork in the road.** Embedding is faster but imports a second front-end
  stack and its own asset/licence questions; reimplementing costs more but keeps one stack. The spike
  settles it on evidence (B128-000e) rather than on preference.

## Files

- This plan: `specs/Planning/B-128-dwpose-studio/plan.md`.
- Dispatch: `specs/Planning/B-128-dwpose-studio/tasks.md`.
- Superseded item: `specs/Planning/B-118-pose-studio/`.
- Pose pack: `helpers/runpod/openposeNSWFPosePackage_final/` (git-tracked).
- Prior-art research record: `/memories/repo/external-pose-editor-research.md`
  (licence status, the embeddable MIT editor's `postMessage` API, and the no-Python ONNX extraction path).
