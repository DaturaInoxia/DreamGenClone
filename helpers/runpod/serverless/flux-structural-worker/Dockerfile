# FLUX structural proof worker.
# Uses the official ComfyUI Serverless base and the existing network volume.
# The volume must contain the FLUX base, VAE, text encoders, and XLabs ControlNet.
FROM runpod/worker-comfyui:5.8.4-base

# Match the established ComfyUI Serverless workers exactly: the official base
# supplies the handler and ComfyUI runtime. Models are pre-staged on the attached
# persistent network volume; no runtime downloads, custom handler, or alternate
# entrypoint are used here.
