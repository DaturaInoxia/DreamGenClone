# Juggernaut Image Generation Pod (`image-gen-juggernaut-prod`)

Dedicated RunPod deployment for the Scene Image Generator (B-032) **image generation**
capability — `Juggernaut XL Ragnarok` via ComfyUI.

## Pod identity

| Field | Value |
|---|---|
| Deployment key | `image-gen-juggernaut-prod` |
| Manifest | `helpers/runpod/deployments/image-gen-juggernaut/deployment.json` |
| Capability | `ImageGeneration` |
| Model identifier | `juggernautXL_ragnarok.safetensors` |
| Base image | `runpod/stable-diffusion:comfy-ui-6.0.0` |
| GPU | NVIDIA A40 |
| Volume | `/workspace` (50 GB local volume) |
| Inference port | 3000 |
| Original podId (manifest) | `mb5pwrm14psof5` |
| Current running pod (2026-08-26) | `orknbkfc0pxktv` — `dreamgen-image-gen-juggernaut`, RUNNING |

**Endpoint:** `https://<running-pod-id>-3000.proxy.runpod.net` (currently
`https://orknbkfc0pxktv-3000.proxy.runpod.net`).

## The recurring outage (READ THIS FIRST)

When the app's renders all start failing with a ComfyUI 400:

```
ckpt_name 'juggernautXL_ragnarok.safetensors' not in
['flux1-schnell-fp8.safetensors', 'sd_xl_base_1.0.safetensors', ...]
```

the cause is **not** the prompt, the settings, or the model. It is the lost
`/ComfyUI/extra_model_paths.yaml`:

- The Juggernaut checkpoint lives on the **persistent** volume:
  `/workspace/comfyui/models/checkpoints/juggernautXL_ragnarok.safetensors`.
- `extra_model_paths.yaml` lives in the **container overlay** (`/ComfyUI/`), which is
  **wiped on every migrate/recycle**.
- Without that YAML, ComfyUI's `CheckpointLoaderSimple` only lists the 5 default
  checkpoints under `/ComfyUI/models`, so the app's request for
  `juggernautXL_ragnarok.safetensors` is rejected.

This has bitten pods `qguv5e029u58lb` → `emqmxptqdxu7pp` → `orknbkfc0pxktv` (three times,
each on a recycle/migrate). **The fix is now a script — do not re-diagnose it from
scratch.**

## Fix (one SSH command)

```bash
ssh -i artifacts/runpod/ssh_ed25519 -p <SSH_PORT> root@<SSH_IP> \
  'bash -s' < helpers/runpod/deployments/image-gen-juggernaut/provision-runtime.sh
```

`provision-runtime.sh` is idempotent and does four things:

1. Writes the exact working `extra_model_paths.yaml` to a **master** copy on the persistent
   volume (`/workspace/comfyui/extra_model_paths.yaml.master`).
2. Copies it to the live `/ComfyUI/extra_model_paths.yaml`.
3. Patches `/pre_start.sh` so every boot restores the live YAML from the master copy.
4. Restarts ComfyUI on 3000 and self-checks readiness + that
   `juggernautXL_ragnarok.safetensors` is listed in `CheckpointLoaderSimple`.

Discover the current SSH IP/port from RunPod status (the `22/tcp` runtime port), or via:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File helpers/runpod/pod.ps1 -Action status -PodId <POD_ID>
```

## Persistent vs ephemeral

| Change | Location | Survives stop/resume | Survives migrate/recycle |
|---|---|---|---|
| Juggernaut checkpoint (7 GB) | `/workspace/comfyui/models/checkpoints/` | ✅ | ✅ |
| `extra_model_paths.yaml.master` | `/workspace/comfyui/` | ✅ | ✅ |
| `/ComfyUI/extra_model_paths.yaml` (live) | container overlay | ✅ | ❌ **restored by `/pre_start.sh`** |
| `/pre_start.sh` bootstrap patch | container overlay | ✅ | ❌ **rerun provisioner once** |
| ComfyUI process | container | — | — |

- **Stop/resume** keeps the overlay → the `/pre_start.sh` bootstrap self-heals, no action
  needed.
- **Migrate/recycle** wipes the overlay (including the `/pre_start.sh` patch) → rerun
  `provision-runtime.sh` **once**. The master YAML and the checkpoint are already on
  `/workspace`, so this only restores the live YAML, the bootstrap patch, and the process.

## ComfyUI restart gotchas

- Relaunch **must** use `setsid nohup ... < /dev/null` redirected to a file
  (`/workspace/comfyui.log`), not `/dev/stdout` — plain `nohup ... &` dies when the SSH
  session closes, and stdout redirection hangs the SSH session.
- On startup ComfyUI briefly pegs CPU (~400%) for the ComfyUI-Manager registry fetch and
  loads the ~7 GB model into VRAM; GPU util reads 0% until VRAM fills. This is transient.

## Related

- `helpers/runpod/POD-CONNECTIONS-AND-MODEL-MANAGER.md` — all-pod inventory and the Model
  Manager endpoint boundary.
- `helpers/runpod/deployments/pose-dwpose/README.md` — the same provisioner pattern for the
  DWPose pod (reference for style/structure).
- `.github/instructions/runpod-pod-migration.instructions.md` — migration + endpoint sync
  rules.
