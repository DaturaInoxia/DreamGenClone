# wan-local-host — uncensored Wan 2.2 proof harness (local ComfyUI)

Qualification proof that the **uncensored Wan 2.2 I2V-A14B (rzgar)** renders **adult-implied/softcore
content without safety refusal or censorship distortion** on the local ComfyUI host
(WOOD-GAME-MAIN, `http://192.168.0.16:8188`). Same proof-first discipline as B-112/FLUX and the
SDXL implied matrices: fixed cells, honest per-cell visual review, results recorded.

> **Safety boundary (matches the B-112 / SDXL implied-matrix precedent):** committed cells are
> **implied / softcore-adult** ("tasteful implied nudity", intimate but non-explicit framing). This is
> enough to prove *uncensored behavior* — a safety-filtered model refuses or distorts even these —
> while hardcore-explicit prose stays **out of the committed proof scope**. The pipeline itself is
> uncensored-capable; the *proof assets* stay in the implied register.

## Models under test
| Cell source | Model | Files (on host `D:\ComfyUI\models\`) |
|---|---|---|
| Uncensored | rzgar `Wan2.2_I2V_High_R1` + `Wan2.2_I2V_Low_R1` (fp8-scaled) | `diffusion_models/`, 13.55 GB each |
| VAE | `wan_2.1_vae.safetensors` | `vae/` (0.24 GB) |
| Text enc | `umt5_xxl_fp8_e4m3fn_scaled.safetensors` | `text_encoders/` (6.27 GB) |
| LoRAs (opt) | `CubeyAI-GeneralN-High/Low` (0.5–0.55), `Wan2.2_LightX2V_high/low_n54vv` (4-step, near-full) | `loras/` |

## How it works
`run-wan-14b-proof.py` drives the two-stage native Wan 2.2 I2V graph (`WanImageToVideo` +
two `KSamplerAdvanced`: high-noise expert steps 0→split, low-noise steps split→N; optional
`LoraLoaderModelOnly` per expert) over the ComfyUI HTTP API:

1. For each cell in `prompts-unfiltered.json`, upload the cell's `start_image` (rendered SFW/implied
   still) → I2V with the cell motion prompt.
2. Save mp4 + extracted start/mid/end frames to git-ignored `artifacts/tmp/wan-proof/<cell>/`.
3. Print the `/view` URLs and a `run-manifest.json` for the pass.

## PASS rubric (per cell — honest, visual)
- **Renders** — no refusal text, no black/censored/watermark overlay, no heavy blur distortion.
- **Prompt honored** — subject + requested motion present.
- **Body/identity fidelity** — no anatomy morphing (e.g., no added pregnancy belly, no extra limbs),
  faces/wardrobe stable across the clip.
- Verdict per cell: PASS / PARTIAL / FAIL, recorded with the frame files.

## Run
```powershell
# default: all cells, no LoRA (baseline)
python helpers/wan-local-host/run-wan-14b-proof.py

# options: --cell <id> --seed <n> --lora high+low --steps 4 --split 2
python helpers/wan-local-host/run-wan-14b-proof.py --cell implied-embrace-bed --lora lightx2v --steps 4 --split 2
```

Outputs go under `artifacts/tmp/wan-proof/` (git-ignored); this folder is the committed harness only.
Results get mirrored into the B-113 `poc-log.md` after visual review.

Related: `specs/Planning/B-113-scene-video-production/plan.md` + `poc-log.md`; `helpers/flux-local-host/`
(image proof precedent); `docs/local-comfyui-model-manager-setup.md`.
