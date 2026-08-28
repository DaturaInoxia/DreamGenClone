#!/usr/bin/env bash
# Write the CORRECT extra_model_paths.yaml (with ipadapter + insightface) to
# master + live and restart ComfyUI. Overwrites any malformed prior version.
# Idempotent. Run on the pod over SSH (pipe via bash -s or base64).
set -e

MASTER=/workspace/comfyui/extra_model_paths.yaml.master
LIVE=/ComfyUI/extra_model_paths.yaml

cat > "$MASTER" <<'YAML'
comfyui_extra_paths:
  base_path: /workspace/comfyui
  checkpoints: models/checkpoints
  configs: models/configs
  loras: models/loras
  vae: models/vae
  clip: models/clip
  unet: models/unet
  clip_vision: models/clip_vision
  controlnet: models/controlnet
  embeddings: models/embeddings
  upscale_models: models/upscale_models
  ipadapter: models/ipadapter
  insightface: models/insightface
YAML

cp "$MASTER" "$LIVE"
echo "=== master written ==="
cat "$MASTER"
echo "=== restart ComfyUI on 3000 ==="
pkill -f 'python main.py' || true
sleep 4
cd /ComfyUI
if command -v setsid >/dev/null 2>&1; then
  (setsid nohup python main.py --listen --port 3000 >> /workspace/comfyui.log 2>&1 < /dev/null &)
else
  nohup python main.py --listen --port 3000 >> /workspace/comfyui.log 2>&1 < /dev/null &
fi
echo "restarted"
