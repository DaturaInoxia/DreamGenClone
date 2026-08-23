# B-032 — RunPod + ComfyUI Automation (Plan)

**State:** `designed` (formal implementation package)
**Scope:** Hosted-GPU ComfyUI development and DreamGenClone integration
**Plan author:** Copilot session 2026-08-22
**Backlog ref:** `specs/Planning/backlog.md` → B-032
**Depends on:** `specs/Planning/B-032-scene-image-generator.md`

---

## 1. Objective

Give the developer and the coding agent reproducible scripts to manage the RunPod Pod,
ComfyUI models, workflows, and generation, and to let DreamGenClone call ComfyUI over HTTP.

The scripts are local repository artifacts. They do not store the RunPod API key or the
ComfyUI URL/password. Access is provided by environment variables or a small local file the
developer creates and never commits.

## 2. Setup — what the user must do to grant access

The agent cannot and should not obtain the developer's RunPod API key or open the pod's public
URL itself. The developer completes the "connection bootstrap" once:

### 2.1 Generate a RunPod API key

1. Log in to the RunPod console.
2. Go to **User Settings → API Keys** (or the equivalent page under the account menu).
3. Create a fresh, narrow-scope API key **only if** the key UI supports scoping to Pods/read and
   stop/terminate. If no scoping is available, use the shortest-lived / most-restricted key
   reasonable, then revoke it after experiments.
4. Copy the key. Do not paste it into the chat.

### 2.2. Store the key outside git

The developer records it in a local file that is **git-ignored**, e.g.:

```text
helpers/runpod/.runpod-env.ps1
```

suggested content:

```powershell
$env:RUNPOD_API_KEY = "rp-xxxxxxxx..."
$env:COMFYUI_URL = "https://POD_ID-3000.proxy.runpod.net"
```

Add `helpers/runpod/.runpod-env.ps1` to `.gitignore` so it is never committed.

### 2.3. Keep the pod private

The developer keeps the HTTP service on port `3000` behind the RunPod proxy and does not publicly
expose port `8188` unless authenticated. The local scripts talk to `$env:COMFYUI_URL` over HTTPS.

### 2.4. What the agent is allowed to do

After the developer sets the environment, the agent may run the helpers **only when asked**:

- `helpers/runpod/model.ps1` — download/install and validate a target checkpoint into
  `ComfyUI/models/checkpoints` via SSH or the filesystem entrypoint when available.
- `helpers/runpod/workflow.ps1` — print/validate/export a saved workflow JSON.
- `helpers/runpod/generate.ps1` — POST a workflow to ComfyUI `/prompt` and poll the output
  (read the image bytes) without modifying account billing settings.
- `helpers/runpod/pod.ps1` — start/stop/terminate the pod and read status/usage.

Destructive operations marked clearly: **terminate** and **delete network volume** always require
an explicit user command; they are never run automatically.

## 3. Design

```text
helpers/runpod/
  .runpod-env.ps1            # created by user; git-ignored
  model.ps1                  # install/validate a checkpoint
  workflow.ps1               # list/save/validate workflow JSON
  generate.ps1               # queue via ComfyUI API + fetch result
  pod.ps1                    # pod status/start/stop/terminate (terminate requires confirmation)
  common.ps1                 # load env, invoke-runpod API, log, exit codes
```

Functions the helpers share:
- `Get-RunPodEnv` — dot-source `.runpod-env.ps1`, fail if missing key/URL.
- `Invoke-RunPodApi` — authorized RunPod REST calls using `$env:RUNPOD_API_KEY`.
- `Invoke-ComfyUi` — POST to `$env:COMFYUI_URL` for `/prompt`, poll `/queue`, `/history`.

## 4. Implementation tasks (ordered)

1. `common.ps1`: env loader, API helper, exit-code conventions, `.runpod-env` detection.
2. `model.ps1`: download to `ComfyUI/models/checkpoints`, verify file size/hash, list models.
3. `workflow.ps1`: validate a workflow JSON against required node keys; export to gist-friendly file.
4. `generate.ps1`: read workflow JSON, inject prompt/seed/size, POST to `/prompt`, poll, save PNG.
5. `pod.ps1`: status; start/stop; terminate with explicit confirmation and budget/cleanup sheet.
6. `.env.example`, `.gitignore` entry, README.
7. Integration contract: `IImageGenerationClient` → `ComfyUIImageClient` (HTTP POST + poll) plus a
   provider record carrying `ComfyUIUrl` and a private token; persisted workflow JSON per image.
8. Tests: script parse, JSON validate, fake ComfyUI responder, pod-api fake replay. All tests
   offline; no live pod, no credentials.

## 5. Non-goals (this phase)

- No training/fine-tuning.
- No automatic pod termination or billing action without explicit confirmation.
- No public URLs stored in git.
- No RunPod MCP installer run by the agent; if the developer chooses MCP, they install it and wire
  OAuth themselves.

## 6. Reopen procedure

When the developer returns:

1. Ensure `helpers/runpod/.runpod-env.ps1` exists with key + `COMFYUI_URL`.
2. Start the pod from the RunPod console (or `pod.ps1 start`).
3. Confirm `helpers/runpod/list-url` reaches the proxy.
4. Have the agent run `model.ps1` to install the target checkpoint, then `workflow.ps1`,
   then `generate.ps1` to produce one image.
5. Re-validate with a fixed seed; compare against baseline.