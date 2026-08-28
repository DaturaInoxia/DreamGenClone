#!/bin/bash
# Identity-conditioning proof pod setup (recycle-proof).
# Run on the pod after any recycle to re-establish custom nodes, Python deps,
# extra_model_paths.yaml, and ComfyUI. Models already live on /workspace (persistent).
set -e
W=/workspace/comfyui

echo "=== symlink custom nodes ==="
mkdir -p /ComfyUI/custom_nodes
for n in ComfyUI_IPAdapter_plus PuLID_ComfyUI ComfyUI-Impact-Pack comfyui_controlnet_aux; do
  if [ -e "/ComfyUI/custom_nodes/$n" ]; then
    echo "  $n already linked"
  else
    ln -s "$W/custom_nodes/$n" "/ComfyUI/custom_nodes/$n"
    echo "  $n linked"
  fi
done

echo "=== pip deps ==="
pip install -q insightface onnxruntime opencv-python-headless facexlib
pip install -q -r "$W/custom_nodes/PuLID_ComfyUI/requirements.txt" \
  -r "$W/custom_nodes/ComfyUI-Impact-Pack/requirements.txt" \
  -r "$W/custom_nodes/comfyui_controlnet_aux/requirements.txt"

echo "=== extra_model_paths.yaml ==="
cat > /ComfyUI/extra_model_paths.yaml <<'YAML'
a111:
  base_path: /workspace/comfyui
  checkpoints: models/checkpoints
  configs: models/configs
  vae: models/vae
  loras: models/loras
  upscale_models: models/upscale_models
  embeddings: models/embeddings
  controlnet: models/controlnet
  clip: models/clip
  clip_vision: models/clip_vision
  ipadapter: models/ipadapter
  pulid: models/pulid
  insightface: models/insightface
YAML

echo "=== input image symlinks ==="
mkdir -p /ComfyUI/input
for img in /workspace/comfyui/input/dean/*.png; do
  [ -f "$img" ] || continue
  name=$(basename "$img")
  if [ ! -e "/ComfyUI/input/$name" ]; then
    ln -s "$img" "/ComfyUI/input/$name"
    echo "  $name linked"
  fi
done

echo "=== insightface dir symlink (PuLID hardcodes models_dir/insightface) ==="
rm -rf /ComfyUI/models/insightface
ln -s /workspace/comfyui/models/insightface /ComfyUI/models/insightface
echo "  insightface linked"

echo "=== restart ComfyUI ==="
pkill -f main.py || true
sleep 4
cd /ComfyUI
nohup python main.py --listen --port 3000 >> /workspace/comfyui.log 2>&1 < /dev/null &
echo "SETUP_DONE"
