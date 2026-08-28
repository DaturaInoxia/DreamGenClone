# DWPose Serverless worker (B-101) - REBUILT on the OFFICIAL RunPod worker-comfyui base.
#
# Pivot decision (2026-08-28, user "are we doing it properly?"): use RunPod's maintained
# `runpod-workers/worker-comfyui` (AGPL-3.0) as the base instead of the pod image +
# hand-rolled handler. Official base gives: standard API contract
# (input.workflow + input.images -> output.images), WebSocket completion, SSH debug via
# PUBLIC_KEY, native network-volume support. Our hand-rolled serverless-worker.py is
# deleted (the base image provides the handler + ComfyUI startup).
#
# This image = official -base + pinned comfyui_controlnet_aux + DWPose deps + baked ckpts.
# Network-volume integration DEFERRED (user: "we will look at this after, we may not need it").
#
# Reference: https://github.com/runpod-workers/worker-comfyui/blob/main/docs/customization.md
# (Method 1: custom Dockerfile FROM runpod/worker-comfyui:<version>-base).
FROM runpod/worker-comfyui:5.8.7-base

# [1] pinned comfyui_controlnet_aux (same commit verified on the pod + 0.5.0).
ARG PINNED_AUX=e8b689a513c3e6b63edc44066560ca5919c0576e
RUN mkdir -p /comfyui/custom_nodes \
 && git clone --quiet https://github.com/Fannovel16/comfyui_controlnet_aux /comfyui/custom_nodes/comfyui_controlnet_aux \
 && git -C /comfyui/custom_nodes/comfyui_controlnet_aux checkout --quiet "$PINNED_AUX"

# [2] validated DWPose Python deps (no onnxruntime; OpenCV-CPU).
RUN pip install --quiet \
      opencv-python-headless==4.10.0.84 \
      matplotlib==3.10.9 \
      scikit-image==0.25.2

# [3] DWPose ckpts BAKED into the aux layout custom_hf_download checks:
#     <repo-root>/ckpts/<hf_repo_id>/<filename>. sha256 = pod-verified.
#     Base does not set hf_transfer, but keep it off defensively.
ENV HF_HUB_ENABLE_HF_TRANSFER=0
ARG AUX_CKPTS_ROOT=/comfyui/custom_nodes/comfyui_controlnet_aux/ckpts
RUN mkdir -p "$AUX_CKPTS_ROOT/hr16/DWPose-TorchScript-BatchSize5" "$AUX_CKPTS_ROOT/hr16/yolox-onnx" \
 && curl -fsSL -o "$AUX_CKPTS_ROOT/hr16/DWPose-TorchScript-BatchSize5/dw-ll_ucoco_384_bs5.torchscript.pt" \
      "https://huggingface.co/hr16/DWPose-TorchScript-BatchSize5/resolve/main/dw-ll_ucoco_384_bs5.torchscript.pt" \
 && echo "d86a0b2b59fddc0901a7076e9f59c9f8602602133ed72511c693fd11eea23d91  $AUX_CKPTS_ROOT/hr16/DWPose-TorchScript-BatchSize5/dw-ll_ucoco_384_bs5.torchscript.pt" | sha256sum -c - \
 && curl -fsSL -o "$AUX_CKPTS_ROOT/hr16/yolox-onnx/yolox_l.torchscript.pt" \
      "https://huggingface.co/hr16/yolox-onnx/resolve/main/yolox_l.torchscript.pt" \
 && echo "80bc14b13c260c24b3014cd42c02994bf52296ab8fa2d80a60b6afe08c93ef42  $AUX_CKPTS_ROOT/hr16/yolox-onnx/yolox_l.torchscript.pt" | sha256sum -c -

# The official base image provides the handler entrypoint + ComfyUI startup. No CMD/COPY.
