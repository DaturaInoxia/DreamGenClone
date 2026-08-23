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
```

Make sure `.runpod-env.ps1` is covered by `.gitignore`.

## Scripts

- `common.ps1` — loads env, RunPod REST + ComfyUI HTTP helpers.
- `model.ps1` — download/install a checkpoint into `ComfyUI/models/checkpoints`, optional hash check; `-List` to list local models.
- `workflow.ps1` — validate/export a saved workflow JSON.
- `generate.ps1` — POST a workflow to `/prompt`, poll history, save the image.
- `pod.ps1` — pod `status` / `start` / `stop`; `usage`; `terminate` is explicit-confirm only.

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
```

## Dependencies / caveats

- ComfyUI must be reachable at `$env:COMFYUI_URL` and expose `/prompt`, `/queue`, `/history`,
  `/view`.
- The job poller waits up to 10 minutes; adjust if your workflow is longer.
- `pod.ps1` uses the RunPod GraphQL API; field names may need tuning to the exact console schema.
- Run scripts from the repo root so relative `ComfyUI/...` paths resolve, or set the paths to your
  pod layout.
- Terminate/delete actions are intentionally never automatic; each requires explicit user input.

## Integration note

DreamGenClone will call ComfyUI over HTTP via a new `ComfyUIImageClient` behind `IImageGenerationClient`,
plus a provider record storing `ComfyUIUrl` and a private token. Workflow JSON + seed are persisted
with each image for reproducibility. That integration is a follow-on implementation task, not part
of these helpers.