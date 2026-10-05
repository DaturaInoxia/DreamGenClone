# B-139 — 360° Location Reference (equirectangular walk-around)

**State:** `designed` — investigated 2026-10-03, refined 2026-10-04 with the verified integration points and the
declared-coverage rule. **Half A (T1 + T2) approved by the operator on 2026-10-04 ("1 then 2"); awaiting the go-ahead
to implement.**
**Scope:** large. **Priority:** low.

## 1. Why this was investigated now

The operator asked for "the side view of the same shed" and hit a wall that is worth stating plainly, because it is
the reason this item exists:

| Attempt | Result (measured) |
|---|---|
| Bind the approved front as a Location reference, describe the angle | Same shed — but the **same viewpoint**, with windows, ground cover and surroundings re-invented |

The reference *did* reach the model (verified in the row: `strategy: NativeMultiReference`, the approved front image
id). The failure is not a bug: a reference image is **appearance conditioning**, and this codebase's own measurement
in `IReferenceConditionedImageClient` is that the first reference **anchors the composition**. There is no yaw in a
reference image, and the compiled prompt for the natural-language dialect is a verbatim one-sentence pass-through.

**So multi-angle consistency for a place is not obtainable from a single reference by describing harder.** That is
exactly what B-139 is for.

## 2. The finding: this item is two halves, and only one of them is risky

### (A) The CONSUMER half — buildable now, zero research

Turning an equirectangular panorama into a perspective view at a chosen yaw / pitch / FOV is **deterministic
per-pixel arithmetic**. It needs no model and no new dependency:

- `SixLabors.ImageSharp` **3.1.11** is already a first-class dependency of `DreamGenClone.Web`.
- It is used by exactly this family of engines already: `ImageCropEngine`, `ImageResizeEngine`, `ImageMirrorEngine`,
  `ImageRegionMaskEngine`, `PoseSkeletonRenderer`.
- The established pattern is explicit in `Program.cs`: a stateless engine interface + implementation, registered as a
  singleton, with its own pixel-level test file and no store/queue plumbing.

The property this buys is the one the operator actually wants: **every view derived from one panorama is
geometrically consistent with its siblings by construction.** Front, left, right and rear taken at yaw 0 / 90 / 180 /
270 are the same building by definition — no re-rolling, no drift, no re-invention of windows or ground cover.

And it composes with what already shipped: a derived view is **just an image**, so naming → approval → binding as a
location reference (B-145) already works, and `SceneAssetImageNaming` already *requires* a name for precisely this
multi-image case.

### (B) The PRODUCER half — the whole risk

Obtaining a usable 360 in the first place. Verified absences:

| Checked | Result |
|---|---|
| Any equirect/panorama/360 code in the app | **None** (`Application`, `Infrastructure`, `Domain`) |
| Panorama-capable base model or panorama LoRA on the pod | **None registered** |
| Pod ControlNet workflows | `controlnet-touch-*`, `dwpose-extract-proof` — pose/touch extraction, **not** architecture depth |
| Depth/canny-for-architecture graph | **None**; no depth node in `ComfyUIImageEditingClient` |

B-139's own note stands: the N6 outpaint extends one flat view sideways and **never rotates or closes the loop**, so
it is a building block at best. Creating a true equirectangular image means a panorama-capable model or LoRA, or an
outpaint chain proven to wrap and meet seamlessly — and that seam is the qualification question, not an
implementation detail.

## 3. Design (half A)

Verified against the codebase on 2026-10-04 — every integration point below is an existing pattern, not a proposal:

| Existing | File | The projection follows it |
|---|---|---|
| `IImageCropEngine` / `ImageCropEngine` | `Editing/ImageCropEngine.cs` | engine interface + stateless implementation, `ImageSharp`, no store/queue knowledge |
| `MediaEditCropOperation` (a record that carries its own `Validate()`) | `Editing/MediaEditOperations.cs` | the projection's parameters record, validated the same way |
| `MediaEditOperationKind` (`Crop` 2, `Mirror` 4, `Enhance` 3) | `Editing/MediaEditOperations.cs` | new `ProjectView = 7` |
| `CropOperationExecutor : IMediaEditOperationExecutor` (`Kind => Crop`) | `Editing/MediaEditOperationExecutors.cs` | `ProjectViewOperationExecutor`, same shape |
| Executor selected by `MediaEditOperationExecutorResolver`, missing kind fails fast | same file | unchanged |
| Derived image written through the run's own writer (`SourceImageId` + provenance) | `Editing/*SubjectWriter.cs` | unchanged — a derived view is just an image |

- **`IImageEquirectangularProjectionEngine` / `ImageEquirectangularProjectionEngine`** in
  `DreamGenClone.Web/Application/RolePlay/Editing/`, beside the other engines, registered as a singleton.
  - Input: the source stream, `yawDegrees`, `pitchDegrees`, `horizontalFovDegrees`, output `width`/`height`,
    and **`sourceCoverageDegrees`** — see the coverage rule below.
  - Output: PNG bytes.
- **The coverage rule (refined from the first draft).** The first draft refused anything that was not 2:1. That is
  right for a true equirectangular panorama and WRONG for a wide strip, which is the case the operator can actually
  reach today. So:
  - `sourceCoverageDegrees = 360` **requires** a 2:1 source, and refuses otherwise — an equirect assumption that is
    silently wrong produces a plausible but wrong picture, which is worse than a refusal.
  - `sourceCoverageDegrees < 360` (a cylindrical strip) has **no** aspect requirement, and the yaw range is clamped
    to the covered arc: a view outside the captured arc is refused by name rather than invented at the edge.
  - The coverage is **declared, never inferred**: nothing in the image says how much of the world it covers.
- **Derived view = a normal image in the location container**, `SourceImageId` pointing at the panorama and
  `DisplayName` set to the view name (e.g. `Left — yaw 90`), created through the same derive path the crop already
  uses. Naming → approval → reference binding (B-145) then work with nothing new.
- **The panorama itself is just another accepted image of the location** (named `Panorama`). No new type, no new flag.
- **Surface:** the projection is offered as an OPERATION beside Crop / Mirror / Enhance in the image edit workspace —
  the existing deterministic-operation surface — rather than a new container-specific control. That reuses the
  operation plumbing, the run journal and the "derive → name → approve" flow, and it means the source can be any
  image the workspace can open.

## 3a. What half A can and cannot deliver on its own (the honest framing)

**Half A is the durable half, but it is a viewer, and a viewer needs something to view.**

- It makes camera placement **exact and repeatable** for ANY equirectangular (or declared-coverage) source.
- It does **not** create such a source. Verified absences stand: no equirect code, no panorama model/LoRA on the
  pod, no architecture depth/canny graph.
- **The cheapest producer is a camera, not the model.** A phone's 360 or panorama mode produces exactly this kind
  of image, and it needs no model, no LoRA and no qualification run. For a real location the operator can walk to,
  that is the fastest route to a navigable room, and it is worth saying plainly instead of framing half A as useless
  until a model exists.

So the recommended order is unchanged — **T1 then T2** — with T3 (a generated producer) as a separate, riskier
question that half A does not depend on.

## 4. Staged plan

| # | Task | Risk |
|---|---|---|
| T1 | `ImageEquirectangularProjectionEngine` + pixel tests (coverage/shape refusal, yaw wrap at 360, yaw clamp on a strip, known-angle fixtures, seam continuity at 0° and 360°) | none — deterministic |
| T2 | `ProjectView` operation kind + parameters record + executor + singleton registration + the workspace control (yaw / pitch / FOV / size / name) | low — copies the crop operation |
| T3 | **Producer qualification run** (not a code change): can a 360 be made at all — outpaint-chain wrap test and/or panorama-LoRA evaluation — measured on **seam continuity** | **this is the item's real risk** |
| T4 | Only if T3 passes: make the panorama a first-class location action | low, once T3 is answered |

T1 and T2 have value even if T3 fails: **any** wide strip can be re-projected, and a re-projected angled view is
still geometrically honest about the part it covers.

## 4a. T3 — PRODUCER QUALIFICATION: measured 2026-10-03 (done, result is mixed)

The producer question was run for real, on the operator's own shed images, on the **local** ComfyUI host
(no pod — my earlier "pod" framing was wrong; the 2.1 stack is `WOOD-GAME-MAIN`).

**Artifact:** `pano360_qwen21_edit_v1.safetensors` ("360 Panorama Maker - Qwen-Image 2.1 Edit", Civitai
model 2976898 v3374088, published 2026-10-01, `nsfw: false`, 152.1 MB, HF mirror
`Gogodr/qwen-image-2.1-edit-pano360-lora`). Staged by `fetch-qwen21-nsfw-lora.ps1 -ModelVersionId 3374088`
(SHA-256 verified, host copy confirmed — 34 LoRAs) and driven by the new
`helpers/local-comfyui-host/run-qwen21-pano360.ps1`, which samples from an **empty 1536×768 latent** as the
card requires (the app's edit graph cannot: its latent follows `image_1`).

| Measurement | 1 view | 2 views (operator's Front + Back) | Meaning |
|---|---|---|---|
| Output shape | **1536×768 (2.000)** | **1536×768 (2.000)** | a valid equirect canvas, exactly as documented |
| **Seam wrap** (left edge vs right edge) | **12.7** | **13.0** | **it DOES wrap** — the card warned it "may not wrap perfectly"; against 59.6 for the two inputs joined directly, this is continuous |
| Re-projected yaw 0 vs input A | +0.292 | +0.261 | superseded — see the fidelity table below; a fixed yaw-0/FOV guess is not the fair test |
| **pano(2 views) vs pano(1 view)** | — | **r = +0.902, mean pixel diff 11.8** | **the second image barely changed anything** (the two INPUTS differ by 53.4) |

**Verdict: a plausible shed interior loosely INSPIRED BY the first photo. It is neither a faithful 360 of
the input room nor a combination of the two views.** The operator's own reading — *"it is not the front and
back images it is only the front and i think it changed alot"* — matched the measurement before the
measurement was finished, twice.

**The LoRA IS working** (a base 2.1 graph cannot emit a wrapping 2:1 equirect at all), but fidelity is the
problem, and the honest scale for it is a *blurred copy of the same image*:

| Measurement | Value | For scale |
|---|---|---|
| best view vs the FRONT photo, over yaw×pitch×FOV | **+0.362** (1 view) / **+0.396** (2 views) | a blurred copy of the front scores **+0.990** |
| best view vs the BACK photo, over yaw×pitch×FOV | **+0.450** (1 view) / **+0.426** (2 views) | FRONT vs BACK directly is **+0.189**; random noise **+0.007** |
| pano(2 views) vs pano(1 view) | **r = +0.902**, mean pixel diff **11.8** | the two INPUTS differ by **53.4** |

- ✅ A wrapping 2:1 equirect canvas is producible locally, no pod, no training.
- ❌ **Fidelity is weak even for the first image** (+0.40 against +0.99 for a blurred copy). The content is
  photo-influenced — well above the +0.19 "nothing in common" floor — but it is a reinterpretation, not a
  reprojection. Geometry the operator cares about (window and door placement, ground cover) will not match.
- ❌ **The second image contributed almost nothing.** The 2-view panorama correlates **+0.902** with the
  1-view panorama and differs from it by only **11.8/255** — while the two inputs differ by **53.4**. So
  this is not "two halves joined"; it is the front image's world with a light nudge.
- ⚠️ **A correction to this section's first draft.** It claimed the second image "leaked into the centre,
  the signature of a content-based blend" (yaw-0 resemblance +0.158 → +0.222). **The control refutes that
  reading.** Re-projecting the **1-view** panorama — which never saw image B — also "best matches" B at
  some angle (yaw 270, r=+0.250) almost as strongly as the 2-view one matches it at its best angle
  (yaw 350, r=+0.273). The metric is therefore detecting **generic "shed interior" likeness**, not
  placement, and no angle-sweep conclusion about where B landed is supportable. The 11.8/255 difference is
  the defensible evidence, and it says B was largely ignored.
- **Most likely cause, for the next attempt:** the card states the extra views are placed **"by their
  content"**, and that views within one training sample "don't overlap much". Two images that both read as
  "a dim shed interior with ribbed tin walls" give the model nothing to tell apart, so it treated them as
  the same view instead of an opposite pair. Combined with their geometric disagreement (59.6 at the join),
  there was nothing to anchor a second direction to.
- **What would test this properly:** two views that are UNAMBIGUOUSLY different directions — one with a
  distinct window on one wall, one with a distinct door on another — so the model has content to place
  apart. Not attempted yet.

**Consequence for the plan: use a real camera.** The generated route is a one-photo, low-fidelity affair in
practice, so it cannot supply consistent backdrops of a SPECIFIC room. **A phone 360 is an actual
measurement** — exact by construction, free, and it feeds the identical viewer.

**What survives, and is now the reason to do T1/T2:** the **viewer was PROTOTYPED** during this
qualification — equirect → perspective at any yaw/pitch/FOV — and it is now **validated against a synthetic
ground-truth equirect**, not against a crop of the thing it was meant to test. `validate_projection.py`
builds an equirect whose top half is the upper hemisphere by construction and asserts that "look up"
returns the sky and "look down" returns the ground: **`lat_sign = -1` passes, `lat_sign = +1` returns
them swapped.** It was used to render a full 360° turn from both panoramas. T1/T2 remain worth building;
they turn ANY equirect — captured or generated — into namable, approvable, bindable views. T3 no longer
gates them.

**Correction to the two earlier comparison sheets.** They were rendered with `lat_sign = +1`, i.e.
**vertically flipped**, and the operator reading "it is there upside down" was reading a defect in my
presentation, not in the model's output. The earlier "validation" of `lat_sign = -1` (re-projected yaw 0
vs a raw centre crop, +0.287/+0.241) was also circular — a raw equirect crop is not a perspective view,
so it could not have validated orientation at all. `compare-corrected.png` supersedes
`front-vs-centre.png` (deleted). **The panorama itself is stored UPRIGHT:** under the correct convention
it matches the known-upright source photo at **+0.397**, against **+0.251** under the flipped convention,
on a metric where a vertically inverted copy of the photo scores **−0.146**.

**A needed ruler for the fidelity numbers** (the same structure-correlation used throughout, now with
baselines that make the small values interpretable):

| Reference point | r |
|---|---|
| a blurred copy of the source photo (what "faithful" looks like) | **+0.990** |
| the source photo mirrored left-right | +0.688 |
| **FRONT vs BACK directly** — same room, 180° apart, no panorama involved | **+0.189** |
| random noise | +0.007 |
| the source photo flipped upside down | **−0.146** |

Against that ruler the best views score: FRONT vs 1-view **+0.362** / vs 2-view **+0.396**; BACK vs
1-view **+0.450** / vs 2-view **+0.426**. Read honestly, that is a **loose, generic shed likeness** — it
sits well above the "nothing in common" floor of +0.189, so the panorama is genuinely photo-influenced and
not noise, but it is nowhere near a reproduction. Note also that the panorama matches BACK (image_2,
which "lands at the centre" only when it is image_1) *at least as well as* FRONT, and that the 1-view
panorama — which never saw BACK — matches BACK **better** than the 2-view one does (+0.450 vs +0.426).
**That control still stands: image_2 contributed nothing detectable.**



1. **Free re-projection, or approval per view?** Recommended: each derived view is approved individually, because
   approval is what makes it bindable as a reference — a freely re-projectable panorama would bypass the very gate
   that keeps references meaningful.
2. **Seam requirement.** If T3 only ever produces a wrap with a visible join, is a "wide strip" (say 180°, no seam)
   acceptable as an intermediate that still covers front / left / right? **Answered in §3: yes, and the engine now
   models it explicitly** — a declared coverage below 360 needs no 2:1 shape and clamps the yaw to the captured arc
   instead of refusing outright.
3. **Does a panorama need its own type/flag**, or is it simply an image named `Panorama`? Recommended: just an image.
   A type would add a second way to say "this is a location image" for no benefit.
4. **Where does the projection live?** Recommended: the image edit workspace, as a deterministic OPERATION beside
   Crop / Mirror / Enhance (see §3), not a new location-container control. Both are plausible; this one reuses the
   operation seam end to end and needs no new persistence.

## 6. Relationship to neighbouring items

- **B-145** (named location references) — the consumer of this work. A derived view is a named image; nothing in
  B-145 needs to change.
- **B-144 Option 1** (location LoRA) — the alternative route to multi-angle consistency. A LoRA learns the place from
  15–30 shots and needs **no** geometry; a panorama gives exact viewpoint control from **one** image. They are
  complementary, not competing: the LoRA can be trained on views derived from a panorama.
- **B-135 N6** (outpaint / panorama) — a possible producer mechanism for T3, explicitly not a solution on its own.
