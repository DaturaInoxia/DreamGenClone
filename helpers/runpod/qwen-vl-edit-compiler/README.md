# Isolated Qwen VL Edit-Prompt Compiler Runtime

These scripts provision and manage the selected vision prompt compiler on the existing RunPod pod.
They use a dedicated `/workspace` virtual environment and model directory. The environment owns its
pinned Torch/CUDA package graph; compiler packages, temporary files, model artifacts, process, and
endpoint remain isolated. The scripts never modify the base ComfyUI or Qwen Image Edit 2511 runtime.

The candidate is fixed to `huihui-ai/Qwen2.5-VL-7B-Instruct-abliterated` revision
`fa935a7958b3669b194c7ba4d1cfcebbe222641d`, managed Python 3.13.2, and `vllm==0.27.1`.
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
bash helpers/runpod/qwen-vl-edit-compiler/health-check.sh <port> <configured-health-timeout-seconds>
python helpers/runpod/qwen-vl-edit-compiler/prove-one-image.py <port> <source-image> <raw-response-path>
bash helpers/runpod/qwen-vl-edit-compiler/stop-vllm.sh /workspace/qwen-vl-edit-compiler
```

The capacity value must match the RunPod control-plane allocation. The provisioner calculates
actual usage with `du` because the FUSE mount's `df` output reports shared-backend capacity, not the
pod volume quota. Provisioning fails before writing unless the runtime, model, and approved 20 GiB
post-install headroom fit.

The service binds to `0.0.0.0` (exposed through the RunPod HTTP proxy) and exposes the
OpenAI-compatible `/v1/chat/completions` API.
The health timeout is an explicit operator input sourced from persisted deployment/Model Manager
configuration; scripts and application code must not supply a hidden default. It must cover the
measured full transition with operational margin. The proof runner separately enforces the
90-second one-image response limit, one image per request, source media limits, and a strict
structured-output schema. The 2026-08-25 standalone run served a schema-valid image request in
2.444 seconds with 5,403 MiB free VRAM. Startup took about 276 seconds after the vLLM process
spawned, plus about 137 seconds of launcher preflight; the user accepted this envelope for the
initial one-pod implementation.
Initial GPU residency is scheduled: stop Qwen VL before loading Qwen Edit or the base ComfyUI model,
and prove the selected runtime healthy before sending work. No script creates a second pod, chooses
another model/provider, or uses a text-only path.

See the Phase 1B candidate manifest and runtime thresholds before running P1B-007:
`specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/proofs/`.