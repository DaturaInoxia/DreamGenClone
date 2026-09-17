# FLUX structural proof worker.
# Uses the official ComfyUI Serverless base and the existing network volume.
# The volume must contain the FLUX base, VAE, text encoders, and XLabs ControlNet.
FROM runpod/worker-comfyui:5.8.4-base

# FLUX-specific ControlNet/IP conditioning nodes. The base ComfyUI worker's
# generic ControlNetLoader is not the XLabs FLUX runtime. x-flux-comfyui is
# installed from the pinned source revision because it is not treated as a
# Comfy Registry package by this worker.
ARG XLABS_FLUX_REV=00328556efc9472410d903639dc9e68a8471f7ac
RUN git clone https://github.com/XLabs-AI/x-flux-comfyui.git /comfyui/custom_nodes/x-flux-comfyui \
	&& git -C /comfyui/custom_nodes/x-flux-comfyui checkout "$XLABS_FLUX_REV" \
	&& python3 -m pip install --no-cache-dir -r /comfyui/custom_nodes/x-flux-comfyui/requirements.txt \
	&& python3 /comfyui/custom_nodes/x-flux-comfyui/setup.py

# Match the established ComfyUI Serverless workers exactly: the official base
# supplies the handler and ComfyUI runtime. Models are pre-staged on the attached
# persistent network volume; no runtime downloads, custom handler, or alternate
# entrypoint are used here.
