# RESEARCH — Wan 2.2 Fun-Camera on 16GB (B-151) + the working alternatives

> Proof session executed 2026-10-04/05 against `WOOD-GAME-MAIN` (RTX 5080 16GB, ComfyUI 0.37.1).
> Companion to `README.md` (the plan) and `../B-148-location-creation-from-moment/OPTIONS-consistent-location.md`.

## Verdict (corrected — supersedes any earlier "inert camera" wording)

| gate | outcome |
|---|---|
| **A — VRAM feasibility** | **PASS.** `wan2.2_fun_camera_14B` does NOT fit 16GB at fp8 (~20GB peak). The QuantStack **Q4_K_M GGUF** (9.94GB/model) loads and runs at 512p / 25–81 frames (peak ~15.8–16.9GB; 81-frame via `--lowvram` offload). |
| **B — camera move** | **PASS (works).** Translation (pan/zoom), roll, and yaw all move the image once the correct LoRA is used and a real yaw preset exists. See "What was actually wrong" below. |
| **C — content invention** | **FAIL.** The model only **warps the provided start image** along the camera path. Content outside the start image (the unseen walls) is **garbage** — a blurry smear, not a coherent room. A 360° spin re-anchors to the start wall after ~90° (wall-colour classifier: red→blue→red, never green). No prompt fixes this. |
| **Identity** | **Not preserved** (as designed). |

**Overall: NO-GO for B-148 Option I** ("camera-move → extracted location frames"), because Option I's entire purpose is to *invent* the walls the camera can't see — and that is the one thing Fun-Camera structurally cannot do. The camera path is real; the hallucinated content is not usable.

## What was actually wrong (and fixed) during the session

1. **Wrong on-disk LoRA.** `Wan2.2_LightX2V_{high,low}_n54vv.safetensors` (1.16GB) was not the official LoRA. Official `wan2.2_i2v_lightx2v_4steps_lora_v1_{high,low}_noise.safetensors` (1.14GB) restored camera adherence (roll rotation went 45° → ~90° at speed 2). The early "camera is inert / speed doesn't scale" conclusion was **an artifact of this**, not a real property.
2. **No yaw preset existed.** The 9 stock camera presets are translation (Pan/Zoom) and **roll** (CW/ACW = Rz tilt — the "slanted image" the operator saw). A true **yaw** (turn-to-face-another-wall) had to be added to `CAMERA_DICT` in `comfy_extras/nodes_camera_trajectory.py`:
   ```
   "Yaw Left":  {"angle":[0., 1., 0.], "T":[0.,0.,0.]},
   "Yaw Right": {"angle":[0.,-1., 0.], "T":[0.,0.,0.]},
   ```
   plus the same two names added to the `WanCameraEmbedding` schema combo (the combo list is hardcoded, not derived from `CAMERA_DICT`). After this, a 90° yaw reveals a genuinely **new** wall.

## What actually works for the goal (B-148 location backdrops)

Two mechanisms were proven on 16GB:

### Mechanism 1 — 360° panorama → 4 walls (works, but NOT the chosen path)

Generate **one equirectangular 360°** then unwrap to 4 cube faces (F/R/B/L = 0/90/180/270°). F and B are exactly 180° apart (the two characters' opposite backdrops); R and L are the 90° side views (omniscient backdrop). Geometrically consistent by construction.

- **Diffusion360** (SD2.1, Apache-2.0, official `Diffusion360_ComfyUI` plugin): text→360 and image→360. Ran on 16GB (~6s for 1024×512). Had to patch the 2023-era plugin for the modern stack (diffusers 0.40 `ControlNetOutput` import path moved; made the RealESRGAN/basicsr SR stage optional so the base path runs without them). Also: the plugin's pinned `numpy==1.23.5`/`diffusers==0.26` are **incompatible with the RTX 5080's torch 2.14** — do not install its requirements.txt verbatim.
- **SDXL + 360Redmond LoRA** (`360Redmond_sdxl_v1.safetensors`, Civitai 118025, ~891MB full file): text→360 at 1600×800 / 2048×1024 (~18–43s), trigger `360 View`. Note: Civitai truncates the download unless retried — verify the safetensors header ends inside the file (the first grab was 29MB of 891MB).
- Unwrap: `py360convert.e2c(..., cube_format='dict')` → `F/R/B/L` faces, or the `PanoramaRectify` node in `ComfyUI-360Panoramas`.

### Mechanism 2 — fal Qwen-Image-Edit-2511-Multiple-Angles-LoRA (THE CHOSEN PATH)

`fal/Qwen-Image-Edit-2511-Multiple-Angles-LoRA` (Apache-2.0, 63k downloads). 96 camera poses (4 elevations × 8 azimuths × 3 distances), trained on 3000+ Gaussian-splatting renders.

- Loads via `LoraLoaderModelOnly` on the 2511 diffusion model `qwen_image_edit_2511_fp8mixed.safetensors` (UNETLoader) + CLIP `qwen_2.5_vl_7b_fp8_scaled.safetensors` + VAE `qwen_image_vae.safetensors`. **Verified 1680/1680 tensors map onto the local checkpoint** (full compatibility).
- Prompt grammar: **`<sks> [azimuth] [elevation] [distance]`**
  - Azimuth (8): `front view` 0° · `front-right quarter view` 45° · `right side view` 90° · `back-right quarter view` 135° · `back view` 180° · `back-left quarter view` 225° · `left side view` 270° · `front-left quarter view` 315°.
  - Elevation (4): `low-angle shot` -30° · `eye-level shot` 0° · `elevated shot` 30° · `high-angle shot` 60°.
  - Distance (3): `close-up` ×0.6 · `medium shot` ×1.0 · `wide shot` ×1.8 (this is how the camera moves toward/away).
- Measured on host: 8 steps euler_ancestral/beta cfg 1.0, ~27–50s per image (first ~50s includes model load).

**The one operational rule:** the LoRA orbits a **single subject**, so the reference must be an **isolated subject** (tightly cropped/framed). On a full-room photo, "back view" copies the front (no unambiguous subject to orbit) — the 90° sides still work, but 180° fails. Once the subject is isolated (e.g. the workbench cropped out of "Indoor Back"), all four angles generate correctly, including the invented back/sides. **This is the chosen mechanism for generating the location backdrops** (opposite walls + omniscient side views).

## Decision (operator, 2026-10-05)

- **Use Mechanism 2 (fal 2511 multi-angle LoRA) to generate the backdrops**, NOT the 360-panorama route (Mechanism 1). The multi-angle LoRA "does what I need": orbit the subject to get the opposite backdrop (`back view`) and the omniscient side backdrops (`left/right side view`).
- Keep all host tools/scripts — they will be reused (no cleanup).

## Working configs (record)

### Fun-Camera (B-151, historical — not the path forward)
| item | value |
|---|---|
| models | `Wan2.2-Fun-A14B-Control-Camera-{High,Low}Noise-Q4_K_M.gguf` (9.94GB each) |
| LoRA | official `wan2.2_i2v_lightx2v_4steps_lora_v1_{high,low}_noise.safetensors` (NOT `*_n54vv`) |
| text encoder / vae | `umt5_xxl_fp8_e4m3fn_scaled.safetensors` / `wan_2.1_vae.safetensors` |
| sampling | 2-pass high 0→2 / low 2→4, euler/simple, cfg 1.0, shift 8 |
| yaw preset | `Yaw Left` / `Yaw Right` (added to `CAMERA_DICT` + schema combo) |

### 360 panorama (Mechanism 1)
| item | value |
|---|---|
| Diffusion360 | `models/diffusers/diffusion360/sd-base` (SD2.1), 20 steps, 1024×512, `StableDiffusionBlendExtendPipeline`, cfg 7.5, `.to("cuda")` (NOT `enable_model_cpu_offload` — device-mix bug in its custom embedding code) |
| SDXL pano | `juggernautXL_ragnarok.safetensors` + `360Redmond_sdxl_v1.safetensors` @ ~0.85, 2048×1024 / 1600×800, ~32 steps euler/normal cfg 7, trigger `360 View` |
| unwrap | `py360convert.e2c(e, face_w=512/1024, mode='bilinear', cube_format='dict')` |

### Multi-angle (Mechanism 2 — chosen)
| item | value |
|---|---|
| model | `qwen_image_edit_2511_fp8mixed.safetensors` (UNETLoader) |
| CLIP / VAE | `qwen_2.5_vl_7b_fp8_scaled.safetensors` (`qwen_image`) / `qwen_image_vae.safetensors` |
| LoRA | `qwen-image-edit-2511-multiple-angles-lora.safetensors` @ 1.0 via `LoraLoaderModelOnly` |
| graph | LoadImage → FluxKontextImageScale → VAEEncode; TextEncodeQwenImageEditPlus (×2) → FluxKontextMultiReferenceLatentMethod (`index_timestep_zero`); UNETLoader → LoraLoaderModelOnly → ModelSamplingAuraFlow(3.1) → CFGNorm(1) → KSampler → VAEDecode → SaveImage |
| sampling | 8 steps euler_ancestral/beta, cfg 1.0 |
| prompt | `<sks> <azimuth> <elevation> <distance>` |

## Measured numbers (kept for reference)

| run | peak VRAM | time |
|---|---|---|
| Fun-Camera Q4_K_M 480×480×25, 4-step LoRA | 15.95 GB | ~70 s |
| Fun-Camera Q4_K_M 512×512×81, 4-step LoRA (--lowvram) | ~16.6 GB | ~155 s |
| Diffusion360 text→360, 1024×512 | — | ~6 s |
| SDXL 360Redmond 1600×800 / 2048×1024 | — | ~18 s / ~43 s |
| fal multi-angle (per image, 8 steps) | — | ~27–50 s |

## Notes for the eventual producer

- The multi-angle LoRA needs an **isolated subject**; plan a crop/segment step before orbiting.
- The `sks` grammar is the LoRA's own prompt contract (from fal's README, not Qwen's official docs); Qwen-Image-Edit-2511 itself has no native camera API.
- License: Qwen-Image-Edit-2511 and the fal multi-angle LoRA are **Apache-2.0**. (Qwen-Image-2.1 is Qwen Research License — non-commercial — so do not build the commercial path on 2.1.)
- Host tools/scripts live at: `artifacts/tmp/fun-camera-proof/` (local) and `C:\run_*.py`, `D:\ComfyUI\models\...` (host). Host custom changes: yaw presets in `nodes_camera_trajectory.py`, `Diffusion360_ComfyUI` plugin (patched), `py360convert`+`accelerate` in the venv, port 8189 proof instance with `--lowvram` + log redirect.
