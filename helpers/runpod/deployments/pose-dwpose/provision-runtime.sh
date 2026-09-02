#!/usr/bin/env bash
# DWPose pose-extraction pod provisioning (recycle/migration-proof).
#
# Re-establishes, on the pod, the pinned comfyui_controlnet_aux custom node, the
# validated Python runtime, the Blackwell-compatible torch build, and ComfyUI on
# the manifest inference port (3003). Idempotent — safe to run on a fresh pod or
# after any migrate/recycle. Run ON the pod over SSH, e.g.:
#
#   ssh -i artifacts/runpod/ssh_ed25519 -p <SSH_PORT> root@<SSH_IP> \
#     'bash -s' < helpers/runpod/deployments/pose-dwpose/provision-runtime.sh
#
# Full runbook: helpers/runpod/deployments/pose-dwpose/README.md
set -euo pipefail

W=/workspace/comfyui
PINNED_AUX=e8b689a513c3e6b63edc44066560ca5919c0576e   # comfyui_controlnet_aux v1.1.5 (phase-0 validated)
INFERENCE_PORT=3003                                   # manifest inferencePort
PY=/usr/bin/python3.10

echo "=== [1/6] comfyui_controlnet_aux (pinned ${PINNED_AUX:0:12}...) ==="
mkdir -p "$W/custom_nodes"
if [ -d "$W/custom_nodes/comfyui_controlnet_aux/.git" ]; then
  echo "  clone exists; pinning commit"
  git -C "$W/custom_nodes/comfyui_controlnet_aux" fetch --quiet --depth 1 origin "$PINNED_AUX" 2>/dev/null || true
  git -C "$W/custom_nodes/comfyui_controlnet_aux" checkout --quiet --detach "$PINNED_AUX" 2>/dev/null || \
    git -C "$W/custom_nodes/comfyui_controlnet_aux" checkout --quiet "$PINNED_AUX"
else
  git clone --quiet https://github.com/Fannovel16/comfyui_controlnet_aux "$W/custom_nodes/comfyui_controlnet_aux"
  git -C "$W/custom_nodes/comfyui_controlnet_aux" checkout --quiet "$PINNED_AUX"
fi

echo "=== [2/6] symlink into /ComfyUI/custom_nodes ==="
mkdir -p /ComfyUI/custom_nodes
if [ -e "/ComfyUI/custom_nodes/comfyui_controlnet_aux" ] && [ ! -L "/ComfyUI/custom_nodes/comfyui_controlnet_aux" ]; then
  rm -rf "/ComfyUI/custom_nodes/comfyui_controlnet_aux"
fi
ln -sfn "$W/custom_nodes/comfyui_controlnet_aux" "/ComfyUI/custom_nodes/comfyui_controlnet_aux"
echo "  linked"

echo "=== [3/6] validated DWPose Python deps (no onnxruntime) ==="
$PY -m pip install --quiet \
  opencv-python-headless==4.10.0.84 \
  matplotlib==3.10.9 \
  scikit-image==0.25.2

echo "=== [4/6] Blackwell (sm_120) torch build (2.7.0+cu128) ==="
CUR_TORCH=$($PY -c "import torch; print(torch.__version__)" 2>/dev/null || echo none)
if [ "$CUR_TORCH" = "2.7.0+cu128" ]; then
  echo "  torch already 2.7.0+cu128; skipping"
else
  echo "  current torch: $CUR_TORCH -> installing 2.7.0+cu128 (required for Blackwell sm_120)"
  $PY -m pip install --quiet \
    torch==2.7.0 torchvision==0.22.0 torchaudio==2.7.0 \
    --index-url https://download.pytorch.org/whl/cu128
fi

echo "=== [5/6] persist inference port ${INFERENCE_PORT} in entrypoint ==="
if [ -f /pre_start.sh ]; then
  if grep -q -- "--port 3000" /pre_start.sh; then
    sed -i s/3000/3003/g /pre_start.sh
    echo "  /pre_start.sh updated 3000 -> 3003 (survives container restart)"
  else
    echo "  /pre_start.sh already uses 3003 or another port; leaving as-is"
  fi
else
  echo "  /pre_start.sh not found; entrypoint port fix skipped (relaunch below uses 3003)"
fi

echo "=== [6/6] restart ComfyUI on port ${INFERENCE_PORT} ==="
pkill -f "python main.py" || true
sleep 4
cd /ComfyUI
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
echo "PROVISION_DONE"
exit 0
