# FLUX structural proof worker

Proof-only Serverless worker for the dual-base location experiment.

`handler.py` is included because RunPod's GitHub integration requires a queue-worker
handler contract during repository validation. The official `worker-comfyui` base image
still owns the actual ComfyUI workflow handler and startup runtime.

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

The worker's idempotent startup provisioner downloads missing public files directly from Hugging
Face into the persistent volume, so the local Windows host does not need to download multi-gigabyte
files. Existing files are reused. After every startup it writes
`/runpod-volume/models/flux-structural-proof-manifest.json` with byte sizes and SHA-256 values.
The endpoint must not be treated as qualified until the manifest and proof render are captured.
