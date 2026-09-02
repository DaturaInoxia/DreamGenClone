#!/usr/bin/env bash
# Add ipadapter + insightface model-path mappings to extra_model_paths.yaml
# (master on /workspace + live /ComfyUI) and restart ComfyUI.
# Idempotent. Run on the pod over SSH (pipe via bash -s, or base64 pipe).
set -e

MASTER=/workspace/comfyui/extra_model_paths.yaml.master
LIVE=/ComfyUI/extra_model_paths.yaml

add_mapping() {
  local file="$1"
  if grep -q '^  ipadapter:' "$file"; then
    echo "  $file already has ipadapter"
  else
    # insert ipadapter line after '  loras:'
    sed -i 's/^  loras:/  loras:\n  ipadapter: models\/ipadapter/' "$file"
    echo "  added ipadapter to $file"
  fi
  if grep -q '^  insightface:' "$file"; then
    echo "  $file already has insightface"
  else
    # insert insightface after ipadapter
    sed -i 's/^  ipadapter: models\/ipadapter/  ipadapter: models\/ipadapter\n  insightface: models\/insightface/' "$file"
    echo "  added insightface to $file"
  fi
}

echo "=== [1] patch master ==="
add_mapping "$MASTER"
echo "=== [2] copy to live ==="
cp "$MASTER" "$LIVE"
echo "  live updated"
echo "=== [3] verify ==="
cat "$LIVE"
echo "=== [4] restart ComfyUI on 3000 ==="
pkill -f 'python main.py' || true
sleep 4
cd /ComfyUI
if command -v setsid >/dev/null 2>&1; then
  (setsid nohup python main.py --listen --port 3000 >> /workspace/comfyui.log 2>&1 < /dev/null &)
else
  nohup python main.py --listen --port 3000 >> /workspace/comfyui.log 2>&1 < /dev/null &
fi
echo "restarted"
