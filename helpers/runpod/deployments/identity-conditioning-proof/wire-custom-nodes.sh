#!/usr/bin/env bash
# Wire persistent IP-Adapter custom nodes into ComfyUI + self-heal on boot.
# Idempotent. Run on the pod over SSH (pipe via bash -s).
set -e

echo "=== [1] symlink persistent custom nodes into /ComfyUI/custom_nodes ==="
for d in /workspace/comfyui/custom_nodes/*/; do
  [ -d "$d" ] || continue
  name=$(basename "$d")
  if [ ! -e "/ComfyUI/custom_nodes/$name" ]; then
    ln -s "$d" "/ComfyUI/custom_nodes/$name"
    echo "  linked $name"
  else
    echo "  exists $name"
  fi
done

echo "=== [2] patch pre_start.sh for custom-node self-heal ==="
if ! grep -q 'dreamgen-customnodes-bootstrap' /pre_start.sh; then
  # Insert the bootstrap block right before 'cd /ComfyUI' if present, else append.
  if grep -q '^cd /ComfyUI' /pre_start.sh; then
    awk '
      /^cd \/ComfyUI/ && !done {
        print "# dreamgen-customnodes-bootstrap: symlink persistent custom nodes"
        print "for d in /workspace/comfyui/custom_nodes/*/; do"
        print "  name=$(basename \"$d\")"
        print "  [ -e \"/ComfyUI/custom_nodes/$name\" ] || ln -s \"$d\" \"/ComfyUI/custom_nodes/$name\""
        print "done"
        print ""
        done=1
      }
      { print }
    ' /pre_start.sh > /tmp/pre_start.new
    mv /tmp/pre_start.new /pre_start.sh
    chmod +x /pre_start.sh
    echo "  patched pre_start.sh (before cd /ComfyUI)"
  else
    cat >> /pre_start.sh <<'BOOTSTRAP'

# dreamgen-customnodes-bootstrap: symlink persistent custom nodes
for d in /workspace/comfyui/custom_nodes/*/; do
  name=$(basename "$d")
  [ -e "/ComfyUI/custom_nodes/$name" ] || ln -s "$d" "/ComfyUI/custom_nodes/$name"
done
BOOTSTRAP
    chmod +x /pre_start.sh
    echo "  appended to pre_start.sh"
  fi
else
  echo "  pre_start.sh already patched"
fi

echo "=== [3] restart ComfyUI on 3000 ==="
pkill -f 'python main.py' || true
sleep 4
cd /ComfyUI
if command -v setsid >/dev/null 2>&1; then
  (setsid nohup python main.py --listen --port 3000 >> /workspace/comfyui.log 2>&1 < /dev/null &)
else
  nohup python main.py --listen --port 3000 >> /workspace/comfyui.log 2>&1 < /dev/null &
fi
echo "restarted"
