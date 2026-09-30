# CASE-21 — Region-targeted editing on Qwen-Image-2.1 (measured 2026-09-25)

**Question:** can a 2.1 edit be limited to a **region** of an existing image?

This gates the B-129 features that modify part of a frame: `§5.4` circle / brush / mask editing, mask-mode
Finish, and `§5.1` panorama (outpaint = mask the newly exposed strip). All of them are "change this part,
leave the rest alone", and none of them exists in the app today.

**Answer: YES — but only through `VAEEncodeForInpaint`, not through the encoder's own latent.** The
containment is essentially perfect; the obvious mechanism destroys the image.

## Why a mask input was never an option

`GET /object_info/TextEncodeQwenImage21` on the host (ComfyUI 0.37.1) — the node's complete input list:

| Input | Type | Notes |
|---|---|---|
| `clip` | CLIP | required |
| `prompt` | STRING | |
| `negative_prompt` | STRING | inert at cfg 1 |
| `resolution` | INT 0–4096 | tooltip: *"0 keeps each reference at its own size"* |
| `images` | `COMFY_AUTOGROW_V3` | template `{image: IMAGE}`, names `image_1..image_16`, min 0 |
| `vae` | VAE | optional |

Outputs: `[positive, negative, latent]`. **There is no mask input**, so the only route was to mask the
latent the encoder already returns.

## Method

One source image (`p0Base`: 1024² waist-up portrait in a large plain white t-shirt, seed 20260925, 25
steps, cfg 1, euler/simple). One instruction, identical in every run:

> Change the colour of the t-shirt the woman is wearing to bright red. Keep everything else in the image
> exactly as it is: her face, hair, pose, the background and the lighting.

One mask rectangle, `MaskRectArea` `x=32, y=55, width=35, height=33` **percent** (= px 327,563–685,900),
`blur_radius=8`. (Percent, not pixels: `MaskRectArea`'s schema caps all four at 100 and the host rejects a
pixel rectangle with `value_bigger_than_max`.)

Four renders differ **only** in how the sampler's starting latent is produced:

| Cell | Lineage |
|---|---|
| `p0RegionControl` | encoder latent → KSampler (no mask node at all) |
| `p0RegionMasked` | encoder latent → `SetLatentNoiseMask(rect)` → KSampler |
| `p0RegionInverted` | encoder latent → `SetLatentNoiseMask(InvertMask(rect))` → KSampler |
| `p0RegionInpaint` | **`VAEEncodeForInpaint(pixels=source, vae, rect, grow_mask_by=6)`** → KSampler |

Containment is measured by `tools/qwen-region-proof/measure_region.py`, which compares each run against
the source **outside** the rectangle, excluding a band around the edge so the blur radius cannot flatter
the result. `measure-region.json` holds the full report for the winning run.

## Result

| Run | OUTSIDE the rectangle (source vs run) | INSIDE | Verdict |
|---|---|---|---|
| **`VAEEncodeForInpaint`** | **0.319** mean abs diff · **0.001 %** pixels changed | 125.25 · 100 % changed | **CONTAINED** |
| `SetLatentNoiseMask` (encoder latent) | 53.379 · 100 % changed | 146.32 · 100 % changed | NOT contained — subject destroyed |
| inverted mask | 20.574 · 54.8 % changed | 150.30 · 100 % changed | NOT contained — tracks the control |
| control (no mask) | 19.860 · 52.7 % changed | 135.61 · 100 % changed | reference point |

**62× separation** between the inpaint-encoder run and the control outside the rectangle.

### Visual verdict, per image

| Image | Verdict |
|---|---|
| `21-region-00-source.png` | the source: flat grey studio background, white t-shirt |
| `21-region-01-control-no-mask.png` | edit works (shirt red) but the **whole frame regenerates** — the flat studio background came back as a textured concrete wall, and the skin gained freckling |
| `21-region-02-latent-mask-FAIL.png` | **FAIL** — only the masked patch survives; the subject is gone and the rest is washed-out haze |
| `21-region-03-inpaint-encoder-PASS.png` | **PASS** — face, hair, arms, background and lighting are the source; the shirt rectangle is red. A soft rectangular seam is visible along the mask edge |
| `21-region-04-inverted-mask-FAIL.png` | **FAIL** — mirrored: the frame drifts like the control and the rectangle is a featureless beige patch |

## What the failures teach (the load-bearing part)

A frozen region decodes to a **featureless beige wash**, and the subject vanishes. That is what a frozen
*empty* latent decodes to — so **`TextEncodeQwenImage21`'s `output[2]` is not a VAE encoding of the source
image.** The source travels as a *reference* latent inside the conditioning, not as the sampler's starting
latent. Masking it therefore cannot preserve anything, in either polarity.

`VAEEncodeForInpaint` adds the one thing that was missing: a latent that genuinely encodes the source.
That is the whole mechanism.

Corollary worth carrying forward: an **unmasked** 2.1 "edit" regenerates the entire frame (visible in the
control). Region masking is not only region targeting — it is what pins everything outside the edit.

## Consequences for the plan

1. **Region-targeted editing is feasible and unblocked.** Recipe: `LoadImage(source)` →
   `VAEEncodeForInpaint(pixels, vae, mask, grow_mask_by)` → `KSampler(latent_image=that, denoise 1.0)`,
   with the instruction conditioning from `TextEncodeQwenImage21`.
2. **Panorama/outpaint rides the same mechanism** — mask the newly exposed strip instead of a garment.
   Structurally identical; not yet measured.
3. **Do not ship a bare rectangle.** The measured run leaves a visible seam. Production wants a
   soft/feathered region (`GrowMask`, `FeatherMask`) or a painted mask, which is an app-side choice.
4. The instruction-only annotation route (a coloured circle in a reference slot) is **no longer needed as
   a fallback** for containment; it may still be worth measuring for *selecting* a region the operator
   drew, but the latent route is the mechanism to build on. **[Measured 2026-09-29 — CASE-23]**: the ring
   DOES select (it moved the edit to the person it enclosed while the clean control changed the other
   one), and it does NOT contain. The two mechanisms compose: ring for intent, mask for containment.

## Not measured

- Non-rectangular masks (painted / SAM-derived / face-box) — only a rectangle.
- More than one disjoint region in one call.
- Adult content (the whole package is a clothed portrait).
- Interaction with reference images: this proof carries **no** references. The app shape is
  source + references + masked region, and `grow_mask_by` / `resolution` interactions are untested.
- Whether the same recipe holds on the **editor** row (the graph kind is shared, so it should, but that is
  an inference).

## Reproduce

```powershell
# 1. the base and the four region runs (each writes request.json + meta.json + result_0.png)
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 `
    -Cells p0Base -Seed 20260925
$t = 'artifacts/tmp/qwen-2-1/p0Base/result_0.png'
foreach ($c in 'p0RegionControl','p0RegionMasked','p0RegionInpaint','p0RegionInverted') {
    powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 `
        -Cells $c -Seed 20260925 -TargetImage $t
}

# 2. the measurement (exit 0 = contained)
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/qwen-region-proof/measure_region.py `
    --source  $t `
    --masked  artifacts/tmp/qwen-2-1/p0RegionInpaint/result_0.png `
    --control artifacts/tmp/qwen-2-1/p0RegionControl/result_0.png `
    --rect-pct 32,55,35,33 --blur 8 `
    --out artifacts/tmp/qwen-2-1/p0RegionInpaint/measure.json
```

Host: ComfyUI 0.37.1, RTX 5080, `qwen_image_2.1_int8_convrot.safetensors` +
`qwen3vl_8b_int8_convrot.safetensors` + `qwen_image_2.1_vae_bf16.safetensors`, 25 steps, cfg 1,
euler/simple. Wall clock: 20.1 s (base), 30.1 s (control), 15.1 s (latent mask), 20.3 s (inpaint encoder),
55.5 s (inverted).
