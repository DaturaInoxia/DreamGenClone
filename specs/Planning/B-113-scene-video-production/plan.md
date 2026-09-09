# B-113 — Scene video production: Wan 2.2 uncensored I2V/FLF2V over rendered keyframes + local ComfyUI video setup

**Status:** research + approach agreed. **State:** `designed` (2026-09-08). **Scope:** large (additive
production epic — does not touch the RP text engine). **Priority:** low.
**Evidence boundary:** research facts verified 2026-09-08 from Hugging Face model cards / repos and
the MiniMax H3 license text. Links and claims are pinned below with dates; re-verify before build.

> **What this is:** video = **animate between already-rendered, identity-consistent keyframes**
> (I2V / first-and-last-frame-to-video), NOT text-to-video. It plugs into the existing beat →
> keyframe seam in B-100/B-111. This doc records the 2026-09-08 model-landscape research, the
> decisions that fall out of it, the **local ComfyUI video setup** (the concrete first infra slice
> on WOOD-GAME-MAIN), and the open watch-gates (MiniMax H3 license, Wan 3.0 weights).

---

## 1. Research summary (2026-09-08) — "latest & greatest" verdict

User asked (a) which current video models to build on, and specifically whether an
**uncensored/adult-capable open path** exists (their still-image stack is Juggernaut/BigLust/Pony +
FLUX, all community-uncensored on ComfyUI; video must match that, since commercial APIs filter adult
content out).

### 1.1 Landscape verdicts

| Candidate | Verdict 2026-09-08 | Why |
|---|---|---|
| Commercial APIs — Google **Veo 3.x**, OpenAI **Sora 2**, Runway **Gen-4**, Kling 2.x | ❌ **Not for this content** | API-only; content/safety filters block adult roleplay output; no ComfyUI/Model Manager integration. Raw quality is the ceiling, but unusable here. |
| **Wan 2.2** (Apache-2.0) | ✅ **Confirmed build path** | Permissive license, ComfyUI-native workflows, mature uncensored fine-tune/LoRA ecosystem. See §1.2. |
| **MiniMax H3** (~33B, trending) | ⚠️ **License gate (US/EU/UK/KR)** | Open weights ARE released, but the community license **excludes the US/EU/UK/South Korea** (requires a formal MiniMax application). Host/developer is US-based → **not auto-usable** until a license is granted. See §1.3. |
| **Wan 3.0** (announced ~2026-09-04) | 👀 **Watch only** | Repo ships **no weights** (README + .gitattributes only, verified 2026-09-08); webapp-first at `wan30.io`. See §1.4. |

### 1.2 Wan 2.2 — the confirmed uncensored open path (Apache-2.0)

Official family (Apache-2.0, ComfyUI-native, `Comfy-Org/Wan_2.2_ComfyUI_Repackaged`):
- **TI2V-5B** — hybrid text+image→video; runs in ~8 GB VRAM with ComfyUI offload → **the local 5080 model**.
- **T2V-A14B / I2V-A14B** — 14B MoE (27B total, ~14B active); quality tier → **RunPod serverless**.
- **FLF2V** — first-frame → last-frame workflow (the exact seam for our MomentTransition/BeatExcerpt coverage).
- Official 5B speed ≈ a 5 s 720p clip in < 9 min on a consumer GPU; Lightx2v 4-step LoRAs cut steps.

**Uncensored ecosystem (verified on HF, all Apache-2.0, tagged ComfyUI):**
- **`rzgar/wan2.2-i2v-A14B-Uncensored-base`** — I2V-14B uncensored base built as a merge/fine-tune
  foundation. Author-reported: understands short explicit prompts out of the box; solid male+female
  anatomy ≈ **7/10 reliability**; follows prompts closely; stable under LoRA stacking. Pair with
  **CubeyAI-GeneralN High/Low** LoRAs at 0.5–0.55 for fluid rhythmical motion.
- **`rzgar/Wan2.2_LightX2V_4Step_Uncensored`** — high/low **4-step Lightx2v uncensored LoRA pair** that
  works for **both I2V and T2V** (one workflow serves both); ~80–85% body accuracy on T2V; optional
  appearance-enhancer LoRAs. Keep this pair near full strength or motion blurs/breaks.

**Prompting discipline (matters for compiler design):** Wan's text encoder is **UMT5-XXL — a weak
reasoner**. Be descriptive/explicit about actions & objects NOT visible in the input frame; vague
prompts fail. The author's workaround for adding new elements: plant a distinctly-colored shape in
the scene and instruct the model to replace it — pairs naturally with first/last-frame face/continuity
swaps. Audio for Wan: MMAudio (bg/SFX), SkyReel V3 (talking), LTX-2.3 (fast, less reliable).

### 1.3 MiniMax H3 — license findings (verified from the license text 2026-09-08)

`MiniMaxAI/MiniMax-H3` (33B dense, ~4.99M downloads/mo, ComfyUI templates exist). Design maps
1-to-1 onto our coverage model (**FL2VA** = first/last-frame; **Ref2VA** = omni-reference ≤9 imgs +
3 clips + 3 audio = character-reference video; native stereo audio; 768p local / 2K via hosted API).
BUT the **MiniMax H3 Community License Agreement** (license date 2026-08-02, §I.4–5):

- **Excluded Territories = European Union, United Kingdom, Republic of Korea, United States of
  America.** Applicable Territory = worldwide **excluding** those. The dev host/user is US-based, so
  the free community grant **does not apply** — a separate formal authorization is required
  (`platform.minimax.io/h3-license`; MiniMax "may" grant based on deployment scenario + controls).
- §IV: additional written authorization once commercial revenue > $20M/yr; must display "MiniMax H3"
  on any commercial UI.
- §V: must bind downstream users to ≥ these restrictions; may not use Outputs to improve other AI
  models; must implement/maintain safeguards against violating uses.
- **Acceptable Use Policy (Exhibit A) does NOT explicitly ban adult content** (bans minors, defamation,
  malware, elections, etc.) — but the **hosted API/Context-IR applies automated moderation that blocks
  pornographic content**, and only the hosted path unlocks Context-IR + 2K Regenerate.
- Governing law HK; patent-style termination clause; US/EU/UK/KR pending regulatory clarity (license
  Q&A says restriction is "not yet", not "not ever").

**Net:** H3 is the strongest *technical* candidate (Ref2VA = real character-reference identity) but is
**blocked for this project by territory** until a MiniMax license is obtained. Recorded as a watch
gate (B-114), not a build target.

### 1.4 Wan 3.0 — repo probe (verified 2026-09-08)

`wanvideo/wan-3-0-video` contains **only `README.md` (4.39 kB) + `.gitattributes`** — **no weights**,
no inference provider, 1 like, repo created ~4 days ago. Capabilities described: realistic human
rendering + consistency across characters/props/space/style, native audio, 2–30 s, 30 fps, up to
1080p, first/last-frame control, editing + extension, ≤20 multimodal reference assets. Access today
is only through the `wan30.io` webapp. **If/when open weights + ComfyUI land, its pitch (reference
consistency + one-pass audio) is a perfect fit — watch gate B-115.**

---

## 2. Decisions

| # | Decision | Status |
|---|---|---|
| D1 | **Near-term build on Wan 2.2 uncensored (Apache-2.0).** Commercial APIs are out (content filters). MiniMax H3 is territory-blocked. Wan 3.0 has no weights. | Made 2026-09-08 |
| D2 | **Route = I2V/FLF2V over rendered keyframes**, not T2V. Renders keyframes with the existing (identity-consistent) image pipeline, then animates between them. Maps onto B-100 coverage kinds (`MomentHold/MomentAction/MomentTransition/BeatExcerpt/WholeBeat`) and the `VideoFirstFrame/VideoLastFrame/VideoInternalKeyframe` typed-reference roles that already exist in `SceneBeatProductionPlan.cs`. | Made 2026-09-08 |
| D3 | **Proof-first on the local ComfyUI host before any app code** (same discipline as B-112/FLUX). Qualify `rzgar/wan2.2-i2v-A14B-Uncensored-base` + `Wan2.2_LightX2V_4Step_Uncensored` with a small SFW→implied cell matrix on the 5080 and a RunPod 14B run, review every clip, then integrate. | Made 2026-09-08 |
| D4 | **Two execution tiers:** local 5080 = 5B class (TI2V-5B official) **plus** the uncensored 14B I2V (rzgar base is fp8-scaled high/low, ~13.55 GB each — fits 16 GB with ComfyUI offload) for iteration; **RunPod serverless = same 14B uncensored** for quality/throughput. Matches the existing image two-tier (local ComfyUI + RunPod Serverless). | Made 2026-09-08; refined after artifact verification |
| D5 | **Model Manager registration pattern** = same additive provider row style as the `local-comfyui-configure` command (already applied 2026-09-08: provider `Local ComfyUI (WOOD-GAME-MAIN 5080)` @ `http://192.168.0.16:8188`, ImageProtocol=ComfyUi, models Juggernaut/BigLust/Pony + disabled FLUX). Video models get a **new** video-capable provider/family slice (or a distinct video provider row), NOT shoehorned into the image `SceneImageModelFamily` enum (currently only Pony/Sdxl/Api). | Made 2026-09-08 |
| D6 | Wan 2.2 **audio** only when required (`Wan2.2-S2V-14B` for speech→video exists); otherwise mix audio at the editor stage per the existing `VideoWithAudio` native-vs-external ownership contract. | Made 2026-09-08 |
| D7 | **Local uncensored-14B artifact set (verified + queued 2026-09-08).** `rzgar/wan2.2-i2v-A14B-Uncensored-base` ships ComfyUI-ready fp8-scaled experts `Wan2.2_I2V_High_R1.safetensors` + `Wan2.2_I2V_Low_R1.safetensors` (13.55 GB each → `diffusion_models/`), with CubeyAI-GeneralN motion LoRAs in-repo; `rzgar/Wan2.2_LightX2V_4Step_Uncensored` ships the uncensored Lightx2v 4-step pair `Wan2.2_LightX2V_high/low_n54vv.safetensors` (1.16 GB each → `loras/`). All Apache-2.0; same `wan2.2_vae` + `umt5_xxl_fp8` as the 5B tier. | Made 2026-09-08 |

**Stage-1 progress 2026-09-08:** SSH to WOOD-GAME-MAIN established (`wood-game-main\kenac`, key at
`~/.ssh/dgcomfy_ed25519` on the dev box); host verified RTX 5080 / driver 591.86 / ~1.25 TB free / venv
3.12; ComfyUI 0.34.0 already exposes native Wan 2.2 nodes (no core bump needed). Official 5B set and the
uncensored-14B set are downloading **in parallel** onto the host (~47.5 GB total, resumable curl). Next:
verify loader registration via `/object_info`, then build + run the POC graph.
**POC results (official TI2V-5B, 2026-09-08) — see `poc-log.md` in this folder:** T2V 2 s PASS (70 s);
I2V 2 s PARTIAL (identity anchors, but model adds a pregnancy belly absent from the source);
I2V 5 s/720p FAIL on body fidelity (250 s; belly recurs mid-clip despite `pregnancy` negatives — **not
prompt-addressable on the official 5B**, so prompting it further is explicitly out of scope). Next:
uncensored-14B I2V A/B on the same frame + Lightx2v 4-step + CubeyAI LoRAs, and FLF2V first/last-frame.

**Open questions to resolve at Stage-2 design:** whether the uncensored 5B class exists yet (the
verified uncensored artifacts are 14B I2V/T2V) vs. running a GGUF-quantized 14B on the 5080; exact
ComfyUI version/node upgrade needed on WOOD-GAME-MAIN for Wan 2.2 native nodes (currently ComfyUI
0.34.0 + torch cu130 per B-112).

---

## 3. Local ComfyUI video setup (the concrete first slice)

Target host = **WOOD-GAME-MAIN** (RTX 5080 16 GB, `D:\ComfyUI`, ComfyUI 0.34.0, torch 2.14.0+cu130,
reachable at `http://192.168.0.16:8188` from the app box — same host/flags as the existing
`Local ComfyUI` provider). This is a **local ComfyUI host, NOT a RunPod pod** — do NOT add it to
`pod-registry.json` / deployment manifests / runpod skills. Persistence = the host's own disk
(Windows); the RunPod `/pre_start.sh` overlay rules do not apply. See
`docs/flux-local-5080-comfyui-setup.md` and `docs/local-comfyui-model-manager-setup.md`.

### 3.1 Steps

1. **ComfyUI core** — DONE 2026-09-08: ComfyUI 0.34.0 on the host already exposes native Wan 2.2
   nodes (`Wan22ImageToVideoLatent`, `WanFirstLastFrameToVideo`, `Wan22FunControlToVideo`) plus the
   Kijai WanVideoWrapper custom set (1220 node types total) — no core bump needed. Verified via
   `/object_info` over the LAN before any work.
2. **Custom nodes** (verify against ComfyUI-Manager, record any additions in this doc + the local-host
   setup doc):
   - `City96/ComfyUI-GGUF` — only if we run GGUF-quantized Wan (QuantStack/bullerwins collections).
   - `Kijai/ComfyUI-WanVideoWrapper` — optional leading-edge path (Lightx2v / hot features that land
     here before core ComfyUI); keep as an alternative, not the primary, per our single-path rule.
3. **Model files** (exact downloads pinned at qualification time — D3 proof — into
   `D:\ComfyUI\models\`):
   - 5B tier (5080) — queued 2026-09-08: `wan2.2_ti2v_5B_fp16.safetensors` (9.31 GB → diffusion_models)
     + `wan2.2_vae.safetensors` (1.31 GB → vae) + `umt5_xxl_fp8_e4m3fn_scaled.safetensors` (6.27 GB →
     text_encoders) from `Comfy-Org/Wan_2.2_ComfyUI_Repackaged` / `Comfy-Org/Wan_2.1_ComfyUI_repackaged`.
   - Uncensored 14B tier (5080 + RunPod) — queued 2026-09-08: rzgar base experts `Wan2.2_I2V_High_R1.safetensors`
     + `Wan2.2_I2V_Low_R1.safetensors` (13.55 GB each → diffusion_models), CubeyAI-GeneralN-High/Low
     (0.57 GB each → loras, apply 0.5–0.55), rzgar uncensored Lightx2v 4-step pair
     `Wan2.2_LightX2V_high/low_n54vv.safetensors` (1.16 GB each → loras, keep near full strength).
4. **Proof matrix** (before any app code): SFW base + implied cells across (a) I2V from a single
   rendered frame, (b) first+last-frame FLF2V, (c) T2V sanity. PASS rubric = motion natural, no
   safety refusal/distortion, frame identity held. Renders → git-ignored `artifacts/tmp/...`; every
   clip visually reviewed (per-image/clip pass/fail recorded honestly).
5. **Smoke + restart**: on the local Windows host, ensure ComfyUI is launched the way the operator
   expects (documented launch command, not a one-off SSH start — but no `/pre_start.sh` requirement
   here). Re-run the smoke after a ComfyUI restart before declaring ready.
6. **App-facing Model Manager registration** (Stage 2, after a `SceneVideoModelFamily`/
   video-capability code slice exists — do NOT enable a video model against the image-only enum
   today, mirroring the disabled-FLUX guardrail). Additive; never repoint function defaults; local
   replaces/joins RunPod per what Model Manager assigns.

### 3.2 Hardware/VRAM truth (set expectations)

- **TI2V-5B** (fp16 ≈ 5B + VAE + fp8 UMT5) fits the 5080 16 GB with ComfyUI native offload; ~5 s 720p
  in single-digit minutes.
- **14B uncensored I2V** needs ~28 GB+ resident; on 16 GB it is GGUF-quantized-slow or offloaded-slow.
  **Run the 14B on RunPod serverless (A100/H100-class)** for the quality tier — same pattern as the
  serverless image endpoints.
- Uncensored 14B is the verified artifact today; the uncensored **5B** class was not found in the
  2026-09-08 search — confirm availability (or accept GGUF-14B-on-5080) at Stage-1 qualification.

---

## 4. Roadmap / phases

| Phase | Content | Exit gate |
|---|---|---|
| 0 | Research persisted (this doc) | Done 2026-09-08 |
| 1 | **Local ComfyUI Wan 2.2 proof** (§3.1) — 5B official + uncensored 14B/LoRA on RunPod + optional GGUF-14B on 5080; SFW→implied cell matrix, every clip reviewed | Proof matrix PASS recorded |
| 2 | App code slice — video capability/family + provider model rows + I2V/FLF2V job over rendered keyframes, fed from B-100/B-111 canonical keyframe/coverage output (or the minimal direct path if canonical inputs are not yet runtime) | Model Manager shows video models; a Studio/workspace video job renders and stores a reviewable clip |
| 3 | RunPod serverless Wan 2.2 14B uncensored provider tier | Serverless provider + round-trip render |

Dependencies/related: **B-100** (canonical video coverage plans/typed refs — read contract only),
**B-111** (visual production program), **B-032** (image/keyframe + identity), **B-112** (same 5080
host + proof-first discipline + local model-manager registration precedent). Additive — not part of
the B-111 superseded map; video execution was explicitly left to "separate epics" by the multimodal
roadmap.

## 5. Backlog cross-refs

- **B-113** (this plan) — scene video production main epic.
- **B-114** — MiniMax H3 evaluation (US license-excluded; formal application gate; revisit if a
  license is granted or the territory scope changes).
- **B-115** — Wan 3.0 open-weight watch (repo has no weights as of 2026-09-08; re-check when the
  model card gains file entries / ComfyUI templates).
