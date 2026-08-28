# Juggernaut XL / SDXL NSFW — Community & Creator Findings

**Status:** evidence (durable, git-tracked research)
**Date:** 2026-08-25
**Owner:** Scene Image Generator (B-032)
**Epic:** B-032 · **Backlog ref:** B-097 (re-opened 2026-08-25)
**Purpose:** Guide scene-image prompting and justify re-opening B-097 (ControlNet OpenPose + Depth as
a requirement for earlier phases). Consumed by Phase 1B / Phase 2 / Phase 3 planning on any
developer host — this file is the canonical record; see the re-open banner in
[`continuity-rendering-architecture.md`](continuity-rendering-architecture.md).

## Sources

- RunDiffusion — Prompt Guide for Juggernaut XIII: Ragnarok (Adam / Team Juggernaut) and the
  Juggernaut XI/XII guide.
- Civitai model page `civitai.com/models/133005` — creator notes (KandooAI) + discussion.
- PicassoIA — "Juggernaut XL for NSFW: Best Settings and Prompts".
- DeepSpicy — "How to Improve NSFW AI Image Quality: Reproducible Recipes for Anatomy, Pose, and
  Face Detail".
- Lewdly — "OpenPose ControlNet for NSFW Pose Control".
- offlinecreator — "ControlNet for Realistic Anatomy (local NSFW workflows, 2026)".

## 1. Root cause — positions fail because text is low-bandwidth for pose

- A bare position term ("doggy style") has thousands of valid interpretations; the model picks one
  roughly randomly per seed → anatomy collapse (fused/merged bodies, extra limbs, wrong leg count,
  malformed hands/feet). Doggy (rear view, two overlapping bodies) is among the worst offenders.
- Multi-subject scenes multiply the ambiguity by the number of people in frame.
- Juggernaut is **booru-token trained** — bare tags ARE recognized but only as compressed labels,
  not as geometry.
- Creator (KandooAI) limit: Ragnarok improved poses/hands/feet over earlier versions but "it's still
  an SDXL model" (weak faces at a distance). **Juggernaut Ragnarok was trained using Lustify as an
  input** — its lane is *softer* NSFW / magazine-quality skin, not a hard-explicit positional
  guarantee.
- Conclusion: prompt changes reduce ambiguity but cannot be the geometry-control architecture.

## 2. Settings consensus

| Setting | Creator / RunDiffusion | NSFW community (PicassoIA / DeepSpicy) |
|---|---|---|
| Sampler | DPM++ 2M SDE | DPM++ 2M Karras (or SDE) |
| Steps | 30–40 | 25–50 |
| CFG | 3–6 (lower = realism) | **5–8; positions ~6–7.5** (adherence; >8.5 waxy skin) |
| Resolution | 832×1216 portrait | **896×1152** full-body (768×1280 breaks anatomy) |
| Negative | start empty | full anatomy-guard set (below) |
| Hires fix | 4xNMKD-Siax, 0.3 denoise, 1.5× | R-ESRGAN 4x+, 0.35–0.45, 1.5–2× |
| CLIP skip | n/a | skip 2 helps body proportions |
| VAE | baked in | keep baked |

Repo note: `ComfyUIImageClient.BuildSdxlWorkflow` = `dpmpp_2m_sde` / 30 steps / CFG 5.0. Valid, but
CFG 5 is on the LOW end for position adherence — the community lands at 6–7.5 for position-critical
NSFW.

## 3. Prompt recipe (position / doggy example)

Positive — subject → action → arrangement → setting → lighting → camera, with the **booru anchor
token first** and **concrete anatomy wording**:

```
doggy style, photorealistic photograph of a man and a woman having sex,
the woman on her hands and knees on the bed, the man kneeling behind her,
rear view, she looks back over her shoulder, his erect penis entering her
vagina from behind, clear penetration, correct penis and vagina anatomy,
natural skin texture, warm bedroom lighting, 35mm, sharp focus
```

Negative — the anatomy guard set (this is what stops the breakage):

```
deformed, bad anatomy, extra limbs, extra legs, four legs, fused legs,
fused bodies, merged bodies, extra fingers, extra arms, missing limbs,
malformed hands, malformed feet, misplaced genitals, penis on arm,
penis on hand, detached penis, extra penis, blurry genitals,
featureless genitals, censored, cartoon, anime, illustration, painting,
sketch, watermark, text, low quality, oversaturated, plastic skin
```

This is consistent with the repo's `.github/instructions/sdxl-juggernaut-prompting.instructions.md`;
the previously-missing pieces were the genital + fused-bodies guards and the booru anchor token.

## 4. Structural fixes (community answer — no prompt tweak fixes geometry)

1. **ControlNet OpenPose / DWPose — THE pose fix.** SDXL model: `thibaud/controlnet-openpose-sdxl-1.0`
   (~2.5 GB, fp16 ok). Preprocessor: **DWPose** (`comfyui_controlnet_aux`). Weight **0.8** single /
   **0.85** two-person.
2. **Stack Depth ControlNet** (Depth Anything V2/V3) at 0.5–0.6 for bed/occlusion/spatial layout.
3. **Two nets at 0.35–0.55 each beat one net at 1.0** — avoids waxy/plastic skin.
4. **ADetailer** (Impact Pack `face_yolov8n.pt` / `hand_yolov8s.pt`) — hands/faces at distance.
5. **Surgical inpaint**: hands denoise 0.8–0.9; face micro-pass 0.6–0.75; upscale denoise 0.2–0.4.
6. **VRAM**: ~+1–1.5 GB per SDXL ControlNet; the A40 pod handles OpenPose+Depth fine.

## 5. Candidate NSFW model landscape (what's available to choose from)

Snapshot of the mainstream NSFW-capable image models as of 2026-08-25, so a checkpoint choice is a
deliberate, documented decision rather than an accident. "Lane" = the style/realism region a model
is strong in; the repo routes checkpoints via `SceneImageModelFamilyResolver.Classify` (Pony vs
Sdxl vs Unknown, fail-fast on unknown).

| Model | Family | Lane / style | Prompt style | NSFW capability | Notes |
|---|---|---|---|---|---|
| **Pony Diffusion V6 XL** | SDXL (booru) | Anime / illustration-heavy | Booru tags + `score_9…score_4_up` rating, CLIP skip 2 | Strong (explicit) | 1.07M dl / 76K likes. Repo's original path; being retired from the pod in Phase 1B. Tag-driven, garbles long prose. |
| **Pony Realism** | SDXL (Pony-derived) | Photoreal Pony | Booru tags + rating tokens, realistic | Strong (explicit photoreal) | Photoreal fine-tune of the Pony lineage; keeps booru anatomy tags. Follows ControlNet pose at slightly lower weight than RealVisXL. |
| **CyberRealistic Pony** | SDXL (Pony-derived) | Photoreal Pony | Booru tags + rating tokens | Strong (explicit photoreal) | 764K dl / 32K likes. Photoreal Pony NSFW alternative. |
| **Juggernaut XL Ragnarok** | SDXL | Photoreal (magazine skin), soft NSFW | Natural-language photo brief + booru tokens; CFG 3–6 | Good (softer NSFW) | 1.62M dl / 36K likes. **Current repo checkpoint.** Trained using Lustify as an input; weak hard-explicit positional guarantee (this doc's whole subject). |
| **Juggernaut-Z V2** | SDXL | Photoreal, NSFW restrictions lifted | Same family prompting | Good–strong | Newest; creator says corporate NSFW restrictions lifted → stronger explicit lane than Ragnarok. |
| **Lustify (ZENITH / APEX)** | SDXL | Dedicated photoreal EXPLICIT | Natural-language + concrete anatomy; minimal negative | Strong (hard explicit) | 373K dl / 14K likes. **Parked 2026-08-24** (B-032 "OUT OF PLAN") but shares DNA with Juggernaut and is the community pick for guaranteed explicit + positions. |
| **RealVisXL v3** | SDXL | Photoreal | Natural-language | Good | Better full-body consistency reported vs Juggernaut; single-subject strong. |
| **Nova Anime XL / Illustrious** | SDXL (Illustrious) | Anime NSFW leader | Booru tags | Strong (anime explicit) | 467K dl. Only relevant if an anime/illustration lane is ever needed. |
| **Realistic Vision v5.1 / epiCRealism / majicMIX / BRA / epiCPhotoGasm** | SD 1.5 | Photoreal workhorses | Natural-language + tags | Good (explicit via fine-tunes) | 403K–885K dl. Older 512/768 architecture, SDXL-era quality gap; legacy options. |
| **Flux / Pixelwave** | Flux (next-gen) | Photoreal / style | Natural-language | SFW-leaning (explicit needs fine-tunes) | Base is SFW-trained. **Creator pipeline:** FluxDev / Pixelwave → Juggernaut Ragnarok refiner (Flux composition, Juggernaut detail). |
| **Base SDXL 1.0** | SDXL | Generic photoreal | Natural-language | **Cannot render explicit genitals** (verified in B-099) | Baseline only; not an NSFW choice. |

### Model-level reality for B-032

- **Juggernaut = soft NSFW / magazine skin.** For hard explicit + guaranteed positions the community
  points to **Lustify** (parked; shares DNA with Juggernaut) or **RealVisXL v3** (better full-body
  consistency reported).
- **Creator pipeline:** FluxDev / Pixelwave → **Juggernaut Ragnarok as refiner** (Flux comp, Juggernaut
  detail).
- **Juggernaut-Z V2** (newest, NSFW restrictions lifted per creator) is a stronger NSFW base if the
  checkpoint ever changes.
- Repo state: pod volume holds `juggernautXL_ragnarok.safetensors`; `/ComfyUI/extra_model_paths.yaml`
  needs recreating to load it (2026-08-25); Pony retired from the active pod per Phase 1B.

## 6. Plan consequence (2026-08-25)

- **RE-OPEN B-097.** ControlNet OpenPose + Depth conditioning is a **requirement for earlier phases**
  (Phase 1B one-pod runtime + Phase 2 identity), NOT deferred to Phase 3.
- **Boundary preserved:** the old **"OpenPose + Juggernaut inpainting exact-contact route" stays
  REJECTED** (prompt/keypoint/strength/mask tuning — do not resume). ControlNet text-to-image
  conditioning is a separate, community-validated mechanism.
- Plan edits: `specs/Planning/backlog.md` (B-097 → `planned`), `specs/Planning/B-032-scene-image-generator/README.md`
  (§1A, §1B, §3), `continuity-rendering-architecture.md` (re-open banner), Phase 1B/2/3 READMEs.
