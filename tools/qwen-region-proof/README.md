# qwen-region-proof — measure whether a Qwen-Image-2.1 edit stayed inside a masked region

## Why this exists

`TextEncodeQwenImage21` has **no mask input**. Verified live from the host's `/object_info` — its entire
input list is `clip`, `prompt`, `negative_prompt`, `resolution`, `images` (autogrow `image_1..image_16`),
and optional `vae`. So "select a region and edit only that region" cannot be expressed by feeding a mask
to the encoder.

The only candidate was to mask the **latent** the encoder already returns as `output[2]`. Whether that
contains the edit cannot be settled by looking at the result — a contained edit and an uncontained one
both "look like an edit". It has to be measured.

`measure_region.py` compares three renders of one source: the **source**, the **masked** run, and a
**control** run with the identical instruction and seed emitted *without* the mask node. The containment
number is the mean absolute pixel difference between the source and the masked run **outside** the masked
rectangle. A band around the rectangle is excluded on both sides of the edge (the mask has a blur radius
and the geometry rounds to whole pixels), so the soft edge cannot flatter the result.

## The measured answer (2026-09-25, host ComfyUI 0.37.1, RTX 5080)

A masked-latent edit of a 1024² portrait ("change the t-shirt to bright red"), mask rectangle
`32,55,35,33` percent (px 327,563–685,900), blur 8:

| Mechanism | Outside the rectangle (source vs run) | Inside | Verdict |
|---|---|---|---|
| **`VAEEncodeForInpaint`** (the documented inpaint encoder) | **0.319** mean, **0.001 %** of pixels changed | 125.25 mean, 100 % changed | **CONTAINED** |
| bare `SetLatentNoiseMask` on the encoder's latent | 53.379 mean, 100 % changed | 146.32 | **NOT contained** — the subject is destroyed outside the rectangle and only the patch survives |
| same, with the mask inverted | 20.574 mean, 54.8 % changed | 150.30 | **NOT contained** — behaves like the control |
| control (no mask at all) | 19.860 mean, 52.7 % changed | 135.61 | reference point |

**62× separation** between the inpaint-encoder run (0.319) and the control (19.860) outside the
rectangle. The inpaint run is pixel-identical to the source outside the mask and fully changed inside it.

### What this means (and the inference it rests on)

Masking the encoder's own latent **collapses the frame to a washed-out haze** wherever the latent is
frozen — the subject disappears. That is what a *frozen empty latent* decodes to, which means the
encoder's `output[2]` is **not a VAE encoding of the source image**: the source travels as a reference
latent in the conditioning, not as the sampler's starting latent. Masking it can therefore never preserve
anything.

`VAEEncodeForInpaint(pixels=source, vae, mask, grow_mask_by)` supplies a latent that genuinely encodes the
source, and that is what makes the edit containable. **That is the recipe for region-targeted editing on
2.1**, and it equally unlocks outpaint/panorama (mask the newly exposed strip).

## Run it

```powershell
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/qwen-region-proof/measure_region.py `
    --source  artifacts/tmp/qwen-2-1/p0Base/result_0.png `
    --masked  artifacts/tmp/qwen-2-1/p0RegionInpaint/result_0.png `
    --control artifacts/tmp/qwen-2-1/p0RegionControl/result_0.png `
    --rect-pct 32,55,35,33 --blur 8 `
    --out artifacts/tmp/qwen-2-1/p0RegionInpaint/measure.json
```

Exit code 0 = contained, 1 = not contained, so it can gate a proof run.

## The C# port (B-135 P3, B135-017)

`DreamGenClone.Web/Application/RolePlay/Evaluation/Gates/RegionContainmentGate.cs` is a pixel-exact
port of this tool's measurement, so a region edit can be gated in-app with no Python at runtime. The
verdict is the tool's own `contained` boolean (`outMasked * 2 < outControl AND inMasked > outMasked`).

- The arithmetic is pinned by `DreamGenClone.Tests/RolePlay/RegionContainmentGateTests.cs` on
  hand-computable synthetic images (contained / not-contained / per-channel vs max-channel).
- **Qualification (before the gate is used to judge anything):** run `measure_region.py` and the C#
  gate on the SAME three renders of one source and record the agreement here — the numbers must agree
  to the tool's 3-decimal rounding.
- **✅ Qualified 2026-10-04.** The C# gate was run on the SAME three renders the Python tool measured
  (`p0Base`, `p0RegionInpaint`, `p0RegionControl`, rect `32,55,35,33` %, blur 8, band 24) and
  reproduces every recorded number to the tool's 3-decimal rounding:
  - margin 105 px (band 24 + blur 8 % of 1024)
  - outside source-vs-masked `0.319` (tool: `0.319`)
  - outside source-vs-control `19.86` (tool: `19.860`)
  - inside source-vs-masked `125.252` (tool: `125.252`)
  - verdict `CONTAINED` (tool: contained)

`--rect-pct` is `x,y,width,height` in **percent of the frame** — the unit `MaskRectArea` itself takes
(its schema caps all four at 100). A pixel rectangle is rejected by the host.

## Caveats

- Pixel containment is not quality. The measured run shows a **visible rectangular seam** where the mask
  edge falls, because the rectangle is hard with only an 8 % blur. Production use wants a soft/feathered
  region (`GrowMask`, `FeatherMask`) or a painted mask, and the in-app UI should not present a bare
  rectangle as the finished feature.
- The control's drift (19.86) is itself informative: an unmasked 2.1 "edit" **regenerates the whole
  frame** — the flat studio background in the source came back as a textured concrete wall. Region masking
  is what pins everything outside the edit.
- Only one rectangle, one image, one seed, non-explicit content. A rectangle is the simplest possible
  region; non-rectangular masks and adult content are unmeasured here.

## Build the graphs under test

The proof cells live in `helpers/local-comfyui-host/run-qwen-2-1-proof.ps1` (`p0Base`, `p0RegionControl`,
`p0RegionMasked`, `p0RegionInpaint`, `p0RegionInverted`); each writes `request.json` + `meta.json` +
`result_0.png` under git-ignored `artifacts/tmp/qwen-2-1/<cell>/`.
