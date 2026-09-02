# BigLust worker (also identity-capable): stock runpod/worker-comfyui base + ComfyUI_IPAdapter_plus.
#
# This is the worker for the img-biglust-serverless endpoint. It serves BOTH plain
# SDXL/BigLust T2I AND identity-conditioned renders (IP-Adapter regional, single- or
# multi-character) from ONE endpoint. It is the BigLust counterpart of
# helpers/runpod/serverless/juggernaut-identity-worker/Dockerfile (same pattern, separate image).
#
# No models are baked here — the checkpoint (bigLust_v16.safetensors), IP-Adapter
# (models/ipadapter/) and CLIP-vision (models/clip_vision/) files all live on the Network Volume
# xkslgh6xo0 at /runpod-volume/models/*, which the official worker-comfyui base auto-detects.
# The workflow selects the checkpoint via ckpt_name (bigLust_v16.safetensors) — the image itself
# is checkpoint-agnostic and shared on the volume.
#
# IPAdapterUnifiedLoader "PLUS FACE (portraits)" resolves:
#   - /runpod-volume/models/ipadapter/ip-adapter-plus-face_sdxl_vit-h.safetensors
#   - /runpod-volume/models/clip_vision/CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors
#
# DEPLOYMENT: RunPod GitHub Integration ("Start from GitHub Repo", repo DaturaInoxia/DreamGenClone,
# branch master) — RunPod clones this repo, builds this Dockerfile, and updates the endpoint on
# every push (a GitHub RELEASE is required for the rebuild to apply). See docs/deployment.md Method 2.
# The Dockerfile PATH is what generates the private image slug (registry.runpod.net/...biglust-worker-dockerfile:<sha>),
# so this endpoint MUST be created from this path — NOT from juggernaut-identity-worker/Dockerfile,
# otherwise it would pull the Juggernaut image build.
FROM runpod/worker-comfyui:5.8.4-base

# IP-Adapter custom node (regional identity conditioning). comfy-node-install is the official tooling
# in the worker-comfyui base image. NOTE: the argument is the COMfy REGISTRY package name
# (comfyui_ipadapter_plus), NOT the GitHub repo name (ComfyUI_IPAdapter_plus) - using the repo name
# installs nothing (verified: node 'IPAdapterUnifiedLoader' not found, 500 on /prompt, 2026-08-28).
RUN comfy-node-install comfyui_ipadapter_plus

# worker-comfyui maps /runpod-volume/models/* into ComfyUI with a FIXED folder list (checkpoints,
# clip, clip_vision, configs, controlnet, embeddings, loras, upscale_models, vae, unet) - it does
# NOT include 'ipadapter' because that folder is only registered by the IP-Adapter custom node at
# runtime, AFTER the volume paths are computed. Without it, IPAdapterUnifiedLoader fails with
# 'IPAdapter model not found' (verified 2026-08-28). Fix: symlink /comfyui/models/ipadapter -> the
# volume's ipadapter folder before the base /start.sh boots ComfyUI. Base ENTRYPOINT is
# /opt/nvidia/nvidia_entrypoint.sh + CMD /start.sh, so overriding CMD with this wrapper works.
RUN cat > /start-dreamgen.sh <<'EOF'
#!/bin/bash
set -e
mkdir -p /comfyui/models
if [ ! -e /comfyui/models/ipadapter ] && [ -d /runpod-volume/models/ipadapter ]; then
    ln -s /runpod-volume/models/ipadapter /comfyui/models/ipadapter
    echo "Linked /comfyui/models/ipadapter -> /runpod-volume/models/ipadapter"
fi
exec /start.sh
EOF
RUN chmod +x /start-dreamgen.sh
CMD ["/start-dreamgen.sh"]
