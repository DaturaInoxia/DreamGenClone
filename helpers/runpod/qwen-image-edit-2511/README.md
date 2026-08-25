# Isolated Qwen Image Edit 2511 Runtime

These scripts create and run the pinned Qwen Image Edit 2511 ComfyUI runtime used by the portable proof package at `specs/image-generator-tests/qwen/`.

Use a GPU-host environment with an NVIDIA driver, CUDA-compatible PyTorch, `python3`, Git, Curl, and enough disk for approximately 29 GB of model files. The scripts bind Qwen to loopback only and deliberately keep it separate from the production Pony/Juggernaut ComfyUI service.

```bash
bash helpers/runpod/qwen-image-edit-2511/provision-runtime.sh /workspace/comfyui-qwen-2511
bash helpers/runpod/qwen-image-edit-2511/start-comfyui.sh /workspace/comfyui-qwen-2511 3002
curl -fsS http://127.0.0.1:3002/system_stats
```

`provision-runtime.sh` checks out ComfyUI revision `e4c61d75555036fa28b6bb34e5fd67b007c9f391`, creates a virtual environment, installs the pinned checkout's Python requirements, and runs `download-models.sh`. The downloader verifies byte counts and SHA-256 values for the Qwen diffusion model, text encoder, and VAE before they can be used.

The ComfyUI process is private at `127.0.0.1:3002`. On a developer workstation, forward that endpoint over SSH and configure Model Manager with the forwarded HTTP URL. SSH credentials and endpoint details stay machine-local; do not commit them.

For the exact replay process, expected runtime model settings, and proof coverage limits, read the proof [runbook](../../../specs/image-generator-tests/qwen/RUNBOOK.md).