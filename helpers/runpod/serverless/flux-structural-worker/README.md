# FLUX structural proof worker

Proof-only Serverless worker for the dual-base location experiment.

## Deployment branch

This worker must be built from the current working branch `development`. Do not configure the
RunPod GitHub Integration to use `master`; the proof and its registry changes are being developed
on `development`.

## Required network-volume assets

Place the exact, hash-recorded files under the ComfyUI model folders on network volume `xkslgh6xo0`:

- `models/unet/flux1-dev-fp8.safetensors`
- `models/vae/ae.safetensors`
- `models/clip/t5xxl_fp8_e4m3fn.safetensors`
- `models/clip/clip_l.safetensors`
- `models/controlnet/flux-canny-controlnet-v3.safetensors`

The Canny artifact is the XLabs-AI `flux-controlnet-canny-v3` artifact. Do not substitute an SDXL ControlNet.

The endpoint must not be created until the files exist and a manifest records their exact SHA-256 values.
