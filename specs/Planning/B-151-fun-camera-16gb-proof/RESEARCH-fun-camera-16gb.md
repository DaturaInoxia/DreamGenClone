# RESEARCH — Wan 2.2 Fun-Camera on 16GB (B-151)

> Proof session executed 2026-10-04 against `WOOD-GAME-MAIN` (RTX 5080 16GB, ComfyUI 0.37.1).
> Companion to `README.md` (the plan) and `../B-148-location-creation-from-moment/OPTIONS-consistent-location.md` Option I.

## Verdict

| gate | outcome |
|---|---|
| **A — VRAM feasibility** | **PASS (conditional).** `wan2.2_fun_camera_14B` does NOT fit at fp8 (~20GB peak). The QuantStack **Q4_K_M GGUF** (9.94GB/model) fits at 480×480×25 frames with a peak of **15.75–15.95GB** — but only for the **translation** presets. |
| **B — consistency** | **PASS.** Camera move is obeyed and temporally consistent: smooth, monotonic drift with stable consecutive-frame deltas on both the synthetic room and a real location image. |
| **Rotation presets (CW/ACW)** | **Thrash on 16GB** at Q4_K_M 480×480×25 (peak intermittently exceeds the card → GPU-memory thrashing, >38 min vs 70 s). |
| **Identity** | **Not preserved** (as designed). Extracted frames are a generated camera path, not the source photo's true other angles. |

**Overall: GO with constraints.** The model runs on 16GB and produces consistent camera-move frames. Use **Q4_K_M GGUF + 4-step LightX2V LoRA + 480×480×25 + a translation preset** (70 s/clip). For true camera *rotation*, either drop to a smaller quant (Q3_K_M / Q2_K) or lower the resolution — do not use rotation at Q4_K_M on a 16GB card.

## The decisive find (Phase A4 answered)

A ready-made low-bit quant **exists**: [`QuantStack/Wan2.2-Fun-A14B-Control-Camera-GGUF`](https://huggingface.co/QuantStack/Wan2.2-Fun-A14B-Control-Camera-GGUF)
(HighNoise + LowNoise, Q2_K → Q8_0). Verified this session:

- The GGUF **contains the camera-control adapter** (`control_adapter.conv.*`, `control_adapter.residual_blocks.*`).
- ComfyUI core model detection (`comfy/model_detection.py`) keys off `control_adapter.conv.weight` → `model_type = camera_2.2` (Wan 2.2 Fun-Camera), so `WanCameraEmbedding` / `WanCameraImageToVideo` work through `UnetLoaderGGUF` (city96 ComfyUI-GGUF).

## Working config (record this)

| item | value |
|---|---|
| models | `Wan2.2-Fun-A14B-Control-Camera-{High,Low}Noise-Q4_K_M.gguf` (9.94GB each, in `models/unet`) |
| text encoder | `umt5_xxl_fp8_e4m3fn_scaled.safetensors` |
| vae | `wan_2.1_vae.safetensors` |
| LoRA (fast path) | `Wan2.2_LightX2V_{high,low}_n54vv.safetensors`, strength 1.0 |
| loader | `UnetLoaderGGUF` (requires ComfyUI-GGUF custom node, `gguf>=0.13`) |
| sampling | 2-pass: high 0→2, low 2→4, `euler`/`simple`, cfg 1.0, ModelSamplingSD3 shift 8 |
| resolution / length | 480×480, 25 frames (7 latent frames) |
| camera presets that fit | `Pan Left`, `Pan Right`, `Zoom In/Out`, `Static` (translation) |
| camera presets that thrash | `ClockWise (CW)`, `Anti Clockwise (ACW)` (rotation) |

## VRAM / time matrix (measured, 480×480×25)

| config | peak VRAM | time |
|---|---|---|
| single-pass high-noise, 4 steps (load test) | 15.75 GB | 34.6 s |
| 2-pass, 20 steps, no LoRA (`Pan Left`) | 16.68 GB | 556 s |
| **2-pass, 4-step LightX2V LoRA (`Pan Left`)** | **15.95 GB** | **70 s** |
| 2-pass, 20 steps, rotation (`ClockWise`) | >16 GB (thrash) | >1500 s, not completed |

## Phase B evidence

**Synthetic room** (`room.py`, wall-colour classifier): `Pan Left` from the yaw-0 red wall produced a smooth, monotonic wall-share change (red 0.347→0.255, green 0.000→0.104, blue 0.089→0.034) across 25 frames — a controlled lateral move, no re-rolled room.

**Real image** (`Maintenance Shed` / "Indoor Back", id `1fb98a0edf4b4bb7a744d187519841f8`, 1024×1024):

| metric | value |
|---|---|
| frame 1 vs source (mean abs diff) | 22.0 / 255 |
| consecutive-frame delta | ~9.1–10.2 (stable) |
| cumulative drift vs frame 1 | 0 → 40.2 (monotonic) |

Frames and sheet: `artifacts/tmp/fun-camera-proof/frames_indoor_panleft/` and `sheet_indoor_panleft.png`.

## What must NOT be claimed

- ❌ "It preserves the identity of the real room." It is generation — the extracted frames are a generated camera path, not the photo's true angles.
- ❌ "Frames are ready to use without review." Each extracted frame still needs approval + naming (B-148 FR-B148-05/06).

## Notes for the eventual producer (B-148 Option I)

- Use the **4-step LoRA** path — it is ~8× faster than the 20-step path and is the documented fast config.
- ComfyUI-GGUF is now installed on the host (node cloned + `gguf 0.19.0` in `D:\ComfyUI\.venv`); the models live in `D:\ComfyUI\models\unet`.
- A 16GB card has ~1GB headroom at this config; free the desktop/app ComfyUI VRAM before a run (translation fits, rotation does not).
- The proof instance used port 8189 with `--user-directory D:\ComfyUI\user8189`; the operator's main instance (8188) was untouched.
