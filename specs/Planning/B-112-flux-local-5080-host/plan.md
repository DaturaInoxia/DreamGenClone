# B-112 — Local uncensored FLUX.1-dev on the RTX 5080 host (ComfyUI) + app integration

**Status:** executed in part on the 5080 host (2026-09-07); **app slice landed 2026-09-08**. **State:**
infra done + app integration implemented (additive; default untouched).
**Decision D1 resolved 2026-09-07** (Route 1 = stock `flux1-dev-fp8` + optional unlock LoRA;
exact downloads pinned in the runbook).
**Host execution 2026-09-07 (WOOD-GAME-MAIN):** ComfyUI 0.34.0 installed (`D:\ComfyUI`, torch
2.14.0+cu130 — cu130 upgrade approved by user, deviates from runbook cu128 pin), stock
`flux1-dev-fp8` + T5/CLIP/VAE placed, full 4-cell qualification proof **run + visually reviewed**:
`campfire-couple` PASS, `standing-behind-seated` PASS, `kneeling-implied` FAIL (composition:
man standing not kneeling; clothing), `garden-fours` FAIL (composition: upright kneeling not
all-fours; clothing). All four stock-FLUX cells rendered **without any safety refusal/distortion** —
failures were composition/clothing fidelity, so the unlock-LoRA fallback is NOT needed (and is
out of scope per safety boundary). ~23–24 s/img steady-state (28 steps, ~1.25–1.31 it/s).
**SDXL coexistence added (user request):** stock Juggernaut XL Ragnarok, BigLust v1.6, and Pony V6
XL single-file checkpoints downloaded into `D:\ComfyUI\models\checkpoints\`, all visible to
`CheckpointLoaderSimple`, each smoke-rendered successfully on the same host (proves FLUX + all
three SDXL checkpoints coexist). See runbook §Phase 2 addendum.

## 6a. SDXL-vs-FLUX implied-cell comparison (2026-09-07, local host)

Ran the same 4 implied (NON-explicit) cells on the three stock SDXL checkpoints using their family
recipes (Juggernaut/BigLust = SDXL natural-language `sdxl-t2i-smoke.json`; Pony = Pony tag dialect
`pony-t2i-smoke.json`, `rating_safe`) via `helpers/flux-local-host/run-sdxl-implied-compare.ps1`
(`prompts-implied-sdxl.json`). Outputs: `artifacts/tmp/images/sdxl-implied-compare/` (git-ignored).
Visually reviewed (PASS rubric = arrangement honored AND implied-but-not-explicit):

| Cell | Juggernaut | BigLust | Pony V6 |
|---|---|---|---|
| campfire-couple | PASS (man left/woman right, arm around, firelight) | PASS (couple + arm, firelight) | PASS (arrangement; anime style, side swap minor) |
| kneeling-implied | PASS (kneeling woman + standing man, clothed, tasteful) | **FAIL — went explicit** (unclothed; not implied) | PASS (squatting woman + standing figure, clothed) |
| garden-fours | **FAIL — went explicit** (nude; not on-all-fours implied) | **FAIL — went explicit** (bottomless; pose ok but explicit) | PASS-ish (kneeling, clothed, but not all-fours bottom-to-cam) |
| standing-behind-seated | PASS (man behind seated woman, hands/shoulders) | **FAIL — reversed** (woman behind seated man) | PASS (man behind woman, hands on shoulders) |

**Takeaway (matches B-112 hypothesis):** the NSFW-capable stock SDXL checkpoints (Juggernaut,
BigLust) **over-index to explicit** on the kneeling/garden "implied" cells despite `tasteful implied
scene`/clothed phrasing, and BigLust reversed the standing-behind-seated arrangement. Pony stayed
non-explicit (`rating_safe`) but is anime-style and less faithful on all-fours. **Stock FLUX stayed
implied-but-non-explicit on all four cells** and only failed on composition fidelity — i.e., FLUX is
the right family for text-driven implied/softcore arrangement; the SDXL models need explicit content
policy routing (they are the app's NSFW production path). This is why FLUX integration is the B-112
goal rather than trying to force SDXL to stay implied.
**Repo suite on localhost:** `run-juggernaut-simple-people-base.ps1` (committed SFW 2-adult base)
ran clean against `http://127.0.0.1:8188` → `artifacts/tmp/images/juggernaut-simple-people-replay/`.
**Related:** B-111 (Consistent Visual Production Program — model/endpoint expansion family),
B-100 (future canonical moment consumption). Not part of the B-111 superseded map; additive infra.

---

## 1. Context & problem

- The active production image model is **BigLust v1.6** (`bigLust_v16.safetensors`, SDXL). SDXL/CLIP
  has an architectural ceiling: it **cannot bind blocking/arrangement from text** ("couple at a
  campfire, man on the left", "woman on all fours, bottom to the camera"). This is documented, not a
  prompt problem.
- User need: text-driven **composition/instruction following** for **implied/softcore, NON-explicit**
  scenes (no nudity, no explicit anatomy). A safety-filtered model would refuse or distort these
  scenes, so the model must be uncensored.
- **LM Studio cannot run FLUX.1-dev**: it is an LLM runtime (no `/v1/images/generations`, no
  diffusion). The target host therefore runs **ComfyUI**.

## 2. Decisions

| # | Decision | Status |
|---|---|---|
| D1 | **RESOLVED 2026-09-07** — Route 1 = stock `flux1-dev-fp8.safetensors` (Comfy-Org; 17.25 GB file / ~16.1 GiB fp8 payload, verified by header parse — runs on 16 GB via ComfyUI offload) + fallback unlock LoRA `aidmaNSFWunlock-FLUX-V0.2.safetensors` (akash-guptag/NSFW-Flux-Lora) applied at 0.7 **only if** a stock-fp8 proof cell fails. Stock files pinned w/ exact URLs in runbook §Phase 2 Decision 1 (Comfy-Org UNet, comfyanonymous T5/CLIP, bfl or raidenzeke VAE). Option A (abliterated) verified: NO pre-built fp8 single-file exists — fp16 single-file (georgesung/rednox 23.8 GB → needs fp8 conversion) or GGUF (raidenzeke/t8star Q8 12.7 GB → needs ComfyUI-GGUF) or fp16 diffusers (aoxo). Follow-up only. Anatomy LoRAs NOT needed (non-explicit). |
| D2 | Runtime = **ComfyUI** on the target host (Windows, torch cu128, `--listen 0.0.0.0 --port 8188`) | Made |
| D3 | **Proof-first:** qualify the model (`helpers/flux-local-host/`) before any app code | Made |
| D4 | App integration = new **Flux scene-image family** (code). The Api/NaturalLanguage path is **rejected** — its compiler applies a "fully clothed, non-explicit" guard that fights implied scenes | Made |

## 3. Target host profile

| Fact | Value |
|---|---|
| OS | Windows 11 (host of `helpers/jer-win-hardware.txt`) |
| GPU | RTX 5080, 16 GB (Blackwell sm_120). Stock `flux1-dev-fp8` = 17.25 GB file / ~16.1 GiB fp8 payload (verified 2026-09-07) → NOT fully resident in 16 GB; ComfyUI auto-offloads (64 GB RAM fine). GGUF Q8 (12.7 GB) = fully-resident alternative. |
| Driver | 576.88 (≥ 570 required for cu128; OK) |
| CPU / RAM | i7-14700K / 64 GB |
| Reachability | app reaches ComfyUI over HTTP, same pattern as the Qwen VL compiler provider (Model Manager BaseUrl) |

## 4. Install list (on the target host)

1. Verify prerequisites: `git`, Python 3.11/3.12, `nvidia-smi` driver.
2. **ComfyUI** — deterministic install (see runbook §1): clone `github.com/comfyanonymous/ComfyUI`
   to `D:\ComfyUI`, create venv, `pip install torch torchvision torchaudio --index-url
   https://download.pytorch.org/whl/cu128`, then `pip install -r requirements.txt`.
3. *(Optional)* **ComfyUI-GGUF** custom node if a GGUF-quantized uncensored build is chosen.
4. **Model files** to place — exact URLs pinned in runbook §Phase 2 Decision 1 (verified 2026-09-07):
   - `models/diffusion_models/flux1-dev-fp8.safetensors` (17.25 GB) — Comfy-Org/flux1-dev (ungated)
   - `models/text_encoders/t5xxl_fp8_e4m3fn.safetensors` (4.9 GB) + `clip_l.safetensors` (0.25 GB)
     — comfyanonymous/flux_text_encoders (ungated)
   - `models/vae/ae.safetensors` (0.33 GB) — black-forest-labs/FLUX.1-dev (gated: accept license +
     HF_TOKEN) or ungated raidenzeke mirror
   - `models/loras/aidmaNSFWunlock-FLUX-V0.2.safetensors` (19 MB, **fallback only**, apply at 0.7)
     — akash-guptag/NSFW-Flux-Lora (ungated)
   - Download via `curl.exe -L --fail` (commands in the runbook). Abliterated builds (georgesung
     fp16 single-file / raidenzeke+t8star GGUF / aoxo fp16 diffusers) are documented Option A
     follow-up — none is a pre-built fp8 drop-in.
5. *(Optional, restart-proof)* Windows Task Scheduler "At log on" entry or NSSM service running the
   Phase 3 launch line.

## 5. Configure list

- Launch: `.venv\Scripts\python main.py --listen 0.0.0.0 --port 8188 --disable-auto-launch`
- Windows Firewall: allow inbound TCP 8188 (LAN) — or a VPN/tunnel; never expose an unauthenticated
  ComfyUI publicly.
- Recipe (baked into `helpers/flux-local-host/flux-t2i-proof.json`): DualCLIPLoader
  (t5xxl + clip_l, type `flux`) → FluxGuidance **3.5** → KSampler **euler / simple / 28 steps /
  cfg 1.0** / denoise 1.0, **empty negative**, 896×1152.
- Proof outputs → git-ignored `artifacts/tmp/images/flux-implied-proof/`.

## 6. Verification (proof — the actual test)

Exact ordered agent steps are in the runbook **§Phase 4 Steps 1–6** (2026-09-07): host check →
stock-fp8 baseline (all 4 cells, `-Seed 20260907`, no LoRA) → mandatory visual review → if a cell
failed as "too timid", LoRA A/B on the same seed (`-LoraFile aidmaNSFWunlock-FLUX-V0.2.safetensors
-LoraStrength 0.7`, retry 0.4 if it over-indexes explicit) → visual review → per-cell report.

Runner: `helpers/flux-local-host/run-flux-proof.ps1` (now supports `-LoraFile`/`-LoraStrength`).
- Cells: `campfire-couple`, `kneeling-implied`, `garden-fours`, `standing-behind-seated`
  (`prompts-implied.json`).
- **PASS rubric:** blocking/arrangement honored AND implied-but-not-explicit (no nudity/anatomy).
- **Visual review of every PNG is mandatory** (no rubber-stamping). Expect ~1–3 min/img fp8 on 5080.
- B-112 is only executable/trustworthy after a clean PASS set.

## 7. App integration — LANDED 2026-09-08 (additive; RolePlaySceneImage default untouched)

Implemented 2026-09-08 after plan + user confirmation:
- `SceneImageModelFamily.Flux` (=4) + `SceneImagePromptDialect.FluxNaturalLanguage` (=4) +
  `SceneImagePromptMetadata.IsCompatible` pair (Domain).
- `ComfyUIImageClient.BuildFluxWorkflow` (split UNETLoader / DualCLIPLoader / FluxGuidance 3.5 /
  cfg 1.0 / euler / simple / 28 steps / empty negative) + Flux dispatch in the ComfyUI and
  serverless clients.
- `FluxSceneImagePromptCompiler` — natural language via the shared SDXL brief builder, empty
  negative, no "fully clothed" guard — plus the scene-asset Flux arm and DI registration.
- Validation arms: `ModelResolutionService`, `RegisteredModelRepository`, and FLUX dropdown options
  in `ModelDetailsEditor.razor` + `ModelManager.razor`.
- Local FLUX row enabled (Family=Flux, Dialect=FluxNaturalLanguage) on the dev DB via
  `local-comfyui-configure http://192.168.0.16:8188` — additive, NOT the `RolePlaySceneImage` default.
- Live verify 2026-09-08: `campfire-couple` implied cell rendered PASS on the 5080 host with the
  app recipe (arrangement honored, implied-but-non-explicit).
- NOTE (follow-up): the shared natural-language system prompt is SDXL-branded; a FLUX-grounded
  system prompt is a documented follow-up, not required to route FLUX.

## 8. Risks / notes

- FLUX fp8 is slow locally (~1–2 min/img) — acceptable: it is free and iteration is the point.
- No pre-built fp8 single-file abliterated FLUX.1-dev exists (verified 2026-09-07): abliterated =
  fp16 single-file (georgesung/rednox, 23.8 GB → needs fp8 conversion), GGUF (raidenzeke/t8star,
  Q8 12.7 GB → needs ComfyUI-GGUF node), or fp16 diffusers (aoxo). Route 1 (stock fp8 + optional
  unlock LoRA) is therefore the proof artifact.
- **cfg must stay 1.0**; the real strength control is FluxGuidance (3.5). Raising CFG breaks FLUX.
- **Empty/minimal negative** — a heavy SDXL negative will fight implied scenes.
- Blackwell requires **cu128** (a cu124 build fails with "no kernel image" on sm_120).
- Restart ComfyUI after placing model files, or UNETLoader/CheckpointLoaderSimple will not list them.
- Forward-only changes; never commit model files or the dev DB.

## 9. References

- Runbook (install/configure steps): `docs/flux-local-5080-comfyui-setup.md`
- Proof assets: `helpers/flux-local-host/{flux-t2i-proof.json, prompts-implied.json, run-flux-proof.ps1}`
- Canonical prompt standards: `.github/instructions/scene-image-prompt-compiler-standards.instructions.md`
- Corrected model facts + LM Studio limitation: `/memories/repo/flux-local-5080-plan.md`
