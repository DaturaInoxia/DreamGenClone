# Identity Conditioning Proof — Model Manifest

**Date:** 2026-08-26
**Pod:** `7i2mutjmry5tkt` — all models on `/workspace/comfyui/models` (persistent volume).

## Checkpoints

| File | Bytes | SHA-256 |
|---|---|---|
| `checkpoints/juggernautXL_ragnarok.safetensors` | 7,105,350,162 | `dd08fa32f98d05a2443ca1419e46df1575a0811f6e3b246d9dd47ff20f5eb66a` |

Copied byte-for-byte from the production pod (`emqmxptqdxu7pp`); SHA-256 matches production exactly.

## IP-Adapter (candidate 1)

| File | Bytes | SHA-256 |
|---|---|---|
| `ipadapter/ip-adapter_sdxl_vit-h.safetensors` | 698,391,064 | `ebf05d918348aec7abb02a5e9ecef77e0aaea6914a5c4ea13f50d45eb1681831` |
| `ipadapter/ip-adapter-plus-face_sdxl_vit-h.safetensors` | 847,517,512 | `677ad8860204f7d0bfba12d29e6c31ded9beefdf3e4bbd102518357d31a292c1` |
| `clip_vision/CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors` | 2,528,373,448 | `6ca9667da1ca9e0b0f75e46bb030f7e011f44f86cbfb8d5a36590fcd7507b030` |

## PuLID (candidate 2)

| File | Bytes | SHA-256 |
|---|---|---|
| `pulid/ip-adapter_pulid_sdxl_fp16.safetensors` | 791,372,856 | `b258a5cd2c0c7d35f4d3e59909d7648498686afae4d0c74917e9c47d7704fbf5` |
| `insightface/models/antelopev2/*.onnx` (5 files) | — | `df5c06b8…`, `f001b856…`, `4fde69b1…`, `4ab1d643…`, `5838f7fe…` |

## ControlNet (pose/layout — B-097)

| File | Bytes | SHA-256 |
|---|---|---|
| `controlnet/controlnet-openpose-sdxl-1.0.safetensors` (OpenPoseXL2) | 5,004,167,829 | `5a4b928cb1e93748217900cb66d4135bf70d932d2924232f925910fad9e43a92` |
| `controlnet/controlnet-depth-sdxl-1.0.safetensors` (fp16) | 2,502,139,134 | `66a6813e6bd7270ecfe68206a59ddd605a011ae85321188376605c66e0a4f303` |

## DWPose preprocessor (in `comfyui_controlnet_aux/ckpts/dwpose`)

| File | SHA-256 |
|---|---|
| `dw-ll_ucoco_384_bs5.torchscript.pt` | `d86a0b2b59fddc0901a7076e9f59c9f8602602133ed72511c693fd11eea23d91` |
| `yolox_l.torchscript.pt` | `80bc14b13c260c24b3014cd42c02994bf52296ab8fa2d80a60b6afe08c93ef42` |

## Model discovery verified via `/object_info`

`juggernautXL_ragnarok.safetensors` in CheckpointLoaderSimple; IPAdapter presets (STANDARD/PLUS/
PLUS FACE); `ip-adapter_pulid_sdxl_fp16.safetensors` in PulidModelLoader; both ControlNets in
ControlNetLoader; `CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors` in CLIPVisionLoader.
