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

# [DWPose ckpts] downloaded at first start if missing (they are small ~200 MB total).
# The aux preprocessor resolves them into its own models dir; see serverless-worker.py
# ensure_ckpts() for the exact URLs + sha256 (reuse the pod's verified files).

# Handler entrypoint. ComfyUI is started lazily by the handler (per-worker, cached).
COPY serverless-worker.py /serverless-worker.py
WORKDIR /ComfyUI
CMD ["python", "/serverless-worker.py"]
