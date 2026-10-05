# Location image experiments — findings

> **What this is.** The durable record of what was actually tried while attempting mutually
> consistent location images (multiple POVs of one place), and what each attempt proved.
>
> **What this is not.** Not a plan. For the options this evidence supports, see
> [`OPTIONS-consistent-location.md`](OPTIONS-consistent-location.md). For depth specifically — what
> it is, which models can consume it, and where it belongs in this application — see
> [`FINDINGS-depth-and-models.md`](FINDINGS-depth-and-models.md).
>
> Measurements span 2026-10-03 → 2026-10-04. The full instrument record, ground-truth renderer and
> scorers live in `specs/image-generator-tests/room-pov-proof/`.

---

## 0. Read this first: the instrument was wrong, and that invalidates numbers

Every correlation figure below came from a **greyscale structure-correlation** metric
(`structure()` in `score-photo.py`: both images reduced to 256×256 luminance, then correlated).

**It does not measure identity.** *"A dim shed interior with ribbed tin walls, a bright doorway at
one end and clutter along the floor"* scores highly under it while being a completely different
building — and for a room like the operator's, that gross layout is almost entirely generic. The
best-scoring generative result (**+0.624**) was shown to the operator, who said plainly:

> *"the sheet images aren't my shed at all — the result is still wrong."*

They are right, and the metric is the problem. This is the **second** time in this work an
instrument was built that the wrong answer could pass: the earlier synthetic 4-wall room "passed
6/6" because its identity *was* its geometry (flat coloured walls), so the test could not
distinguish layout-consistency from identity-consistency.

### The ruler, and what is trustworthy

| measurement | value | what it can tell you |
|---|---|---|
| two real photographs of this room, opposite ends | **+0.189** | a floor, not a target |
| a blurred copy of the same image | **+0.990** | what "faithful" looks like numerically |
| random noise | ~0.000 | nothing |
| an upside-down copy | **−0.146** | the metric does recognise orientation |
| **the operator's judgement** | — | **the only instrument here that measures identity** |

**Rule for future work: any claim about identity must be settled by looking at the image, not by a
structure correlation.** A layout metric may be reported, but it must never be presented as
evidence of identity.

---

## 1. What was tried, and the verdict on each

| # | Attempt | Result | Verdict |
|---|---|---|---|
| 1 | Analytic 4-wall synthetic room + depth-as-reference | 6/6 pass | ⚠️ **invalid as an identity test** — the room's identity *was* its geometry |
| 2 | Qwen 2.1 native multi-reference (source + depth of target view) | 6/6 on synthetic | ⚠️ works for layout; synthetic caveat applies |
| 3 | Qwen 2.1 + depth ControlNet | `RuntimeError` | misdiagnosed for a day — see §4 |
| 4 | Real shed: depth as an extra reference image (2.1) | moved toward the target view (+0.291) | ✅ real effect, weak fidelity |
| 5 | Real shed: **no** depth, just ask for the angle | **copied** the given view (+0.966 vs source) | ❌ the angle is ignored |
| 6 | Real shed: the **actual wanted photograph** as a reference | **copied** the given view (+0.694) | ❌ a reference photo does not supply a viewpoint |
| 7 | Real shed: depth + the wanted photograph together | +0.302 — the photo added nothing | ❌ geometry is the lever, photographs are not |
| 8 | Benches: two more POVs from the shed | +0.453 / +0.304 | ⚠️ but no real photo exists from those positions, so the depth supplied was the *other* views' — not a genuine new camera |
| 9 | Denoise sweep (0.35 → 1.0) | 1.0 preserves (r **+0.994**); every lower value destroys | ❌ there is no partial-denoise trick on this graph |
| 10 | 360 panorama LoRA (`pano360_qwen21_edit_v1`) | wraps, but weak fidelity | ❌ **use a real camera** |
| 11 | Equirect → perspective projection (prototype) | validated against a synthetic ground-truth equirect | ✅ **deterministic, exact — the durable half** |
| 12 | 16-channel lineage (`qwen_image_edit_2511` + `qwen_2.5_vl_7b` + `qwen_image_vae`) + depth-as-reference | **+0.624** — the best generative result | ✅ best generative, ❌ still not the operator's shed |
| 13 | Same lineage + depth as **ControlNet** | +0.239 @1.0, +0.436 @2.0, **+0.446 @4.0**, +0.383 with both channels | ✅ the controlnet works (strength-responsive) but is the weaker channel |
| 14 | Host capability inventory | 1,273 node types; full 3D/reconstruction stack present but **unarmed** | ℹ️ see §6 |

---

## 2. Qwen-Image-Edit — how it actually behaves

These hold across both 2.1 and 2511 unless stated.

**Mechanics.** The graph generates from an **empty latent**, conditioned on the source image(s)
injected by the encoder (`TextEncodeQwenImage21` with `images.image_1..N`, or
`TextEncodeQwenImageEditPlus` with `image1..image3`). The source's latents are carried *inside the
conditioning* — so the model can see the source, but **nothing is copied.** Every pixel is
re-drawn from noise and the source only *biases* the redraw. This single fact explains every
failure that follows.

**A reference image carries appearance, not viewpoint. There is no yaw in it.** Asked for "the side
of this shed", the model receives *what a shed looks like* and nothing at all about camera
orientation — so it obeys the part it can receive ("this shed") and re-rolls the details. That is
why attempts 5–7 produce the *same viewpoint with different contents*.

**Ordering is load-bearing.** The same depth map works as `image_2` and fails as `image_4`
(validated on the synthetic room).

**More is not better.** 4 wall references performed worse than 3. Two depth maps performed worse
than one.

**Guidance worked against itself.** CFG 3 and 6 with a more explicit instruction *regressed* the
result (green-wall agreement 0.34 → 0.05). The plainest instruction at the application's CFG 1.0
was best.

**Denoise is inverted on this graph** (measured, and contrary to the usual img2img intuition):

| denoise | r vs the source photo | mean diff /255 | outcome |
|---|---|---|---|
| **1.0** | **+0.994** | **4.85** | essentially the source (0.84% of pixels changed) |
| 0.7 | −0.029 | 60.09 | a different image |
| 0.5 | −0.032 | 58.56 | a different image |
| 0.35 | −0.040 | — | a different image |

**Keep denoise at 1.0.** There is no "keep the photo, nudge the view" setting.

**The instruction is the whole lever.** *"Keep everything exactly as it is"* returns the source
almost untouched — a no-op. *"The camera has moved"* invents a new room. Asked to *add two people*
**and** *keep everything as it is*, it honoured the latter (0.84% of pixels changed, no figures
appeared).

---

## 3. Lighting prose is an instruction, not a description

Transcribing a scene's `lighting` text verbatim (**"Thinning blue light from the last of the day;
dim inside the shed"**) erased the windows and re-tinted the room:

| measure | before | after |
|---|---|---|
| bright pixels (>150 luma) | 13.6% | **0.11%** |
| p95 luma | 212 | **88.8** |
| mean luma | 73.7 | 35.3 |
| blue-minus-red | −15.5 | **+9.0** (it obeyed the word *"blue"*) |

Restoring an explicit daylight clause recovered most of it — bright 4.3%, p95 131.5, B−R −11.0 —
and **fidelity on the identical seed rose +0.291 → +0.442.**

**Rule: any prose describing light, time of day or mood will be obeyed as an instruction to
re-light the image.** Never paste a scene's `lighting` field into a location-image prompt
unexamined.

---

## 4. The depth ControlNet was never broken — it was mispaired

This wasted most of a day and the correction matters.

**Reported symptom:** `RuntimeError: expanded size of the tensor (1) must match existing size (16) …
Target [1,16,64,64] Tensor [16,16,64,64]` from `QwenImageBlockWiseControlNet.process_input_latent_image`,
which hardcodes:

```python
latent_image[:, :16] = comfy.latent_formats.Wan21().process_in(latent_image[:, :16])
```

This was declared *"broken, not needed"* and routed around. **Both halves of that were wrong.**

- **`Wan21()` is correct for its family.** `supported_models.py`: `QwenImage → latent_formats.Wan21`
  (16 channels — Qwen-Image's VAE *is* the Wan 2.1 VAE). The line is not a typo.
- **The real cause was a model pairing error** — a 16-channel controlnet attached to a 64-channel
  model. See [`FINDINGS-depth-and-models.md`](FINDINGS-depth-and-models.md) §3 for the shape proof.

**Lesson: on a `RuntimeError` in a shared node, establish which family the checkpoint belongs to
before declaring the node broken.** "Broken" and "wrongly paired" are different diagnoses with
opposite fixes.

---

## 5. The 360 panorama LoRA — measured, verdict NO

`pano360_qwen21_edit_v1.safetensors` (Civitai 2976898 v3374088, base `Qwen/Qwen-Image-2.1`,
trained on 232 Poly Haven panoramas). Driven by `helpers/local-comfyui-host/run-qwen21-pano360.ps1`.

| measurement | 1 view | 2 views | meaning |
|---|---|---|---|
| output shape | 1536×768 (2.000) | 1536×768 (2.000) | a valid equirect canvas — the LoRA **is** working |
| seam wrap (left edge vs right edge) | 12.7 | 13.0 | **it does wrap** (59.6 for the two inputs joined directly) |
| best view vs the FRONT photo, over yaw×pitch×FOV | +0.362 | +0.396 | against **+0.990** for a blurred copy |
| pano(2 views) vs pano(1 view) | — | r **+0.902**, diff 11.8 | the two **inputs** differ by 53.4 → **the second image contributed nothing** |

**Verdict: a plausible shed interior loosely inspired by the first photo — not a faithful 360.**
Geometry the operator cares about (window and door placement, ground cover) will not match. The
model card's own caveats apply: photos must come from **one spot** turning the camera, and
*"whatever no view shows is invented."*

**Recommended alternative: use a real camera.** A phone 360 is an exact measurement, free, and
feeds the identical viewer.

**Viewer caveat that was also wrong once:** the comparison sheets were first rendered with
`lat_sign = +1`, i.e. **vertically flipped**. The operator reading *"it is there upside down"* was
reading a defect in the presentation, not in the model's output. `lat_sign = −1` is correct and is
now validated against a synthetic ground-truth equirect (look up → sky, look down → ground).

---

## 6. Host inventory (2026-10-04) — capability that exists but is not armed

ComfyUI runs on **`WOOD-GAME-MAIN`** (@192.168.0.11, RTX 5080), **not** on this workstation
(`WOODGAME`). An earlier "no 3D capability exists" conclusion was reached by inspecting the
**wrong machine** and is withdrawn.

- **1,273 node types** exposed, including a full 3D surface: `TripoMultiviewToModelNode`,
  `Hunyuan3Dv2ConditioningMultiView` (optional `front`/`left`/`back`/`right`),
  `TripoImageToMultiviewNode`, `SV3D_Conditioning`, `DA3GeometryToMesh`, `MoGePanoramaInference`,
  `RenderSplat` + `CreateCameraInfo`.
- **Unarmed:** `models/geometry_estimation` is empty, there is no Hunyuan3D/Tripo/Meshy model, no
  API keys are configured, and only 6 custom-node packs are installed (none of them 3D). These are
  node *definitions*, not capabilities.
- **Armed and relevant:** `qwen_image_2.1_int8_convrot`, **`qwen_image_edit_2511_fp8mixed`**
  (unused until now), `qwen_image_vae`, `qwen_image_2.1_vae_bf16`, `qwen_2.5_vl_7b_fp8_scaled`,
  `qwen3vl_8b_int8_convrot`, **`controlnet-depth-sdxl-1.0`**, `controlnet-canny-sdxl-1.0`,
  `juggernautXL_ragnarok`, `pony*`, `bigLust_v16`, and the staged
  `qwen_image_depth_diffsynth_controlnet.safetensors` in `models/model_patches`.

**Loose end:** that staged controlnet is a host change that was never recorded. Per the repo's
pod/host documentation rule it belongs in `helpers/local-comfyui-host/README.md` with a
re-appliable script.

---

## 7. Corrections made during this work

Recorded because each was caught by a control or by the operator, not by inspection:

1. The ground-truth room renderer was **horizontally mirrored** (`right = cross(worldUp, forward)`
   yields the *left* vector). Fixing it revealed a passing arm the mirrored run had hidden.
2. The first verdict gate was **too lenient** — it passed an arm on its dominant wall alone while a
   required second wall was absent. Tightened to require every required wall *and* a structure pass.
3. **Denoise semantics were stated backwards** in one session and corrected by measurement (§2).
4. **"The depth ControlNet is broken"** — wrong; it was mispaired (§4).
5. **"No 3D capability exists on the host"** — checked the wrong machine (§6).
6. **The structure-correlation metric was treated as an identity metric** — refuted by the operator
   (§0). This is the most consequential of the six.
7. **"B-145 may have superseded the promotion-naming task"** — it did not; verified 2026-10-04.

---

## 8. What remains untested

- **SDXL depth ControlNet on the shed.** `controlnet-depth-sdxl-1.0` + `juggernautXL_ragnarok` are
  armed and were **never tried** — the SDXL arm was abandoned early when Qwen was chosen. This is
  the cheapest untested route to depth-*structural* control.
- **A ControlNet built for Qwen-Image-2.1.** The staged one cannot serve 2.1 (shape mismatch). None
  is known to exist; not searched for.
- **Multiple distinct viewpoints with real coverage.** Every real-photo test supplied the target's
  own depth, so scores are an **upper bound** — in real use that geometry would not be available.
- **All views in one generation** (a 2×2 room turnaround, then crop). Consistency would be free
  (one latent). Never attempted.
- **Depth tone normalisation.** Depth-map tone tracks output exposure **inversely** across runs
  (FRONT-depth p50 79 → output mean 36.2; BACK-depth p50 26 → output mean 73.9) — one run per
  condition, not isolated. Normalise before supplying and re-test.

---

## 9. Where the evidence lives

| artifact | what |
|---|---|
| `specs/image-generator-tests/room-pov-proof/FINDINGS.md` | full instrument record, all readings, the synthetic-room proof |
| `specs/image-generator-tests/room-pov-proof/room.py` | analytic ground-truth room + exact depth maps |
| `specs/image-generator-tests/room-pov-proof/score.py` / `score-photo.py` | the scorers — **read §0 before trusting either** |
| `specs/image-generator-tests/room-pov-proof/extract-depth.ps1` | DepthAnythingV2 depth extraction |
| `helpers/local-comfyui-host/run-qwen21-native-edit.ps1` | the app-mirroring Qwen edit harness (`-References`, `-DepthControlImage`) |
| `helpers/local-comfyui-host/run-qwen21-pano360.ps1` | the 360 producer harness |
| `artifacts/tmp/depth-controlnet/` | the depth-channel comparison run + `sheet.png` (git-ignored) |
