# B-129 — Qwen-Image-2.1 as the default scene composer + editor (strategy, studio-surface mapping, rollout)

**State:** `designed` (strategy + design doc written; each implementation slice still requires operator go-ahead)
**Created:** 2026-09-24
**Type:** strategy / roadmap doc — NOT a single implementation task. Slices below become their own design → plan → implement cycles.
**Predecessor (engine):** `specs/Planning/B-128-qwen-image-2-1-app-integration.md` — the 2.1 generation/edit/plumbing is shipped there. This doc owns the *product positioning* and the *feature rollout* on top of it.

> **Numbering note:** the engine spec above is titled "B-128" but the backlog row `B-128` is **DWPose Studio**. That pre-existing collision is left as-is (no destructive renumber). This doc is a distinct backlog row, **B-129**, and references the engine spec by filename.

**Related:** B-032 (scene image generator), B-100 (progressive beat pipeline), B-106 (production studio staged workflow), B-110 (unified image edit workbench), B-111 (consistent visual production), B-116 (local img2img re-skin), B-117 (OpenPose ControlNet render), B-118/B-128 (Pose/DWPose Studio), B-119 (multi-character layout workflow), B-120 (asset layout extraction), B-121 (identity studio), B-122 (body-complete pack), B-123 (LoRA studio), B-124 (reference model + Asset Manager shell), B-125 (editor LoRA), B-126 (multi-character composition), B-127 (identity ownership).

---

## 1. Outcome

Make **Qwen-Image-2.1** the default model for the four studio verbs — **generate, compose (multi-reference), edit, and produce transparent assets** — and demote every other image model to a **capability-gated, explicit one-off route** with a single, documented reason for existing. 2.1 is the only model in the inventory that unifies text-to-image, multi-reference composition, source-image editing, and native transparency in one model, one call, on the local 5080 host.

Binding constraints inherited from the repo hard rules and the compiler standards:

- **No fallback / no silent substitution.** Model choice is resolved through `ReferenceStrategyResolver` / `SceneImagePromptCompilerRegistry` / `ImageEditorModelResolver`; an unqualified route fails fast with a named reason.
- **Default is persisted config, never hardcoded.** 2.1 becomes the default only where the operator pins it.
- **Settings stay in the documented 2.1 envelope:** `cfg 1`, `euler/simple`, 25–50 steps, `resolution` is a pixel *budget* not a dimension, negative prompt is inert. *(B-128 §1, defects 2026-09-24.)*
- **Every render keeps its audit event** (`SceneImageRequestSubmitted` with family + steps/cfg/sampler/scheduler).

---

## 2. Qwen-Image-2.1 feature catalog (categorized)

Source: Qwen blog (2026-09-20) + this repo's measured host runs (`helpers/local-comfyui-host/`, `qwen-21-native-reference/`).

### A. Architecture / efficiency (infrastructure, not a user feature)

| Feature | Notes |
|---|---|
| 7B DiT, 32 single-stream layers | Fits 5080 16 GB via `int8_convrot` |
| Qwen3-VL 8B text encoder | Strong instruction following incl. edit instructions |
| Mixed-granularity attention + KV-cache reuse | Multi-input edits get cheaper per added reference (edit1 106 s → edit2 80.5 s) |
| 64-ch RGBA VAE | Genuine alpha channel (rgba 15.1 s measured) |

### B. Native transparency (RGBA)

- Generate transparent assets from text.
- Edit transparent layers in place (expression/clothing change keeps alpha).
- Extract a subject from an RGB photo as an RGBA layer (cut-out).

### C. Versatile editing

**C1. Multiple references (≤10 official; qualification stores `MaxReferences` 16 — validate >10 before relying).**
- Group portrait from 6 singles → multi-character scene in one call.
- Virtual try-on from 5 inputs → wardrobe try-on.
- Interior from 10 furnishing refs → location assembly.

**C2. Local editing — flexible region selection.**
- Colored circles (one circle per region; instruction names the circle).
- Painted annotations (brush the region).
- Separate mask + original image as two inputs (preserves the full source under the mask — the production form).

**C3. Fidelity (people + products).**
- Portrait identity preserved across edits; product text/texture/shape preserved.

**C4. Task coverage.**
- Panorama from a photo (explorable in a viewer).
- Infographic from a model photo.
- Storyboard from a three-view character reference.

### D. Realistic textures & refined aesthetics

- Improved in-image typography; improved portrait lighting/fine detail.

---

## 3. Measured grounding (load-bearing; every suggestion assumes these)

1. **Pose = skeleton-as-reference.** 2.1 reads an OpenPose skeleton in a reference slot as pose guidance — no ControlNet needed. A *photoreal* person in slot 1 is treated as the base image to copy, not a pose donor. The 472-pose pack + `pose-library/*.png` are directly usable. *(qwen-image-2-1-pose-skeleton-finding.md)*
2. **Reference head angle dominates requested pose in wide frames.** Identity lands with the **angle-matched** pack view; a profile view only carries hair. *(B-128 §4, §5.2 rule 3)*
3. **Wide-frame identity *editing* fails by scale, not plumbing.** Fix = crop face region → edit → composite back, or raise `resolution` budget to 2048. Generation-with-references does not have this problem. *(qwen-image-2-1-local-host.md "IDENTITY")*
4. **Location + faces in one call is semantically faithful, not pixel-faithful.** Pixel-faithful continuity stays on the composite-from-location-base path (B-116 re-skin family). *(loc3/loc3r)*
5. **Slot order = placement** (`image_1` anchors left); order is user-visible request data.
6. **Accessory leakage** (e.g. necklace) — fix is reference *selection* (clean view), never a negative.
7. **Envelope** cf. §1.
8. **Autogrow reference payload** is flat dotted keys `images.image_1…N`; wrong forms fail silently — already pinned by tests.

---

## 4. Studio-surface mapping

| Surface | Route | 2.1 features |
|---|---|---|
| Location Studio | `/locations/{id}` | Transparency/cut-out, panorama (C4), multi-ref interior assembly (C1), location+identity one-call render (proven) |
| Character Studio | `/characters/{id}` | Multi-ref identity + body, RGBA subject extraction, fidelity edits (C3), character sheets/storyboards (C4) |
| Production Studio (scene/beat/moment) | `/roleplay/studio/…` | Generation+refs, local-edit Finish (C2), per-POV re-render, moment storyboards (C4) |
| Asset Manager index | `/asset-studio` | No direct feature — owner index; features flow through the studios |
| Shared create/edit workspace | `/asset-studio/{assetId}` | Circle/paint/mask editing UI (§5.4), the single edit path (B-124) |

---

## 5. Feature incorporation plan

### 5.1 Location Studio — panoramas, cut-outs, assembly

- **Panorama action** on a location view: approved location image as `image_1` → 2.1 extends to panorama. Persist as a new view (views are data, `ViewDescriptorJson`/label — no schema change). Pannable viewer is a small follow-up.
- **Cut-out / RGBA extraction** on any photo → reusable transparent prop/overlay for composition; automates part of B-120's extraction for props/wardrobe.
- **Location + characters in one call** as a `Location` reference kind + identity faces in the ordered reference list (B-128 U2/U3). Collapses the dual-base-location pipeline for *new shots in a referenced environment*; pixel-faithful shots keep the composite-from-base path. User chooses the route explicitly.

### 5.2 Character Studio — identity, body, sheets

- **Character sheets** from the approved 5-view pack + full-body refs (B-121/B-122 output) → three-view → storyboard (the blog's exact example).
- **Body + identity in one reference set** (C1, C3); B-122 body refs become 2.1 reference slots. Fidelity preserves identity while clothing/pose change.
- **Angle-matched view surfacing** — the picker must show which pack view is bound (B-128 §5.2 rule 3).
- **De-clothe / crop / enhance via 2.1 editing** — point the editor at the 2.1 editor row for fidelity + transparency; keep Qwen-Edit-2511 + LoRA for male-anatomy (B-125).

### 5.3 Production Studio — scene, beat, moments (centerpiece)

- **One-call composition:** canonical brief → ordered references: identity faces first, then location, then **skeleton pose** last. One render = "same location, N characters, each posed." This is **B-126's primary route**.
- **Same location, different poses:** keep location + face refs constant, swap only the skeleton reference. One click per pose variant (B-128 U4 promoted).
- **Different POV by character, same scene/beat:** re-render per-POV with (a) the POV character's face **excluded** from references and (b) the brief compiled from the POV character's viewpoint (compiler standards §2.3.1: never include the POV character in frame). Extends the existing per-POV production groups.
- **Finish / local edits:** draw a circle on the watch, fix the hair, change the shirt — three regions in one instruction (§5.4).
- **Moment storyboards** from a beat's keyframe → feeds B-100 and the B-113 video keyframe seam.

### 5.4 Editing UI — circle / outline / mask targeting (highest-value new surface)

Owned by the shared workspace (`/asset-studio/{assetId}`) so B-124's one-edit-path rule holds. Three tools over the source image:

1. **Circle tool** — each circle gets a color; instruction auto-lists "the {color} circle".
2. **Brush / paint tool** — scribble the region.
3. **Mask mode** — opaque region saved as a **separate mask image**, sent as the second input alongside the untouched original. Preferred for production (circles/paint obscure content).

- **Instruction compilation** names each region's change + preserved properties (mirrors the `qwen-image-edit-2511.instructions.md` contract).
- **Multi-region = one call**, not N calls.

### 5.5 Asset Manager

- **RGBA assets** become a first-class usage on existing asset types (a transparency flag, not a new schema — flag in B-124).
- **Panoramas** stored as location views; **character sheets** stored against the character owner (B-127 prerequisite so sheets follow the template, not the scenario instance).

---

## 6. Model positioning — 2.1 default; others one-off

| Model | Role after this change | Why it stays |
|---|---|---|
| **Qwen-Image-2.1** | **Default generation + editing + composition + transparency** | Only unified gen+edit+refs+RGBA model; local on 5080 |
| Qwen-Image-Edit-2511 + editor LoRA | Male-anatomy-specific edits (B-125) | Proven superior for that one case; 2.1 has no equivalent LoRA ecosystem yet |
| SDXL / BigLust / Juggernaut | Pixel-faithful base render + re-skin (B-116) | 2.1 one-call location is semantic, not pixel-faithful |
| Pony V6 / Pony Realism | Existing qualified Pony-tag renders (explicit user choice) | No regression |
| FLUX / FLUX.2 | Cloud text-driven implied/softcore composition | 12B doesn't fit the 5080 |
| ControlNet stack (OpenPoseXL2, depth/canny) | Lying / all-fours / forced multi-body layouts + SDXL/FLUX pose | 2.1 native skeleton is upright; structural routes remain for those |

Every entry is a capability-gated route selected by the resolver — never a silent fallback.

---

## 7. Roadmap alignment

| 2.1 capability | Advances | Effect |
|---|---|---|
| Skeleton-as-reference pose | B-128 / B-118 / B-117 | Pose control without ControlNet for upright poses |
| Multi-ref (N faces + location + pose) | B-126, B-119 | Collapses the multi-char ladder for the common case |
| Native transparency + extraction | B-124, B-120 | Automates derived-asset / cut-out extraction |
| Local editing (circles/masks) | B-110, B-124, B-121/122/123 | Single edit primitive with region targeting |
| Fidelity (people/products) | B-121, B-122 | Identity/body preservation across edits |
| Storyboard / panorama | B-100, B-032 phase 3, B-113 | Beat → keyframe → storyboard/video material |
| Generation-with-references (shipped) | B-128 | UI rework U1–U5 is the remaining work |

**Key simplification:** B-128 §4 concluded pose is *solved* on 2.1. Re-scope B-117/B-119 to the awkward-layout + location-structure niches only; B-126 consumes 2.1 as primary route with the structural ladder as the explicit lying/all-fours fallback.

---

## 8. Recommended phasing

1. **Finish B-128 U1–U5** — ordered reference list, data-driven strategies, Compose `NativeReference`, capability surfacing. Unlocks everything below.
2. **Promote U4 (pose-as-reference)** deferred → planned (skeleton-as-reference is proven).
3. **Circle/mask editing UI** in the shared workspace (B-124/B-110).
4. **Location + identity one-call composition** (B-126 primary).
5. **Transparency catalog + panorama action** (B-124/B-120).
6. **Per-POV re-render** with POV-face exclusion.
7. **Re-scope B-117/B-119** to the awkward-layout niche; record 2.1 as default composer/editor in the compiler-standards family quick-reference.

---

## 9. Risks / open questions (close before each slice)

- Lying / all-fours on 2.1 native skeleton — unmeasured; assume ControlNet still needed.
- Accessory leakage + head-angle dominance — mitigated by clean-view picker + surfacing (B-128 §5.2 rule 3).
- `MaxReferences` 16 vs official 10 — validate >10 refs on the host before relying.
- Adult-content *editing* behavior on 2.1 is unmeasured (2511 was measured) — no scored claim until a controlled proof exists.
- NSFW/abliteration ecosystem immature (1 HF LoRA, no anatomy merge, 2026-09-22) — keep B-125 for anatomy.
- Enhance stays model-agnostic (upscaler); no 2.1 refiner pass unless requested.

---

## 10. Non-negotiables for every slice

- No silent strategy downgrade; unqualified route fails with an explicit message.
- No reference is ever dropped to make a render succeed.
- Ordering is user data; the engine normalises but reports.
- Default model is persisted config; capability gates answer through one resolver.
- `.razor` edits follow `.github/instructions/razor-editing.instructions.md`.
