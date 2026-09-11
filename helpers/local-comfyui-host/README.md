# local-comfyui-host — local ComfyUI (WOOD-GAME-MAIN) host scripts

Scripts that run **on** the local ComfyUI host (or drive it over SSH), not in the app.
Host facts: `WOOD-GAME-MAIN`, Windows 11, RTX 5080 16 GB, ComfyUI 0.34.0 at `D:\ComfyUI`,
`http://192.168.0.16:8188`. SSH: `wood-game-main\kenac@192.168.0.16`, key `~/.ssh/dgcomfy_ed25519`.

See `docs/local-comfyui-model-manager-setup.md` for the Model Manager registration procedure.

## Scripts

| Script | Purpose |
|---|---|
| `download-qwen-aio-checkpoint.ps1` | Downloads the `Qwen-Rapid-AIO-NSFW-v23.safetensors` merged checkpoint (28,431,840,023 bytes) into `D:\ComfyUI\models\checkpoints`. Idempotent + resumable (`curl -C -`); verifies the exact byte count and prints the SHA-256. |
| `diagnose-download-prereqs.ps1` | Reports the host's PowerShell version, `curl.exe` presence, target-directory existence, and any parse errors in the downloader. Run first when a download "does nothing". |

## Run the download

```powershell
# from the dev box — must stay attached: ssh kills detached children when the session closes
ssh -i "$env:USERPROFILE\.ssh\dgcomfy_ed25519" 'wood-game-main\kenac@192.168.0.16' `
  "powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\kenac\download-qwen-aio-checkpoint.ps1"
```

Copy the script to the host first (`scp ... 'wood-game-main\kenac@192.168.0.16:C:/Users/kenac/'`).

## Host gotchas (learned the hard way)

- The host runs **Windows PowerShell 5.1** — no null-conditional (`?.`), no PS7-only syntax.
- **`Start-Process` detachment does not survive the SSH session**; the child is killed with the session's
  process tree. Long downloads must run inside a persistent SSH session (resume works if it drops).
- `dir` can report a growing curl download as `0 bytes` while it is still in flight; watch free space or
  the final size check instead.

## Why this checkpoint

`Qwen-Rapid-AIO-NSFW-v23` is the same renderer the RunPod serverless editor endpoint runs
(`img-qwen-edit-serverless`, `79wkn5jz5d5txx`) — a merged full checkpoint with NSFW LoRAs baked in at merge
time, replacing the stock safety-aligned `qwen_image_edit_2511_fp8mixed.safetensors`, which blanks the
genital region. It is a Lightning-style merge: run it at **~4–8 steps / CFG 1 / euler_ancestral / beta**,
never at the stock 40 steps / CFG 4. See `specs/Planning/B-101-serverless-migration/plan.md`.
