# DWPose Serverless worker (B-101, infra-proof first migration).
#
# Faithful to helpers/runpod/deployments/pose-dwpose/provision-runtime.sh:
#   - pinned comfyui_controlnet_aux e8b689a (v1.1.5)
#   - validated DWPose Python deps (no onnxruntime)
#   - DWPose ckpts (dw-ll_ucoco_384_bs5 + yolox_l)
# Unlike the pod, we do NOT need the Blackwell (sm_120) cu128 torch build —
# the endpoint uses a non-Blackwell GPU (see endpoints.json candidateGpuTypeIds),
# so the base image's stock torch is fine. That deletes a whole class of drift.
#
# Base: the same ComfyUI 0.6.0 image the pod uses (ComfyUI at /ComfyUI).
FROM runpod/stable-diffusion:comfy-ui-6.0.0

# Serverless worker SDK.
RUN pip install --no-cache-dir runpod

# [1/6] pinned comfyui_controlnet_aux (must be inside /ComfyUI/custom_nodes).
ARG PINNED_AUX=e8b689a513c3e6b63edc44066560ca5919c0576e
RUN mkdir -p /ComfyUI/custom_nodes \
 && git clone --quiet https://github.com/Fannovel16/comfyui_controlnet_aux /ComfyUI/custom_nodes/comfyui_controlnet_aux \
 && git -C /ComfyUI/custom_nodes/comfyui_controlnet_aux checkout --quiet "$PINNED_AUX"

# [3/6] validated DWPose Python deps (no onnxruntime).
RUN pip install --quiet \
      opencv-python-headless==4.10.0.84 \
      matplotlib==3.10.9 \
      scikit-image==0.25.2

# [DWPose ckpts] BAKED into the image so first-run never downloads at runtime.
# The aux node resolves ckpts at <repo-root>/ckpts/<hf_repo_id>/<filename> (see
# src/custom_controlnet_aux/util.py custom_hf_download: local_dir = ckpts/<repo_id>).
# For DWPreprocessor (node_wrappers/dwpose.py) with our workflow inputs:
#   bbox_detector  yolox_l.torchscript.pt              -> ckpts/hr16/yolox-onnx/
#   pose_estimator dw-ll_ucoco_384_bs5.torchscript.pt  -> ckpts/hr16/DWPose-TorchScript-BatchSize5/
# sha256 = the pod's verified files (identity model manifest). A runtime download
# HANGS the workflow (observed "workflow did not finish in time" on the 0.3.0 image).
# Also disable hf_transfer: the base image sets HF_HUB_ENABLE_HF_TRANSFER=1 but does
# not ship the package, so ANY hf_hub_download crashes with a ValueError.
ENV HF_HUB_ENABLE_HF_TRANSFER=0
ARG AUX_CKPTS_ROOT=/ComfyUI/custom_nodes/comfyui_controlnet_aux/ckpts
RUN mkdir -p "$AUX_CKPTS_ROOT/hr16/DWPose-TorchScript-BatchSize5" "$AUX_CKPTS_ROOT/hr16/yolox-onnx" \
 && curl -fsSL -o "$AUX_CKPTS_ROOT/hr16/DWPose-TorchScript-BatchSize5/dw-ll_ucoco_384_bs5.torchscript.pt" \
      "https://huggingface.co/hr16/DWPose-TorchScript-BatchSize5/resolve/main/dw-ll_ucoco_384_bs5.torchscript.pt" \
 && echo "d86a0b2b59fddc0901a7076e9f59c9f8602602133ed72511c693fd11eea23d91  $AUX_CKPTS_ROOT/hr16/DWPose-TorchScript-BatchSize5/dw-ll_ucoco_384_bs5.torchscript.pt" | sha256sum -c - \
 && curl -fsSL -o "$AUX_CKPTS_ROOT/hr16/yolox-onnx/yolox_l.torchscript.pt" \
      "https://huggingface.co/hr16/yolox-onnx/resolve/main/yolox_l.torchscript.pt" \
 && echo "80bc14b13c260c24b3014cd42c02994bf52296ab8fa2d80a60b6afe08c93ef42  $AUX_CKPTS_ROOT/hr16/yolox-onnx/yolox_l.torchscript.pt" | sha256sum -c -

# Handler entrypoint. ComfyUI is started lazily by the handler (per-worker, cached).
COPY serverless-worker.py /serverless-worker.py
WORKDIR /ComfyUI
CMD ["python", "/serverless-worker.py"]
