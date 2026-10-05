# Research — MiniMax "M3" text-to-video for local ComfyUI (Option I)

> External research, 2026-10-04. Source URLs accompany every material claim. This corrects the
> model name in `OPTIONS-consistent-location.md` Option I and answers "will it work on my local
> ComfyUI."

---

## 1. The name is wrong — there is no "MiniMax M3" video model

**MiniMax-M3 is a ~428B-parameter multimodal *language* model** (text/image/video *understanding*,
~23B active, 1M context) — not a video generator.

- `MiniMax-AI/MiniMax-M3:README.md` — "a native multimodal model … ~428B parameters and ~23B
  activated parameters." https://github.com/MiniMax-AI/MiniMax-M3 , https://arxiv.org/abs/2606.13392
- MiniMax's **video** line is the **H series**: Hailuo 01 → Hailuo 02 → **H3** (= "Hailuo 03").
  https://www.minimax.io/blog/minimax-h3
- ComfyUI's `MinimaxHailuo03TextToVideoNode` docs: "generates a video … using the MiniMax H3 family:
  MiniMax H3, MiniMax H3 Max, MiniMax H3 Max Turbo."
  https://github.com/Comfy-Org/docs/blob/main/built-in-nodes/MinimaxHailuo03TextToVideoNode.mdx

**Verdict:** the intended model is **MiniMax H3 / Hailuo 03**. Drop "M3".

---

## 2. Can it run locally? Partly — and not for the goal

### What this host actually has (verified 2026-10-04, WOOD-GAME-MAIN @192.168.0.11)

| node | kind | schema |
|---|---|---|
| `MiniMaxH3ImageToVideo` | **local, open weights** | `clip, vae, prompt, w/h/length` + opt `first_frame, last_frame` |
| `MiniMaxH3ReferenceToVideo` | **local, open weights** | `clip, prompt, …` + `ref_images, ref_videos` |
| `MiniMaxH3AddGuide` / `MiniMaxH3FunControlNetApply` / `MiniMaxH3SigmaShift` / `EmptyMiniMaxH3LatentAV` | local | — |
| `MinimaxHailuo03TextToVideoNode` | **API** (official cloud) | `model, seed, watermark` |
| `ComfyCloudMiniMaxH3TextToVideoNode` | **API** (ComfyCloud partner) | `prompt, seed, aspect_ratio, resolution, duration_seconds` |
| `MinimaxTextToVideoNode` / `MinimaxImageToVideoNode` | **API** (older wrappers) | — |

**Key nuance:** the *local* MiniMax H3 nodes on this host are **image-to-video and
reference-to-video only**. Text-to-video MiniMax is exposed **only through API nodes**, and no
MiniMax API key is configured (`comfy.settings.json` has none, verified 2026-10-04). **No MiniMax H3
weights are downloaded** (disk inventory: none).

### Even downloaded, H3 is a poor fit for the goal

- **Size:** a 33B DiT + 32B text-encoder system. Official SGLang example uses **4 GPUs**
  (`--num-gpus 4`). Community VRAM tiers: 24GB comfortable; 12–16GB only via heavy quantisation
  (`Q4_K_M` GGUF 10.64 GiB + `Q2_K` encoder 7.91 GiB + fp8 VAE) + offload → slow.
  https://github.com/MiniMax-AI/awesome-minimax-h3-integration
- **No camera-pose/trajectory/intrinsics control.** The official ComfyUI node has zero camera
  parameters (verified by searching `Comfy-Org/ComfyUI:comfy_api_nodes/nodes_minimax.py` for
  `camera|trajectory|pose|intrinsic` → no matches). H3's only "camera move" is **reference-video
  imitation + natural language** — not the explicit pose control the operator wants.
  https://docs.comfy.org/tutorials/video/minimax/minimax-h3
- **Licensing:** H3 open weights use a custom **Community License**; commercial use requires a paid
  MiniMax license via Comfy. Contrast Wan 2.2 = **Apache 2.0**.
  https://github.com/MiniMax-AI/MiniMax-H3

---

## 3. The model that actually does what was asked — Wan 2.2 Fun-Camera

**This is the correct local tool for explicit camera-move video.** It is a *fine-tune on Wan 2.2*
(not the base T2V/I2V/TI2V weights — the base `Wan-Video/Wan2.2` repo ships no camera module).

- Model: `wan2.2_fun_camera_high_noise_14B` + `wan2.2_fun_camera_low_noise_14B` (fp8_scaled), from
  Alibaba PAI / WanVideoFun / VideoX-Fun, repackaged by Comfy-Org.
  https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged
- **Genuine camera intrinsics + trajectory**: `WanCameraEmbedding` builds rotation/translation,
  computes **`fx/fy/cx/cy`**, and renders **Plücker ray embeddings** (Sitzmann et al. 2021),
  adapted from CameraCtrl. Source: `Comfy-Org/ComfyUI:comfy_extras/nodes_camera_trajectory.py`;
  technique origin https://github.com/hehao13/CameraCtrl
- **Image-to-video with a starting frame**: `WanCameraImageToVideo` takes `start_image`,
  `camera_conditions`, `length`. Source: `Comfy-Org/ComfyUI:comfy_extras/nodes_wan.py` (~line 403).
- Injection via a dedicated `WanCamAdapter` (`in_dim_control_adapter=24`):
  `x = x + self.control_adapter(camera_conditions)`.
  https://github.com/Comfy-Org/ComfyUI/blob/master/comfy/ldm/wan/model.py

### The VRAM problem — decisive for this machine

Official ComfyUI test on an **RTX 4090D 24GB**, 640×640, 81 frames: `fp8_scaled` = **84% VRAM**
(~20GB peak); with 4-step LoRA = 89%.
https://docs.comfy.org/tutorials/video/wan/wan2-2-fun-camera

**On the 16GB RTX 5080 it will not fit at fp8.** It needs a low-bit quant of the 14B Fun-Camera
model (no dedicated GGUF confirmed — unverified) + block-swap offload, and will be slow.

### What IS comfortably local today on 16GB

- **Wan2.2-TI2V-5B** (`wan2.2_ti2v_5B_fp16`, already on disk) — "should fit well on 8GB VRAM with
  ComfyUI native offloading." https://docs.comfy.org/tutorials/video/wan/wan2_2 — but it has **no
  camera-adapter support**, so camera moves are **text-prompted only** ("pan right", "dolly in"),
  not pose-controlled.
- **Wan 2.2 I2V High/Low R1** (already on disk) — same text-prompted camera, image-conditioned.

---

## 4. Frame extraction → stills (Q5)

- Native output resolution is ~1MP: Wan 2.2 T2V/I2V-A14B at 1280×720; TI2V-5B at 1280×704;
  MiniMax H3 at 768p short edge. Acceptable for location-reference images, below print grade.
- No authoritative "extract-frame-as-reference" standard found. Established practice (general
  knowledge, not a cited standard): decode frames (SaveVideo / VideoHelperSuite / ffmpeg) → pick
  the sharpest → **upscale 2–4× (Real-ESRGAN / UltimateSDUpscale / SUPIR / Topaz)** before use.

---

## 5. Direct answer

| question | answer |
|---|---|
| Is there a "MiniMax M3" video model? | **No.** M3 is a language model. The video line is Hailuo 01/02/**H3**. |
| Will MiniMax T2V work on local ComfyUI? | **Text-to-video MiniMax on this host is API-only** and no key is configured. Local H3 is I2V/reference-to-video only, needs ~100GB download, 12–16GB only via heavy quant + offload (slow), custom commercial license, and **no camera-pose control**. |
| Which local model gives the camera control the goal needs? | **Wan 2.2 Fun-Camera (14B)** — true intrinsics + trajectory + I2V start frame, Apache 2.0. But ~20GB peak at 640×640 → **marginal/impossible at 16GB without a low-bit quant** (unverified to exist). |
| What is usable on this machine today? | **Wan2.2-TI2V-5B / I2V (on disk) with text-prompted camera moves** — fits, but no true pose control. |

**Bottom line:** the operator's idea is right — a camera-move video is the strongest *consistency*
route — but **"MiniMax M3" is not the tool**. The honest local path is **Wan 2.2**: the 5B/I2V
models already on disk for text-prompted moves today, or Fun-Camera-14B (download + likely
quantisation) for explicit reproducible camera trajectories. Neither preserves *identity* — see
`FINDINGS-location-images.md` — so this serves **RP-invented** locations, not a real photographed
room.

---

## 6. Uncertainties (flagged, not resolved)

- Which Hailuo release first added text "camera movement" — press cites Hailuo 2.3 / Hailuo 02;
  no MiniMax primary source found. **Unverified.**
- Existence of a ready-made low-bit (GGUF) quant of `wan2.2_fun_camera_14B` for 16GB cards.
  **Unverified.**
- Exact H3 peak VRAM on a Blackwell RTX 5080 (Sol-Attn is reported to cut MLP peak VRAM ~37%, but
  no 5080-specific number). **Unverified.**
- HunyuanVideo / LTX-Video / Mochi 1 lack official camera-trajectory control — general knowledge,
  not independently verified.
- Whether a local `MiniMaxH3TextToVideo` node exists in a newer ComfyUI than this host's 0.37.1 —
  not confirmed.

---

## 7. Sources

- https://github.com/MiniMax-AI/MiniMax-M3 · https://arxiv.org/abs/2606.13392
- https://www.minimax.io/blog/minimax-h3
- https://github.com/MiniMax-AI/MiniMax-H3 · https://huggingface.co/MiniMaxAI/MiniMax-H3
- https://github.com/MiniMax-AI/awesome-minimax-h3-integration
- https://docs.comfy.org/tutorials/video/minimax/minimax-h3
- https://docs.comfy.org/tutorials/video/minimax/minimax-h3-fun-controlnet
- https://docs.comfy.org/tutorials/video/wan/wan2_2
- https://docs.comfy.org/tutorials/video/wan/wan2-2-fun-camera
- https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged
- https://github.com/Comfy-Org/ComfyUI/blob/master/comfy_extras/nodes_camera_trajectory.py
- https://github.com/Comfy-Org/ComfyUI/blob/master/comfy_extras/nodes_wan.py
- https://github.com/Comfy-Org/ComfyUI/blob/master/comfy/ldm/wan/model.py
- https://github.com/Comfy-Org/ComfyUI/blob/master/comfy_api_nodes/nodes_minimax.py
- https://github.com/Comfy-Org/ComfyUI/blob/master/comfy_api_nodes/nodes_comfy_cloud.py
- https://github.com/Wan-Video/Wan2.2
- https://github.com/hehao13/CameraCtrl
