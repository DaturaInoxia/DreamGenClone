# Dual-base FLUX OpenPose pipeline — proven and repeatable (2026-09-18)

**Status:** Proven on RunPod Serverless. Both bases pass all visual gates. Pipeline is deterministic and app-incorporable.

This document records the working pipeline, the failures that led to it, and the exact reason each failed approach cannot work. Read this before touching the scene-image generation pipeline.

## Proven pipeline (5 steps, all deterministic)

```
1. Skeleton   DWPose canvas normalized to the exact generation canvas (1216×832)
              Base B = mirror ONLY the right figure (strip mirror incl. face), then shift for an 80px gap
2. Figures    T2I + XLabs OpenPose ControlNet on a PURE WHITE backdrop
              (per-character prompt; facing instruction is the FIRST sentence)
3. Cutout     rembg (isnet-general-use) — person segmentation, no thresholds
4. Composite  scale figures to 72% frame height, feet at 92%, paste on bedroom reference
5. Harmonize  Standard KSampler + SetLatentNoiseMask, denoise 0.4, figure mask only
```

Step 5 is the only sampling step that touches the final image, and it is masked to the figures — **the bedroom reference is never re-noised**, which is why it survives pixel-stable.

## Final results (all gates pass, both bases)

| Gate | Base A (facing each other) | Base B (both facing right) |
|---|---|---|
| Reference bedroom preserved | ✅ | ✅ |
| Two solid people | ✅ | ✅ |
| Full body head-to-toe | ✅ | ✅ |
| Natural necks | ✅ | ✅ |
| Correct pose / facing | ✅ | ✅ |
| Opaque clothing, correct characters | ✅ | ✅ |
| No cutout edges / lighting integrated | ✅ | ✅ |
| Sharp faces, natural eyes | ✅ | ✅ |

Artifacts:
- Base A: `proofs/harmonize-face-to-face-v2/img-flux-openpose-serverless_0.png`
- Base B: `proofs/harmonize-base-b-rembg/img-flux-openpose-serverless_0.png`

## Critical verified findings (why each failed approach fails)

### 1. XLabs `XlabsSampler` ignores `SetLatentNoiseMask` — masked "inpainting" is impossible on this path

Read from the x-flux-comfyui source: the sampler reads only `latent_image['samples']` and never checks `noise_mask`. Standard ComfyUI's KSampler checks it; XLabs' does not. Any graph that combines XLabs ControlNet with `SetLatentNoiseMask` silently ignores the mask.

**Consequence:** OpenPose-conditioned generation on the FLUX endpoint cannot be mask-limited. Compositing must happen in pixel space (rembg + paste), with the standard KSampler used only for the un-controlled masked harmonization pass.

### 2. XLabs img2img math erases the source at strength 1.0

`XlabsSampler`: `img = t * noise + (1 - t) * orig`. At `image_to_image_strength = 1.0`, `t ≈ 1.0` → the source latent contributes zero. Earlier "inpaint" runs were pure T2I; the bedroom never conditioned them. Strength sweeps (0.35–0.85) traded room preservation against figure integrity with no acceptable point: the empty-room latent and the two-figure skeleton are conflicting signals inside one latent.

### 3. Pose canvas MUST match the generation canvas exactly

A 768×512 skeleton on a 1216×832 canvas caused: waist-up crops (XLabs bicubic-stretches the control; mismatched aspect leaves FLUX to resolve framing → its "two people" prior wins and crops), and elongated necks when combined with img2img. Normalizing the skeleton to exactly 1216×832 (stretch-to-fill, `normalize-pose-canvas.py`) fixed both.

### 4. OpenPose does NOT control head/face direction

The XLabs FLUX OpenPose checkpoint locks bodies (legs, torso, arms) but faces follow FLUX's prior. Base B (both facing right) required the facing instruction as the literal first sentence of the prompt plus "facing each other" in the negative. Even then, one render produced two men (prompt pressure distorted characters) — per-character descriptions must stay explicit and inseparable ("The man, Dean, ... The woman, Becky, ... NO beard").

### 5. Backdrop extraction: use rembg, never thresholds

The AI backdrop is never uniform (folds, gradients, texture). Every threshold approach failed:
- color-distance from median → dark fold touching the man leaked in as a "blob"
- border-connected component exclusion → backdrop texture ring blocked interior reach
- flood-fill from borders → same texture ring blocked the flood (white rectangle paste)

`rembg` (`isnet-general-use`) segments the figures correctly regardless of backdrop. Threshold-based extraction must not be used in the app.

### 6. Backdrop prompt matters for extraction reliability

"pure white seamless studio backdrop, perfectly even flat white lighting, no shadows, no gradients, no folds" gives rembg the cleanest input. Keep this prompt for the figure stage.

### 7. Figures need margins in the studio frame

If the figures touch the frame edges, feet/shoes get cropped and cannot be recovered downstream. Prompt for "generous empty space above their heads and below their feet". Composite places feet at 92% frame height, figures at 72% of frame height.

### 8. Gap between figures is required for clean extraction

If the two figures touch, the backdrop between/below them merges into the figure component. The Base B skeleton shifts the mirrored right figure for an 80px gap (`create-base-b-pose-right-only.py`). Extraction then validates on component count/shape.

### 9. Mirror for Base B must include the face

Mirroring "the right figure" by connected-component bounding box missed the face (face keypoints were a separate component from the body at threshold >20). Correct approach: split the canvas at the black gap column between the figures, mirror the entire right strip (face included), then translate it for the gap.

### 10. Location reference must have open floor where the people stand

The first bedroom reference had its bed in the center — exactly where the figures go. Any masked/figure placement destroyed the room's identity. The working reference (`proofs/locref-open-floor/`) has bed/window/furniture at the periphery and open carpet center. **When generating location references for the app, prompt for open floor in the subject area.**

### 11. Two-pass hires-fix: pass 2 must be an UPSCALE, not same-resolution polish

The canonical 2-pass (ComfyUI "Hires fix") is: pass 1 at base res → `UpscaleLatent` (or LatentUpscale) → pass 2 img2img at higher res, denoise 0.4–0.6. A same-resolution second pass just repaints (it drifted the room in earlier tests). The upscale pass is what fixed soft faces. Note: `UpscaleLatentBy` is not installed on the current worker; use core `LatentUpscale`. (In the final pipeline, faces are handled by the harmonize pass on the rembg composite instead.)

## Pipeline files (source of truth)

| Step | File |
|---|---|
| Skeleton normalize | `normalize-pose-canvas.py` |
| Base B skeleton (mirror right + gap) | `create-base-b-pose-right-only.py` |
| Studio figures (A) | `proofs/figures-studio.workflow.json` (prompt in `isolate-text-only` lineage; white-backdrop variant used for B) |
| Studio figures (B) | `proofs/figures-studio-b.workflow.json` |
| Composite + mask | `composite-figures-b-rembg.py` (the keeper; rembg-based) |
| Harmonize (A) | `proofs/harmonize-face-to-face.workflow.json` |
| Harmonize (B) | `proofs/harmonize-base-b.workflow.json` |
| Location reference | `proofs/locref-open-floor.workflow.json` |

## App integration notes

- Steps 1–4 are local CPU work (DWPose optional if skeletons are authored; rembg runs locally on CPU). Step 5 is one Serverless job.
- The harmonize workflow uses the standard KSampler (not XLabs), so it runs on the existing `img-juggernaut`-style ComfyUI worker or the FLUX worker — it needs no XLabs nodes.
- Identity: apply the app's Qwen edit pass (angle-matched refs) AFTER the harmonized base is accepted. The base must be artifact-free first — Qwen edit fixes faces, not compositing artifacts.
- rembg adds `rembg` + `onnxruntime` as app dependencies (local CPU, ~180 MB model, downloaded on first use; pin/ship the model for deterministic cold starts).
- Prompt rule: facing/composition instruction FIRST, per-character identity descriptions explicit and inseparable, backdrop spec ("pure white seamless, no shadows/gradients/folds") verbatim.

## Failed-approach graveyard (do not retry)

- XLabs img2img strength sweeps for room preservation (no acceptable point exists)
- `SetLatentNoiseMask` with `XlabsSampler` (mask silently ignored)
- Threshold/color-distance backdrop extraction (non-uniform AI backdrops always leak)
- Same-resolution second "refine" pass (repaints the room; not what hires-fix is for)
- Mirroring the full Base A canvas for Base B (flips BOTH figures)
- Hand-placed repaint ellipses for artifacts (chasing, not converging)
