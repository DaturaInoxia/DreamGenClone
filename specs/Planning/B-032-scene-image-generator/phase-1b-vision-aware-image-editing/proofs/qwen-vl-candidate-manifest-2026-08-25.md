# Qwen VL Candidate Manifest - 2026-08-25

**Task:** P1B-003 candidate freeze
**Purpose:** Pin the first one-pod edit-prompt compiler candidate before any runtime or weight
download. This is a proof candidate, not a fallback chain.

## Model

| Field | Pinned value |
|---|---|
| Repository | `Qwen/Qwen2.5-VL-7B-Instruct-AWQ` |
| Immutable revision | `536a35794df8831aa814970ee8f89eff577e7718` |
| License | Apache-2.0 |
| Task | `image-text-to-text` |
| Architecture | `Qwen2_5_VLForConditionalGeneration` |
| Quantization | AWQ, 4-bit weights; visual modules excluded from conversion |
| Access | Public, ungated at inventory time |

## Required Weight Artifacts

| File | Bytes | SHA-256 |
|---|---:|---|
| `model-00001-of-00002.safetensors` | 3,982,163,944 | `4f75e3de726546ee43620d1227d3596cd3ba0fdd19f11faeea71de578d2d1052` |
| `model-00002-of-00002.safetensors` | 2,941,808,440 | `dae4128bbfd2b8d489e838048edc0bbe6e31f269d9b96fa3effe11cc534b8f0c` |
| `tokenizer.json` | 11,422,063 | `5eee858c5123a4279c3e1f7b81247343f356ac767940b2692a928ad929543214` |

The provisioner must also obtain, from the same immutable revision, the model index, config,
preprocessor config, generation config, chat template, tokenizer config, special token map,
vocabulary, merges, added tokens, and license. File hashes for non-LFS metadata are verified from
the revision download manifest at provisioning time.

## Runtime Candidate

| Field | Pinned value |
|---|---|
| Serving runtime | `vllm==0.27.1` |
| Python | Managed CPython 3.13.2 on the persistent volume |
| GPU | NVIDIA A40, 46,068 MiB VRAM |
| Driver / advertised CUDA | 580.159.04 / CUDA 13.0 |
| Compiler Torch baseline | `torch==2.13.0` with the vLLM CUDA 13 package graph |
| Host CUDA compiler | CUDA 11.8; incompatible with the pinned FlashInfer sampler JIT |
| Sampling path | vLLM native sampler selected explicitly with `VLLM_USE_FLASHINFER_SAMPLER=0` |
| Runtime path | `/workspace/qwen-vl-edit-compiler` |
| Package isolation | Dedicated direct `/workspace` venv without system site packages; the base host and Qwen Edit environments remain unmodified |
| Endpoint binding | `127.0.0.1` only |
| API | OpenAI-compatible `/v1/chat/completions` |

The original `vllm==0.8.5.post1` minimum-series candidate was rejected after advisory review and
failed host-baseline reuse. Current vLLM documentation supports Qwen2.5-VL, and `vllm==0.27.1`
resolved and installed its complete Python 3.13/CUDA 13 dependency graph in an isolated proof
environment. Python 3.13 is required because the pinned FlashInfer package evaluates
`array.array[int]` during import. The host exposes only a CUDA 11.8 compiler, so vLLM's supported
native sampler is the single configured sampling path; FlashAttention remains active for model and
vision attention.

## Initial Launch Contract

The provisioner will test an explicit launch equivalent to:

```text
vllm serve <pinned-local-model-path>
  --host 127.0.0.1
  --port <configured-private-port>
  --served-model-name qwen2.5-vl-7b-edit-compiler
  --max-model-len 8192
  --limit-mm-per-prompt '{"image":1}'
  --gpu-memory-utilization 0.90
```

The P1B-004 limits are 1,048,576 source pixels, 10 MiB per source image, 180 seconds for the
same-pod transition, 90 seconds for a one-image response, and at least 4 GiB free VRAM after load.
The vision compiler emits schema-bound JSON; it does not use model thinking/reasoning output or a
text-only mode.

## Evidence Sources

- Hugging Face model metadata API queried from the existing pod without downloading weights.
- Current vLLM Qwen2.5-VL deployment guidance: OpenAI-compatible service, structured JSON support,
  and explicit memory/context controls.
- [`pod-inventory-2026-08-25.md`](pod-inventory-2026-08-25.md) for measured host facts.

## Result

P1B-003 is complete. P1B-007 proved endpoint identity, loopback binding, one-image structured
output, and post-load VRAM, but the candidate remains unaccepted because its approximately
276-second process-to-health time exceeded the frozen 180-second transition limit. The failure is
recorded and is not replaced silently.