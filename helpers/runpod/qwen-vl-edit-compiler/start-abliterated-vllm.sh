#!/usr/bin/env bash
# Start vLLM serving the abliterated Qwen2.5-VL compiler model on port 3004.
set -euo pipefail

runtime=/workspace/qwen-vl-edit-compiler
export VLLM_USE_FLASHINFER_SAMPLER=0
cd "$runtime"

nohup "$runtime/.venv/bin/vllm" serve "$runtime/model" \
    --host 0.0.0.0 \
    --port 3004 \
    --served-model-name huihui-ai/Qwen2.5-VL-7B-Instruct-abliterated \
    --max-model-len 8192 \
    --limit-mm-per-prompt '{"image":1}' \
    --gpu-memory-utilization 0.9 \
    > "$runtime/vllm.log" 2>&1 < /dev/null &

echo "VLLM_LAUNCHED pid=$!"
