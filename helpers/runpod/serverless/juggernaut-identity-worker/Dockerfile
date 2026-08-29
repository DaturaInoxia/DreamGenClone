# Juggernaut worker (also identity-capable): stock runpod/worker-comfyui base + ComfyUI_IPAdapter_plus.
#
# This is the worker for the img-juggernaut-serverless endpoint. It serves BOTH plain
# SDXL/Juggernaut T2I AND identity-conditioned renders (IP-Adapter regional, single- or
# multi-character) from ONE endpoint. No models are baked here — the checkpoint
# (juggernautXL_ragnarok.safetensors), IP-Adapter (models/ipadapter/) and CLIP-vision
# (models/clip_vision/) files all live on the Network Volume xkslgh6xo0 at /runpod-volume/models/*,
# which the official worker-comfyui base auto-detects.
#
# IPAdapterUnifiedLoader "PLUS FACE (portraits)" resolves:
#   - /runpod-volume/models/ipadapter/ip-adapter-plus-face_sdxl_vit-h.safetensors
#   - /runpod-volume/models/clip_vision/CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors
#
# DEPLOYMENT: RunPod GitHub Integration ("Start from GitHub Repo", repo DaturaInoxia/DreamGenClone,
# branch master) — RunPod clones this repo, builds this Dockerfile, and updates the endpoint on
# every push (a GitHub RELEASE is required for the rebuild to apply). See docs/deployment.md Method 2.
#
# Proof of this image: helpers/runpod/serverless/run-all-proofs.ps1 (identity-2char proof = the
# matrix c1 workflow + identity-2char-c1.images.json through endpoint img-juggernaut-serverless).
FROM runpod/worker-comfyui:5.8.4-base

# IP-Adapter custom node (regional identity conditioning). comfy-node-install is the official tooling
# in the worker-comfyui base image. NOTE: the argument is the COMfy REGISTRY package name
# (comfyui_ipadapter_plus), NOT the GitHub repo name (ComfyUI_IPAdapter_plus) - using the repo name
# installs nothing (verified: node 'IPAdapterUnifiedLoader' not found, 500 on /prompt, 2026-08-28).
RUN comfy-node-install comfyui_ipadapter_plus
