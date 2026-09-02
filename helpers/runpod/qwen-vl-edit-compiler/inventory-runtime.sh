#!/usr/bin/env bash
set -euo pipefail

runtime=${1:?Usage: inventory-runtime.sh <runtime-path>}
: "${QWEN_VL_WORKSPACE_CAPACITY_GIB:?Set QWEN_VL_WORKSPACE_CAPACITY_GIB to the configured RunPod volume size.}"
[[ "$QWEN_VL_WORKSPACE_CAPACITY_GIB" =~ ^[1-9][0-9]*$ ]] || {
    printf 'QWEN_VL_WORKSPACE_CAPACITY_GIB must be a positive integer.\n' >&2
    exit 1
}
workspace_capacity_bytes=$((QWEN_VL_WORKSPACE_CAPACITY_GIB * 1024 * 1024 * 1024))
workspace_used_bytes=$(du -sx -B1 /workspace | cut -f1)
(( workspace_used_bytes <= workspace_capacity_bytes )) || {
    printf '/workspace usage exceeds configured capacity: used=%s capacity=%s\n' "$workspace_used_bytes" "$workspace_capacity_bytes" >&2
    exit 1
}
workspace_available_bytes=$((workspace_capacity_bytes - workspace_used_bytes))

printf 'QWEN_VL_RUNTIME_INVENTORY path=%s\n' "$runtime"
printf 'WORKSPACE_CAPACITY capacity=%s used=%s available=%s\n' \
    "$workspace_capacity_bytes" "$workspace_used_bytes" "$workspace_available_bytes"
df -h /
nvidia-smi
if [[ -d "$runtime" ]]; then
    du -sh "$runtime"
    find "$runtime" -maxdepth 2 -type f -printf '%s %p\n' | sort -n
else
    printf 'Runtime directory does not exist.\n'
fi