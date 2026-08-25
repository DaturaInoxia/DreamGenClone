#!/usr/bin/env bash
set -euo pipefail

runtime=${1:?Usage: start-vllm.sh <runtime-path> <port> <gpu-memory-utilization>}
port=${2:?Usage: start-vllm.sh <runtime-path> <port> <gpu-memory-utilization>}
gpu_memory_utilization=${3:?Usage: start-vllm.sh <runtime-path> <port> <gpu-memory-utilization>}
model_dir="$runtime/model"
venv="$runtime/.venv"
pid_file="$runtime/vllm.pid"
log_file="$runtime/vllm.log"
model_name=qwen2.5-vl-7b-edit-compiler

[[ -x "$venv/bin/vllm" ]] || { printf 'Missing vLLM runtime at %s\n' "$venv" >&2; exit 1; }
[[ -f "$model_dir/config.json" ]] || { printf 'Missing pinned Qwen VL model at %s\n' "$model_dir" >&2; exit 1; }
[[ "$gpu_memory_utilization" =~ ^0\.[0-9]+$|^1\.0$ ]] || { printf 'GPU memory utilization must be in (0, 1].\n' >&2; exit 1; }
ss -ltn | grep -q ":${port} " && { printf 'Port %s is already in use.\n' "$port" >&2; exit 1; }
[[ ! -f "$pid_file" ]] || { printf 'Existing PID file: %s\n' "$pid_file" >&2; exit 1; }

export PATH="$venv/bin:$PATH"
export VLLM_USE_FLASHINFER_SAMPLER=0
"$venv/bin/python" -c "import sys, torch, vllm; assert sys.version_info[:3] == (3, 13, 2); assert vllm.__version__ == '0.27.1'; assert torch.version.cuda == '13.0'; assert torch.cuda.is_available()"
command -v ninja >/dev/null || { printf 'Missing Ninja executable in %s\n' "$venv" >&2; exit 1; }
[[ "$("$venv/bin/vllm" --version)" == *'0.27.1'* ]] || { printf 'Unexpected vLLM version.\n' >&2; exit 1; }

nohup "$venv/bin/vllm" serve "$model_dir" \
    --host 127.0.0.1 \
    --port "$port" \
    --served-model-name "$model_name" \
    --max-model-len 8192 \
    --limit-mm-per-prompt '{"image":1}' \
    --gpu-memory-utilization "$gpu_memory_utilization" \
    >"$log_file" 2>&1 < /dev/null &
printf '%s\n' "$!" > "$pid_file"
printf 'QWEN_VL_STARTED port=%s pid=%s log=%s\n' "$port" "$(cat "$pid_file")" "$log_file"