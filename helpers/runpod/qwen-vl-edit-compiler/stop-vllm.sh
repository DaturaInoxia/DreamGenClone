#!/usr/bin/env bash
set -euo pipefail

runtime=${1:?Usage: stop-vllm.sh <runtime-path>}
pid_file="$runtime/vllm.pid"

[[ -f "$pid_file" ]] || { printf 'No vLLM PID file at %s\n' "$pid_file" >&2; exit 1; }
pid=$(cat "$pid_file")
[[ "$pid" =~ ^[0-9]+$ ]] || { printf 'Invalid vLLM PID file.\n' >&2; exit 1; }
if ! kill -0 "$pid" 2>/dev/null; then
    rm "$pid_file"
    printf 'QWEN_VL_STOPPED pid=%s already-exited=true\n' "$pid"
    exit 0
fi
kill "$pid"
for _ in {1..30}; do
    kill -0 "$pid" 2>/dev/null || { rm "$pid_file"; printf 'QWEN_VL_STOPPED pid=%s\n' "$pid"; exit 0; }
    sleep 1
done
printf 'Timed out stopping vLLM process %s.\n' "$pid" >&2
exit 1