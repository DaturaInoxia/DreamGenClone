# RunPod + ComfyUI Automation Helpers

Local scripts to manage a RunPod pod running ComfyUI and to let DreamGenClone later call
ComfyUI. These are repository-local; they do **not** store or transmit secrets on their own.

## One-time setup (you must do this)

1. Generate a narrow RunPod API key in RunPod console → **User Settings → API Keys**.
2. Find your pod's ComfyUI proxy base URL, e.g. `https://<POD_ID>-3000.proxy.runpod.net`.
3. Create (do NOT commit) `helpers/runpod/.runpod-env.ps1`:

```powershell
$env:RUNPOD_API_KEY = "rp-xxxxxxxx"
$env:COMFYUI_URL    = "https://<POD_ID>-3000.proxy.runpod.net"
$env:RUNPOD_POD_ID  = "<POD_ID>"
$env:CIVITAI_API_TOKEN = "<CIVITAI_TOKEN>" # only for authenticated model downloads
```

Make sure `.runpod-env.ps1` is covered by `.gitignore`.

## Scripts

- `common.ps1` — loads env, RunPod REST + ComfyUI HTTP helpers.
- `model.ps1` — download/install a checkpoint into `ComfyUI/models/checkpoints`, optional hash check; `-List` to list local models.
- `workflow.ps1` — validate/export a saved workflow JSON.
- `generate.ps1` — POST a workflow to `/prompt`, poll history, save the image.
- `pod.ps1` — pod `status` / `start` / `stop`; `usage`; `terminate` is explicit-confirm only.
- `deployment.ps1` — validate/preview one manifest-defined deployment and operate its assigned pod.
- `ssh.ps1` — SSH maintenance through the local `artifacts/runpod/.ssh-env.ps1` connection file.
- `install-model-remote.ps1` — authenticated checkpoint download to the persistent `/workspace` volume.
- `runpod-billing-query.ps1` — RunPod `GET /v2/billing` cost query (`-StartTime`/`-EndTime`/`-BucketSize hour|day|month`). Promoted from `artifacts/tmp/` (2026-09-02).

## Usage

```powershell
# after creating .runpod-env.ps1
.\helpers\runpod\model.ps1 -List
.\helpers\runpod\model.ps1 -ModelName some.safetensors -SourceUrl <url>
.\helpers\runpod\workflow.ps1 -WorkflowPath workflow.json
.\helpers\runpod\generate.ps1 -WorkflowPath workflow.json -Prompt "..." -Seed 123
.\helpers\runpod\pod.ps1 -Action status
.\helpers\runpod\pod.ps1 -Action stop
.\helpers\runpod\pod.ps1 -Action terminate   # requires typing exact PodId
.\helpers\runpod\deployment.ps1 -Action validate -ManifestPath helpers\runpod\deployments\image-gen-juggernaut\deployment.json
.\helpers\runpod\deployment.ps1 -Action preview -ManifestPath helpers\runpod\deployments\image-gen-juggernaut\deployment.json
```

## Manifest-driven deployments

New model deployments are defined under `helpers/runpod/deployments/<deployment-name>/`. The
manifest records capability, model/runtime identity, GPU/container requirements, persistent volume,
inference port, SSH-over-TCP port, and readiness identity. A blank `podId` means the deployment has
not been created yet and is valid for offline validation/preview only.

`deployment.ps1` currently supports offline `validate` and `preview`, plus `status`, `start`, and
`stop` for an already assigned pod. The next provisioning slice will add explicit-confirm creation
of a new pod and volume, expose SSH over TCP, discover the assigned endpoint, and write the resulting
runtime assignment to local ignored state. It will never modify the existing legacy pod, and it will
not include automatic terminate or volume-delete behavior.

Before live provisioning, replace every `REQUIRED_*` value in the selected manifest and confirm the
RunPod API key has the required create/start/stop permissions. Resource creation is billable and
requires a separate explicit approval after the preview output has been reviewed.

## Dependencies / caveats

- ComfyUI must be reachable at `$env:COMFYUI_URL` and expose `/prompt`, `/queue`, `/history`,
  `/view`.
- The job poller waits up to 10 minutes; adjust if your workflow is longer.
- `pod.ps1` uses the RunPod GraphQL API; field names may need tuning to the exact console schema.
- Run scripts from the repo root so relative `ComfyUI/...` paths resolve, or set the paths to your
  pod layout.
- Terminate/delete actions are intentionally never automatic; each requires explicit user input.

## SSH setup on another development machine

RunPod SSH keys are account-level. A pod does not normally have an SSH-key editor. The key used by
the machine must already be listed in RunPod account settings under **SSH Public Keys**. Add the
complete one-line public key, beginning with `ssh-ed25519`; do not add the fingerprint or private key.

1. Clone/pull the repository on the other machine.
2. Copy the matching private key to `artifacts/runpod/ssh_ed25519` and ensure its public key is
   registered in RunPod. Never commit either key. If the machine has a different key, register that
   key and copy its private half to this exact local path.
3. In the pod's **Connect** tab, copy the current **SSH over exposed TCP** values. Set
   `artifacts/runpod/.ssh-env.ps1` to the current public IP and port:

   ```powershell
   $env:RUNPOD_SSH_USER = "root"
   $env:RUNPOD_SSH_HOST = "<PUBLIC_IP>"
   $env:RUNPOD_SSH_PORT = "<PUBLIC_SSH_PORT>"
   ```

   The basic `SSH` gateway command is useful for interactive access, but it allocates a PTY and
   echoes piped commands. Use the exposed TCP route for automation and model installation.
4. Test from the repository root, bypassing stale host-key entries on a new machine:

   ```powershell
   ssh -o BatchMode=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=NUL `
     -o IdentitiesOnly=yes -i artifacts\runpod\ssh_ed25519 `
     -p <PUBLIC_SSH_PORT> root@<PUBLIC_IP> whoami
   ```

   Expected output is `root`. A `Host key verification failed` message is local host-key state;
   `Permission denied (publickey)` means the public key does not match the private key or is not
   registered/authorized for the pod.
5. Set the same values in `.ssh-env.ps1`, then use:

   ```powershell
   .\helpers\runpod\ssh.ps1 -Command "whoami"
   ```

## Persistent model storage

The container root filesystem is small (typically 5 GB). Store checkpoints only under
`/workspace/comfyui/models/checkpoints`, which is on the persistent volume. The installer refuses
to use `/ComfyUI/models/checkpoints` so a model cannot silently fill the container disk.

For the current Juggernaut Ragnarok model:

```powershell
.\helpers\runpod\install-model-remote.ps1 `
  -ModelName "juggernautXL_ragnarok.safetensors" `
  -SourceUrl "https://civitai.com/api/download/models/1759168?fileId=1659952"
```

After a new pod or ComfyUI restart, verify the checkpoint through:

```powershell
. helpers\runpod\.runpod-env.ps1
$r = Invoke-RestMethod "$env:COMFYUI_URL/object_info/CheckpointLoaderSimple"
$r.CheckpointLoaderSimple.input.required.ckpt_name[0] |
  Where-Object { $_ -eq "juggernautXL_ragnarok.safetensors" }
```

## Integration note

DreamGenClone will call ComfyUI over HTTP via a new `ComfyUIImageClient` behind `IImageGenerationClient`,
plus a provider record storing `ComfyUIUrl` and a private token. Workflow JSON + seed are persisted
with each image for reproducibility. That integration is a follow-on implementation task, not part
of these helpers.