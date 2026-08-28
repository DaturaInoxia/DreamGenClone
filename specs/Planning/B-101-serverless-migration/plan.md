# B-101 — Migrate RunPod image workloads from dedicated pods to Serverless

**Status:** `new` (design draft)
**Date:** 2026-08-27
**Related:** B-032 (scene image engine), B-097 (pose/layout control), B-098/B-099 (Pony/SDXL paths)

**Progress (2026-08-27):** concept confirmed (same pattern ×5; DWPose first as infra proof);
scaffold created under `helpers/runpod/serverless/` — `README.md` (no-local-Docker build options),
`endpoints.json` (serverless endpoint registry, DWPose entry), `dwpose-worker/` (Dockerfile +
`serverless-worker.py`), and operator helpers `create-endpoint.ps1` / `smoke-test.ps1` /
`build-on-pod.ps1` (all P0/P1 gated). **Decision (2026-08-27):** registry = **GHCR** (`ghcr.io/daturoinoxia/dreamgenclone`, repo is public →
set package visibility Public so RunPod pulls without creds) + builder = **GitHub Actions**
(`.github/workflows/build-serverless-worker.yml`, Docker built-in, no local tooling). Temp-pod
builder (`build-on-pod.ps1`) kept as fallback only. **(SUPERSEDED 2026-08-28 — replaced by RunPod
GitHub Integration; see progress below. No GHCR/GA needed.)**

**Progress (2026-08-28):** **DWPose image built + pushed** (GH Actions run 33139945603) and
**public pull verified with a real anonymous `docker pull` on a runner**
(`.github/workflows/verify-serverless-image.yml`, run 33143748737 → `ANON_PULL_EXIT=0`, digest
sha256:49321d…; GHCR took ~50 min to propagate visibility — see
`/memories/repo/ghcr-public-propagation-delay.md`). Live GPU prices pulled (RTX 4000 Ada $0.28/hr
secure — chosen DWPose GPU). Remaining P0: validate `createEndpoint` mutation shape (RunPod
introspection disabled — use documented shape) and get user go-ahead to create the endpoint (first
RunPod action; `minWorkers 0` → no GPU cost until a job runs).

**Progress (2026-08-28, endpoint live + debugging):** endpoint `pose-dwpose-serverless`
(`06lhf1akdg34c2`) created via REST v2 (NOT GraphQL — the legacy `createEndpoint` mutation does
not exist). Image `0.3.0` → `0.4.0` → `0.5.0`. Debug trail (each failed job had a distinct,
diagnostic cause):
1. `0.3.0`: job ran handler but workflow never finished → endpoint per-job `timeout:300000` (5 min)
   too short for cold start + first-run ckpt download; **raised to `900000` (15 min)**. The handler's
   own `"workflow did not finish in time"` (6-min poll) then exposed the real issue:
2. `0.4.0`: **DWPose ckpts were not being found by the aux node** — baked into
   `ckpts/dwpose/` but `custom_hf_download` resolves at `ckpts/<hf_repo_id>/<filename>`; the node
   tried to download and crashed on `HF_HUB_ENABLE_HF_TRANSFER=1` with no `hf_transfer` package.
   **Fix (`33f4fca`, image `0.5.0`)**: bake ckpts at `ckpts/hr16/DWPose-TorchScript-BatchSize5/…`
   + `ckpts/hr16/yolox-onnx/…` (verified from pinned `util.py`/`node_wrappers/dwpose.py`), set
   `HF_HUB_ENABLE_HF_TRANSFER=0`, handler `ensure_ckpts()` verifies repo-id layout, and
   `_run_comfyui_workflow()` surfaces real ComfyUI errors (no more blind timeout).
   **Key lesson:** worker `timeout` = per-JOB execution cap; base pod image
   (`runpod/stable-diffusion:comfy-ui-6.0.0`) runs the serverless handler fine; old workers on a
   deleted image show UNHEALTHY forever and rollout cycles them automatically — be patient.

**Progress (2026-08-28, DWPose VALIDATED + pivot decision):** **DWPose serverless endpoint is
WORKING.** Smoke test COMPLETED (job `2f61ca55`, image `0.5.0`, worker `j8f88p8hvx10nh` RTX 4000
Ada): `executionTime 17449ms`, rendered pose PNG verified via `view_image` — real DWPose skeleton
output. Endpoint `pose-dwpose-serverless` status → `working-validated` in `endpoints.json`.
Infra proof complete: image build+public pull, REST-v2 create/patch/rollout, cold start
(queue delay ~25 min = 33 GB runtime pull + GPU throttle), fast warm execution, correct output.
**PIVOT (user question "are we doing it properly?" → YES, we should change base):** we hand-rolled a
handler on a POD image; RunPod maintains the purpose-built `runpod-workers/worker-comfyui`
(AGPL-3.0, latest 5.8.7, 13.8k deploys) with a clean serverless base, **standard API contract**
(`input.workflow` + `input.images` → `output.images[{filename,type:base64,data}]`), **WebSocket**
completion, **native network-volume model support**, SSH debug via `PUBLIC_KEY`. Plan: rebuild all
worker images `FROM runpod/worker-comfyui:<ver>-base` + custom nodes/models; delete our
hand-rolled `serverless-worker.py`; adopt the official API contract (our app's ComfyUI client
already speaks workflow-JSON). This is the foundation for the network-volume storage integration
(§1.5).

**Progress (2026-08-28, GitHub-Integration endpoint VALIDATED — the proven pattern):** endpoint
`pose-dwpose-serverless-comfy` (id `urls820tw26oew`) created via RunPod **"Start from GitHub Repo"**
— RunPod built our Dockerfile from master @ `198c0a8` (image
`registry.runpod.net/.../helpers-runpod-serverless-dwpose-worker-dockerfile:198c0a86f`, official
`worker-comfyui:5.8.4-base` + `comfy-node-install` + baked ckpts). Smoke test **PASSED** (job
`8053baca`, COMPLETED, exec 12829ms, delay 12632ms): official contract (`input.workflow` +
`input.images` → `output.images[0]` = `dwpose_sls_00001_.png`, pose skeleton verified via
`view_image`). **This is the pattern for ALL remaining workloads:** write the worker Dockerfile
(official base + comfy-node-install + models) → push → RunPod auto-builds + deploys → job works.
NO GHCR / NO GitHub Actions (superseded; the two old workflows can be removed). Old hand-built
endpoint (`06lhf1akdg34c2`, 0.5.0) = rollback fallback; retire after soak.

## 1.5 Storage integration (Network Volume) — proven on DWPose, then applied to big models

**Goal (user decision 2026-08-28):** after the DWPose endpoint works with baked ckpts, move the
model payload to a **RunPod Network Storage volume** (Standard $0.07/GB/mo) so worker images stay
= base-only (smaller pull) and models are already in the data center (no internet download —
fixes the DWPose-style hang; faster + reliable cold starts). Full model-payload sizing (~55–62 GB
for all 5 workloads, Qwen Edit ≈ half) captured in the discussion; monthly ≈ $4–5 at Standard.

**Proof on DWPose first (cheap, reuses the existing endpoint):**
0. **PIVOT FIRST:** rebuild `dwpose-worker` on the official `runpod/worker-comfyui:<ver>-base`
   image (deletes our hand-rolled handler; official API contract; native network-volume support).
   This is a small Dockerfile change (base + `comfyui_controlnet_aux` + ckpts) and reuses the
   endpoint. `0.6.0` = official base + baked ckpts (baseline), `0.7.0` = official base + ckpts on
   volume (the proof).
1. Create a small Network Volume (≤ 5 GB) in the workers' data center (EU-RO-1).
2. Seed it with the two DWPose ckpts at the aux layout
   `ckpts/hr16/{DWPose-TorchScript-BatchSize5,yolox-onnx}/…`.
3. Build `0.7.0` worker image **without** baked ckpts (base-only image → smaller pull; models read
   from the volume — official worker reads them from the volume mount natively).
4. PATCH endpoint: add `networkVolumes: [<volumeId>]` (endpoint already exposes
   `networkVolumes`/`dataCenterIds` fields — currently `[]`); set `dataCenterIds` to the volume's DC.
5. Handler (official worker) reads ckpts from the volume mount path.
6. Smoke test (official `input.workflow`+`input.images` contract) → measure cold start vs the
   baked-ckpt baseline. Record timings here.
7. **FlashBoot experiment** (endpoint field `flashboot` is currently `OFF`): enable, re-measure.
   If it attacks the base-image pull as expected, adopt for the big-model endpoints.

**Apply to big models after the proof:** Qwen Edit (~26.5 GB → dominates the volume), Qwen VL
(~16 GB), Juggernaut (~7 GB), identity stack (~12 GB). Each worker becomes base-image + handler;
models live on the shared volume. **Dev-usage operating model (user decision):** scale-to-zero +
`idleTimeout` 30–60 min on heavy endpoints = warm during active work, $0 when away (only the
trailing idle window is billed); the volume + small image + FlashBoot keep the one-per-session cold
start in single-digit minutes. (DWPose keeps short idle 180 s.)

## 1. Problem statement

The four production RunPod pods (plus the identity-conditioning stack) work, but their operating
model is wrong for this project's usage:

- **Stopping a pod to save money releases the GPU back to the pool.** Restarting is a *new rental*,
  not "wake up my machine" — if no GPU of that type has free capacity at that moment, the pod
  **will not start** (observed repeatedly).
- **Recreating a pod just to run a test** is the fallback when a start fails, and a fresh pod has an
  **empty volume** → full model re-download + re-provision (~7–30 GB per pod). This is the expensive,
  slow pain the user has hit.
- The fleet cannot be collapsed onto a single always-on GPU: **Qwen Image Edit 2511 requires ~48 GB
  VRAM by itself** (20.5 GB diffusion + 9.4 GB TE + VAE ≈ 30 GB of weights), and the other workloads
  (SDXL ~7 GB + Qwen VL ~16 GB + IP-Adapter/PuLID/ControlNet stack) do not fit alongside it, nor
  together on a 24 GB card.

Serverless replaces the manual pod lifecycle with an auto-scaling fleet: **scale-to-zero** stops
spending when idle (the user's original cost goal), a **warm worker** is fast (model already in
VRAM), and a **cold start** loads the model (minutes for the big ones). No pod start/stop, no
"GPU type unavailable," no recreate-to-test.

## 2. Current state (fleet)

Prices are SECURE-cloud $/hr from `helpers/runpod/GPU-SELECTION.md` (2026-08-27 snapshot).

| Deployment | GPU ($/hr) | Port | Workload | VRAM need | Custom things |
|---|---|---|---|---|---|
| `image-gen-juggernaut-prod` (pod `817glbpee7l99q`) | A40 ($0.44) | 3000 | SDXL/Juggernaut text→image | ~7 GB ckpt, 24 GB comfy | `extra_model_paths.yaml` + `/pre_start.sh` self-heal; Civitai ckpt `juggernautXL_ragnarok.safetensors` (~6.62 GB); **no** IP-Adapter nodes |
| `image-edit-qwen-2511-prod` (pod `jkms7ljhb54we9`) | L40S ($0.99) | 3002 | Qwen Image Edit 2511 edit | **~48 GB (hard)** | Isolated ComfyUI clone pinned rev `e4c61d75`; own `.venv`; 3 models ~30 GB (`qwen_image_edit_2511_fp8mixed` 20.5 GB, `qwen_2.5_vl_7b_fp8_scaled` TE 9.4 GB, `qwen_image_vae` 0.25 GB) with SHA-256 verify; `tokenizers==0.22.2` pin + stale dist-info removal; binds `0.0.0.0:3002`; `/pre_start.sh` auto-start |
| `image-vision-qwen-vl-prod` (pod `ed75qs3tty7xq2`) | L40S ($0.99) | 3004 | Qwen2.5-VL 7B abliterated compiler | ~16 GB bf16, 24 GB comfy | vLLM `0.27.1` in Python 3.13.2 venv; pinned abliterated model 4 shards (rev `fa935a79`, SHA-256 verified); **torch CUDA 13 → host driver ≥ 580** (the A5000/CUDA-12.4 disaster); `--gpu-memory-utilization 0.9`, `VLLM_USE_FLASHINFER_SAMPLER=0`, `--max-model-len 8192`, `--limit-mm-per-prompt {"image":1}`; auto-start hook **GAP** |
| `pose-dwpose-prod` (pod `wwbl2kjjvizb46`) | RTX PRO 4500 ($0.72) | 3003 | DWPose pose extraction | tiny (mostly CPU) | pinned `comfyui_controlnet_aux` `e8b689a` symlinked into `/ComfyUI/custom_nodes`; Blackwell torch 2.7.0+cu128 (only needed for sm_120); DWPose ckpts (`dw-ll_ucoco_384_bs5`, `yolox_l`) |
| `identity-conditioning-proof` (pod `7i2mutjmry5tkt`) | A40 ($0.44) | 3000 | IP-Adapter / PuLID + ControlNet identity renders | ~12 GB models, 24 GB comfy | IP-Adapter (`ip-adapter_sdxl_vit-h`, `plus-face`, `CLIP-ViT-H-14`), PuLID (`sdxl_fp16` + insightface `antelopev2`), ControlNet (`openpose-sdxl-1.0` 5 GB, `depth-sdxl-1.0` 2.5 GB), DWPose ckpts; all custom nodes + models on `/workspace` |
| `image-edit-qwen-2511-a40-preserved` (backup) | A40 ($0.44) | 3002 | Qwen Edit backup | 48 GB | duplicate of prod Qwen Edit runtime |

**All 4 prod pods always-on:** $0.44 + $0.99 + $0.99 + $0.72 = **$3.14/hr ≈ $2,292/mo**. (Both
non-prod pods running add +$0.88/hr → $4.02/hr ≈ $2,935/mo.)

## 3. Cost comparison

| Option | Monthly (est.) | First-call latency | Availability | Lifecycle pain |
|---|---|---|---|---|
| **A. Current pods, stop/start manually** | $0 idle / **~$2,292/mo** if all-on | minutes (boot) | ❌ not guaranteed on start | high (start failures, recreate+re-download) |
| **B. Consolidate to 2 always-on pods** (A40 $0.44 for Qwen Edit + 24 GB pod $0.27–0.50 for the rest) | **~$518–$686/mo** | fast (always warm) | still pod-start roulette | medium; **still can't fit Qwen Edit + the rest** |
| **C. Serverless scale-to-zero** (idle timeout ~5 min) | **~$20–50/mo** for sporadic use (pay per GPU-second actually working + cold-start seconds) | **minutes on cold** (model load), fast when warm | ✅ auto-scaling fleet | none — this is the point |
| **D. Serverless `minWorkers: 1` on every endpoint** | ~$1,560–1,960/mo (worse than B — you're paying for idle 24/7) | always fast | ✅ | none, but cost defeats the purpose |

Notes:

- Serverless bills per GPU-second at roughly the same $/hr as the on-demand pod price for the same
  GPU type. **Verify the live Serverless GPU price list before committing** (it changes; the pod
  catalog in `GPU-SELECTION.md` is the closest published proxy).
- The killer cost advantage (Option C) comes from **scale-to-zero + short idle timeout**. It only
  makes sense for sporadic use — which is exactly this project's usage (tests + occasional renders).
- Cold starts are the real tradeoff: loading 30 GB (Qwen Edit) or 16 GB (Qwen VL) into VRAM is
  minutes. **FlashBoot** (snapshots a warmed container) reduces cold boot to seconds and is the
  recommended enabler for the big-model endpoints. Alternatively, keep only the two big-model
  endpoints warm (`minWorkers: 1`) and scale-to-zero the light ones (Juggernaut/DWPose).
- **The user's assumption is confirmed: one A40 cannot host all workloads.** Qwen Edit alone needs
  ~48 GB; SDXL + VL + the identity stack do not fit a 24 GB card together. Serverless doesn't change
  the VRAM physics — it changes *how you rent* (separate auto-scaling endpoints per workload instead
  of N hand-managed pods).

## 4. Can every pod be transitioned? — YES, all of them

Every workload's "custom things" are exactly the kind of thing that belongs in a **worker Docker
image + handler startup**, not a live-pod SSH edit. None are blocked by statefulness, proprietary
hardware, or anything that can't be containerized.

| Workload | Transitionable? | Worker image plan | Key concern |
|---|---|---|---|
| Juggernaut XL | ✅ yes | base ComfyUI image + ckpt + `extra_model_paths.yaml` (as a `COPY`) | none — standard ComfyUI serverless pattern |
| Qwen Edit 2511 | ✅ yes (heaviest) | image pins ComfyUI rev `e4c61d75`, venv, `tokenizers==0.22.2`, the 3 models (SHA-256 verified at build) | **30 GB model payload** → big image / slow cold pull; use FlashBoot or warm worker |
| Qwen VL | ✅ yes | image pins Python 3.13.2 + vllm 0.27.1 + abliterated model; `VLLM_USE_FLASHINFER_SAMPLER=0`, gpu-mem 0.9 in the handler | **CUDA-13 driver issue disappears on Serverless** (fleet hosts are current); cold load ~minutes |
| DWPose | ✅ yes (easiest) | image pins `comfyui_controlnet_aux` + ckpts; skip Blackwell torch by picking a non-sm_120 GPU | none |
| Identity conditioning | ✅ yes (most custom nodes) | image installs IP-Adapter/PuLID/ControlNet custom nodes + ~12 GB models | biggest image; needs 24 GB+ GPU |
| Qwen Edit backup | ✅ retire | Serverless gives redundancy via multiple workers / auto-scaling | the preserved pod can be stopped |

**Conclusion:** 5 workloads, all transitionable. The migration cost is **app-side**, not pod-side:

> ### ⚠️ MODEL DECISION for `qwen-edit-worker` (2026-08-28) — use Rapid-AIO-NSFW-v23, NOT the stock 2511 + LoRA
>
> **Problem the pod could not solve:** the stock `qwen_image_edit_2511_fp8mixed.safetensors` is a
> safety-aligned model — it renders breasts but **blanks the genital region** ("mannequin" crotch
> the user observed). A pod-side attempt to fix it with the Scottzilla **MCNL v1** NSFW LoRA
> (`qwen-image-edit-plus-nsfw-lora.safetensors`, strength 1.0 on the fp8 UNET) **FAILED proof**
> (2026-08-28): the render produced anatomy confusion — both male and female figures came out with
> female anatomy. **Do NOT resume pod LoRA tuning; the LoRA-on-fp8-UNET path is dead.**
>
> **Chosen model for the Serverless Qwen Edit worker:** **`Phr00t/Qwen-Image-Edit-Rapid-AIO-NSFW-v23`**
> — a **merged full checkpoint** with NSFW LoRAs baked in at merge time (SNOFS, Sex-tacular, GNASS…),
> so the worker needs **no runtime LoRA**. Verified:
> - **Ungated & downloadable** (26.48 GB, FP8): `huggingface.co/Phr00t/Qwen-Image-Edit-Rapid-AIO/resolve/main/v23/Qwen-Rapid-AIO-NSFW-v23.safetensors`
> - SFW sibling exists at `v23/Qwen-Rapid-AIO-SFW-v23.safetensors` in the same repo.
> - Runs on L40S/A40-class hardware (FP8). **The GGUF NVFP4 variants are a dead end — they require
>   NVIDIA Blackwell (RTX 50-series) + CUDA 13.0+** (this fleet is CUDA 12.x).
> - Author-validated settings: **~4–8 steps, 1 CFG, `euler_ancestral/beta`** (it is a Lightning
>   accelerator merge — do NOT run at 40 steps / CFG 4).
>
> **Integration implications for the worker + app (different from today):**
> - The Rapid-AIO file is a **full checkpoint** → the workflow must load it with
>   `CheckpointLoaderSimple` (outputs model+clip+vae together), **not** the current
>   `UNETLoader`+`CLIPLoader`+`VAELoader` split in `ComfyUIImageEditingClient.BuildWorkflow`.
> - Persisted editor model fields change: `ImageEditorDiffusionModel` → checkpoint name;
>   steps/cfg/sampler/scheduler defaults differ (4–8 / 1 / euler_ancestral / beta).
> - Bake `Qwen-Rapid-AIO-NSFW-v23.safetensors` into the worker image at build (SHA-256 verified),
>   not at first start.
> - Update `.github/instructions/qwen-image-edit-2511.instructions.md` when this lands.

## 5. Target architecture

```
DreamGenClone.Web
  └─ RunPod Serverless Client (new, Infrastructure)
       ├─ POST https://api.runpod.io/v2/{endpoint_id}/run     (async submit)
       ├─ GET  https://api.runpod.io/v2/{endpoint_id}/status/{job_id}  (poll)
       └─ reads worker JSON result (PNG/base64 or structured output)
  └─ Model Manager (new Serverless provider shape)
       └─ ProviderType=Serverless: EndpointId, ApiKey (encrypted), JobMode
          (async|sync), ResultSchema, GpuType, MinWorkers, IdleTimeout
  └─ 5 worker images + handlers (helpers/runpod/serverless/)
       └─ comfy-worker   (Juggernaut, DWPose, identity: wrap ComfyUI workflow)
       └─ qwen-edit-worker (isolated ComfyUI clone rev e4c61d75)
       └─ qwen-vl-worker   (vLLM OpenAI-compatible handler)
```

### 5.1 App-side client changes (the real work)

| Today | After |
|---|---|
| `ComfyUIImageClient`: `POST /prompt` → poll `/history/{id}` → `GET /view` | submit Serverless job → poll status → read base64/JSON result |
| `ComfyUIImageEditingClient`: same ComfyUI HTTP pattern | same Serverless job pattern |
| `ComfyUIIdentityConditionedClient`: same ComfyUI HTTP pattern | same Serverless job pattern |
| Qwen VL: OpenAI `/v1/chat/completions` via `OpenAiMultimodalCompletionClient` | Serverless job with `{"input":{"messages":[…]}}` → parse structured JSON output |
| DWPose: ComfyUI `/prompt` | Serverless job |
| Model Manager `Provider` (BaseUrl + path) | new `Serverless` provider kind (endpoint id + api key + job mode + result schema) |

Keep the existing HTTP clients + pod providers working during transition (dispatcher pattern already
exists in `ImageGenerationClientDispatcher`); add a `RunPodServerlessClient` and switch provider
resolution per endpoint behind config.

### 5.2 Cold-start / cost configuration (per endpoint)

| Endpoint | Recommended mode | Why |
|---|---|---|
| DWPose | scale-to-zero, idle ~3 min | loads in seconds, cheap GPU |
| Juggernaut | scale-to-zero (FlashBoot if desired) | 7 GB load is tolerable; sporadic use |
| Identity | scale-to-zero | sporadic proof work |
| Qwen VL | `minWorkers: 1` **or** FlashBoot | 16 GB vLLM load is minutes; users feel it |
| Qwen Edit | `minWorkers: 1` **or** FlashBoot | 30 GB load is minutes; worst cold start |

## 5.3 Operations — how we connect & configure (pod tooling → Serverless)

Serverless workers have **no SSH** (no live box to log into). Every operation the current pod
tooling performs has a Serverless equivalent:

| Current pod operation | Current tool | Serverless equivalent |
|---|---|---|
| Provision scripts, tail logs, `nvidia-smi`, host-driver check | `helpers/runpod/ssh.ps1` (SSH key + IP) | **None.** No SSH/shell/live GPU inspection. Runtime + custom nodes + models are baked into a worker image via `Dockerfile`; failures surface in **per-job logs** (console / logs API). The CUDA-13 host-driver roulette disappears — RunPod controls Serverless fleet hosts |
| Create/migrate/start pods, pick GPU, get IPs | GraphQL: `pod.ps1`, `recreate-pod.ps1`, `start-retry-loop.ps1`, `start-and-monitor-multi.ps1` | **Endpoint management** via `runpodctl` or GraphQL: create endpoint, set GPU type, min/max workers, idle timeout, env vars, container image |
| `extra_model_paths.yaml` / `/pre_start.sh` self-heal after recycle | provision scripts over SSH | **Inherent** — fresh container from image each cold start; the fix is a `COPY`/`CMD` in the Dockerfile, no live patching |
| Readiness / `object_info` probes via `https://<pod>-<port>.proxy.runpod.net` | `generate-one.ps1`, `prove-one-image.py`, PowerShell probes | **Job-based smoke tests** — send a test job (`runpodctl send test` or RunPod API `POST /v2/{endpoint}/run`), read the job output/logs. No persistent HTTP to probe |
| Update Model Manager `BaseUrl` → pod proxy | `dbq.ps1` | Same — update the new Serverless provider fields (endpoint id + api key + job mode + result schema) |
| Document changes in `pod-registry.json` + manifests; reproduce via pod-creation skill | registry + skill | Becomes **image/endpoint registry** under `helpers/runpod/serverless/` (Dockerfiles + endpoint templates), reproducible via `docker build` + endpoint create; `pod-registry.json` gains a `serverless` migration note per workload |

Easier: no GPU roulette / recreate, restart-proofness is free, config is version-controlled.
Harder: **no interactive debugging** (build → deploy → job log → iterate), no live `nvidia-smi`/`ps`,
and large images (~30 GB Qwen Edit) make builds/pushes heavy. RunPod's **local dev mode** for
Serverless handlers lets handlers be developed/tested locally in a container before any GPU spend.

## 6. Implementation phases

- **P0 — Verify & decide (no code):** live Serverless GPU prices per endpoint; FlashBoot
  availability/cost; confirm `runsync` vs `run`/`status` timeout behavior for big jobs. Decide
  scale-to-zero vs minWorkers vs FlashBoot per endpoint (table above). Record in `pod-registry.json`
  migration notes.
- **P1 — Worker images + handlers (new `helpers/runpod/serverless/`):**
  - `comfy-worker/` (Juggernaut + DWPose + identity — one ComfyUI worker image with the identity
    node stack baked in; the identity stack is a superset of Juggernaut's).
  - `qwen-edit-worker/` (isolated ComfyUI clone at rev `e4c61d75` + 3 models + tokenizers pin).
  - `qwen-vl-worker/` (Python 3.13.2 + vllm 0.27.1 + abliterated model + vLLM launch flags).
  - Each handler: `handler(job)` → run workflow/inference → return JSON (PNG base64 or structured).
  - Verify handler + model SHA-256 checks from the existing provision scripts are reused.
- **P2 — Serverless client abstraction (Infrastructure):** `IRunPodServerlessClient` with submit +
  poll + parse; unit tests with fake HTTP. Add `Serverless` provider shape to Model Manager domain +
  SQLite migration + UI (ModelManager.razor) to add/edit endpoints.
- **P3 — Rewire call sites:** swap `ComfyUIImageClient` / `ComfyUIImageEditingClient` /
  `ComfyUIIdentityConditionedClient` / Qwen VL / DWPose call paths behind the Serverless client,
  gated per endpoint by provider type (HTTP provider still works → rollback-safe).
- **P4 — Cut over one endpoint at a time** (DWPose → Juggernaut → identity → Qwen VL → Qwen Edit).
  Each: create Serverless endpoint, point Model Manager at it, run the existing smoke test /
  deep proof, keep the pod running as fallback until the endpoint is validated.
- **P5 — Retire pods:** stop pods (keep volumes for fallback), then deprovision after a soak
  period. Update `pod-registry.json` + deployment manifests (per the RunPod-pod-changes rule) to
  record the Serverless migration and mark the pods retired. Update `GPU-SELECTION.md` + runbook
  docs with the Serverless workflow.
- **P6 — Docs + backlog close:** update `.github/instructions/*` runbooks, snapshot DB refresh not
  needed (no data-model change beyond new provider rows), close B-101.

## 7. Rollback strategy

- Each endpoint is behind a per-provider toggle; flipping a Model Manager provider back to the pod
  HTTP provider restores the previous behavior immediately.
- Pods are **stopped, not deleted**, until every endpoint has soaked green → deleting a pod is
  reversible within the migration window.
- No DB schema removal: the new Serverless provider columns/types are additive.

## 8. Risks & open questions

1. **Cold-start UX** for Qwen Edit / Qwen VL — mitigated by minWorkers or FlashBoot; must be
   demonstrated, not assumed.
2. **Serverless image size** — a 30 GB Qwen Edit image is heavy; consider download-at-first-start +
   FlashBoot instead of baking models into the image.
3. **`runsync` timeout** — long generations may exceed the sync timeout; plan for async
   submit + poll (the app already has a background-job pattern for scene image work).
4. **Live Serverless pricing** must be confirmed (pod catalog is a proxy).
5. **NSFW content on Serverless** — RunPod Serverless is generally permissive, but the adult-content
   editing path should be validated on the endpoint (same as the Qwen proof did on the pod).
