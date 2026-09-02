---
name: runpod-pod-creation
description: 'Re-create DreamGenClone RunPod pods on an alternate GPU when a pod cannot be started (no available GPU) and RunPod manual migration fails for the same reason. Covers GPU selection (automatic via API), fresh-pod creation, from-scratch provisioning, per-pod smoke tests, and direct Model Manager database updates. Use when the user says "create new pods", "re-create the pod", "no GPU available", "migration fails", or "pick a GPU for the pod".'
argument-hint: '<pod function name or deployment key>'
user-invocable: true
---

# RunPod Pod Creation

## Purpose

When a DreamGenClone pod cannot be **started** (RunPod reports no available GPU) **and** the manual
**Migrate** action in the RunPod UI fails for the same reason, re-create a **fresh pod** on an
available GPU, provision it from scratch, smoke test it, and sync the new endpoint into **Model
Manager** in the development database.

This complements the `runpod-pod-migration` skill (which never creates pods). This skill's whole
job is controlled pod creation.

## Entry assumptions

- We are in the state where **no GPUs are currently available** for the affected pod(s) — start and
  migrate both fail with capacity errors.
- The user wants the pod back **with the same function/model**, on the cheapest GPU that still runs
  the workflow and is not too slow.
- GPU selection is done automatically through the RunPod API. If the user instead pastes a
  screenshot of available GPUs, use the manual checklist in `helpers/runpod/GPU-SELECTION.md`.

## Key facts (read first)

1. **There is no RunPod REST "GPU availability" endpoint.** Selection works by passing an
   **ordered list of `gpuTypeIds`** with `gpuTypePriority=custom` to the pod-create API — RunPod
   rents the **first GPU with current capacity**. Cheapest-first ordering implements the cost
   policy automatically.
2. **Fresh pod = empty local volume.** Pods use RunPod **local volumes**; a newly created pod has
   **no models**. Every pod must be **re-provisioned from scratch** (model re-download). This is
   expected, not an error. Download sizes: Juggernaut ~7 GB, Qwen Edit ~30 GB, Qwen VL ~16 GB,
   DWPose ~1 GB.
3. The **pod registry** (`helpers/runpod/pod-registry.json`) is the single source of truth for the
   5 pods: function name → deployment manifest, candidate GPU list (cheapest-first), provisioning
   steps, smoke test, and Model Manager provider IDs. **Do not hardcode these anywhere else.**
4. Model Manager updates use the **compare-and-swap** `provider-endpoint-update` command. The
   `-ExpectedCurrentBaseUrl` must be the CURRENT URL read from the DB, or the update refuses.

## Persistence & documentation (MANDATORY)

Read `helpers/runpod/POD-PERSISTENCE.md` (the standard) before provisioning any pod.

- **Two layers:** `/workspace` is the PERSISTENT volume (survives stop/resume + migrate); the
  container overlay (`/ComfyUI/**`, `pip`/`apt` installs, `/pre_start.sh`) is WIPED on
  recycle/migrate. "Missing after restart" is always overlay loss, never a model problem.
- **Rules:** models/venvs/custom-node clones/config masters go on `/workspace`; anything that must
  exist in the overlay is restored on every boot by an idempotent `/pre_start.sh` patch; services
  (ComfyUI, vLLM) auto-start on boot via `/pre_start.sh`; every provisioner is idempotent and is
  the single reproduce-from-scratch path.
- **Every change added to a pod** (model, custom node, package, config, service) MUST be recorded
  as a provision step in `helpers/runpod/pod-registry.json` and the deployment manifest, with a
  `persistence` entry. A change is not done until documented + reproducible + restart-proof.
- **Verify restart-proofness:** after provisioning, `stop` + `start` the pod and re-run the smoke
  test. No manual SSH, no re-download should be needed. (2026-08-27 gap: Qwen VL vLLM and Qwen Edit
  ComfyUI do not auto-start on boot yet — add `/pre_start.sh` hooks.)

## Known gotchas (from 2026-08-27 session)

1. **Nested `powershell -File` arg binding.** Arrays/hashtables do NOT bind reliably across a
   nested `powershell -File` call. Pass GPU candidates as a comma-joined string
   (`-GpuTypeIdsCsv`, create-pod.ps1 splits it) and convert registry `$step.env` (PSCustomObject) →
   hashtable (`ConvertTo-Hashtable` in provision-pod.ps1).
2. **Manifest `previousPodIds` write** fails inside a nested invocation with a plain pipe +
   `+=`; build the list first and reassign via `Add-Member -PassThru`.
3. **GPU candidate order:** registry (`candidateGpuTypeIds`, cheapest-first) wins over the
   manifest's single `gpuTypeId`.
4. **Readiness success contract must prove model identity.** The Qwen VL provider's
   `ReadinessSuccessContractJson` must contain the ENABLED registered model's `ModelIdentifier`
   (the served model name). A stale AWQ-era contract breaks `CheckHealthAsync` with "does not prove
   the exact model identity" before any HTTP call — update the contract + `ServerIdentityPolicyJson`
   whenever the served model changes. There is no dbquery command for this; update via direct SQL
   with a row backup (user-sanctioned "update Model Manager directly in the database").
5. **Verification discipline:** never declare a pod done until smoke test passes AND (for Qwen VL)
   the deep proof passes AND Model Manager is confirmed updated in the DB AND the pod survives a
   restart. Confirm each provider endpoint via `runpod-provider-endpoints.sql` after sync.
6. **Host driver/CUDA vs pinned runtime (MANDATORY — 2026-08-27 A5000/Qwen-VL lesson).** GPU
   selection is not just VRAM. After creating any pod, verify the landed host's driver
   (`nvidia-smi --query-gpu=driver_version`) against the runtime's `torch.version.cuda`. The Qwen VL
   runtime is torch-CUDA-13 → needs driver >= CUDA 13 (580+). The A5000 pod landed on driver
   550.127.05 (CUDA 12.4) → vLLM died with `driver too old (12040)`. If the host driver is too old
   for the pinned runtime, recreate on another GPU/host — do not reuse that GPU for that workload.
7. **Qwen Edit `tokenizers` shadowing (2026-08-27).** A stale `tokenizers-0.23.1.dist-info` in the
   venv shadowed the pinned `0.22.2` (importlib.metadata resolved 0.23.1 → transformers 5.15.1
   refused to run, HTTP 502 on 3002). Fix = pin `tokenizers==0.22.2` + `rm` the stale `.dist-info`.
   Also: `start-comfyui.sh` binds `127.0.0.1`, which makes the RunPod proxy return 502 — the app
   must bind `0.0.0.0:<port>` for the HTTPS proxy to reach it.
8. **DWPose/Blackwell needs CUDA-12.8 torch.** The DWPose ComfyUI runtime requires
   `torch 2.7.0+cu128` (Blackwell GPU); the CUDA-11/12.4 default failed. Re-running the idempotent
   provisioner after pinning torch + fixing the `/pre_start.sh` port brought it up on `0.0.0.0:3003`.
9. **Background orchestrator can stall at `start-vllm`.** The Qwen VL auto-orchestrator stalled at
   the vLLM start step (no error). Resume manually over SSH: `ninja install` then
   `start-vllm.sh <port> --gpu-memory-utilization 0.9`; vLLM takes ~5–7 min to reach health.
10. **CRLF in `.sh` scripts breaks bash over SSH (2026-08-27).** The local `start-vllm.sh` is saved
    with CRLF line endings. `provision-pod.ps1` strips `\r` before piping (`$script -replace "\r", ""`),
    but a manual `scp` of the file verbatim, or a raw `Get-Content -Raw | ssh 'bash -s'` pipe, keeps the
    CR → bash rejects `set -euo pipefail\r` with "invalid option name" (and CRs garble the output). Fix:
    strip CR before piping (`(Get-Content ... -Raw) -replace "`r", ""`) or `scp` then run with CRLF
    removed. Also check the Qwen VL model was actually DOWNLOADED after provision (`ls model/*.safetensors`)
    and that `start-vllm.sh` was actually run — the start step can be skipped with no vLLM on the port.

## The 5 pods (function → deployment)

| # | Function | Deployment key | Port | Min VRAM | Cheapest-first candidates (secure) |
|---|---|---|---|---|---|
| 1 | Image generation (Juggernaut) | `image-gen-juggernaut-prod` | 3000 | 24 GB | A40 → A5000 → RTX 3090 Ti → RTX 3090 → RTX 4090 |
| 2 | Image editing (Qwen Edit 2511) | `image-edit-qwen-2511-prod` | 3002 | **48 GB** | A40 → A6000 → L40 → RTX 6000 Ada → RTX PRO 5000 → L40S |
| 3 | Image compiler (Qwen2.5-VL 7B) | `image-vision-qwen-vl-prod` | 3004 | 24 GB | A5000 → RTX 3090 Ti → RTX 3090 → A40 → RTX 4090 → L40S |
| 4 | Pose extraction (DWPose) | `pose-dwpose-prod` | 3003 | tiny | A5000 → RTX 4000 Ada → RTX 3090 Ti → RTX PRO 4500 |
| 5 | Image editing backup (A40 preserved) | `image-edit-qwen-2511-a40-preserved` | 3002 | **48 GB** | A40 → A6000 → L40 → ... (recreate only on explicit request) |

`identity-conditioning-proof` is an informational 6th entry; recreate only on explicit request.

## Procedure

### Step 1 — Inventory: which pods are down?

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/list-pods.ps1
```

Compare `Status` against the registry. A pod is a recreation candidate when its pod is not
`RUNNING` (EXITED) **and** starting/migrating it fails for GPU capacity. Report the down pods and
get the user's go-ahead before creating anything.

### Step 2 — Check the GPU catalog (pricing/VRAM)

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1 -SortByPrice
```

This refreshes live secure/community prices. Candidate lists in the registry are already ordered
cheapest-first; you do not need the catalog to pick — but show it when the user wants a manual
choice or a screenshot comparison.

### Step 3 — Create the pod (automatic GPU selection)

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/recreate-pod.ps1 `
  -ManifestPath helpers/runpod/deployments/<pod>/deployment.json
```

`recreate-pod.ps1` runs create → provision → smoke and, with the sync flags, the Model Manager
update. To create only (no provision), pass `-SkipProvision -SkipSmokeTest`.

- The manifest's `podId` is updated to the new pod and the previous id is recorded in
  `previousPodIds`. Old (dead) pods are **never terminated without explicit approval**.
- If creation fails with a capacity error for the whole candidate list, report which candidates
  failed and consult the registry / user for alternates (e.g. allow community cloud or a higher
  tier). Log the attempt in `artifacts/runpod/pod-creation-state.json`.

### Step 4 — Provision from scratch + smoke test

If you created only in Step 3:

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/provision-pod.ps1 `
  -ManifestPath helpers/runpod/deployments/<pod>/deployment.json
```

This pipes each registry provision/start script over SSH and then verifies:
- **readiness** (e.g. `/system_stats`, `/health`)
- **identity probe** (e.g. `/object_info/CheckpointLoaderSimple` contains the model, `/v1/models`
  contains the served model name)

For the **Qwen VL** pod the deep proof is required before Model Manager sync:

```powershell
python helpers/runpod/qwen-vl-edit-compiler/prove-one-image.py `
  <port> <source-image> <raw-response-path>   # against the HTTPS proxy endpoint
```

For ComfyUI pods the identity probe is the acceptance check; a real render/edit proof is optional.

### Step 5 — Update Model Manager (direct DB, CAS-guarded)

Read the current provider URL first:

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql `
  DreamGenClone.DbQuery/queries/runpod-provider-endpoints.sql
```

Then sync the endpoint for the matching provider (id is in the registry's `modelManager` block):

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/recreate-pod.ps1 `
  -ManifestPath helpers/runpod/deployments/<pod>/deployment.json `
  -SkipCreate -SkipProvision -SkipSmokeTest `
  -UpdateModelManager -ProviderId <PROVIDER_ID> -ExpectedCurrentBaseUrl <CURRENT_URL>
```

Only Juggernaut, Qwen Edit, and Qwen VL have providers to update. DWPose and the preserved A40 pod
are intentionally **not** in Model Manager — record their endpoints in the registry/summary only.
Verify afterwards with the same `runpod-provider-endpoints.sql` query and report old/new pod ids,
endpoints, and provider status.

## Safety rules

- Never create a pod while a same-named pod is already `RUNNING` (the script refuses).
- Never terminate/delete old pods or volumes without explicit user approval.
- Never update Model Manager before the new pod is provisioned and smoke-tested.
- Never bypass the endpoint compare-and-swap guard (`-ExpectedCurrentBaseUrl`).
- Never silently choose among multiple available GPUs outside the ordered candidate list; the
  candidate order IS the policy (cheapest first that fits + is fast enough).
- A fresh pod has no models — if provisioning is skipped, the endpoint is NOT ready; do not sync
  Model Manager.
- Prefer SECURE cloud (SSH TCP + proxy guaranteed). COMMUNITY is cheaper but spot/preemptible and
  no public-IP guarantee — only with explicit approval.
- **Restart-proof before done:** after provisioning + smoke, `stop` + `start` the pod and re-run
  the smoke test. If a service or model is missing without manual SSH, fix the provisioner/
  `/pre_start.sh` (never hand-patch a live pod and call it done). Document every change in
  `pod-registry.json` per the persistence standard.

## Canonical command (full auto, one pod)

```powershell
.\helpers\runpod\recreate-pod.ps1 `
  -ManifestPath helpers/runpod/deployments/<pod>/deployment.json `
  -ProviderId <PROVIDER_ID> -ExpectedCurrentBaseUrl <CURRENT_URL> -UpdateModelManager
```

(If the manifest already points at a provisioned pod, add `-SkipCreate`.)

## Related

- `helpers/runpod/POD-PERSISTENCE.md` — persistence standard (MANDATORY reading).
- `helpers/runpod/POD-CONNECTIONS-AND-MODEL-MANAGER.md` — inventory and endpoint boundary.
- `helpers/runpod/GPU-SELECTION.md` — GPU catalog, tiers, manual checklist.
- `helpers/runpod/pod-registry.json` — the pod + GPU option record (single source of truth),
  incl. per-pod `persistence` metadata.
- `.github/copilot-instructions.md` — global rule: pod changes must be documented + persistent.
- `.github/skills/runpod-pod-migration/SKILL.md` — migration-only path (no pod creation).
- `.github/instructions/runpod-pod-migration.instructions.md` — endpoint sync rules.

## Session log / worked example (2026-08-27)

- Juggernaut recreated end-to-end: pod `817glbpee7l99q` (A40), checkpoint 6.7 GB, `IDENTITY_OK`,
  Model Manager updated to `https://817glbpee7l99q-3000.proxy.runpod.net`. NOTE: this plain pod
  has NO IP-Adapter nodes → identity/2-person renders fail (`IPAdapterUnifiedLoader does not
  exist`); identity renders need the `identity-conditioning-proof` pod (migration successor
  `ncsmze3anko7w2`).
- Qwen VL recreated: pod `6rmvao8y9kadhv` (A5000, cheapest), 16 GB model + vLLM venv on
  `/workspace`. Background orchestration stalled (never reached start-vllm); driven manually
  (ninja + start-vllm.sh).
- Found + fixed tooling bugs: nested `-File` array/hashtable binding, manifest `previousPodIds`
  write, candidate-order (registry over manifest), readiness-contract model identity.
