# 070 — "From above" pose-library tests come out distorted on the reference route

**Reported:** 2026-09-28
**Surface:** Pose Library page → per-pose **Test this pose** (page-level step is the same service)
**Symptom (user):** the from-above poses "are not working" → clarified as "the pose is applied but badly —
distorted, squashed, limbs in wrong places."
**Choice made by the user:** stay on Qwen-Image-2.1 for now (do not switch the test to a ControlNet model).

## Report

The four "From above" libraries (imported 2026-09-28) render, are selectable, and appear in the picker, but a
test render of one does not reproduce the pose.

## Analysis

The packs are not implicated. Proven separately on 2026-09-28:

- all 61 pose files are byte-identical (`-cne`) to the creator's zip entries;
- all four zips verify against the SHA256 that Civitai's own API publishes;
- every file parses (1 person, 54 body values) and the importer reported `0 skipped`;
- 61 skeleton PNGs rendered, all 1024×1024, visually checked;
- `KnownGood` is only ever WRITTEN (`PoseLibraryImporter` :237, `PoseLibraryService` :430) and never filtered on,
  so the new presets are selectable.

The evidence points at the **mechanism the test used**, not the data.

### 1. Every pose test ran the reference-image route

`DreamGenClone.Web/logs/dreamgenclone-20260928.log` — 25 `Pose test render` lines, all identical in mechanism:

```
Pose test render: model=qwen_image_2.1_int8_convrot.safetensors protocol="ComfyUi"
                  mechanism=NativeMultiReference pose=on_knees 05
```

No pose test in the log used `PoseControlNet`. `ReferenceStrategyResolver.ResolvePoseAsync` prefers ControlNet
first and falls back to the model's native references; Qwen-Image-2.1 has no ControlNet graph, so it resolves to
`NativeMultiReference` = the label **"pose as a reference image"**. `PoseTestRenderService.ListModelsAsync`
deliberately sorts that route FIRST, so it is the default selection.

What that route can and cannot do is stated by the app itself, in the `Note` the service returns:

> The skeleton travelled as a reference image. This carries the body's geometry but NOT which way the figure
> faces (measured 2026-09-24: an identical front-facing skeleton came back turned away), so state the view in
> the prompt — "front view, facing the camera" — or expect the figure to face either way.

### 2. The request was fighting the pose from three sides at once

Live page state for `on_back 01` (Qwen-Image-2.1 selected):

- **References: 3** — log confirms `ComfyUI reference generation start: ... References=3`.
  The panel states: *"Conditioning on **Becky**: approved face + approved approved clothed front build, sent
  beside the skeleton."* So the request carries a **frontal face** and a **frontal clothed build** reference
  alongside the skeleton.
- **Prompt** (the step default, unchanged): *"A full-body photograph of a fully clothed person, natural skin
  texture, photorealistic, plain studio background, 85mm."* — a full-body **studio** shot with **no view stated**.
- **Canvas:** `1024x1024`, matching the skeleton's square canvas, so this is not an aspect mismatch.

A "from above" pose is almost entirely a statement about the camera axis and foreshortening. In this request the
only thing saying "overhead" is the skeleton, while a frontal face reference, a frontal build reference and a
"full-body photograph" prompt all say "normal framed figure". The skeleton is outvoted, and the model splits the
difference — which is what a squashed figure with limbs in the wrong places looks like.

This also explains why ordinary poses look acceptable on the same route: a standing/kneeling/sitting skeleton
still reads as a person on the frontal references' terms, so the conflict is invisible.

### 3. Ruled out

- **Portrait centre-crop clipping the figure.** Measured every pose's visible-joint bounding box: the four
  from-above packs are the NARROW ones (median width/height 0.47–0.60, max 0.78) and **0** would be clipped by a
  2:3 centre-crop. The bundled NSFW pack is the exposed one (median 0.80, max 2.85, **205** poses would clip).
- **Head keypoints missing.** Audited all keys of all packs: 4 of 15 `on_stomach` poses have no nose (a top-down
  view genuinely cannot see one) — real, but far too narrow to explain the whole symptom, and the import path does
  not call `RequireHeadKeypoints` (only `PoseExtractionService` does, for studio extraction).
- **Skew in the app's skeleton render.** `RenderPng` → `FitToCanvas` → `ComputeFraming` scales by
  `min(widthFit, heightFit)` and centres, so the figure is geometrically undistorted.

## Analysis — ROOT CAUSE (superseding the mechanism theory)

**The mechanism is not the cause. The source keypoint data is, exactly as the user said: "something is
different about the poses from the other library."**

### The defect: the lower legs are missing from the data

In `openpose-from-above-back`, **19 of 21 poses** mark BOTH ankle joints as absent (confidence 0, below the
0.1 floor). `openpose-from-above-stomach` is **13 of 15**. Per-pose visibility of `Rhip Rknee Rankle Lhip
Lknee Lankle`:

```
openpose-from-above-back       on_back_01 .. on_back_21   ##.##.   (ankles absent)
                               on_back_11, on_back_14     ######   (the only 2 complete)
openpose-from-above-stomach    13 of 15                   ##.##.
openpose-from-above-standing   ankles present in 8/10
openpose-from-above-knees      ankles present in 10/15
```

`PoseSkeletonRenderer.Rasterize` draws a bone **only when BOTH endpoints clear the visibility floor** — an
audited, deliberate rule (a lenient "either endpoint" predicate once produced a false all-clear on the pack).
So a missing ankle means **no shin is drawn at all**: the figure's legs stop at the knee. The rendered
`on_back 01` skeleton confirms it — neck→hip→knee and nothing below; no shins, no feet. The joint colour
chains end at the two knees.

That is the whole symptom. Handed a skeleton whose lowest joint is a knee, the model must invent the entire
lower leg, and it reconciles "the leg ends here" against its human prior by compressing and distorting the
body — "distorted, squashed, limbs in wrong places."

The libraries that render fine do not have this gap:

| Pack | Ankle joint present |
|---|---|
| openpose-nsfw | R 350/472, L 352/472 |
| openposes-collection | 46/46 |
| **from-above-back** | **2/21** |
| **from-above-stomach** | **2/15** |

### Second, independent defect: leg proportions

Where the ankles ARE present, the from-above legs are built wrong. Median shin ÷ thigh:

| Pack | ShinOverThigh |
|---|---|
| **from-above-standing** | **0.44** |
| **from-above-back** | **0.52** |
| **from-above-knees** | 0.66 |
| **from-above-stomach** | **0.70** |
| openposes-collection | 0.96 |
| openpose-nsfw | 1.00 |

A human shin and thigh are about equal. For a STRAIGHT leg the two segments are collinear, so any perspective
or foreshortening shortens both by nearly the same factor and the ratio stays near 1 — it cannot fall to 0.44.
Measured on the raw values, e.g. `standing_01` right leg: hip (209,562) → knee (211,633) → ankle (210,660) —
a straight vertical line, thigh 71 px, shin **27 px**. `on_stomach_01` right leg is 0.21, left 0.70, which is
the source of that pack's 3.32× left/right mismatch.

Counting legs that have a knee ≥150° (straight) yet a shin <80% of the thigh — physically contradictory:

| Pack | such legs |
|---|---|
| **from-above-standing** | **16 / 16** |
| **from-above-back** | **4 / 4** |
| **from-above-stomach** | **4 / 4** |
| from-above-knees | 0 / 20 |
| openposes-collection | 9 / 92 |
| openpose-nsfw | 54 / 702 |

### The control that clears the import and the renderer

`openpose-from-above-knees` comes from the **same author, same Civitai model, same zip family, same importer,
same renderer** — and it is anatomically sound: median knee angle **22.2°** (genuinely kneeling, so a short
shin is legitimate there) and **0** contradictory legs. If the import, the coordinate handling or the renderer
were at fault, that pack would be broken too. It is not. The defect travels with the source data of the
`back`, `stomach` and `standing` versions.

### Status of the mechanism theory (kept for the record, no longer the cause)

All 25 pose tests in the log did use `mechanism=NativeMultiReference` on Qwen-Image-2.1, so the reference route
and its documented inability to carry facing are real context, and the three references (Becky's face, Becky's
clothed build, the skeleton) plus a view-less "full-body photograph … studio background" prompt are a genuine
headwind for an overhead pose. They are not, however, why these packs fail and the others do not.

## Options (no code has been changed)

The root cause is in the pack data, so this is a data decision, not a code fix.

- **A. Drop the three defective libraries** (`back`, `stomach`, `standing`) and keep `on_knees`, which is
  anatomically sound. Nothing else in the library loses a working pose. Cleanest, and honest.
- **B. Keep them, but say so in each `pack.json`.** The library then documents "lower legs truncated at the
  knee in N/M poses" instead of leaving the operator to discover it. A manifest edit only, no code.
- **C. Synthesise the missing ankles.** Extrapolate a human-proportioned shin along each thigh direction. This
  INVENTS keypoints the author never supplied, would put fabricated geometry into the library, and would need
  an importer change (RP-adjacent → plan + approval). Not recommended; flagged for completeness.
- **D. Leave as-is** and use them only where the lower body does not matter.

Recommendation: **A**, optionally plus **B** — the `standing` version is 16/16 contradictory on legs that are
fully present, and the `back`/`stomach` versions are ~90% legless, so neither can produce a correct body under
any mechanism or prompt.

## Resolution

_Awaiting the user's decision on A/B/C/D. No code, config or DB change made._


## Validated

[ ] pending
