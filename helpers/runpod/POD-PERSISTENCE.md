# RunPod Pod Persistence Standard

**Mandatory for every DreamGenClone RunPod pod.** This is the reference for the global rule
"RunPod Pod Changes Must Be Documented" (`.github/copilot-instructions.md`) and the
`runpod-pod-creation` skill. Read this before provisioning, changing, or "fixing" any pod.

## The two-layer model

RunPod GPU pods have two storage layers. Confusing them is the root cause of every
"things are missing after restart" outage.

| Layer | Example paths | Survives stop/resume | Survives recycle/migrate |
|---|---|---|---|
| **Persistent volume** | `/workspace/**` | ✅ | ✅ (migrate carries it over) |
| **Container overlay** | `/ComfyUI/**`, `/usr/local/lib/python*/dist-packages`, `/pre_start.sh`, `/etc/**`, installed `apt`/`pip` packages | ✅ | ❌ **WIPED** |

`stop/resume` keeps the container overlay (same container). **Recycle, migrate, and fresh creation
wipe the overlay.** That is why an `extra_model_paths.yaml` written to `/ComfyUI/`, a custom node
cloned under `/ComfyUI/custom_nodes`, a `pip install`, or a `pre_start.sh` edit "disappear" after a
restart.

## Rules

1. **Persistent by default.** Models, venvs, custom-node clones, and config *masters* go under
   `/workspace`. Never store a model or a runtime under `/ComfyUI` or the container root.
2. **Self-heal the overlay.** Anything that MUST exist in the container overlay must be restored
   automatically on **every boot** by an idempotent `/pre_start.sh` patch. The patch copies from a
   `/workspace` master (or symlinks from `/workspace`) if-and-only-if the overlay copy is missing.
3. **Auto-start services.** Services that must run (ComfyUI, vLLM, etc.) MUST be launched from
   `/pre_start.sh` on boot (or from a restart-proof entrypoint). A service started manually over
   SSH will NOT survive a restart.
4. **Idempotent provisioner.** Every pod has one idempotent `provision-runtime.sh` (registered in
   `pod-registry.json`) that reproduces the pod from scratch: writes the `/workspace` masters,
   patches `/pre_start.sh`, restarts the service, and self-checks readiness + identity. Rerunning
   it after a recycle must fully restore the pod.
5. **Verify by restarting.** After provisioning, `stop` + `start` (or restart) the pod and re-run
   the smoke test. If the service comes up with the same models without manual SSH, the pod is
   restart-proof. Do not declare a pod ready otherwise.
6. **Document every change.** Any model/custom node/package/config added to a pod is recorded as a
   provision step in `pod-registry.json` and the deployment manifest. No undocumented pod changes.

## The `/pre_start.sh` self-heal pattern

Idempotent bootstrap inserted by the provisioner (this is what the Juggernaut provisioner does for
`extra_model_paths.yaml`):

```bash
# /pre_start.sh (patched by provision-runtime.sh)
if [ -f /workspace/comfyui/extra_model_paths.yaml.master ] && [ ! -f /ComfyUI/extra_model_paths.yaml ]; then
  cp /workspace/comfyui/extra_model_paths.yaml.master /ComfyUI/extra_model_paths.yaml
fi
# start the service on boot (restart-proof)
if ! pgrep -f "python main.py" >/dev/null; then
  cd /ComfyUI && setsid nohup python main.py --listen --port 3000 >> /workspace/comfyui.log 2>&1 < /dev/null &
fi
```

Custom nodes follow the same idea, symlinking from the persistent volume:

```bash
mkdir -p /ComfyUI/custom_nodes
ln -sfn /workspace/comfyui/custom_nodes/comfyui_controlnet_aux /ComfyUI/custom_nodes/comfyui_controlnet_aux
```

## Per-pod persistence (from `pod-registry.json`)

| Pod | Persistent on `/workspace` | Ephemeral overlay (restored by `/pre_start.sh` on boot) |
|---|---|---|
| Juggernaut | `comfyui/` tree + checkpoint + `extra_model_paths.yaml.master` | `/ComfyUI/extra_model_paths.yaml`, `/pre_start.sh` patch, ComfyUI process on 3000 |
| Qwen Image Edit | `comfyui-qwen-2511/` clone + models + `.venv` | isolated ComfyUI process on 3002 (auto-start on boot) |
| Qwen VL | `qwen-vl-edit-compiler/` (venv + model + runtime) | vLLM process on 3004 (auto-start on boot) |
| DWPose | `comfyui/custom_nodes/comfyui_controlnet_aux` + ckpts | `/ComfyUI/custom_nodes` symlink, torch/package overlay, ComfyUI on 3003 |
| identity-proof | checkpoint + identity custom nodes/masters | overlay custom nodes + symlinks, ComfyUI on 3000 |

> **Gap to close (2026-08-27):** Qwen VL currently starts vLLM manually; it MUST get a
> `/pre_start.sh` auto-start patch so vLLM survives a restart. Same for the Qwen Edit isolated
> ComfyUI. Any identity-conditioning custom nodes added to a pod must be on `/workspace` + symlinked
> + restored on boot.

## Recycle-proof = the goal

After a recycle/migrate/recreate, **no manual SSH, no re-download, no re-install** should be
needed. `start` the pod → `/pre_start.sh` restores the overlay → the service comes up → smoke test
passes. If you find yourself "re-downloading" or "reinstalling" after a restart, that is a
persistence bug in the provisioner — fix the provisioner and registry, then rerun once.

## Related

- `.github/copilot-instructions.md` — the mandatory global rule.
- `.github/skills/runpod-pod-creation/SKILL.md` — recreation workflow (includes this standard).
- `helpers/runpod/pod-registry.json` — per-pod provision + persistence metadata.
- `helpers/runpod/POD-CONNECTIONS-AND-MODEL-MANAGER.md` — pod inventory and Model Manager boundary.
- `helpers/runpod/deployments/image-gen-juggernaut/README.md` — persistent-vs-ephemeral worked example.
