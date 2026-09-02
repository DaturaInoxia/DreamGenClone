#!/usr/bin/env bash
set -euo pipefail

runtime="${1:-/workspace/comfyui-qwen-2511}"
port="${2:-3002}"
revision=e4c61d75555036fa28b6bb34e5fd67b007c9f391
log="/workspace/qwen-2511-comfyui.log"

[[ "$(git -C "$runtime" rev-parse HEAD)" == "$revision" ]] || { printf 'Unexpected ComfyUI revision.\n' >&2; exit 1; }
ss -ltn | grep -q ":${port} " && { printf 'Port %s is already in use.\n' "$port" >&2; exit 1; }
for model in models/diffusion_models/qwen_image_edit_2511_fp8mixed.safetensors models/text_encoders/qwen_2.5_vl_7b_fp8_scaled.safetensors models/vae/qwen_image_vae.safetensors; do
    [[ -f "$runtime/$model" ]] || { printf 'Missing model: %s\n' "$model" >&2; exit 1; }
done
setsid -f "$runtime/.venv/bin/python" "$runtime/main.py" --listen 127.0.0.1 --port "$port" >"$log" 2>&1 </dev/null
printf 'QWEN_COMFYUI_STARTED port=%s log=%s\n' "$port" "$log"