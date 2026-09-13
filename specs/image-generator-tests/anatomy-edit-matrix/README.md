# Anatomy Edit Matrix (local ComfyUI — Qwen source-image editor)

Proof harness for the **source-image editor's adult-anatomy behaviour**: can a *clothed* figure be edited
into a correct, naturally-integrated anatomical result, across gender, arousal/erect state and framing.

This exists because the original bug ("editing a clothed man renders a female-groin form instead of male
anatomy") was only diagnosed by running controlled A/Bs outside the app. Keep it that way: **one variable
at a time**, everything else pinned.

## Why it lives here

Scripts in `artifacts/tmp/` are git-ignored and therefore not reproducible or reviewable, so the harness
is tracked here. **The rendered output is tracked too**, in `images/` beside the scripts: a proof set whose
images are not committed cannot be reviewed by anyone else, diffed against a later run, or re-checked when
the checkpoint or LoRA changes — and the repo already tracks adult proof imagery under
`specs/image-generator-tests/qwen/images/`. Pass a custom `-OutDir` for scratch work you do not want
committed.

## Layout

| File | Purpose |
|---|---|
| `make-base-image.ps1` | Renders ONE base image (T2I) locally — BigLust v1.6 defaults |
| `build-bases.ps1` | Builds the 6 base images (gender × posture × framing) and their crops |
| `crop-band.py` | Crops a horizontal band (fractions of height) to make the "close" framings |
| `run-anatomy-matrix.ps1` | Runs the edit cells and writes results into one flat folder |
| `make-contact-sheet.py` | Composes a labelled contact sheet so a whole set is reviewable in one image |
| `images/` | Base images, per-cell results and contact sheets — **tracked** proof output |

The editor itself is exercised through the **app-faithful** runner
`helpers/local-comfyui-host/run-local-aio-edit-proof.ps1`, which reproduces
`ComfyUIImageEditingClient.BuildAioMergedCheckpointWorkflow` exactly (merged checkpoint, AuraFlow shift,
CFGNorm, QwenImageEditPlus text encodes, MultiReferenceLatentMethod).

## Usage (from the repo root)

```powershell
# 1. base images -> specs/image-generator-tests/anatomy-edit-matrix/images/base-*.png
& specs/image-generator-tests/anatomy-edit-matrix/build-bases.ps1

# 2. run cells. -Set picks a group (core|mast|closeup|rubretry|all); -Prefix names the config.
& specs/image-generator-tests/anatomy-edit-matrix/run-anatomy-matrix.ps1 -Set all -Prefix v23lora08
& specs/image-generator-tests/anatomy-edit-matrix/run-anatomy-matrix.ps1 -Set all -Prefix remix `
    -Checkpoint 'qwenImageEditRemix_aioV20.safetensors' -LoraName ''

# 3. contact sheet
& .venv/Scripts/python.exe specs/image-generator-tests/anatomy-edit-matrix/make-contact-sheet.py `
    specs/image-generator-tests/anatomy-edit-matrix/images/contact-sheet.png 4 "caption" <files...>
```

## Cells

| Set | Cell | Base |
|---|---|---|
| `core` | male-far-soft / male-far-erect | `base-male-far` |
| `core` | male-close-soft / male-close-erect | `base-male-close` |
| `core` | female-far-unaroused / female-far-aroused | `base-female-far` |
| `core` | female-close-unaroused / female-close-aroused | `base-female-close` |
| `mast` | female-lying-far-unaroused / -aroused | `base-female-lying-far` |
| `mast` | female-lying-close-unaroused / -aroused | `base-female-lying-close` |
| `closeup` | female-close-spread-lips / -insert-finger / -rub-clit | `base-female-lying-close` |
| `rubretry` | female-close-rub-clit-v2 / -v3 | `base-female-lying-close` |

Bases are named `base-<gender>-<framing>.png`; the "close" framings are crops of the matching "far"
render, so the only difference between them is framing.

## Base-image recipe (learned the hard way — do not "improve" these)

- **Standing bases: 832x1216 portrait.** A standing full-body subject needs a portrait canvas.
- **Lying base: MUST be 1216x832 LANDSCAPE, seed 6601.** At 832x1216 the model **ignored
  "lying on her back with legs spread" entirely and returned a woman STANDING against a wall** — a
  portrait canvas plus "photograph of a woman" biases straight to standing full-body. Landscape plus
  `"taken from directly above, looking straight down"` is what actually produces the supine pose.
- **The lying prompt needs a negative that fights the standing pose** (`standing, standing pose,
  upright, vertical, full body standing, legs together, knees together, cropped top`), otherwise the
  wardrobe also drifts (crop top instead of t-shirt, shoes instead of barefoot).
- **Seed 7701 was rejected:** legs were not spread *and* the render carried a stock-photo watermark
  (`VAKULU.COM`). Always check a new base for watermarks — a watermark would silently invalidate a set.
- **The lying "close" framing needs COLUMN bounds, not just a row band.** A full-width horizontal band
  on a top-down figure slices a stripe across the body. `base-female-lying-close` uses rows 0.45-0.80
  and cols 0.30-0.70, which lands on the pelvis / jeans fly.

## Verified findings this harness established (2026-09-12)

- **Checkpoint + LoRA, not checkpoint alone.** `Qwen-Rapid-AIO-NSFW-v23` + `QwenEdit2511_AllIncludedGay_v2`
  (Civitai 2700552 / v3160956) was the operator-accepted configuration; **strength 0.8 best, 1.0 good**.
  The v23 merge on its own renders a female-biased groin form for male anatomy.
- **`qwenImageEditRemix_aioV20.safetensors`** (Civitai 2338517 / v2812714, SHA-256
  `10CF71B500DE46A09C48FD29B4CBE9DA07495593FE5514007B96EEAFDD68686B`) is a verified drop-in for the
  `MergedCheckpoint` graph (baked CLIP+VAE ⇒ single `CheckpointLoaderSimple`).
- **Instruction phrasing matters.** `"add a male erect penis to…"` produced a shape **pasted over** a closed
  jeans fly; `"unzip his jeans and pull the front open so…"` produced a naturally integrated result. An
  editor told to "add" a body part has no reason to alter the *clothing*. **The clothing-ACTION phrasing is
  used for every cell here** — note the `core` set was originally run with the "add" phrasing.
- SDXL anatomy LoRAs (e.g. `Gays_Anatomy_2`) are **architecturally incompatible** with Qwen-Image.

## Rules

- The `images/` output **is** tracked (see "Why it lives here"); use a custom `-OutDir` for scratch runs.
- One variable per comparison. Pin seed, steps, CFG, sampler, scheduler, denoise, AuraFlow shift, CFGNorm.
- Change the phrasing AND the config in separate runs, or the result is unattributable.
