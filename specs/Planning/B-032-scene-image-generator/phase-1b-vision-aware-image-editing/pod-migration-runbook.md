# Phase 1B Pod Migration Runbook

## Purpose

Migrate Juggernaut, Qwen Image Edit, Qwen VL, and DWPose from the legacy combined deployment to
independent capability pods and volumes. The legacy pod remains intact as the migration source until
all dedicated replacements and assignments pass. This runbook is a plan; commands and paths must be
filled from live inventory.

This dedicated-pod amendment supersedes the earlier same-pod instructions in this document. The
authoritative architecture and acceptance gates are in `multi-pod-separation-plan.md`.

## Hard Rules

- Never delete by glob, directory recursion, model-name guess, or free-space pressure alone.
- Never delete while a queue or model process may have the file open.
- Never store SSH details, tokens, endpoint URLs, or API keys in committed evidence.
- Never treat the Qwen Edit text encoder as a multimodal analysis endpoint.
- Never download into container root; all runtimes/artifacts live under `/workspace`.
- Never claim recovery until artifact hashes and health checks pass.
- Never install a replacement or candidate capability into the legacy combined pod.
- Never terminate a pod or delete a volume without separate explicit user authorization.

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

## Legacy Artifact Retirement

Pony retirement is deferred until its dedicated candidate decision and all legacy cutover gates are
complete. Any later implementation script must require:

- exact absolute expected path;
- expected filename `ponyDiffusionV6XL_v6.safetensors` unless inventory proves a different deployed
  artifact and the manifest is reviewed;
- expected byte count;
- expected SHA-256;
- typed confirmation containing the exact model filename.

It rechecks all four values immediately before one exact-file removal, syncs, and reports free
space. It does not remove workflows, previews, metadata, generated images, or directories.

## Vision Runtime Provisioning

Use the dedicated `image-vision-qwen-vl-prod` pod and its own persistent volume. The committed
provisioner must:

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

## Dedicated Capability Pod Gate

Juggernaut, Qwen Edit, Qwen VL, and DWPose each run on their manifest-selected dedicated pod and
volume. Start, endpoint discovery, exact identity readiness, drain, and stop are verified
independently for each deployment. A workflow resolves exactly one configured deployment for each
required capability; endpoint unavailability is failure and never permission to use another pod or
model.

## Post-Migration Verification

- Each dedicated persistent volume and container root remains above its approved free-space floor.
- Vision `/v1/models`, health, one-image schema request, and frozen corpus pass.
- Qwen ComfyUI `/system_stats`, object info, and one canonical edit pass.
- Juggernaut generation path resolves and one health/proof request passes.
- The legacy combined pod remains intact until every dedicated replacement and cutover gate passes.
- Pony is not assigned to an active production function; its dedicated candidate is evaluated
  independently without changing Juggernaut's production assignment.
- No `.partial` downloads, duplicate model caches, or unbounded logs remain.

## Recovery

Recovery is an explicit operator action, not application fallback. If a dedicated deployment cannot
be accepted, preserve its failure evidence and repair or reprovision only that deployment from its
manifest. Do not alter another capability pod or the retained legacy source as an unrelated recovery
step. Rollback is a new explicit persisted assignment to a previously proven deployment.