# Phase 1B Pod Migration Runbook

## Purpose

Reclaim the persistent volume space occupied by Pony and install the vision compiler on the same
pod and persistent volume as Qwen Image Edit and Juggernaut. Pony is no longer an active POC model.
This runbook is a plan; commands and paths must be filled from live inventory.

## Hard Rules

- Never delete by glob, directory recursion, model-name guess, or free-space pressure alone.
- Never delete while a queue or model process may have the file open.
- Never store SSH details, tokens, endpoint URLs, or API keys in committed evidence.
- Never treat the Qwen Edit text encoder as a multimodal analysis endpoint.
- Never download into container root; all runtimes/artifacts live under `/workspace`.
- Never claim recovery until artifact hashes and health checks pass.
- Never create a second pod for the vision service; runtime isolation means directories/processes
  on the existing pod.

## Preflight Evidence

Record sanitized outputs for:

```bash
df -h /workspace /
du -xhd1 /workspace | sort -h
nvidia-smi
find /workspace -type f -size +1G -printf '%s %p\n' | sort -n
pgrep -af 'ComfyUI|vllm|python.*main.py'
```

Then record:

- active/queued ComfyUI jobs;
- exact Pony path from live node/model inventory;
- `stat`, SHA-256, and whether any process has the file open;
- Qwen diffusion/text-encoder/VAE hashes;
- Juggernaut path/hash;
- Hugging Face and pip cache sizes;
- expected vision download bytes plus temporary staging overhead;
- required free-space floor after installation.

## Configuration Impact Gate

Before file deletion:

1. Query Model Manager for every enabled model/function referencing Pony.
2. Verify no required application function resolves to Pony.
3. Disable the deployed Pony model or mark it unavailable through persisted UI-backed state.
4. Verify the intended Juggernaut/SDXL generation function and Qwen editor configuration.
5. Preserve tracked historical Pony workflows/evidence while removing Pony from the active POC.

If any active required function still resolves to Pony, stop. Do not delete the artifact.

## Authorized Retirement

The implementation script must require:

- exact absolute expected path;
- expected filename `ponyDiffusionV6XL_v6.safetensors` unless inventory proves a different deployed
  artifact and the manifest is reviewed;
- expected byte count;
- expected SHA-256;
- typed confirmation containing the exact model filename.

It rechecks all four values immediately before one exact-file removal, syncs, and reports free
space. It does not remove workflows, previews, metadata, generated images, or directories.

## Vision Runtime Provisioning

Use a separate dependency/runtime path such as `/workspace/qwen-vl-edit-compiler` on the same pod
and persistent volume. "Separate" does not mean another pod. The committed provisioner must:

1. pin source/runtime revisions;
2. use a dedicated virtual environment;
3. download only manifest-listed artifacts;
4. verify size and SHA-256 before activation;
5. bind the endpoint to loopback;
6. set explicit model length, image limits, GPU utilization, and generation configuration;
7. expose health and model-list checks;
8. write logs outside container root with bounded retention.

The endpoint is forwarded privately for development. Model Manager stores the HTTP provider/model
configuration, not SSH credentials.

## One-Pod Residency Gate

Juggernaut, Qwen Edit, and Qwen VL artifacts remain installed on this pod. Measure two GPU-loading
modes and approve exactly one:

- **Co-resident:** vision endpoint and Qwen ComfyUI remain started, and worst-case compilation/edit
  operations each pass with the approved VRAM headroom.
- **Same-pod scheduled residency:** services and artifacts remain on the pod, but a pinned
  coordinator unloads/stops the GPU-heavy model not in use, verifies GPU release, loads/starts the
  required model, and waits for health before jobs run.

Scheduled residency is expected on the 24 GB-class GPU. This is not multi-pod routing or model
fallback. The final manifest records process commands, ports, transition time, health probes, and
failure behavior. Endpoint unavailability is failure, never permission to create another pod or
use another model.

## Post-Migration Verification

- Persistent and root volumes remain above their approved free-space floors.
- Vision `/v1/models`, health, one-image schema request, and frozen corpus pass.
- Qwen ComfyUI `/system_stats`, object info, and one canonical edit pass.
- Juggernaut generation path resolves and one health/proof request passes.
- Pony is absent from live checkpoint inventory and unavailable in Model Manager.
- Existing Pony workflows remain in git and fail with an explicit missing-model diagnostic if run.
- No `.partial` downloads, duplicate model caches, or unbounded logs remain.

## Recovery

Recovery is explicit same-pod operator action, not application fallback. If the vision deployment
cannot be accepted, preserve its failure evidence and repair or reprovision only its manifest-listed
runtime on the existing pod. Do not reinstall Pony, create another pod, or modify retained Qwen and
Juggernaut artifacts as an unrelated recovery step.