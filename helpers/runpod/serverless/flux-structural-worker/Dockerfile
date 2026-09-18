# FLUX structural proof worker.
# Uses the official ComfyUI Serverless base and the existing network volume.
# The volume must contain the FLUX base, VAE, text encoders, and XLabs ControlNet.
FROM runpod/worker-comfyui:5.8.4-base

# FLUX-specific ControlNet/IP conditioning nodes. The base ComfyUI worker's
# generic ControlNetLoader is not the XLabs FLUX runtime. x-flux-comfyui is
# installed from the pinned source revision because it is not treated as a
# Comfy Registry package by this worker.
ARG XLABS_FLUX_REPOSITORY=https://github.com/XLabs-AI/x-flux-comfyui.git
ARG XLABS_FLUX_REV=00328556efc9472410d903639dc9e68a8471f7ac
RUN git clone --depth 1 "$XLABS_FLUX_REPOSITORY" /comfyui/custom_nodes/x-flux-comfyui \
	&& git -C /comfyui/custom_nodes/x-flux-comfyui checkout "$XLABS_FLUX_REV" \
	&& python3 -m pip install --no-cache-dir -r /comfyui/custom_nodes/x-flux-comfyui/requirements.txt \
	&& test -f /comfyui/custom_nodes/x-flux-comfyui/nodes.py

# The official worker maps the standard model folders from the network volume,
# but XLabs uses the additional models/xlabs folder. Link that persistent folder
# before ComfyUI starts so LoadFluxControlNet can discover the staged checkpoint.
RUN cat > /start-dreamgen-flux.sh <<'EOF'
#!/bin/bash
set -e
mkdir -p /comfyui/models
if [ ! -e /comfyui/models/xlabs ]; then
	mkdir -p /comfyui/models/xlabs
fi
if [ ! -e /comfyui/models/xlabs/controlnets ] && [ -d /runpod-volume/models/xlabs/controlnets ]; then
	ln -s /runpod-volume/models/xlabs/controlnets /comfyui/models/xlabs/controlnets
	echo "Linked /comfyui/models/xlabs/controlnets -> /runpod-volume/models/xlabs/controlnets"
elif [ ! -e /comfyui/models/xlabs/controlnets ] && [ -d /runpod-volume/models/controlnet ]; then
	ln -s /runpod-volume/models/controlnet /comfyui/models/xlabs/controlnets
	echo "Linked /comfyui/models/xlabs/controlnets -> /runpod-volume/models/controlnet"
fi
exec /start.sh
EOF
RUN chmod +x /start-dreamgen-flux.sh

# Match the established ComfyUI Serverless workers exactly: the official base
# supplies the handler and ComfyUI runtime. Models are pre-staged on the attached
# persistent network volume; no runtime model downloads or custom handler are used.
CMD ["/start-dreamgen-flux.sh"]
