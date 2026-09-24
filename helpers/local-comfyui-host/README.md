# local-comfyui-host — local ComfyUI (WOOD-GAME-MAIN) host scripts

Scripts that run **on** the local ComfyUI host (or drive it over SSH), not in the app.
Host facts: `WOOD-GAME-MAIN`, Windows 11, RTX 5080 16 GB, ComfyUI 0.34.0 at `D:\ComfyUI`,
**LAN `http://192.168.0.11:8188`** (reassigned 2026-09-21; was `192.168.0.16` — that address no
longer routes and was why the box looked unreachable), public front `https://comfy.kenacwood.net`
(Cloudflare). SSH: `wood-game-main\kenac@192.168.0.11`, key `~/.ssh/dgcomfy_ed25519`.

ComfyUI is launched by the **scheduled task `ComfyUI 8188`** (state `Running`), so restarting it
is `Stop-ScheduledTask -TaskName 'ComfyUI 8188'` / `Start-ScheduledTask …` — that survives the SSH
session closing, whereas a bare `Start-Process` child is killed with the session. It is NOT a
torch install owned by this repo's venv: `D:\ComfyUI\.venv\Scripts\python.exe`, torch `2.14.0+cu130`.

See `docs/local-comfyui-model-manager-setup.md` for the Model Manager registration procedure.

## Scripts

| Script | Purpose |
|---|---|
| `download-qwen-aio-checkpoint.ps1` | Downloads the `Qwen-Rapid-AIO-NSFW-v23.safetensors` merged checkpoint (28,431,840,023 bytes) into `D:\ComfyUI\models\checkpoints`. Idempotent + resumable (`curl -C -`); verifies the exact byte count and prints the SHA-256. |
| `diagnose-download-prereqs.ps1` | Reports the host's PowerShell version, `curl.exe` presence, target-directory existence, and any parse errors in the downloader. Run first when a download "does nothing". |
| `provision-xlabs-flux.ps1` | **Runs ON the host.** Installs the XLabs FLUX runtime (`x-flux-comfyui` @ `00328556…`) + the FLUX OpenPose controlnet (`flux-openpose-controlnet-raulc0399.safetensors`, SHA-256 verified) that the dual-base-location proofs need. Required before any FLUX-OpenPose proof can run locally. Idempotent. |
| `run-local-proof.ps1` | **Runs from the dev box.** Local-ComfyUI replacement for `helpers/runpod/serverless/smoke-test.ps1`: uploads an image manifest to `/upload/image`, submits a workflow to `/prompt`, polls `/history/<id>`, saves outputs from `/view`. Preflights every `class_type` against the host and fails fast listing missing ones. |
| `run-local-aio-edit-proof.ps1` | **Runs from the dev box.** The app's merged-checkpoint Qwen-Image-Edit graph (what `ComfyUIImageEditingClient` submits) against the local host. `-IdentityBindingsPath <json>` reproduces either of the app's identity actions end to end — see below. |
| `run-sequential-identity.ps1` | **Runs from the dev box.** Applies the identity pass to a multi-character scene **one character per edit**, chaining each result into the next pass's source. Required for more than one character — see below. |

## Identity face pass (`run-local-aio-edit-proof.ps1 -IdentityBindingsPath`)

The app has **two** identity actions, and the runner reproduces whichever the bindings describe:

- `ImageService.EnqueueIdentityAsync` (what `SceneImageStudio.razor` calls) discards the freeform instruction
  it is handed and persists `SceneImageService.BuildFaceOnlyIdentityInstruction(...)`. It resolves each
  character to their **approved pack's canonical face asset** — no view choice, no face area.
- `ImageService.EnqueueEditorIdentityAsync` (the face picker in `Components/Editing/ImageEditWorkspace.razor`)
  persists `BuildEditorIdentityInstruction(...)`: short, face-primary, and it names each character's
  `VisibleLocator` on the frame. The **caller picks the reference asset per face**, so this is the path that
  can match a reference view to a head.

Bindings carrying `visibleLocator` select the editor instruction; bindings without it select the face-only
instruction; a mix throws. Both wire references as `image2, image3, …` on both text encodes, exactly as
`ComfyUIImageEditingClient.AddReferenceInputs/AddReferenceLoaders` does (`LoadImage` node `20+i`).

```powershell
$run = 'specs/image-generator-tests/dual-base-location/runs/<run>'
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-local-aio-edit-proof.ps1 `
  -ComfyUiUrl http://192.168.0.11:8188 `
  -SourceImage "$run/06-harmonized-bedroom-a/img-local-bedroom-a_0.png" `
  -IdentityBindingsPath "$run/07-identity/bindings.json" `
  -OutDir "$run/07-identity/bedroom-a"
```

Bindings file (`[{ "ordinal": 1, "characterName": "Becky", "fileRelativePath": "identity/<profile>/<asset>.png", "sha256": "…" }]`),
resolved under `-IdentityStorageRoot` (default `DreamGenClone.Web/data/scene-images`). Ordinals must be
1..N contiguous and every `sha256` is verified before the job is queued — the same fail-fast the handler does.

The instruction text is a hand-kept copy of the C# builder; **`SceneImageServiceJobTests.BuildFaceOnlyIdentityInstructionIsPinnedForTwoCharacterIdentityPass`
asserts the app's exact string**, so change one and the other fails loudly.

### Two characters need two passes — `run-sequential-identity.ps1`

**With `BuildEditorIdentityInstruction`, do not put two identity references in one edit.** Isolated
2026-09-21 — the trigger is the **instruction**, not the reference count or the view:

| Instruction builder | References | Result |
|---|---|---|
| `BuildFaceOnlyIdentityInstruction` | Front ×2 / Profile ×2 | clean (no extra figure) |
| `BuildEditorIdentityInstruction` | Front ×2 / Profile ×2 | **extra face tiled into the frame** |

The editor path runs the unsafe builder in the app (`EnqueueEditorIdentityAsync`, the face picker in
`ImageEditWorkspace`) — see B-126 Phase 1. The Studio identity action uses the safe builder and is unaffected.
Artifacts: `artifacts/tmp/b126-repro-app-identity/`.

`run-sequential-identity.ps1` splits the bindings into one single-reference pass per character and chains
the outputs:

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-sequential-identity.ps1 `
  -ComfyUiUrl http://192.168.0.11:8188 `
  -SourceImage "$run/06-harmonized-bedroom-a/img-local-bedroom-a_0.png" `
  -IdentityBindingsPath "$run/07-identity/bindings-A.json" `
  -OutDir "$run/07-identity/bedroom-a"
```

Writes `final.png`, `identity-manifest.json`, and `step<N>-<character>/` per pass.

## Console logging

The `ComfyUI 8188` task runs `D:\ComfyUI\run-comfyui.bat`, which captures stdout/stderr to
`D:\ComfyUI\logs\comfyui-console.log` (one previous log kept as `.prev`, `PYTHONUNBUFFERED=1`). Without it
a failed startup left no evidence at all. The original task definition is backed up at
`D:\ComfyUI\xlabs-install-log\ComfyUI-8188-task-backup.xml`.

> **Never add `--fast` to the launcher.** It changes accumulation numerics and produced *pure noise* for
the Qwen-Image-Edit graphs (verified 2026-09-21: the app's own edit was clean before it, noise after, and
the failure reproduced with zero reference images). FLUX looked slow because LM Studio's `llama-server`
held ~6 GB of VRAM — check `lms ps` before blaming the host.

## Run the download

```powershell
# from the dev box — must stay attached: ssh kills detached children when the session closes
ssh -i "$env:USERPROFILE\.ssh\dgcomfy_ed25519" 'wood-game-main\kenac@192.168.0.11' `
  "powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\kenac\download-qwen-aio-checkpoint.ps1"
```

Copy the script to the host first (`scp ... 'wood-game-main\kenac@192.168.0.11:C:/Users/kenac/'`).
Quoting tip: rather than fight `ssh` + `cmd` quoting, pipe a script to the host
(`Get-Content -Raw x.ps1 | ssh ... "powershell -NoProfile -Command -"`).

## Host gotchas (learned the hard way)

- The host runs **Windows PowerShell 5.1** — no null-conditional (`?.`), no PS7-only syntax.
- **`Start-Process` detachment does not survive the SSH session**; the child is killed with the session's
  process tree. Long downloads must run inside a persistent SSH session (resume works if it drops).
- `dir` can report a growing curl download as `0 bytes` while it is still in flight; watch free space or
  the final size check instead.
- **Never stop/start the `ComfyUI 8188` task back-to-back.** The new process cannot bind while the old one
  still holds 8188/VRAM, and it hangs silently (processes alive, no listener, no log). Wait for the port to
  free, and never restart while jobs are queued or running — a restart kills an in-flight render.
- **PowerShell 5.1 `ConvertFrom-Json` trap:** `@(Get-Content -Raw x.json | ConvertFrom-Json)` nests the whole
  array as ONE element. Iterate it with `foreach` (or `Sort-Object` on a property) instead — this silently
  broke both the image manifests and the identity bindings.
- ComfyUI API-format workflow JSON must **not** carry a top-level `_comment` key: every top-level key is
  parsed as a node.
- Measure render progress with `GET /internal/logs/raw` (`entries[].t/.m`) — it carries the live
  `Sampling: n%|…| x/28 [mm:ss<mm:ss, s/it]` line even when no console is attached.
- Restart is safe only when `GET /queue` shows `running=0 pending=0` twice, ~20 s apart.

## Why this checkpoint

`Qwen-Rapid-AIO-NSFW-v23` is the same renderer the RunPod serverless editor endpoint runs
(`img-qwen-edit-serverless`, `79wkn5jz5d5txx`) — a merged full checkpoint with NSFW LoRAs baked in at merge
time, replacing the stock safety-aligned `qwen_image_edit_2511_fp8mixed.safetensors`, which blanks the
genital region. It is a Lightning-style merge: run it at **~4–8 steps / CFG 1 / euler_ancestral / beta**,
never at the stock 40 steps / CFG 4. See `specs/Planning/B-101-serverless-migration/plan.md`.
