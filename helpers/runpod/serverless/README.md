# RunPod Serverless Migration Toolkit (B-101)

Replaces the dedicated RunPod pods with Serverless endpoints. One concept, repeated per workload:

```
1. Worker image   Dockerfile + handler(job)    <- the runtime, baked (models, custom nodes, pins)
2. Endpoint       GPU type, min/max workers, idle timeout, env   <- the config
3. Provider       Model Manager: endpoint id + api key + job mode + result schema   <- the app hookup
```

All five pod workloads are transitionable; the custom things on each pod (pinned ComfyUI revisions,
venvs, `tokenizers==0.22.2` pin, `extra_model_paths.yaml`, model SHA-256 downloads, vLLM flags)
become image-build steps. Full analysis and migration plan: `specs/Planning/B-101-serverless-migration/plan.md`.

## Image map (you do NOT need 5 images)

| Worker image | Hosts (endpoints) | VRAM |
|---|---|---|
| `comfy-worker` | Juggernaut XL, DWPose, identity (IP-Adapter/PuLID/ControlNet — superset) | 24 GB |
| `qwen-edit-worker` | Qwen Image Edit 2511 (isolated ComfyUI clone rev `e4c61d75` + 3 models) | 48 GB |
| `qwen-vl-worker` | Qwen2.5-VL 7B (vLLM 0.27.1 + Python 3.13.2 + abliterated model) | 24 GB |

**Pony (or any future model) is the same concept again:** add the checkpoint to a ComfyUI-based
worker image (or its own endpoint), register a Model Manager provider, done.

## No Docker on this host — build the images with GitHub Actions

The repo is on GitHub (`DaturaInoxia/DreamGenClone`, **public**), so the default builder is
**GitHub Actions → GHCR** (Docker is built into Actions runners — nothing installed on this host):

1. **GitHub Actions → GHCR (default):** run the `build-serverless-worker` workflow
   (`.github/workflows/build-serverless-worker.yml`) with the worker + tag. It builds the
   Dockerfile and pushes to `ghcr.io/daturoinoxia/dreamgenclone/dreamgen-<worker>-worker:<tag>`.
   Reproducible and logged.
2. **RunPod cloud build** from this public repo — viable now that the repo is public (RunPod builds
   the Dockerfile directly); less control/logging than Actions, so Actions is preferred.
3. **Temp RunPod pod** (`build-on-pod.ps1`) — fallback only, if Actions/GHCR are unavailable.

**GHCR pull auth:** the image must be visible to RunPod. Set the GHCR package visibility to
**Public** (repo → Packages → settings) after the first push so the endpoint pulls it with no
credentials; or keep it private and pass a fine-grained PAT (`read:packages`) in the endpoint's
`container.imageAuth`.

All the *code* (Dockerfiles, handlers, endpoint tooling, app-side client) needs **zero** Docker.

## Endpoint registry

`endpoints.json` is the source of truth for Serverless endpoints (mirrors `pod-registry.json`).
Worker images are pushed to `ghcr.io/daturoinoxia/dreamgenclone`. Per the RunPod-changes rule,
every image/endpoint change must be recorded there and be reproducible from the Dockerfile +
endpoint config.

## Quickstart (DWPose infra proof — first migration)

1. Build `dwpose-worker` via the `build-serverless-worker` GitHub Actions workflow (worker=`dwpose`, tag=`0.1.0`); set the GHCR package visibility to Public
2. Create the endpoint: `helpers/runpod/serverless/create-endpoint.ps1 -EndpointKey pose-dwpose-serverless` (schema verified at P0)
3. Smoke test: `helpers/runpod/serverless/smoke-test.ps1 -EndpointKey pose-dwpose-serverless -ImageRef path/to/image.png`
4. Once green, move to Juggernaut (first app-integrated migration — reworks `ComfyUIImageClient`).

## Operations vs pods (no SSH)

| Pod ops | Serverless equivalent |
|---|---|
| SSH + provision scripts | Dockerfile + handler; image build + push |
| GraphQL pod create/start/stop | `runpodctl` / GraphQL endpoint management |
| `nvidia-smi` host-driver check | not applicable (RunPod controls fleet hosts) |
| HTTP probes via `proxy.runpod.net` | job-based smoke tests (`runpodctl send test` / RunPod API) |
| Model Manager `BaseUrl` update | Serverless provider fields (endpoint id + api key + job mode) |
