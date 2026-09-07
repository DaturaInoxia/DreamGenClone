# B-112 — Local uncensored FLUX.1-dev on the RTX 5080 host (ComfyUI) + app integration

**Status:** planned (2026-09-07). **State:** `designed` — runbook + proof assets exist; open
**Decision D1** (uncensored artifact pin) blocks execution.
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
| D1 | Uncensored FLUX.1-dev artifact to pin: (A) abliterated/uncensored FLUX fp8 **or** (B) stock `flux1-dev-fp8` + NSFW **unlock** LoRA ~0.7 | **OPEN — needs the real artifact URL/filename.** Anatomy LoRAs NOT needed (non-explicit). |
| D2 | Runtime = **ComfyUI** on the target host (Windows, torch cu128, `--listen 0.0.0.0 --port 8188`) | Made |
| D3 | **Proof-first:** qualify the model (`helpers/flux-local-host/`) before any app code | Made |
| D4 | App integration = new **Flux scene-image family** (code). The Api/NaturalLanguage path is **rejected** — its compiler applies a "fully clothed, non-explicit" guard that fights implied scenes | Made |

## 3. Target host profile

| Fact | Value |
|---|---|
| OS | Windows 11 (host of `helpers/jer-win-hardware.txt`) |
| GPU | RTX 5080, 16 GB (Blackwell sm_120) — FLUX fp8 ~12 GB fits |
| Driver | 576.88 (≥ 570 required for cu128; OK) |
| CPU / RAM | i7-14700K / 64 GB |
| Reachability | app reaches ComfyUI over HTTP, same pattern as the Qwen VL compiler provider (Model Manager BaseUrl) |

## 4. Install list (on the target host)

1. Verify prerequisites: `git`, Python 3.11/3.12, `nvidia-smi` driver.
2. **ComfyUI** — deterministic install (see runbook §1): clone `github.com/comfyanonymous/ComfyUI`
   to `D:\ComfyUI`, create venv, `pip install torch torchvision torchaudio --index-url
   https://download.pytorch.org/whl/cu128`, then `pip install -r requirements.txt`.
3. *(Optional)* **ComfyUI-GGUF** custom node if a GGUF-quantized uncensored build is chosen.
4. **Model files** to place:
   - `models/text_encoders/t5xxl_fp8_e4m3fn.safetensors` (~4.9 GB)
   - `models/text_encoders/clip_l.safetensors` (~0.25 GB)
   - `models/vae/ae.safetensors` (~0.3 GB)
   - `models/diffusion_models/<D1 file>` (fp8 UNet or GGUF)
   - *(Option B)* `models/loras/<unlock LoRA>` — applied at ~0.7
   - Download via `curl.exe -L --fail`; **record the pinned URL + SHA** in the runbook's
     "Decision 1" section before executing.
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

```
powershell -File helpers/flux-local-host/run-flux-proof.ps1 -ComfyUiUrl http://<host>:8188 -ModelFile "<D1 file>"
```
- Cells: `campfire-couple`, `kneeling-implied`, `garden-fours`, `standing-behind-seated`
  (`prompts-implied.json`).
- **PASS rubric:** blocking/arrangement honored AND implied-but-not-explicit (no nudity/anatomy).
- **Visual review of every PNG is mandatory** (no rubber-stamping). Expect ~1–2 min/img fp8 on 5080.
- B-112 is only executable/trustworthy after this PASS.

## 7. App integration (future code slice, gated on §6)

Separate plan-first code workstream (repo rule: no engine/image-pipeline code without plan +
confirmation):
- New `SceneImageModelFamily.Flux` + prompt dialect + `SceneImagePromptMetadata.IsCompatible` pair.
- ComfyUI client FLUX workflow builder (the ComfyUi transport the host exposes) — mirrors
  `flux-t2i-proof.json`.
- `FluxSceneImagePromptCompiler`: natural-language, **empty negative**, no "fully clothed" guard.
- Model Manager: provider (`ImageProtocol.ComfyUi`) + model row + dropdown options; Studio model
  picker; optional `RolePlaySceneImage` function-default.
- Tests green before done.

## 8. Risks / notes

- FLUX fp8 is slow locally (~1–2 min/img) — acceptable: it is free and iteration is the point.
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
