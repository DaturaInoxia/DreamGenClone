# RunPod Connections and Model Manager

This runbook identifies each dedicated RunPod deployment, connects through RunPod's
dynamic SSH-over-TCP mapping, and records the application configuration boundary.

## Safety Rules

- Pod IDs and persistent volumes are stable deployment records. Public IP addresses and SSH TCP
  ports are runtime values that can change after every start; never commit them.
- The application never uses SSH, a RunPod host, a TCP port, or a pod filesystem path. SSH is only
  for operator provisioning and diagnosis.
- Start only the pod required for the current operation, and stop it after its configured idle
  period or immediately after a maintenance/proof operation.
- Do not terminate a pod or delete a volume through this runbook. That requires separate explicit
  approval.
- Keep the private key at `artifacts/runpod/ssh_ed25519` local and untracked. Its matching public
  key must be registered in RunPod account SSH Public Keys.

## Dedicated Pod Inventory

| Capability | Deployment key | Stable pod ID | GPU | Persistent volume | Inference port | Manifest | State at record time |
|---|---|---|---|---:|---:|---|---|
| Image generation, Juggernaut Ragnarok | `image-gen-juggernaut-prod` | `mb5pwrm14psof5` | A40 | 40 GB | 3000 | `deployments/image-gen-juggernaut/deployment.json` | Migrated 2026-08-26 to `orknbkfc0pxktv` (RUNNING); recycle-proof provisioner installed — see `deployments/image-gen-juggernaut/README.md` |
| Image editing, Qwen Image Edit 2511 | `image-edit-qwen-2511-prod` | `jkms7ljhb54we9` | L40S | 85 GB | 3002 | `deployments/image-edit-qwen-2511/deployment.json` | Stopped |
| Preserved unused Qwen Image Edit allocation | `image-edit-qwen-2511-a40-preserved` | `u1bykx2grhns82` | A40 | 85 GB | 3002 | `deployments/image-edit-qwen-2511-a40-preserved/deployment.json` | Stopped; do not use or delete without approval |
| Image identification/compiler, Qwen VL | `image-vision-qwen-vl-prod` | `h7c0jzlxl1u0rn` | L40S | 85 GB | 3004 | `deployments/image-vision-qwen-vl/deployment.json` | Migrated 2026-08-27 to `yx7zudzunz95b3` (RUNNING); model swapped to `huihui-ai/Qwen2.5-VL-7B-Instruct-abliterated` (uncensored) — see `qwen-vl-edit-compiler/README.md` |
| Pose extraction, DWPose | `pose-dwpose-prod` | `wwbl2kjjvizb46` | RTX PRO 4500 Blackwell | 40 GB | 3003 | `deployments/pose-dwpose/deployment.json` | Migrated 2026-08-26 to `bmcqknli61o49b` (`dreamgen-pose-dwpose-pro-4500-migration`, RUNNING); runtime provisioned + service-proven; not in Model Manager — see `deployments/pose-dwpose/README.md` |

The pre-existing all-in-one pod is a migration source only. Do not add it to Model Manager and do
not assign new application traffic to it.

## Start, Discover, and Connect with TCP SSH

Run commands from the repository root. Replace `<manifest>` with the relative manifest path from
the inventory table.

1. Validate and start the intended pod:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File helpers/runpod/deployment.ps1 `
     -Action validate -ManifestPath <manifest>

   powershell -NoProfile -ExecutionPolicy Bypass -File helpers/runpod/deployment.ps1 `
     -Action start -ManifestPath <manifest>
   ```

2. Wait until `runtime.ports` appears in status. The `22/tcp` entry contains the current public IP
   and port. Do not proceed while `runtime` is `null`.

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File helpers/runpod/deployment.ps1 `
     -Action status -ManifestPath <manifest>
   ```

3. Copy only the current `22/tcp` mapping into the ignored local connection file:

   ```powershell
   $env:RUNPOD_SSH_USER = "root"
   $env:RUNPOD_SSH_HOST = "<runtime.ports IP for 22/tcp>"
   $env:RUNPOD_SSH_PORT = "<runtime.ports publicPort for 22/tcp>"
   ```

   Persist those three lines in the ignored `artifacts/runpod/.ssh-env.ps1` only for the pod being
   maintained. Do not put them in a deployment manifest, this document, application settings, or
   git.

4. Prove key-only TCP SSH before changing the pod:

   ```powershell
   ssh -o BatchMode=yes -o PasswordAuthentication=no -o KbdInteractiveAuthentication=no `
     -o IdentitiesOnly=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=NUL `
     -i artifacts/runpod/ssh_ed25519 -p $env:RUNPOD_SSH_PORT `
     root@$env:RUNPOD_SSH_HOST "whoami; nvidia-smi --query-gpu=name,memory.total --format=csv,noheader; df -h /workspace"
   ```

   Expected user: `root`. Confirm the GPU and `/workspace` capacity match the manifest before
   provisioning, installing, or starting an inference service.

5. Use the helper for subsequent read-only or maintenance commands:

   ```powershell
   . helpers/runpod/.ssh-env.ps1
   .\helpers\runpod\ssh.ps1 -Command "nvidia-smi"
   ```

6. Stop the pod after the work completes:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File helpers/runpod/deployment.ps1 `
     -Action stop -ManifestPath <manifest>
   ```

## Application Endpoint Boundary

For each provisioned service, the operator discovers the current HTTP proxy endpoint from RunPod
after startup and writes it into the provider's **Base URL** in Model Manager. The application uses
that HTTPS endpoint, not `127.0.0.1`, an SSH tunnel, the public TCP SSH mapping, or `/workspace`.

Before enabling a provider/model assignment:

1. Start the dedicated pod and its pinned runtime.
2. Verify its readiness endpoint and its exact deployment/model identity.
3. Configure the current HTTPS inference endpoint in Model Manager.
4. Use **Test Connection** and the capability-specific proof before enabling or assigning it.
5. Stop it again when the explicit lifecycle policy allows.

The current Model Manager schema does not persist a RunPod pod ID. Record the deployment key, pod
ID, runtime revision, artifact revision, and cost notes in the provider/model notes and identity
policy fields. SSH connection data stays in ignored local configuration.

## Fast Start and Model Manager Sync

Use `start-and-sync-provider.ps1` when a dedicated provider record already exists and its pod
runtime starts its inference service automatically. The command starts the manifest pod, waits for
its HTTP mapping, verifies both readiness and the manifest's model-specific identity probe, and
then atomically changes exactly one `Providers.BaseUrl` row:

```powershell
.\helpers\runpod\start-and-sync-provider.ps1 `
  -ManifestPath helpers/runpod/deployments/image-gen-juggernaut/deployment.json `
  -ProviderId <MODEL_MANAGER_PROVIDER_ID> `
  -ExpectedCurrentBaseUrl <CURRENT_MODEL_MANAGER_BASE_URL> `
  -RuntimeTimeoutSeconds <EXPLICIT_TIMEOUT> `
  -PollIntervalSeconds <EXPLICIT_POLL_INTERVAL>
```

The expected URL is a compare-and-swap guard. If another process or user changed the provider
after the command began, the DB update fails without overwriting that change. An already-current
URL succeeds as a no-op. The update command changes only `BaseUrl` and `UpdatedUtc` in the live
development DB:

```powershell
dotnet run --project DreamGenClone.DbQuery -- provider-endpoint-update `
  <PROVIDER_ID> <EXPECTED_CURRENT_BASE_URL> <VERIFIED_NEW_BASE_URL>
```

When the primary pod reports unavailable GPU capacity, use RunPod's **Migrate** action and wait for
the migrated pod to be running, then rerun the same command. Migration transfers the local volume
to a new pod and therefore produces a new pod ID and proxy URL. The script lists the account's pods,
requires exactly one `RUNNING` successor with the same image and the migration name derived from the
manifest, and derives the new proxy URL from that ID. No manifest edit is needed.

A migrated pod is trusted once RunPod reports it as `RUNNING` and exposes the manifest's HTTP port.
The script does not repeat readiness or model-identity validation for that migrated pod; it updates
Model Manager when the derived proxy URL differs from the guarded current URL.

An explicitly supplied replacement manifest remains available when a pod name has multiple running
matches or a separately tracked replacement must be selected:

```powershell
.\helpers\runpod\start-and-sync-provider.ps1 `
  -ManifestPath <PRIMARY_MANIFEST> `
  -ReplacementManifestPath <PREPROVISIONED_REPLACEMENT_MANIFEST> `
  -ProviderId <MODEL_MANAGER_PROVIDER_ID> `
  -ExpectedCurrentBaseUrl <CURRENT_MODEL_MANAGER_BASE_URL> `
  -RuntimeTimeoutSeconds <EXPLICIT_TIMEOUT> `
  -PollIntervalSeconds <EXPLICIT_POLL_INTERVAL>
```

An explicitly supplied replacement is accepted only after its readiness and exact model-identity
probes pass. A discovered migrated pod follows the `RUNNING`-status rule above. If the derived URL
already matches Model Manager, the DB operation is a no-op. The script never creates, migrates,
terminates, or deletes a pod.

### Local Volume Limitation

The current manifests use RunPod local pod volumes, not reusable network volumes. RunPod's API can
resume an existing pod, but its pod-update contract cannot select another GPU host or attach that
local volume to a newly created pod. Creating a new pod after a capacity error would therefore
create a fresh volume with no proven models.

For that reason, fresh-pod creation by this tooling is forbidden. Use RunPod's automatic migration
to transfer the existing local volume; the script only discovers the resulting running pod and
updates Model Manager after verifying its service and model identity. A future deployment revision
could instead use a RunPod network volume and record its stable volume ID. This guard prevents a
quick endpoint update from routing the application to an empty runtime.

## Qwen VL Image Identification/Compiler

The Qwen VL pod is the next Model Manager integration. It is distinct from Qwen Image Edit: Qwen VL
accepts a source image plus intent and returns a structured compiler result through an OpenAI-
compatible chat-completions endpoint.

### Provider Record

Create or update one enabled provider after the `3004` vLLM runtime is healthy:

| Field | Required value |
|---|---|
| Name | `RunPod Qwen VL image compiler` |
| Base URL | Current HTTPS endpoint for pod `h7c0jzlxl1u0rn`; do not use an SSH address |
| Chat Completions Path | `/v1/chat/completions` |
| Readiness Path | `/health` |
| Readiness Success Contract | Explicit JSON contract proven against the pinned vLLM runtime |
| Content Policy | Explicit configured policy; never `Unknown` |
| Timeout Seconds | Explicit persisted value covering one request |
| Lifecycle Strategy Identifier | `ManagedDedicatedPod` |
| Transition Timeout and Margin | Explicit persisted values covering the measured runtime startup envelope |
| Maximum Active Requests and Queue Capacity | Explicit persisted limits for this one-GPU service |
| Credential Reference and API key | Secret-backed configured values; never a plaintext value in notes or provenance |
| Server Identity Policy | Require deployment key `image-vision-qwen-vl-prod`, model identifier, and pinned runtime revision |
| Allowed Network Boundary | The configured RunPod HTTPS inference endpoint only |

The exact timeout, margin, concurrency, queue, credentials, and readiness JSON must come from the
accepted runtime proof and UI-backed persisted data. Do not add hidden defaults or a provider
fallback.

### Registered Model Record

Create one enabled registered model under that provider:

| Field | Required value |
|---|---|
| Display Name | `Qwen2.5-VL 7B AWQ image compiler` |
| Model Identifier | `qwen2.5-vl-7b-edit-compiler` — the vLLM `--served-model-name` (see `start-vllm.sh`). The app sends `ModelIdentifier` as the `model` field and validates the response identity against it, so the registered value MUST equal the served name. The pinned HF repo id `Qwen/Qwen2.5-VL-7B-Instruct-AWQ` is recorded by Artifact Revision; do not register the repo id as the model identifier. |
| Model Kind | `Text` |
| Supports Image Input | Enabled |
| Maximum Input Images | `1` |
| Accepted Input Media Types | Values proven by the runtime, such as `image/png` and `image/jpeg` only if accepted |
| Image byte/pixel/dimension limits | Explicit proof-backed limits |
| Maximum Response Bytes | Explicit schema/proof-backed limit |
| Runtime Revision | `vllm==0.27.1` |
| Artifact Revision | `536a35794df8831aa814970ee8f89eff577e7718` |
| Parameter Count | `7B` |
| Quantization | `AWQ` |
| Notes | Deployment key, stable pod ID, L40S/85 GB allocation, and the source-controlled runtime path `helpers/runpod/qwen-vl-edit-compiler` |

### Function Assignment

In **Model Manager**, assign this registered model as the sole active default for:

`RolePlaySceneImageEditPromptCompiler`

Do not assign it to `RolePlaySceneImageEditor`; that function remains the separate Qwen Image Edit
2511 ComfyUI deployment. Do not configure a fallback model for the compiler function.

## Other Pods and Model Manager

| Pod | Application role | Current Model Manager action |
|---|---|---|
| Juggernaut | `RolePlaySceneImage` | Configure/retain the ComfyUI image provider and the enabled Juggernaut image model only after endpoint and workflow identity proof. |
| Qwen Image Edit 2511 | `RolePlaySceneImageEditor` | Configure/retain the ComfyUI image provider, pinned editor artifacts/settings, and the editor function default only after the port `3002` endpoint is healthy. |
| Qwen VL | `RolePlaySceneImageEditPromptCompiler` | Configure as described above after vLLM provisioning and proof. |
| DWPose | No Model Manager function assignment yet | Provisioned + service-proven 2026-08-26 on migration successor `bmcqknli61o49b` (endpoint `https://bmcqknli61o49b-3003.proxy.runpod.net`; repro: `deployments/pose-dwpose/provision-runtime.sh` + `README.md`). Still do not register as an image model or compiler until its dedicated service contract and application capability integration exist. |
| Preserved Qwen A40 pod | None | Keep disabled and unassigned. |

## After Any Restart

Public endpoints and TCP SSH ports can rotate. Always repeat status discovery, update only ignored
SSH connection state, verify readiness and identity, then update the provider Base URL only if the
inference endpoint changed. Never update application configuration with an SSH mapping.

## Recycle-Proof Provisioning (image-gen-juggernaut)

When the Juggernaut pod starts returning `400: ckpt_name 'juggernautXL_ragnarok.safetensors'
not in [...]` (or "no prompt works in the app" right after a recycle/migrate), the cause is the
lost `/ComfyUI/extra_model_paths.yaml` — the container overlay is wiped but the checkpoint is
still on `/workspace`. Do NOT re-diagnose this from scratch.

Run the idempotent provisioner once over SSH (it also patches `/pre_start.sh` to self-heal on
stop/resume):

```bash
ssh -i artifacts/runpod/ssh_ed25519 -p <SSH_PORT> root@<SSH_IP> \
  'bash -s' < helpers/runpod/deployments/image-gen-juggernaut/provision-runtime.sh
```

Full runbook and persistent-vs-ephemeral table:
`helpers/runpod/deployments/image-gen-juggernaut/README.md`.