# B-119 — Multi-character scene images with controlled positions: layout toolbox + production workflow

**Status:** designed (landscape + feasibility verified 2026-09-09). **State:** `designed`.
**Priority:** medium · **Scope:** medium. Additive; never repoints defaults; no fallbacks.

> **Program context:** this item is in the **structural-capability** block of
> `specs/Planning/identity-lora-program-map.md`, not on the identity → body → LoRA critical path.
> It becomes load-bearing for B-123 only for **forced/awkward layout** training cells (lying,
> all-fours, multi-body). It **consumes** B-120's derived-asset store and **does not** duplicate
> B-117's OpenPose path. Scheduled in parallel — it does not block B-121/B-122 or B-123's schema and
> ordinary-cell work, but required layout cells cannot execute or freeze until B-119/B-120 exist.
**Companions (canonical numbers already allocated by parallel work):**
- **B-117 — Pose-controlled scene compositions via local ControlNet (DWPose + OpenPoseXL2)** = the OpenPose blocking/render implementation (Route C2 here). This item does NOT duplicate it.
- **B-118 — Pose Studio** (2D wireframe pose editor + DWPose extract + pose library) = pose authoring (feeds B-117).
- **B-119 (this item)** = the *decision workflow* (`workflow.md`) + the *non-OpenPose structural toolbox* (depth/canny ControlNet from a reference, regional IP-Adapter identity, img2img re-skin coordination) + the FLUX.2 local verdict. Related: B-116 (img2img re-skin), B-032, B-111, B-112, B-100.

---

## 1. Purpose
The single hardest problem in the image pipeline: **multiple characters, each in a specific position/pose, in one coherent frame** — with identity and location preserved. This item records (a) the field's toolbox for solving it, (b) which tools actually run on the local ComfyUI host (`192.168.0.16:8188`, ComfyUI 0.34.0, RTX 5080 16 GB), (c) the FLUX.2 local verdict, and (d) how each tool maps into the application as a **decision workflow an agent/user follows per situation** (not a strict script): Step 1 is A, Step 2 is B, Step 3 branches C/D by situation.

## 2. Ground truth (the field's answer)
Text cannot place multiple people in specified positions on any model — priors win. Every working solution puts layout in via an **image or a structure**, never prose. Mechanisms, in reliability order for this problem:
1. **ControlNet** on a whole-scene structure image — the primary "positions" tool:
   - **Multi-person OpenPose / DWPose** (skeletons) — good for upright poses; **weak for lying/reclining** (2D skeleton ambiguity, verified in repo on Juggernaut SDXL). Implementation tracked in canonical B-117.
   - **Depth** (DepthAnything/MiDaS/Zoe/Metric3D) of a reference photo — **best for reclining/multi-body** (a horizontal body is an unambiguous depth mass). Layout inherited from a real image. (B-119 scope.)
   - **Canny / Lineart** — edges of a reference → strong layout lock. (B-119 scope.)
   - **Segmentation maps** (OneFormer/UniFormer) — explicit per-region semantic boxes.
   - **Regional prompting + regional ControlNet** (Impact Pack RegionalPrompt/RegionalSampler) — each latent region gets its own prompt + control.
2. **Regional IP-Adapter** (per-character identity in its region, one render) — conditions *identity*, not position.
3. **img2img re-skin** (B-116) — carry a whole layout/location image forward at denoise < 1 (proven local on FLUX @ ~0.75).
4. **Native multi-reference models** (FLUX.2 Kontext, Qwen-Image-Edit 2511, GPT-image, Nano-Banana, Seedream 4.0) — take actual character images + place them; best identity-presence, weaker exact blocking.
5. **Deterministic staging** (3D/rig → depth/seg pass → ControlNet) — exact independent per-character positions; most labor.
6. **2.5D compositing** (render/cut/place/harmonize) — most labor, weakest fusion realism.

## 3. Local host feasibility (verified by /object_info probe 2026-09-09)

| Tool | Nodes on host | Weights on host | Runs locally today? |
|---|---|---|---|
| Multi-person **OpenPose** ControlNet (SDXL) | ✓ (`OpenposePreprocessor`, `DWPreprocessor`, `ControlNetApplyAdvanced`) | ✓ **`thibaud-openpose-xl2/OpenPoseXL2.safetensors`** (installed) | **YES** — impl tracked in canonical B-117 (BigLust/Juggernaut/Pony Realism) |
| **Depth** ControlNet (SDXL) | ✓ (DepthAnything v1/v2, MiDaS, Zoe, LeReS, Metric3D) | ✗ no depth controlnet weight | Install weight to run (B-119) |
| **Canny** ControlNet (SDXL) | ✓ (built-in `Canny` + `PyraCannyPreprocessor`) | ✗ | Install weight to run (B-119) |
| Segmentation / Lineart / Scribble / HED / Normal preprocessors | ✓ (OneFormer, UniFormer, Lineart, HED, PiDi, BAE/DSINE) | ✗ (need matching controlnet weights) | Install weight to run |
| **Regional prompting** (Impact Pack) | ✓ full pack (`RegionalPrompt`, `RegionalSampler`, `CombineRegionalPrompts`, SEGS/Detailer) | n/a | **YES** |
| **Regional IP-Adapter** per-character identity | ✓ (`IPAdapterRegionalConditioning`, `IPAdapter`, masks) + adapter files (PLUS FACE, generic `ip-adapter_sdxl_vit-h`, CLIP-ViT-H) | ✓ | **YES** (SDXL-family) |
| **img2img re-skin** (`LoadImage → VAEEncode → KSampler denoise`) | ✓ | n/a (FLUX ae VAE present) | **YES** as hand-run workflow; app wiring = B-116 |
| FLUX.1-dev ControlNet | ✓ generic `ControlNetApply*` | ✗ FLUX-format weights not installed; fp8+ControlNet tight on 16 GB | Optional; heavier |
| **FLUX.2 Kontext / dev / flex / schnell** | nodes present (`Flux2Scheduler`) but **VRAM wall** | — | **NO on 5080 16 GB** (12B family; Kontext ~24–30 GB+ even fp8) → **cloud** (TogetherAI `black-forest-labs/FLUX.2-pro` registered) |
| **Qwen-Image-Edit 2511** multi-ref | — | — | Cloud serverless only (no local AIO) |
| Seedream-4.0 / gpt-image-2 | — | — | Cloud image API (TogetherAI rows registered) |

## 4. Decisions (open — confirm before implementation)
- **D1 — FLUX.2 = cloud-only** for this user. Do not plan local FLUX.2; use the TogetherAI `FLUX.2-pro` row for multi-subject reference experiments.
- **D2 — structural v1 = SDXL OpenPose (canonical B-117) + depth/canny ControlNet (B-119)** on BigLust/Juggernaut/Pony Realism, combined with regional IP-Adapter for per-character identity. FLUX stays the non-structural geometry/instruction follower.
- **D3 — weight installs are host changes** (documented + reproducible): depth + canny weights to the local ComfyUI, recorded in `docs/local-comfyui-model-manager-setup.md` + an idempotent helper (`helpers/flux-local-host/install-sdxl-controlnets.ps1`); each smoke-rendered.
- **D4 — incorporate as workflow, not a strict pipeline** (see `workflow.md`).

## 5. Application incorporation map (workflow route → app capability)
| Workflow route | Needed app capability | Status |
|---|---|---|
| Single-subject / standard multi-char (high prior) | FLUX render by model pick + identity render + Qwen image-editor | **exists today** |
| Layout already exists as an image (pose base / location plate) | **img2img re-skin** operation (`Reskin`, denoise) | B-116 (planned) |
| Forced layout from an OpenPose skeleton | **OpenPose ControlNet composition render** + pose picker | canonical **B-117** (+ B-118 pose studio) |
| Forced layout from depth/canny of a reference photo (reclining/multi-body) | **ControlNet render** with layout-reference input + structure type (Depth/Canny) + optional per-char identity regions | **B-119** (this item) |
| Location reuse across beats | location plate → re-skin (B-116) or depth of canonical location → ControlNet | B-116 / B-119 |
| Identity last (faces/anatomy on a settled geometry) | existing Qwen identity edit + regional IP-Adapter render | exists / extend regional |

No new defaults; all routes additive and capability-gated (fail-fast, no fallback) per repo rules.

## 6. Out of scope / notes
- Text-only multi-character layout remains unsupported (do not attempt).
- Lying/reclining dictation should prefer **depth/canny of a reference** over OpenPose (OpenPose lying fails).
- Compositing separately-rendered people (2.5D) is explicitly NOT recommended as an app route.
