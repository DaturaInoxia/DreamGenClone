# Identity Conditioning Proof — Host Inventory

**Date:** 2026-08-26
**Pod:** `7i2mutjmry5tkt` (`dreamgen-identity-proof`) — RUNNING
**Purpose:** isolated host for the Section C identity-conditioning proof (IP-Adapter vs PuLID +
ControlNet pose/layout + regional masks). Not registered in Model Manager; production hosts untouched.

## Host

| Field | Value |
|---|---|
| Image | `runpod/stable-diffusion:comfy-ui-6.0.0` |
| GPU | NVIDIA A40, 1× (46,068 MiB visible, 47.7 GB total) |
| Datacenter | EU-SE-1 (SECURE, non-interruptible) |
| vCPU / RAM | 9 vCPU / 50 GB |
| ComfyUI | v0.3.10, `main.py --listen --port 3000` |
| Python | 3.10.12 |
| PyTorch | 2.6.0+cu124 |
| ComfyUI HTTP | `https://7i2mutjmry5tkt-3000.proxy.runpod.net` |
| SSH | `root@194.68.245.149:22065` (ed25519 key, account-level) |
| Cost | $0.44/hr |

## Storage

- Container disk `/` (overlay): 20 GB, ~125 MB used — ComfyUI lives here.
- Persistent volume `/workspace`: 85 GB network FS (`mfs`), currently **empty except `/workspace/comfyui`**.
- Base image pre-downloads 38 GB of checkpoints into the image layers (not the writable overlay).

## ComfyUI base image — checkpoints present

`flux1-schnell-fp8.safetensors`, `sd_xl_base_1.0.safetensors`, `sd_xl_refiner_1.0.safetensors`,
`v1-5-pruned-emaonly.safetensors`, `v2-1_768-ema-pruned.ckpt`.

**`juggernautXL_ragnarok.safetensors` is NOT present** — must be copied from the production pod's
volume or re-downloaded.

## Custom nodes present (base image)

Only `ComfyUI-Manager` (+ the standard `example_node.py.example`, `websocket_image_save.py`).

## Node presence check (`/object_info`)

| Node | Present | Needed by |
|---|---|---|
| `ControlNetLoader`, `ControlNetApplyAdvanced` | ✅ (core) | pose/layout control |
| `CLIPVisionLoader`, `VAELoader`, `UNETLoader` | ✅ (core) | IP-Adapter / general |
| `IPAdapterUnifiedLoader`, `IPAdapterApply` | ❌ | IP-Adapter candidate |
| `PuLID*` | ❌ | PuLID candidate |
| `DWPreprocessor` | ❌ | OpenPose extraction |
| Impact Pack regional nodes | ❌ | multi-actor masks |

## Python packages missing

`insightface` (PuLID antelopev2), `onnxruntime` (face/pose preprocessing), `controlnet_aux` /
`opencv` (DWPose). None installed on the base image.

## Empty model folders (all need populating)

`clip/`, `clip_vision/`, `controlnet/`, `loras/`, `vae/`, `text_encoders/`, `unet/`,
`embeddings/`, `upscale_models/` — all 0 bytes. Only `checkpoints/` (base SD/Flux set) is non-empty.
