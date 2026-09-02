#!/usr/bin/env bash
# Juggernaut image-gen pod provisioning (recycle/migration-proof).
#
# Re-establishes /ComfyUI/extra_model_paths.yaml — which is LOST on every migrate/recycle
# because it lives in the container overlay, not the persistent /workspace volume — and
# restarts ComfyUI on the manifest inference port (3000). Without this YAML, ComfyUI only
# sees the 5 default checkpoints and every app render fails with:
#
#   400: ckpt_name 'juggernautXL_ragnarok.safetensors' not in [...]
#
# Idempotent — safe on a fresh pod or after any migrate/recycle. Run ON the pod over SSH, e.g.:
#
#   ssh -i artifacts/runpod/ssh_ed25519 -p <SSH_PORT> root@<SSH_IP> \
#     'bash -s' < helpers/runpod/deployments/image-gen-juggernaut/provision-runtime.sh
#
# Root-cause background + repro + persistent-vs-ephemeral table:
#   helpers/runpod/deployments/image-gen-juggernaut/README.md
set -euo pipefail

W=/workspace/comfyui
INFERENCE_PORT=3000              # manifest inferencePort
MASTER="$W/extra_model_paths.yaml.master"
LIVE=/ComfyUI/extra_model_paths.yaml

echo "=== [1/4] write master extra_model_paths.yaml to persistent /workspace ==="
mkdir -p "$W"
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
YAML
echo "  wrote $MASTER"

echo "=== [2/4] copy to live /ComfyUI path ==="
cp "$MASTER" "$LIVE"
echo "  wrote $LIVE"

echo "=== [3/4] patch /pre_start.sh to self-heal on every boot ==="
if [ -f /pre_start.sh ]; then
  if grep -q "dreamgen-juggernaut-bootstrap" /pre_start.sh; then
    echo "  /pre_start.sh already patched"
  else
    # Insert the restore block after the shebang, preserving the original body.
    head -n 1 /pre_start.sh > /tmp/pre_start.new
    cat >> /tmp/pre_start.new <<'BOOTSTRAP'
# dreamgen-juggernaut-bootstrap (provision-runtime.sh): restore extra_model_paths.yaml
# from the persistent /workspace volume whenever the container overlay is wiped.
if [ -f /workspace/comfyui/extra_model_paths.yaml.master ] && [ ! -f /ComfyUI/extra_model_paths.yaml ]; then
  cp /workspace/comfyui/extra_model_paths.yaml.master /ComfyUI/extra_model_paths.yaml
fi
BOOTSTRAP
    tail -n +2 /pre_start.sh >> /tmp/pre_start.new
    mv /tmp/pre_start.new /pre_start.sh
    chmod +x /pre_start.sh
    echo "  /pre_start.sh patched"
  fi
else
  echo "  /pre_start.sh not found; self-heal patch skipped (relaunch below still writes the live YAML)"
fi

echo "=== [4/4] restart ComfyUI on port ${INFERENCE_PORT} ==="
pkill -f "python main.py" || true
sleep 4
cd /ComfyUI
# setsid so the process survives this SSH session closing (plain nohup dies on disconnect).
if command -v setsid >/dev/null 2>&1; then
  (setsid nohup python main.py --listen --port "$INFERENCE_PORT" >> /workspace/comfyui.log 2>&1 < /dev/null &)
else
  nohup python main.py --listen --port "$INFERENCE_PORT" >> /workspace/comfyui.log 2>&1 < /dev/null &
fi

echo "=== waiting for readiness (up to 90s) ==="
for i in $(seq 1 18); do
  if curl -fsS "http://127.0.0.1:${INFERENCE_PORT}/system_stats" >/dev/null 2>&1; then
    echo "READY on :${INFERENCE_PORT} (poll $i)"
    break
  fi
  [ "$i" = 18 ] && echo "WARN: not ready after 90s; see /workspace/comfyui.log"
  sleep 5
done

echo "=== identity check (CheckpointLoaderSimple) ==="
if curl -fsS "http://127.0.0.1:${INFERENCE_PORT}/object_info/CheckpointLoaderSimple" 2>/dev/null \
     | grep -q "juggernautXL_ragnarok.safetensors"; then
  echo "IDENTITY_OK: juggernautXL_ragnarok.safetensors present"
else
  echo "IDENTITY_WARN: juggernautXL_ragnarok.safetensors NOT in CheckpointLoaderSimple"
fi

echo "PROVISION_DONE"
exit 0
