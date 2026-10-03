# CASE-24 — Outpaint on Qwen-Image-2.1 (measured 2026-10-02)

**Question:** can a 2.1 edit extend the canvas and regenerate only the newly exposed strip, preserving the original?

This gates B-135 N6 (Feather + panorama, EVP-2). The region-edit mechanism (CASE-21) confines an edit to a
rectangle *inside* the frame; outpaint is the same recipe on a canvas *larger* than the source.

**Answer: YES.** `ImagePadForOutpaint` + inverted `MaskRectArea` + `VAEEncodeForInpaint` preserves the
original area and generates the new strip.

## Graph

1. `LoadImage(source)` → encoder `images.image_1` (the scene conditioning).
2. `ImagePadForOutpaint(source, left/top/right/bottom, feathering=0)` → the padded canvas.
3. `MaskRectArea(source-rect, percent)` → white over the source rect, then `InvertMask` → white over the
   exposed strip, black over the original.
4. `VAEEncodeForInpaint(padded, mask, grow_mask_by=6)` → the sampler's starting latent.
5. `KSampler(denoise 1.0)` → `VAEDecode` → `SaveImage`.

The encoder conditions on the **original** source (`images.image_1`), while the sampler starts from the
**padded** latent — the split that makes the conditioning scene-aware and the canvas larger.

## Result (source `locref-bedroom.png` 1216×832, direction right +50% → 1824×832)

| Measurement | Value | Verdict |
|---|---|---|
| Preserved region (left 1216px) mean\|diff\| vs source | **2.32/255** | PASS (VAE round-trip noise; 3.86% pixels >8/255, the grow-6 seam band) |
| New strip (right 608px) std | **78.49** | PASS (generated content, not a flat/blank strip) |
| Seam (x=1216) | coherent — window + chair continue across | acceptable (hard edge, no feather yet) |

## What this means for the app

- Outpaint is **unblocked**: direction + amount (or a target ratio) rides the same `VAEEncodeForInpaint`
  mechanism the region edit already ships.
- The seam is hard at `blur_radius=0` / `grow_mask_by=6`; production bakes feathering into the strip mask
  (the region mask engine already paints a falloff) so the boundary is soft.
- The canvas size must be a multiple the VAE accepts (the 1824×832 output encoded cleanly).

## Reproduce

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen-2-1-outpaint-proof.ps1 -Direction right -Percent 50
d:/src/DreamGenClone/.venv/Scripts/python.exe artifacts/tmp/qwen-2-1-outpaint/measure.py \
    specs/image-generator-tests/dual-base-location/runs/dual-location-local-fast/refs/locref-bedroom.png \
    artifacts/tmp/qwen-2-1-outpaint/right-50/qwen21-outpaint_00001_.png 1216
```

## Not measured

- Other directions (left/top/bottom) — the runner supports them, untested.
- Panorama to a fixed ratio (2:1 / 3:1) — direction+amount is the primitive.
- Interaction with reference images (this proof carries none).
- Whether the same recipe holds through the app's `ComfyUIImageEditingClient` path (the graph is shared, so it should).
