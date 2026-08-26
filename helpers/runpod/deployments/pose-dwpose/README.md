# DWPose Pose Extraction Pod (`pose-dwpose-prod`)

Dedicated RunPod deployment for the Scene Image Generator (B-032), Phase 3 (Location & Multi-POV)
**Pose Editor** — the DWPose "extract pose from image" input adapter. It is a **pose-extraction /
conditioning** capability, not an image generator or compiler.

## Pod identity

| Field | Value |
|---|---|
| Deployment key | `pose-dwpose-prod` |
| Manifest | `helpers/runpod/deployments/pose-dwpose/deployment.json` |
| Capability | `PoseExtraction` |
| Model identifier | `dw-ll_ucoco_384_bs5.torchscript.pt` |
| Base image | `runpod/stable-diffusion:comfy-ui-6.0.0` |
| GPU | NVIDIA RTX PRO 4500 Blackwell (compute cap **12.0 / sm_120**) |
| Volume | `/workspace` (40 GB local volume) |
| Inference port | 3003 |
| Original podId (manifest) | `wwbl2kjjvizb46` (EXITED) |
| Migration successor (2026-08-26) | `bmcqknli61o49b` — `dreamgen-pose-dwpose-pro-4500-migration`, RUNNING, machineId `6ssfplu1wsm5` |

**Endpoint:** `https://<running-pod-id>-3003.proxy.runpod.net` (currently `https://bmcqknli61o49b-3003.proxy.runpod.net`).

> The manifest `podId` stays the ORIGINAL stable record; migration produces a new successor ID.
> To start/stop the running pod, target the **successor ID** (RunPod console or GraphQL), NOT the
> manifest `podId` — `deployment.ps1` start/stop resolves via the manifest `podId` and will not
> reach the migrated pod.

## Status

- **Provisioned + service-proven 2026-08-26** (pod `bmcqknli61o49b`): readiness `/system_stats` OK
  (ComfyUI v0.3.10), identity `/object_info/DWPreprocessor` OK (lists `dw-ll_ucoco_384_bs5.torchscript.pt`),
  and two real extractions returned full OpenPose JSON + rendered pose-control PNGs.
- **NOT registered in Model Manager (intentional).** Per `POD-CONNECTIONS-AND-MODEL-MANAGER.md`:
  do not register DWPose as an image model or compiler until its dedicated service contract and
  application capability integration (the Pose Editor `PoseService` / `ScenePoseImport`) exist.
  No application code references this pod.

## Persistent vs ephemeral (critical for repro)

| Change | Location | Survives stop/resume | Survives migrate/recycle |
|---|---|---|---|
| `comfyui_controlnet_aux` clone (pinned) | `/workspace/comfyui/custom_nodes/` | ✅ | ✅ |
| DWPose ckpts assets (auto-downloaded on first use) | `.../comfyui_controlnet_aux/ckpts/` | ✅ | ✅ |
| Python deps + torch 2.7.0+cu128 | container overlay (`/usr/local/lib/python3.10/dist-packages`) | ✅ | ❌ **redo** |
| `/ComfyUI/custom_nodes` symlink | container overlay | ✅ | ❌ **redo** |
| `/pre_start.sh` port-3003 edit | container overlay | ✅ | ❌ **redo** |
| ComfyUI process | container | — | — |

Stop/resume keeps everything. **Migrate/recycle loses the container overlay** — rerun the
provisioner to restore it (the persistent `/workspace` clone + ckpts are reused).

## From-scratch provisioning (new pod / after migrate / after recycle)

1. Start the pod, wait for `RUNNING` with the HTTP `3003` mapping exposed. SSH user `root`.
2. Pipe the idempotent provisioner to the pod (from the repo, with the pod's current SSH IP/port):

   ```bash
   ssh -i artifacts/runpod/ssh_ed25519 -p <SSH_PORT> root@<SSH_IP> \
     'bash -s' < helpers/runpod/deployments/pose-dwpose/provision-runtime.sh
   ```

   It clones+pins `comfyui_controlnet_aux` (`e8b689a…`), symlinks it into `/ComfyUI/custom_nodes`,
   installs the validated deps, installs the **Blackwell torch build `2.7.0+cu128`**, sets
   `/pre_start.sh` to port 3003, and relaunches ComfyUI on 3003 (self-checks readiness).
3. Verify through the proxy:

   ```bash
   curl -fsS https://<pod-id>-3003.proxy.runpod.net/system_stats
   curl -fsS https://<pod-id>-3003.proxy.runpod.net/object_info/DWPreprocessor
   ```
4. (Optional) run the extraction proof — see below.

## Why torch 2.7.0+cu128

The RTX PRO 4500 Blackwell is compute cap **12.0 (sm_120)**. The base image ships torch
**2.6.0+cu124**, which only supports sm_50–sm_90, so every CUDA compute op fails with
`CUDA error: no kernel image is available for execution on the device`. **torch 2.7.0+cu128 is the
first build with sm_120 kernels.** Do not downgrade below 2.7.0+cu128 on this GPU.

## Validated dependency set

| Package | Version | Notes |
|---|---|---|
| `comfyui_controlnet_aux` | `e8b689a513c3e6b63edc44066560ca5919c0576e` (v1.1.5) | provides `DWPreprocessor` |
| `opencv-python-headless` | 4.10.0.84 | phase-0 validated |
| `matplotlib` | 3.10.9 | phase-0 validated |
| `scikit-image` | 0.25.2 | phase-0 validated |
| `numpy` | 1.26.4 | retained — do not force 2.x |
| `torch` / `torchvision` / `torchaudio` | 2.7.0+cu128 / 0.22.0 / 2.7.0 | cu128 index |
| `onnxruntime` | **NOT installed** | DWPose runs OpenCV CPU; slower but validated |

## Extraction proof

Workflow: `helpers/runpod/workflows/dwpose-extract-proof.json` (`LoadImage → DWPreprocessor →
SaveImage`). Select **TorchScript** assets explicitly (`bbox_detector=yolox_l.torchscript.pt`,
`pose_estimator=dw-ll_ucoco_384_bs5.torchscript.pt`) because onnxruntime is absent.

Proven 2026-08-26 (pod `bmcqknli61o49b`): portrait source → face 70/70, body 8/18 (lower body
off-frame); full-body kneeling source → body 16/18, face 70/70, hands 20/21 + 15/21 (right hand
partially occluded in source — expected). Artifacts in `artifacts/tmp/images/dwpose-proof/`.

## Related

- `POD-CONNECTIONS-AND-MODEL-MANAGER.md` — all-pod inventory and the Model Manager boundary.
- `specs/Planning/B-032-scene-image-generator/phase-3-location-and-multi-pov/pose-editor-plan.md` —
  the Pose Editor feature that consumes DWPose.
