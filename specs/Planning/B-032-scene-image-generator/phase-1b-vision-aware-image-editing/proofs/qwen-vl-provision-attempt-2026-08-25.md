# Qwen VL Provision Attempt - 2026-08-25

**Task:** P1B-007
**Outcome:** Accepted by explicit user waiver on 2026-08-25. Runtime and one-image inference work;
the measured startup exceeds the original 180-second gate, which is superseded for initial
deployment by the accepted evidence-based startup envelope in
[`../multi-pod-separation-plan.md`](../multi-pod-separation-plan.md).

## Scope

The attempt used only the new isolated `/workspace/qwen-vl-edit-compiler` runtime directory. The
base ComfyUI process and `/workspace/comfyui-qwen-2511` runtime were not modified or stopped. No
model artifact download began because vLLM installation did not complete.

## Attempt 1: Default Pip Staging

The pinned `vllm==0.8.5.post1` installation attempted to download the Torch 2.6.0 CUDA dependency
stack into the small container root's default pip staging location. It failed with:

```text
OSError: [Errno 28] No space left on device
```

The provisioner was repaired forward to use runtime-local `/workspace` pip cache and temporary
directories.

## Attempt 2: Workspace Pip Staging

The same installer then failed while downloading the Torch wheel with:

```text
OSError: [Errno 122] Disk quota exceeded
```

At this point `/workspace` reported 137 TiB free, and the container root reported 2.4 GiB free.
This showed that aggregate filesystem free space was not an adequate installation preflight.

## Attempt 3: Proven Shared CUDA/Torch Baseline

The existing Qwen Edit virtual environment records `include-system-site-packages = true` and
exposes host `torch==2.6.0`. The compiler runtime was changed to the same dedicated-venv pattern,
avoiding a duplicate Torch/CUDA download. vLLM downloaded its 326.4 MB wheel and remaining
dependencies, then its pip process terminated with:

```text
Bus error (core dumped)
```

The post-failure state was:

| Check | Evidence |
|---|---|
| Compiler venv configuration | `include-system-site-packages = true` |
| Compiler runtime footprint | 2.3 GiB |
| vLLM package | not installed |
| Container root | 2.4 GiB free |
| `/workspace` | 137 TiB free |
| Kernel diagnostics | unavailable in container: `dmesg: read kernel buffer failed: Operation not permitted` |

## Runtime Pin Review

The failed minimum-series candidate was not retained for convenience. A review of current vLLM
Qwen2.5-VL support, published package metadata, and relevant advisories selected `vllm==0.27.1`.
Its Python 3.10 package resolves a complete `torch==2.13.0` and CUDA 13 dependency graph compatible
with the A40, driver 580.159.04, and glibc 2.35 host. Older releases were not selected as a storage
workaround.

## Attempt 4: Direct Workspace Venv

`uv` replaced pip for deterministic, cache-free installation. Seeding a fresh venv directly under
`/workspace` failed after approximately 16 MiB with `EDQUOT`. The FUSE mount still reported shared
backend capacity rather than the pod's configured 50 GiB allocation.

## Attempt 5: Shared-Memory Install

The same `uv` command resolved, prepared, and installed all 198 packages under `/dev/shm` in about
24 seconds. The resulting environment measured 7.6 GiB and contained `vllm==0.27.1`,
`torch==2.13.0`, and the CUDA 13 runtime graph. Import then failed before any model download:

```text
OSError: .../torch/lib/libtorch_global_deps.so: failed to map segment from shared object
```

`/dev/shm` is mounted `noexec`, so native Torch libraries cannot be mapped from it. The pod also
lacks mount capability, exposes no `/dev/fuse`, and has no container engine; kernel SquashFS/tmpfs
and userspace FUSE runtime images are unavailable.

## Quota Diagnosis and Remediation

Actual usage must be measured with `du` against the RunPod control-plane allocation. After the
exact Pony checkpoint retirement, `/workspace` used 46,246,889,472 bytes, leaving only 6.93 GiB
under the former 50 GiB quota. That explains the workspace `EDQUOT` despite misleading `df` output.

The persistent volume is now 85 GiB. The provisioner was repaired forward to require the configured
capacity, compute availability from actual `du` usage, use an executable direct `/workspace` venv,
and preserve the approved 20 GiB final free-space floor. Failed install data and caches remain
identified for exact-path cleanup before the next attempt.

## Attempt 6: Managed Python 3.13 Runtime

The isolated runtime and exact model revision were provisioned successfully on the 85 GiB volume.
The final measured quota-aware state was 64,800,274,432 bytes used and 26,467,780,608 bytes
available, preserving approximately 24.65 GiB and passing the 20 GiB floor. Both weight shards and
`tokenizer.json` matched the candidate manifest's byte counts and SHA-256 values.

Python 3.10 and 3.11 could not import the pinned FlashInfer package because it evaluates
`array.array[int]`; managed CPython 3.13.2 resolved that incompatibility. The interpreter, venv,
packages, model, and caches remain on the same persistent volume.

The first Python 3.13 launch reached kernel warmup but failed because the venv's `ninja` executable
was not visible to child processes. The launcher now prepends the isolated venv to `PATH` and fails
before launch if `ninja` cannot be resolved.

## Attempt 7: Explicit Native Sampler

After resolving `ninja`, FlashInfer's sampling JIT invoked `/usr/local/cuda/bin/nvcc`. The pod has
only CUDA compiler 11.8 even though the driver advertises CUDA 13.0 and the pinned Torch runtime is
CUDA 13.0. FlashInfer 0.6.16 rejected the CUDA 11.8 headers because it requires CUDA 12 or newer.

The pinned vLLM runtime explicitly supports selecting its native sampler with
`VLLM_USE_FLASHINFER_SAMPLER=0`. The launcher now selects that one sampler path at process start;
it does not retry or fall back at runtime. FlashAttention 2 remains active for model and vision
attention.

This launch became functional with the following evidence:

| Check | Evidence |
|---|---|
| Endpoint | `http://127.0.0.1:8002` |
| Served model | `qwen2.5-vl-7b-edit-compiler` |
| Maximum model length | 8,192 |
| Process-to-health time | approximately 276 seconds; exceeds the original 180-second gate and is accepted for initial deployment by explicit waiver |
| Post-load GPU memory | 46,068 MiB total, 40,086 MiB used, 5,403 MiB free; passes 4 GiB floor |
| Source image | PNG, 32 x 32, 1,148 bytes, exactly one image |
| One-image response | 2.444 seconds; passes 90-second gate |
| Structured output | strict JSON schema parsed with exactly `source_summary`, `edit_instruction`, `preserve`, and `avoid` |
| Raw response | `/workspace/qwen-vl-edit-compiler/proofs/one-image-response.json` |

The full launcher path was slower than process-to-health because its isolated Python/Torch/CUDA
preflight took approximately 137 additional seconds from the FUSE-backed volume. Configured
transition timeout must cover this measured full path with explicit operational margin. The
evidence does not support inventing one exact replacement timeout.

After the proof, the Qwen VL process was stopped, its PID file was removed, port 8002 had no
listener, and GPU usage returned to 271 MiB. Final quota-aware workspace availability was
25,557,990,912 bytes (approximately 23.8 GiB), and container-root availability was 5,156,413,440
bytes (approximately 4.8 GiB); both storage floors pass.

## Decision

Do not change the model, activate a second pod, use a cloud or text-only compiler, or add a hidden
fallback. Hashes, imports, loopback service health, image input, structured JSON, response latency,
VRAM headroom, and storage floors are proven. The user explicitly accepts the measured startup for
initial application implementation, so P1B-007 is complete. This is functional acceptance only:
P1B-008 through P1B-010 remain open and block production enablement and end-to-end acceptance until
the frozen compiler corpus passes.