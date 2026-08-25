#!/usr/bin/env bash
set -euo pipefail

port=${1:?Usage: health-check.sh <port> <timeout-seconds>}
timeout_seconds=${2:?Usage: health-check.sh <port> <timeout-seconds>}
model_name=qwen2.5-vl-7b-edit-compiler
deadline=$((SECONDS + timeout_seconds))

while (( SECONDS < deadline )); do
    models=$(curl --fail --silent --show-error "http://127.0.0.1:${port}/v1/models" 2>/dev/null || true)
    if [[ "$models" == *"$model_name"* ]]; then
        printf 'QWEN_VL_HEALTHY port=%s model=%s\n' "$port" "$model_name"
        exit 0
    fi
    sleep 2
done

printf 'Qwen VL health check timed out after %s seconds on port %s.\n' "$timeout_seconds" "$port" >&2
exit 1