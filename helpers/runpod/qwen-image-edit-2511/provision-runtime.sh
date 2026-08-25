#!/usr/bin/env bash
set -euo pipefail

runtime="${1:-/workspace/comfyui-qwen-2511}"
revision=e4c61d75555036fa28b6bb34e5fd67b007c9f391

if [[ ! -d "$runtime/.git" ]]; then
    git clone https://github.com/Comfy-Org/ComfyUI.git "$runtime"
fi
git -C "$runtime" fetch --depth 1 origin "$revision"
git -C "$runtime" checkout --detach "$revision"
python3 -m venv --system-site-packages "$runtime/.venv"
"$runtime/.venv/bin/pip" install --upgrade pip
"$runtime/.venv/bin/pip" install -r "$runtime/requirements.txt"
"$(dirname "$0")/download-models.sh" "$runtime"
printf 'QWEN_RUNTIME_PROVISIONED revision=%s path=%s\n' "$revision" "$runtime"