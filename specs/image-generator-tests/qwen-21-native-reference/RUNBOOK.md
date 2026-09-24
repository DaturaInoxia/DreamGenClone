# Qwen-Image-2.1 native references — pose + identity + location proof (2026-09-23)

## Purpose

This directory is the source-controlled evidence package for how **Qwen-Image-2.1** takes extra images in
its own graph: the autogrow **reference slots** (`images.image_1..image_N` on `TextEncodeQwenImage21`).
It answers three questions the app's composition design depends on, and it does so **without any
ControlNet** — 2.1 has no ControlNet weights published (HF search `Qwen-Image-2.1-ControlNet` returns
nothing; `Comfy-Org/Qwen-Image-2.1` ships only `diffusion_models`/`text_encoders`/`vae`).

1. Does a **pose** survive as a reference image?
2. Does **identity** survive alongside it?
3. Do **location + pose + identity** land together in ONE call — the shape the app sends?

- `images/` — the ten proof images, in argument order (`01`…`10`).
- `prompts/` — the exact ComfyUI request graph submitted for each case (flat dotted reference ids).
- `manifest.json` — prompt/seed/reference-hash/checksum metadata and a per-case verdict.

## Verdicts

| # | Case | Inputs (slot order) | Result |
|---|---|---|---|
| 01 | `pose-plate` | *(none)* | plate produced (used only as the photoreal input for 03) |
| 02 | `pose-text-baseline` | face (profile) | pose **pass** — stance described in words |
| 03 | `pose-photoreal-reference` | **photograph** + face | **FAIL** — reproduced the plate's man wholesale; the face reference did nothing |
| 04 | `pose-standing-skeleton` | **standing skeleton** + face | **pass** — followed the skeleton's splayed arms / wide stance |
| 05 | `pose-kneeling-skeleton` | **kneeling skeleton** + face | **pass (decisive)** — kneeling, arms overhead, while the prompt still said "standing" |
| 06 | `app-pose-identity-front` | standing skeleton + **frontal** face | pose **pass**, identity **pass** |
| 07 | `app-pose-identity-kneel` | kneeling skeleton + frontal face | pose **pass**, identity **pass** |
| 08 | `app-location-pose-identity-tall` | location + skeleton + face, 832×1216 | **location + pose + identity all pass** |
| 09 | `app-location-pose-identity-wide` | location + skeleton + face, 1216×832 | **all three pass in the wide frame** |
| 10 | `app-slot-order-face-first` | face + skeleton + location | all three pass — order was not a gate |

**The decisive case is 05.** It has the same seed and identical wording as 04, and the prompt still says
"standing", yet the output follows the *kneeling* skeleton (kneeling, both arms raised overhead, knees
apart). The pose came from the skeleton image, overriding both the seed and the explicit word.

**Two rules follow:**

1. **A skeleton in a reference slot is pose guidance** on 2.1, so the existing
   `DreamGenClone.Web/wwwroot/pose-library/{standing,squatting,kneeling}.png` stances (and the wider
   OpenPose pack) are directly usable — **no 2.1 ControlNet and no photoreal pose plates are needed.**
2. **A photoreal full-body person in slot 1 is the base image to reproduce, not a pose donor** (case 03).
   A *face crop* in slot 1 does not have that behaviour (case 10), so the rule is about what the image
   depicts, not about the slot number alone.

Also established: an **angle-matched (frontal) face reference transfers identity well** (06/07/08/09) while
the profile reference used in 02–05 did not (only its hair carried) — so reference *view* selection is an
input the caller must make, matching the earlier head-angle finding.

## Known limitations (do not over-read this package)

- Adherence is approximate: the skeleton drives the stance, but limb angles are not reproduced exactly.
- Ambiguous stances (lying, all-fours) are untested here — ControlNet itself fails those on SDXL.
- Only one posed person per call; multi-skeleton composition is untested.
- Content is non-explicit; adult-content behaviour of this composition is untested.
- The location is reproduced **semantically**, not pixel-faithfully (measured separately: mean|diff| ≈ 7.7
  vs ≈ 2.4 for the composite-from-location-base path).

## Replay

Every reference input is SHA-256 verified by the runner before upload; the plate in run A must exist
before run B consumes it (its checksum is pinned in `$references`).

```powershell
# A: the photoreal plate + the words-only baseline
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells posePlate,poseTextRef
# B: the decisive pose pair (identical wording; only <image1> differs)
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells poseRef,poseSkel,poseSkelKneel
# C: the ten-case app shape (location + pose + identity)
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells appPoseIdFront,appPoseIdKneel,appComposeWide,appComposeTall,appComposeOrder
```

Case 11 (`images/11-app-builder-face-and-skeleton.png`) is not produced by that runner: it is the
**application's own graph**. Emit it from the app's production builder, then submit it unchanged with the
two references uploaded under their graph names:

```powershell
$env:QWEN21_EMIT_GRAPH  = "artifacts\tmp\qwen-2-1\app-pose-graph.json"
$env:QWEN21_EMIT_REFS   = "proof-face.png,proof-skeleton.png"
$env:QWEN21_EMIT_PROMPT = "<the prompt recorded in the manifest>"
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~BuildWorkflow_EmitGraphForHostProof"

& helpers/local-comfyui-host/run-local-proof.ps1 `
    -WorkflowPath artifacts/tmp/qwen-2-1/app-pose-graph.json `
    -ImagesJsonPath artifacts/tmp/qwen-2-1/app-pose-refs.json `
    -ComfyUiUrl http://192.168.0.11:8188 -OutDir artifacts/tmp/qwen-2-1/app-pose-proof -Prefix app-pose
```

`prompts/app-posed-graph-emitted.json` is that emitted graph (flat dotted `images.image_1/2`, `LoadImage`
nodes 20/21) — proof the references were really wired rather than silently dropped.

Outputs land in git-ignored `artifacts/tmp/qwen-2-1/<cell>/` (`result_0.png` + `request.json`), which is
what this package was copied from. The runner also prints every prompt verbatim and refuses to submit a
reference whose checksum does not match.

## Runtime

Local ComfyUI 0.37.1 on WOOD-GAME-MAIN (RTX 5080 16 GB), checkpoint set
`qwen_image_2.1_int8_convrot.safetensors` + `qwen3vl_8b_int8_convrot.safetensors` +
`qwen_image_2.1_vae_bf16.safetensors`; 25 steps, cfg 1, euler/simple, resolution budget 1024, seed 20260922.
Wall clock per case: 15–30 s.

---

## Body-angle proof (cases 12–18, 2026-09-23)

The body pipeline's problem case: it holds an **accepted frontal body** and needs the other canonical
views (3/4 left/right, profile left/right). Today those are produced as edits/rotations of the accepted
source. These cases ask whether 2.1 can **generate** them instead, from references.

### The angle skeleton is ANNOTATED, never rotated

Three rotation approaches were built and measured, and **all three were rejected**:

| Approach | Outcome |
|---|---|
| Rotate the skeleton **drawing** in-plane by 45° | a **tilted** figure (head top-left, feet bottom-right), partly clipped — it says nothing about a body turned about its vertical axis |
| **World-3D yaw** using MediaPipe world landmarks | a **sheared** figure. Measured: mean z runs **−0.286 m at the ears → +0.202 m at the ankles**, i.e. 0.49 m of depth along a 1.30 m body, so a 45° yaw injects **≈27 cm of horizontal shear** (head one way, feet the other). The monocular depth estimate is not usable for this |
| **Planar yaw** (body as a flat cutout, `x' = x·cos45`, z = 0) | stable and mathematically exact for a flat figure, but it merely **narrows** the figure (shoulders 31 → 22 cm) and carries almost no turn information |

So an angle skeleton is produced the way OpenPose datasets are: **annotated from a real image in the
target view.**

1. **Plate** — generate a photoreal figure in the target view (`plate34Left`, `plate34Right`,
   `plateProfileLeft`, `plateProfileRight`; 1024×1536, no references). Explicit camera geometry in the
   wording works best: *"photographed directly from her left side so her body is seen edge-on in full
   left profile, her nose pointing to the left of the frame"*. A first `plate34Right` attempt produced
   only a weak turn and was regenerated with the camera-position phrasing.
   **But camera-position phrasing alone is NOT sufficient for a 3/4 turn — measured 2026-09-23.** The
   regenerated `plate34Right` still annotated to the *same body pose as the left*, and re-running it with
   the identical seed (`20260922`) reproduced that pose exactly (annotation is deterministic). What did
   produce a right turn was stating the turn as a **view** and forcing an asymmetric occlusion: the plate
   now reads *"a clear three-quarter view, her body turned three-quarters away from the camera with her
   right shoulder and right arm nearest to the camera and her left arm largely hidden behind her torso, her
   body and head facing toward the right side of the frame"*, with `seed 20260924`.
   **Verify a plate by annotating it and measuring the skeleton against the opposite view's skeleton — not
   by looking at the plate.** A weak turn is invisible to the eye but unmistakable in the annotation.
2. **Annotate** — DWPose the plate with `prompts/dwpose-annotate.json`:

   ```powershell
   & helpers/runpod/generate-one.ps1 -WorkflowPath specs/image-generator-tests/qwen-21-native-reference/prompts/dwpose-annotate.json `
       -ComfyUiUrl http://192.168.0.11:8188 -InputImagePath artifacts/tmp/qwen-2-1/<plateCell>/result_0.png `
       -Prefix skel -OutputDir artifacts/tmp/qwen-2-1-pose
   ```

3. The result is committed under `skeletons/` (`angle-34-left|right`, `angle-profile-left|right`) and was
   **visually verified** before use in every case below.

> **CORRECTION (2026-09-23) — the 3/4 pair was not verified for direction and was wrong.**
> "Visually verified" did not include comparing the left and right skeletons *against each other*, and
> `angle-34-right.png` was in fact the **same body pose as `angle-34-left.png`** — not a right turn.
> Measured: `chamfer(left, right) = 7.13 px` versus `chamfer(left, mirrored right) = 14.58 px`, i.e. the
> un-mirrored match is twice as good. The **profile** pair measured correctly the whole time
> (`33.19 px` same vs `1.43 px` mirrored = a true flip) and is the control that shows what a correct pair
> looks like. Consequence: every 3/4-right body render came out facing left, including case 13 below.
> **Resolved in two deliberate steps.** First a horizontal mirror of the left was installed as an immediate
> stopgap (exact flip, IoU 1.0000), so the app stopped emitting two left-facing 3/4 views. Then the **plate
> itself** was fixed and the skeleton **re-annotated** from it — the method this runbook requires. The
> regenerated annotation measures `16.63 px` same-orientation vs `7.19 px` mirrored: a genuine right turn
> that is *not* a clone of the left. All three copies were updated (`wwwroot/pose-library/`, `skeletons/`,
> and the runner's staging copy) together with the `angleSkel34Right` hash in `manifest.json` and in
> `run-qwen-2-1-proof.ps1`. Originals preserved at `artifacts/tmp/dbquery/backup/pose-library/`.
> Case 13's output image still shows the OLD left-facing render and must be re-run.
>
> **Lesson for the next skeleton:** verify a left/right pair by measuring the pair, not by looking at each
> file on its own. A mirrored pair and a duplicated pair look similar one at a time.

The plates are used **offline only** and are never sent to Qwen — which is what keeps the
`pose-photoreal-reference` rule (case 03: a photoreal full body in a slot is REPRODUCED) from turning a
plate into the output.

### Cases

| # | Case id | Runner cell | References (slot order) | Result |
|---|---|---|---|---|
| 12 | `body-angle-34-left-front-plus-skeleton` | `bodyAngle34FrontPose` | accepted front body + 3/4-left skeleton | **pass — the recommended shape** |
| 13 | `body-angle-34-right-front-plus-skeleton` | `bodyAngle34RightFromFront` | accepted front body + 3/4-right skeleton | **pass (RE-VERIFIED 2026-09-23).** The original verdict claimed a "mirrored turn" without ever measuring it; the render actually faced **left** (`nose_offset_pct` −15.73 %) because the skeleton was the same pose as the left. With the regenerated skeleton the render measures **+38.11 %** — a genuine right-facing turn. |
| 14 | `body-profile-left-front-plus-skeleton` | `bodyProfileLeftFromFront` | accepted front body + profile-left skeleton | pass — true left profile, left-calf tattoo visible (near leg) |
| 15 | `body-profile-right-front-plus-skeleton` | `bodyProfileRightFromFront` | accepted front body + profile-right skeleton | pass — true right profile, tattoo correctly hidden (far leg) |
| 16 | `body-angle-34-left-words-and-face-only` | `bodyAngle34NoSkel` | 3/4-left face only | pass — an angle-matched face + the words **alone** also turn the body |
| 17 | `body-angle-34-left-skeleton-plus-face` | `bodyAngle34Real` | 3/4-left skeleton + 3/4-left face | pass with caveat — angle and identity fine, **body invented** (visibly heavier than the accepted front) |
| 18 | `body-angle-34-left-front-plus-skeleton-plus-face` | `bodyAngle34FrontPoseFace` | accepted front body + 3/4-left skeleton + 3/4-left face | pass but **redundant** — identical to case 12 (mean absolute pixel difference **2.89/255**) |

**What follows for the application:**

1. A non-front body view is a **generation**, not an edit-rotation: references
   `[accepted source body, angle skeleton]` with the view's own prompt text.
2. The accepted body reference is what preserves the BUILD (case 17 shows the body is invented without
   it); the angle skeleton is what sets the turn; the prompt carries wardrobe and everything else — case
   12's clothing is the prompt's white t-shirt and rolled jeans, not anything in a reference.
3. The **face reference is optional** and, at full-body scale, redundant once the body reference is
   present (case 18). It stays useful when no body reference is sent, or for close-ups.
4. **A skeleton slot is pose guidance**, so the earlier rule that a pose skeleton cannot accompany a
   native-reference render does not hold for 2.1 — the skeleton is simply another reference.
5. The angle-skeleton library is four ~15 KB files, regenerable by the two-step recipe above. They are
   committed here under `skeletons/`; wiring them into the app means copying them to
   `DreamGenClone.Web/wwwroot/pose-library/` and mapping them to the canonical view keys the way
   `BodyStanceSkeletons` maps stances (see the body-angle implementation plan).
6. The app renders every angle from the accepted **front** of the state, not from the edit chain's source: all
   four angles above are "the accepted front, turned" (cases 12–15). A profile drawn from its own
   three-quarter — which is what the *edit* chain uses — is a different shape and was NOT measured here, so
   the render path deliberately does not take it. One base also keeps the four angles comparable to each other.

### Known limitations of this package

- **Skeleton proportions are off**: the DWPose annotation in these tall (1024×1536) frames yields a long
  neck and legs (~62% of figure height vs ~47% anatomically). It did **not** harm the results — every
  render above has correct anatomy, because the body reference supplies the build and the skeleton only
  supplies limb/joint ANGLES. Do not read the committed skeletons as clean anatomical references.
- **Regeneration instability, measured**: the annotation is pixel-deterministic for a given plate, but
  the PNG file bytes are not (two runs produced identical pixels — mean abs difference 0.0 — with
  different SHA-256). The runner pins references by file hash, so a re-annotation invalidates the pin
  even when nothing changed visually.
- **One iteration is often needed**: a plate whose turn is too weak produces a weak angle skeleton, and
  the fix is camera-position wording in the plate prompt.
- Adherence is approximate; ambiguous stances and adult content are untested here, as in the pose cases.

## The BACK view (case 19, 2026-09-24)

Operator request: *"the body view needs another angle, which is the back view, no face, full back side view"*. The back
view is not "another 45 degrees" — the whole front of the body becomes invisible, while the reference the app holds is a
picture **of** that front (which has a face). Two things therefore had to be measured rather than assumed, and this is
why the cell exists:

1. `[accepted front body, back skeleton]` + the back clause produces a **true back view**;
2. **no face appears** — the reference shows one and the view forbids it, which is exactly the kind of contradiction a
   model resolves by turning the head.

**Result — passed, both.** Case 19 (`body-back-front-plus-skeleton`, refs `beckyFrontBody` + `angleSkelBack`) renders the
back of the head with no face at all, the accepted build preserved, and the tree tattoo on her **left** calf (in a back
view her left leg is on the viewer's left — so the side is right, not mirrored).

### The back skeleton is annotated, like the others

`plateBack` (case source, `prompts/plateBack.json`) is a plate of a figure photographed from directly behind. Its
wording has to say the face is not visible: without that, "a back view" produces a figure who has turned her head to
look at the camera, and the skeleton annotated from that plate carries a turned head into every back view the app
renders.

The DWPose annotation of a back view has one honest oddity, visible in `skeletons/angle-back.png`: the **face keypoints
come out as a clump at the top of the head**, because there is no face to find. It did not harm case 19 — the pose the
skeleton carries (stance, limb angles) is what the render follows — but it is recorded here rather than smoothed over.

### What the app does with it

The back view is a canonical slot like the other five (six per state, `SceneImageReferenceBodyView.Back = 6`, added at
the END so the persisted values 1–5 are untouched), rendered from the accepted base of its state plus
`pose-library/back.png`. It is the one angle that offers **no identity face reference**: the service refuses one with
that reason, and the panel says so instead of drawing a switch the render would reject.

---

### Replay
```powershell
# The four angle skeletons (steps 1–2 above), then:
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells bodyAngle34FrontPose
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells bodyAngle34RightFromFront
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells bodyProfileLeftFromFront
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells bodyProfileRightFromFront
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells bodyAngle34NoSkel
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells bodyAngle34Real
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells bodyAngle34FrontPoseFace
# The back view (2026-09-24): accepted front body + the back skeleton
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells bodyBackFromFront
```

Pass **one cell per invocation** (or use `-Command`): with `-File`, PowerShell passes `a,b` as a single
string, so `-Cells a,b` fails with "Unknown cell 'a,b'".

