# Isolated Qwen VL Edit-Prompt Compiler Runtime

These scripts provision and manage the selected vision prompt compiler on the existing RunPod pod.
They use a dedicated `/workspace` virtual environment and model directory. The environment owns its
pinned Torch/CUDA package graph; compiler packages, temporary files, model artifacts, process, and
endpoint remain isolated. The scripts never modify the base ComfyUI or Qwen Image Edit 2511 runtime.

The candidate is fixed to `Qwen/Qwen2.5-VL-7B-Instruct-AWQ` revision
`536a35794df8831aa814970ee8f89eff577e7718`, managed Python 3.13.2, and `vllm==0.27.1`.
The provisioner downloads only that revision and verifies required LFS artifact byte counts and
SHA-256 values. The launcher selects vLLM's native sampler explicitly because the pod's CUDA 11.8
compiler cannot build the pinned FlashInfer sampler; model and vision attention remain on
FlashAttention.

Run these commands on the pod, supplying the approved port and GPU utilization explicitly:

```bash
QWEN_VL_WORKSPACE_CAPACITY_GIB=85 \
	bash helpers/runpod/qwen-vl-edit-compiler/inventory-runtime.sh /workspace/qwen-vl-edit-compiler
QWEN_VL_WORKSPACE_CAPACITY_GIB=85 \
	bash helpers/runpod/qwen-vl-edit-compiler/provision-runtime.sh /workspace/qwen-vl-edit-compiler
bash helpers/runpod/qwen-vl-edit-compiler/start-vllm.sh /workspace/qwen-vl-edit-compiler <port> <gpu-memory-utilization>
bash helpers/runpod/qwen-vl-edit-compiler/health-check.sh <port> 180
python helpers/runpod/qwen-vl-edit-compiler/prove-one-image.py <port> <source-image> <raw-response-path>
bash helpers/runpod/qwen-vl-edit-compiler/stop-vllm.sh /workspace/qwen-vl-edit-compiler
```

The capacity value must match the RunPod control-plane allocation. The provisioner calculates
actual usage with `du` because the FUSE mount's `df` output reports shared-backend capacity, not the
pod volume quota. Provisioning fails before writing unless the runtime, model, and approved 20 GiB
post-install headroom fit.

The service binds to `127.0.0.1` and exposes the OpenAI-compatible `/v1/chat/completions` API.
The health check's 180-second limit is the same-pod transition gate. The proof runner separately
enforces the 90-second one-image response limit, one image per request, source media limits, and a
strict structured-output schema. The 2026-08-25 standalone run served a schema-valid image request
in 2.444 seconds with 5,403 MiB free VRAM, but cold startup took about 276 seconds after the vLLM
process spawned and therefore failed the transition gate.
Initial GPU residency is scheduled: stop Qwen VL before loading Qwen Edit or the base ComfyUI model,
and prove the selected runtime healthy before sending work. No script creates a second pod, chooses
another model/provider, or uses a text-only path.

See the Phase 1B candidate manifest and runtime thresholds before running P1B-007:
`specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/proofs/`.