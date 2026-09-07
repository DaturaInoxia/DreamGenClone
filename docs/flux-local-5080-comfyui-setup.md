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
16 GB** (Blackwell sm_120), NVIDIA driver **576.88** (CUDA 12.8+ capable), ample disk. 16 GB VRAM
fits FLUX.1-dev fp8 (~12 GB) with headroom.

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
- Enough disk: FLUX fp8 + encoders + VAE ≈ 18–20 GB; ComfyUI + venv ≈ 6 GB.

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

**Decision 1 — the diffusion model (do not fabricate; pin the real artifact at execution):**
- **Option A — uncensored/abliterated FLUX.1-dev fp8 (preferred).** Download the exact
  community build you selected (Civitai / HuggingFace), place in `models/diffusion_models/`, and
  pass its filename as `-ModelFile` to the proof runner. Name/URL must be recorded here once chosen.
- **Option B — stock `flux1-dev-fp8.safetensors` + NSFW "unlock" LoRA (~0.7).** Stock FLUX refuses
  implied-sexual scenes; the unlock LoRA neutralizes the refusal. LoRA goes in `models/loras/` and
  is applied via a `LoraLoaderModelOnly` node between UNETLoader and KSampler.
- **Anatomy/NSFW LoRAs are NOT required** for this use case (non-explicit, non-nude).

If a chosen build is GGUF (quantized), install the `ComfyUI-GGUF` custom node and use the `.gguf`
filename; the rest of this runbook is unchanged.

Download helper example (fill in the real URL at execution; outputs are git-ignored if staged in
`artifacts/tmp`):
```powershell
curl.exe -L --fail -o D:\ComfyUI\models\diffusion_models\<your-file>.safetensors "<pinned-url>"
```

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

From the dev box (or host) against the local origin:
```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/flux-local-host/run-flux-proof.ps1 `
  -ComfyUiUrl http://<host>:8188 `
  -ModelFile "<your-pinned-uncensored-file>.safetensors"
# optional: -Only kneeling-implied,garden-fours   -Seed 20260907
```
Then **visually review every PNG** (repo rule — never rubber-stamp). Rubric per cell
(`prompts-implied.json`): PASS only if the prompt's **blocking/arrangement is honored** AND the
result is **implied-but-not-explicit** (no nudity/genital detail). Record per-cell PASS/FAIL.
Expect ~1–2 min/image fp8 on the 5080.

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
