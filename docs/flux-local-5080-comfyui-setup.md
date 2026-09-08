# Uncensored FLUX.1-dev on the RTX 5080 host (local ComfyUI) — executable runbook

> **Status:** plan/persisted runbook (2026-09-07). Not yet executed. Agent executes this **on the 5080
> host** (Windows), then the app on the dev box reaches the ComfyUI origin over HTTP — the same way
> the app already reaches the Qwen VL compiler provider.
>
> **Why not LM Studio?** LM Studio is an LLM runtime (llama.cpp GGUF / MLX). Its OpenAI-compatible
> surface is `/v1/models`, `/v1/chat/completions`, `/v1/responses`, `/v1/embeddings`,
> `/v1/completions` — there is **no `/v1/images/generations` and no diffusion support**. The
> Qwen2.5-VL abliterated *compiler* runs there because it is an LLM. FLUX.1-dev is a **diffusion**
> model and needs **ComfyUI** on this host.

## Goal

Run an **uncensored / abliterated FLUX.1-dev** behind ComfyUI on the 5080 host and prove it does the
thing BigLust/SDXL cannot: honor **blocking/arrangement from text** (e.g. *"a woman on her knees, a
man standing in front of her"*, *"woman in a garden on all fours, her bottom showing to the camera"*)
for **non-explicit, implied/softcore** scenes that a safety-filtered model would refuse or distort.
App integration (driving it from Model Manager) is a **separate, later** code feature — see §8.

Host facts (from `helpers/jer-win-hardware.txt`): Windows 11, i7-14700K, 64 GB RAM, **RTX 5080
16 GB** (Blackwell sm_120), NVIDIA driver **576.88** (CUDA 12.8+ capable), ample disk. NOTE: stock
`flux1-dev-fp8.safetensors` is a **17.25 GB file / ~16.1 GiB fp8 payload** (verified 2026-09-07), so
on 16 GB it runs via ComfyUI's automatic model offload (64 GB RAM makes that fine) — not fully
VRAM-resident. See Decision 1 for exact numbers and the fully-resident GGUF alternative.

## Files in this package

| Path | Purpose |
|---|---|
| `helpers/flux-local-host/flux-t2i-proof.json` | Canonical FLUX.1-dev txt2img workflow (node ids 6/7/3/9 match `generate-one.ps1` overrides). |
| `helpers/flux-local-host/prompts-implied.json` | Qualification cells (implied/softcore, non-explicit) + PASS rubric. |
| `helpers/flux-local-host/run-flux-proof.ps1` | Runs every cell once through `helpers/runpod/generate-one.ps1`, saves PNGs to git-ignored `artifacts/tmp/images/flux-implied-proof/`. |

## Phase 0 — Prereqs (run on the 5080 host)

```powershell
nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv
python --version        # need 3.11 or 3.12
git --version
```
- Driver must be ≥ 570 for Blackwell + cu128 torch. 576.88 is fine.
- Enough disk: stock fp8 UNet 17.25 GB + T5 4.9 GB + CLIP 0.25 GB + VAE 0.33 GB + LoRA 0.02 GB
  ≈ **23 GB**; ComfyUI + venv ≈ 6 GB.

## Phase 1 — Install ComfyUI (deterministic path)

```powershell
git clone https://github.com/comfyanonymous/ComfyUI.git D:\ComfyUI
cd D:\ComfyUI
python -m venv .venv
.\.venv\Scripts\python -m pip install --upgrade pip
.\.venv\Scripts\python -m pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu128
.\.venv\Scripts\python -m pip install -r requirements.txt
```
> The `cu128` wheel is mandatory on RTX 50-series (sm_120 needs torch ≥ 2.7 + cu128).

## Phase 2 — Models (decision gate first)

FLUX.1-dev needs **three** separate components in ComfyUI:

| Component | File (default name) | Location |
|---|---|---|
| Text encoder (T5) | `t5xxl_fp8_e4m3fn.safetensors` (~4.9 GB) | `models/text_encoders/` |
| Text encoder (CLIP-L) | `clip_l.safetensors` (~0.25 GB) | `models/text_encoders/` |
| VAE | `ae.safetensors` (~0.3 GB) | `models/vae/` |
| Diffusion UNet | **Decision 1** | `models/diffusion_models/` |

The T5/CLIP/VAE come from the stock FLUX.1-dev split (Comfy-Org / black-forest-labs repackages).
The UNet is where the uncensored choice lives:

**Decision 1 — the diffusion model (PINNED 2026-09-07; every URL verified live + gating checked):**

**Route 1 (PRIMARY — execute now): stock Comfy-Org `flux1-dev-fp8.safetensors`; the unlock LoRA is a
FALLBACK, applied only if a stock cell fails the proof rubric.**

Verified facts (HF API + safetensors header parse, 2026-09-07):
- `flux1-dev-fp8.safetensors` (Comfy-Org/flux1-dev, ungated) is a **17.25 GB file** ≈ **16.1 GiB**
  of `F8_E4M3` tensors (+ ~0.55 GB f32/f16). It is **NOT ~11.6 GB** and is **not fully
  VRAM-resident** on a 16 GB card — ComfyUI runs it via automatic model offload (host has 64 GB
  RAM, so fine; expect ~1–3 min/image fp8). A fully-resident fp8 alternative = GGUF Q8 below.
- Download sources below verified ungated except where noted.

Exact downloads (file → ComfyUI folder):

| # | File → destination | Verified URL (2026-09-07) | Size |
|---|---|---|---|
| 1 | `flux1-dev-fp8.safetensors` → `models/diffusion_models/` | `https://huggingface.co/Comfy-Org/flux1-dev/resolve/main/flux1-dev-fp8.safetensors` | 17.25 GB |
| 2 | `t5xxl_fp8_e4m3fn.safetensors` → `models/text_encoders/` | `https://huggingface.co/comfyanonymous/flux_text_encoders/resolve/main/t5xxl_fp8_e4m3fn.safetensors` | 4.9 GB |
| 3 | `clip_l.safetensors` → `models/text_encoders/` | `https://huggingface.co/comfyanonymous/flux_text_encoders/resolve/main/clip_l.safetensors` | 0.25 GB |
| 4 | `ae.safetensors` → `models/vae/` | `https://huggingface.co/black-forest-labs/FLUX.1-dev/resolve/main/ae.safetensors` (gated: accept the FLUX.1-dev license once, then export `HF_TOKEN`); ungated fallback (standard FLUX VAE): `https://huggingface.co/raidenzeke/flux.1dev-abliterated-gguf/resolve/main/ae.safetensors` | 0.33 GB |
| 5 | *(fallback only)* `aidmaNSFWunlock-FLUX-V0.2.safetensors` → `models/loras/` | `https://huggingface.co/akash-guptag/NSFW-Flux-Lora/resolve/main/aidmaNSFWunlock-FLUX-V0.2.safetensors` | 19 MB |

Route 1 download commands (run on the 5080 host):
```powershell
curl.exe -L --fail -o D:\ComfyUI\models\diffusion_models\flux1-dev-fp8.safetensors "https://huggingface.co/Comfy-Org/flux1-dev/resolve/main/flux1-dev-fp8.safetensors"
curl.exe -L --fail -o D:\ComfyUI\models\text_encoders\t5xxl_fp8_e4m3fn.safetensors "https://huggingface.co/comfyanonymous/flux_text_encoders/resolve/main/t5xxl_fp8_e4m3fn.safetensors"
curl.exe -L --fail -o D:\ComfyUI\models\text_encoders\clip_l.safetensors "https://huggingface.co/comfyanonymous/flux_text_encoders/resolve/main/clip_l.safetensors"
curl.exe -L --fail -o D:\ComfyUI\models\vae\ae.safetensors "https://huggingface.co/black-forest-labs/FLUX.1-dev/resolve/main/ae.safetensors"
curl.exe -L --fail -o D:\ComfyUI\models\loras\aidmaNSFWunlock-FLUX-V0.2.safetensors "https://huggingface.co/akash-guptag/NSFW-Flux-Lora/resolve/main/aidmaNSFWunlock-FLUX-V0.2.safetensors"
```

**LoRA rule (Route 1):** run the stock-fp8 proof FIRST with no LoRA (the scenes are NON-explicit;
stock FLUX may already pass). Only if a cell fails the rubric (arrangement honored AND
implied-but-not-explicit) because stock FLUX is too timid, re-run that cell with the unlock LoRA at
**0.7**: re-run the proof with `run-flux-proof.ps1 -LoraFile aidmaNSFWunlock-FLUX-V0.2.safetensors`
(the runner wires a `LoraLoaderModelOnly` node — node 12 — between UNETLoader and KSampler's
`model` input at `strength_model` 0.7). Ensure the LoRA file is in the host's `models/loras/`
(the Route-1 download step put it there). Anatomy/NSFW LoRAs are NOT required for any route
(non-explicit, non-nude).

**Option A (abliterated — NOT the proof artifact; separate follow-up).** Verified 2026-09-07 via HF
API: every public abliterated FLUX.1-dev is fp16 or GGUF — **no pre-built fp8 single-file ComfyUI
checkpoint exists**:

| Build | Format / file | Size | Note |
|---|---|---|---|
| `georgesung/flux.1-dev-abliterated-merged` | `flux_ablit_v2.safetensors` single-file fp16 UNet | 23.8 GB | Full set (`t5_xxl_ablit_v2` 9.5 GB, `clip_l_ablit_v2`, `ae_ablit_v2`); ungated. Too big to be resident on 16 GB → convert to fp8 (~11.9 GB) for the real drop-in. |
| `rednox/flux.1dev-abliteratedv2_merged` | `TransparentFLUX.safetensors` | 23.8 GB | fp16 single-file merged; same conversion story. |
| `raidenzeke/flux.1dev-abliterated-gguf` | `flux.1dev_abliterated_Q8_0.gguf` | 12.7 GB | Fits 16 GB fully; needs ComfyUI-GGUF node + GGUF loader. |
| `t8star/flux.1-dev-abliterated-V2-GGUF` | Q8_0 / Q6_K / Q4_K_M `.gguf` | 12.7 / 9.9 / 6.9 GB | Highest adoption (~9.3k dl); needs ComfyUI-GGUF. |
| `aoxo/flux.1dev-abliterated[v2]` | fp16 diffusers (transformer shards ~23.8 GB + T5 ~11.5 GB) | — | Not ComfyUI-loadable as-is; needs diffusers→ComfyUI fp8 conversion. |

## Phase 3 — Launch + verify

```powershell
cd D:\ComfyUI
.\.venv\Scripts\python main.py --listen 0.0.0.0 --port 8188 --disable-auto-launch
```
Verification (from the host, then from the dev box):
```powershell
curl.exe http://127.0.0.1:8188/system_stats
# Confirm your diffusion file is visible to UNETLoader:
curl.exe "http://127.0.0.1:8188/object_info/UNETLoader"
```
Make it reachable from the app box: allow TCP 8188 in Windows Firewall (LAN), or put it behind a
VPN/tunnel. The app connects server-side, so no CORS issues. Do **not** expose an unauthenticated
ComfyUI directly to the public internet.

## Phase 4 — Qualification proof (the actual "test")

**Agent executes these steps IN ORDER.** The runner is `helpers/flux-local-host/run-flux-proof.ps1` on
the **dev box** (repo root); it talks to the 5080 host's ComfyUI over HTTP, and PNGs land under the
dev box repo's git-ignored `artifacts/tmp/images/flux-implied-proof/<cell-id>/` (not on the host).

**Prereq (dev box):** repo must be current — `-LoraFile`/`-LoraStrength` + workflow node 12 were
added 2026-09-07:
```powershell
git pull
powershell -ExecutionPolicy RemoteSigned -File helpers/flux-local-host/run-flux-proof.ps1 -?   # params incl. -LoraFile/-LoraStrength
```

**Step 1 — Confirm the host is up and both files are visible to ComfyUI** (`<HOST>` = 5080 host LAN
IP/hostname):
```powershell
curl.exe http://<HOST>:8188/system_stats
# UNETLoader must list flux1-dev-fp8.safetensors:
curl.exe "http://<HOST>:8188/object_info/UNETLoader"
# LoraLoaderModelOnly must list aidmaNSFWunlock-FLUX-V0.2.safetensors:
curl.exe "http://<HOST>:8188/object_info/LoraLoaderModelOnly"
```
If the LoRA is missing, download it to `D:\ComfyUI\models\loras\` on the host (Decision 1
download #5) and restart ComfyUI.

**Step 2 — Stock fp8 baseline (NO LoRA), all 4 cells, fixed seed:**
```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/flux-local-host/run-flux-proof.ps1 `
  -ComfyUiUrl http://<HOST>:8188 -Seed 20260907
```
(~1–3 min/image fp8 via offload on the 5080; baseline ≈ 6–12 min for 4 cells. Model default is
`flux1-dev-fp8.safetensors`; no `-LoraFile` = stock.)

**Step 3 — Visual review (MANDATORY, never rubber-stamp).** Open every PNG under
`artifacts/tmp/images/flux-implied-proof/<cell-id>/` and record per-cell PASS/FAIL against the
rubric in `prompts-implied.json`: **PASS = the prompt's blocking/arrangement is honored AND the
result is implied-but-not-explicit** (no nudity/genital detail). Cells: `campfire-couple`,
`kneeling-implied`, `garden-fours`, `standing-behind-seated`.

**Step 4 — Decision / LoRA A/B.**
- If **all 4 PASS**: stop — stock fp8 is sufficient; the LoRA is not needed. Record the result.
- If **≥1 FAILED because stock was too timid** (arrangement OK but the implied point was flattened or
  refused): re-run ONLY the failing cell(s) with the unlock LoRA at 0.7, **same seed**:
```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/flux-local-host/run-flux-proof.ps1 `
  -ComfyUiUrl http://<HOST>:8188 -Seed 20260907 `
  -Only <failing-cell-ids-comma-separated> `
  -LoraFile aidmaNSFWunlock-FLUX-V0.2.safetensors -LoraStrength 0.7
```

**Step 5 — Visual review the LoRA outputs** (same mandatory rule) and A/B each cell against its stock
baseline (same seed). Record per-cell PASS/FAIL. If a result over-indexes toward explicit framing
(against the non-explicit goal), retry at `-LoraStrength 0.4`.

**Step 6 — Report back** with a per-cell table `cell | stock PASS/FAIL | +LoRA PASS/FAIL | note` and
the final verdict: which configuration qualifies (stock, stock+LoRA@0.7, or stock+LoRA@0.4). Only a
clean PASS set makes B-112 executable.

## Phase 5 — Optional: keep it running (restart-proof)

Add a Windows Task Scheduler "At log on / At startup" entry running the Phase 3 launch line, or use
NSSM to run ComfyUI as a service — the same "services must survive restart" philosophy as the pod
registry. Not required for a one-off test.

## Phase 6 — App integration (FUTURE, separate workstream)

Not in this runbook. The app's ComfyUI client only builds **Pony/SDXL** workflows and its
`SceneImageModelFamily` has no **Flux**; driving FLUX like the other models needs a small code
feature (Flux family + dialect + ComfyUI FLUX workflow builder + compiler + Model Manager entries).
**Trap:** registering FLUX behind an OpenAI-images "API" model would hit the existing
Api/NaturalLanguage compiler's *"fully clothed, non-explicit"* guard meant for filtered cloud
providers — that would fight the implied scenes this model exists to render, so that is not a
faithful test. Do the proof (§4) first; then plan the family feature against the backlog.

## Gotchas / rules

- **cfg must stay 1.0** for FLUX; the real strength control is the **FluxGuidance** node (3.5).
  Do not raise CFG like an SDXL model.
- **Empty/minimal negative.** A heavy SDXL negative will fight implied scenes. Default = empty.
- Resolution must be **multiples of 16** (~1 MP). Proof default 896×1152.
- Blackwell needs **cu128**; a cu124 build fails with "no kernel image" on sm_120.
- If CheckpointLoaderSimple/UNETLoader "not in list": the file is in the wrong folder or ComfyUI
  wasn't restarted after adding it.
- Outputs go to git-ignored `artifacts/tmp/...`. Never commit model files or the dev DB. Forward-only
  fixes (no `git restore`).
