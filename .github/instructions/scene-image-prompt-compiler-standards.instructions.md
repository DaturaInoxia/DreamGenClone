---
description: "CANONICAL prompt-compiler standards for the DreamGenClone scene image generator. Formal external research on SDXL/Juggernaut/BigLust prompting techniques and model settings, plus HARD RULES governing every future change to prompt compilers/builders so fixes follow documented model best practices instead of ad-hoc 'just get an image' tweaks. Read before writing, changing, or fixing ANY prompt compiler, prompt builder, or image-model settings. The difference between a working image pipeline and an unusable one."
applyTo: DreamGenClone.Web/Application/RolePlay/SdxlSceneImagePromptBuilder.cs,DreamGenClone.Web/Application/RolePlay/ISdxlSceneImagePromptBuilder.cs,DreamGenClone.Web/Application/RolePlay/PonySceneImagePromptBuilder.cs,DreamGenClone.Web/Application/RolePlay/IPonySceneImagePromptBuilder.cs,DreamGenClone.Web/Application/RolePlay/ISceneImageLLMPromptBuilder.cs,DreamGenClone.Web/Application/RolePlay/SceneImagePromptCompilers.cs,DreamGenClone.Web/Application/RolePlay/ISceneImagePromptCompiler.cs,DreamGenClone.Web/Application/RolePlay/QwenSceneImageEditPromptCompiler.cs,DreamGenClone.Web/Application/RolePlay/ISceneImageEditPromptCompiler.cs,DreamGenClone.Web/Application/RolePlay/DeterministicMultimodalMediaCompiler.cs,DreamGenClone.Web/Application/RolePlay/MultimodalMediaCompilerRegistry.cs,DreamGenClone.Application/RolePlay/IMultimodalMediaCompiler.cs,DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs,DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs,DreamGenClone.Web/Application/RolePlay/SceneImageRenderBriefBuilder.cs,DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs,DreamGenClone.Tests/RolePlay/**/*.cs,helpers/runpod/workflows/**,specs/Planning/B-100-progressive-scene-beat-pipeline/**,specs/Planning/B-032-scene-image-generator/**,specs/Planning/B-103-production-studio-composition-transparency/**
---

# Scene Image Prompt Compiler Standards (CANONICAL)

> **Scope:** every prompt compiler, prompt builder, and image-model settings surface in the scene
> image generator. This is the **governance + research** document. Practical per-model rules that
> were validated live on the pod live in the sibling files:
> `sdxl-juggernaut-prompting.instructions.md`, `pony-v6-prompting.instructions.md`,
> `qwen-image-edit-2511.instructions.md`. When they conflict, this document is the source of truth
> for **why**; the sibling files are the source of truth for **exact current strings/settings**.
>
> **Why this exists:** the image pipeline is either excellent or unusable — there is no middle
> ground. Every past "prompt fix" that was a shot-in-the-dark (`just tweak the prompt until an image
> comes out`) produced a worse, less-maintainable system. From now on, **every compiler change must
> be grounded in the documented, externally-researched behavior of the target model family.**

---

## 0. The non-negotiable governance rules (read FIRST)

These are hard rules. A compiler change that violates them is a bug, not a fix.

1. **No "just get an image" changes.** Never tweak a prompt, negative, or setting solely to force
   one specific render to succeed. Every change must implement a documented best practice for the
   target model family and be explainable in one sentence beginning "The model family docs say…".
2. **Research before you change.** If a compiler change is not already backed by this document or a
   cited external source, do the research first (see §6), record the source, then change.
3. **Settings must stay inside the model's documented envelope** (see §3 tables). Changing CFG /
   steps / sampler / scheduler / resolution / negative outside the documented range for the family
   requires an evidence-backed deviation record (source + A/B proof), not a guess.
4. **New model ⇒ new research.** Before any compiler targets a model that is not already in §3, the
   model's official/Civitai/author-recommended prompting guide and settings MUST be fetched and
   folded into §3 + the family's instruction file. A model with no researched settings cannot be the
   target of a compiler.
5. **Validation is structural, not eyeballed.** Compiler output must be validated by structure/rule
   checks (length caps, forbidden tokens, required components, POV rules) in code and tests — never
   by "I ran it and it looked fine". Every render path must keep an audit event
   (`SceneImageRequestSubmitted`) capturing the exact positive/negative/seed/checkpoint.
6. **Deviations must be documented in the code.** If a build deliberately deviates from a model's
   best practice (e.g. we need a heavier negative than "start minimal" because of the NSFW guard),
   say so in a comment and in the family instruction file — with the reason.
7. **Never regress a documented rule.** The validated rules in the sibling instruction files (Pony,
   SDXL/Juggernaut, Qwen) are load-bearing. A fix must not silently drop them.
8. **No identity fluff.** Never write "You are an expert…" or any flattery/identity claim into a
   prompt. A title carries no information the model can act on ("telling a model it is an expert is
   like telling it not to make mistakes"). Instead, give the model what it can actually use:
   **grounding** (facts about the target family: what it reads, what it cannot render, its training
   biases), **explicit rules**, and **a concrete example** of the target output shape.
9. **Examples must be externally sourced.** Few-shot examples in compiler prompts come from the
   model family's official/authoritative guides (primary). Internal references are secondary, and
   only allowed when they demonstrate a rule the external examples do not cover (e.g. POV
   exclusion), and only if they align with the externally-researched anatomy in §2.5.

---

## 1. The active model & type

- **Family:** SDXL 1.0 photorealistic finetunes (natural-language photography briefs).
- **Active production model:** **Big Lust v1.6** (`bigLust_v16.safetensors`, fp16, ~6.7 GB,
  SDXL 1.0 base, CreativeML Open RAIL++-M). A merge of **bigASP** + **LUSTIFY** — explicitly
  designed by its author as the best **non-Pony** SDXL NSFW checkpoint. Published 2024-11-20;
  v1.6 merged bigASP 2. ~110k downloads, "Overwhelmingly Positive" (2.8k reviews).
- **Reference photorealistic finetune:** **Juggernaut XL Ragnarok**
  (`JuggernautXL_Ragnarok_ByRunDiffusion.safetensors`, fp16, 6.62 GB, SDXL 1.0 base, RAIL++-M).
  Final SDXL release of the Juggernaut line; "Overwhelmingly Positive" (8.5k reviews).
- **Sibling families (separate instruction files):** Pony V6 XL (tags; `pony-v6-prompting.instructions.md`)
  and Qwen Image Edit (edit; `qwen-image-edit-2511.instructions.md`).

> **Critical model fact (from the model authors):** SDXL-family models **cannot render faces at a
> distance** and are **weak at text**. The Juggernaut Ragnarok guide states it explicitly: *"it
> still has limitations when it comes to text rendering or faces at a distance."* A distant or
> shadowed subject in a wide shot (exactly the B-103 "Becky dropped" failure) is a **known model
> limitation**, not a prompt typo. The compiler must **tighten framing / keep subjects near and
> clearly described**, never rely on a distant figure carrying the image.

---

## 2. The researched prompting technique (SDXL family)

Authoritative guidance — the **official Juggernaut XIII Ragnarok Prompt Guide** (RunDiffusion,
by Team Juggernaut; URL in §6) defines the canonical prompt anatomy. Use it to design and review
every SDXL-family compiler.

### 2.1 Prompt anatomy (the 17 components)

| Component | Meaning | Notes for the compiler |
|---|---|---|
| **Subject** | primary focus — the main character/object | **Keep the subject at the START of the prompt, not the end.** |
| **Action** | what the subject is doing | Place after the subject. |
| **Environment/Setting** | the surrounding world | First two sentences for scene-critical detail. |
| **Object** | important secondary items | Add objects in the first two sentences. |
| **Color** | dominant colors | If you omit colors the model uses its training-data priors. |
| **Style** | artistic approach (photorealism, cinematic…) | Anchor with `photorealistic` / `photograph` / `35mm`. |
| **Mood/Atmosphere** | emotional tone | Explicit mood tokens reinforce it. |
| **Lighting** | light type affecting mood/texture | `soft natural light`, `dramatic lighting`, etc. |
| **Perspective/Viewpoint** | camera distance/angle | `close-up`, `wide shot`, `low angle`, **distance is load-bearing** (see §1). |
| **Texture/Material** | surface feel | `natural skin texture` is a known realism enhancer. |
| **Time Period** | era (optional) | |
| **Cultural Elements** | cultural grounding (optional) | |
| **Emotion** | expressed feeling (esp. portraits) | |
| **Medium** | photograph vs painting | `Photograph` is as important as style+subject. |
| **Clothing** | attire | **ALWAYS describe clothing** — anchors imagery and prevents accidental NSFW (the model is NSFW-trained). |
| **Text** | literal in-image text | SDXL is weak at text; put any text at the very front and expect retries. |
| (Explicitness) | anatomical content level | For explicit content use concrete anatomy language in the positive; for safe/questionable imply, and see the SFW rules below. |

### 2.2 Prompt structure rules (from the guide)

1. **First sentence sets the foundation.** Lead with subject + framing.
2. **Token budget:** do not exceed ~75 tokens / ~600–800 characters. Long caption prose degrades output.
3. **Tokens or natural sentences both work** — this family is not Pony; prefer natural-language
   photographic briefs for our use case.
4. **Weights sparingly** — apply to primary subjects only; do not exceed ~1.4.
5. **More specific = more control.** The more specific you are, the more control you have.
6. **NSFW-trained model ⇒ clothing is a safety anchor.** Always prompt clothing in the positive so
   accidental NSFW does not occur.

### 2.3 The four canonical compiler rules (B-103; now research-backed)

When a compiler projects a canonical Still brief into an SDXL-family prompt, it MUST enforce:

1. **POV framing.** Render strictly from the `PRODUCTION POV` character's viewpoint — render only
   what that character sees; **never include the POV character in the frame** for a named observer
   POV. (Omniscient POV = full scene, all characters visible.) The old `BuildCanonicalSystemPrompt`
   failure put Dean *in* his own POV frame — that is exactly the anti-pattern the model docs warn
   about (subject/framing confusion).
2. **No names / relations / ownership.** Text-to-image models cannot map "Dean", "Becky", or
   "Ken's shirt". Describe every visible person by **physical appearance** (build, hair style/color,
   clothing type+color, visible pose). Never emit story names, relationships, or property.
3. **Renderable-only.** Include only what is visually renderable at the frozen instant: people,
   clothing, hair, pose, location, objects, lighting, camera. Omit narrative distance, intent,
   metaphor, and off-screen facts ("watching across twenty feet", "swallowed in shadow").
4. **Tight photo caption.** ~600–800 chars; lead with framing and subject; end with camera/style
   cues (`35mm, shallow depth of field, natural skin texture`).

### 2.4 Distance/framing rule (the B-103 failure class)

Because the model cannot render faces at a distance (§1), the compiler must ensure **every required
subject is described as a near, clearly-lit, in-focus figure**. If the canonical moment puts a key
character far away or in shadow, the compiler must **re-compose to bring that character to the
foreground** or state the shot is a wide establishing shot and accept the character is not the
focus — never silently "include" a distant subject the model will drop.

### 2.5 Externally-researched reference examples

Few-shot examples in prompt compilers MUST be externally sourced (the model family's official
guides). Internal examples are secondary, only when they demonstrate a rule the external examples
do not cover, and only if they align with the anatomy below.

General photorealistic caption shape (Juggernaut X guide, Team Juggernaut):

> "Young woman reading a book in a cozy coffee shop, brunette hair cascading over her shoulders,
> colors warm browns and soft whites, style candid photography, mood relaxed, lighting natural
> light streaming through a nearby window, perspective over-the-shoulder, texture soft wool
> sweater and glossy wooden table."

Camera-first / lighting / emotion shape (Juggernaut XIII Ragnarok guide):

> "Close-up of an elderly woman's face, deep wrinkles highlighted by dramatic lighting, strong
> shadows across her features, intense and somber mood."

Appearance + clothing + camera + detail shape (Stable Diffusion Art, "How to generate realistic
people"):

> "photo of young woman, highlight hair, sitting outside restaurant, wearing dress, rim lighting,
> studio lighting, looking at the camera, dslr, ultra quality, sharp focus, tack sharp, dof, film
> grain, Fujifilm XT3, crystal clear, 8K UHD, highly detailed glossy eyes, high detailed skin,
> skin pores"

What these external examples encode:
- Subject/action first; appearance (hair, build) and **clothing** named; camera/perspective named.
- Lighting and mood are explicit.
- **Clothing is always in the positive** — Stable Diffusion Art: *"use clothing terms like dress in
  the prompt and nude in the negative prompt to suppress"* explicit output on NSFW-prone models.
- **POV exclusion** (POV character never in frame) has no external example because the official
  guides are single-camera shots. The internal B-103 Dean-POV example is the only place it is
  demonstrated, and it follows the same external anatomy (framing first, appearance-only, clothing,
  lighting, camera cues) — kept as a labeled secondary, aligned reference.

### 2.6 Multi-character SDXL scenes (attribute bleed)

When more than one person is in frame, SDXL's self-attention confuses **which attribute belongs to
whom** (hair color, clothing) — the "token bleed" problem (Juggernaut guides) and Stable Diffusion
Art's *"self-attention incorrectly associates the hair color and the person."* The compiler MUST:

1. **State count + gender first** ("a man and a woman") as the common lead. Without it SDXL draws a
   single person.
2. **Describe each person as ONE self-contained clause** (appearance + clothing together). Never
   interleave attributes across people.
3. **Accept the prompt-only ceiling.** Plain-text prompts get multi-person attribute separation right
   only some of the time (regional prompting reports ~75%). The reliable structural fixes are
   **regional prompting** (per-region BREAK prompts) and **ControlNet OpenPose/Depth** for pose and
   composition — that is the B-097 continuity-rendering scope, not a prompt-string tweak.

---

## 3. Model settings (researched, authoritative)

### 3.1 SDXL / Juggernaut / Big Lust (natural-language photorealistic family)

| Setting | Documented value | Source |
|---|---|---|
| Resolution | 832×1216 portrait; any SDXL native res (≥1024, e.g. 1024×1024, 1536×1024) | Juggernaut guide; SDXL art guide |
| Sampler | **DPM++ 2M SDE** (or DPM++ 2M Karras) | Juggernaut guide (author) |
| Steps | **30–40** | Juggernaut guide (author) |
| CFG | **3–6** (lower = more realistic) | Juggernaut guide (author) |
| Negative | **Start minimal**; add only what you must avoid | Juggernaut guide (author); SDXL art guide |
| VAE | Baked in — no separate VAE | Juggernaut guide |
| HiRes | 4xNMKD-Siax_200k, 15 steps, 0.3 denoise, 1.5× upscale | Juggernaut guide |
| CLIP skip | **None** by default | ComfyUI SDXL practice (Pony keeps skip-2; SDXL does not) |
| Keyword weights | ≤ ~1.4, sparingly | SDXL art guide |

**Big Lust (active production model) — specifics:**
- **Model:** `bigLust_v16.safetensors` (v1.6) — a merge of **bigASP** × **LUSTIFY**, the author's pick
  for "the best non-Pony SDXL checkpoints for NSFW images"; photorealistic, NSFW, CreativeML Open
  RAIL++-M, published 2024-11-20. **v1.6 merged bigASP 2** (quality/aesthetics); **v1.5 merged
  LUSTIFY 4.0** (darker images — a known stylistic trade-off).
- **No author-written prompt guide and no trigger words** (unlike Juggernaut). It is a photorealistic
  SDXL merge, so it reads **natural-language photography briefs** — the same anatomy as Juggernaut/SDXL.
  **Do NOT use Pony tag vocabulary on it.**
- **Settings (community envelope):** sampler **DPM++ 2M (DPM2 A)**, **CFG ≥ 3.5 to 5** + **hires-fix**.
  Tighter than the generic family CFG 3–6 — keep Big Lust renders at CFG ~4–5 for realism.
- **Companion style LoRA (community):** "Sunburned (Big Lust)" at weight **0.25–0.4, no trigger word**;
  a commonly cited combo is v1.5 + Sunburned for the strongest results.

**Our production values (already correct, match the envelope):** `dpmpp_2m_sde` / `karras` / 30
steps / CFG 5.0 / 1024×1024 (per `ComfyUIImageClient.BuildSdxlWorkflow` and the Juggernaut harness).

### 3.2 Negative-prompt philosophy

- The SDXL art guide is explicit: **easy on negative prompts** — include only what you want to
  avoid. A giant negative list fights the model.
- **Our documented exception (must keep, with reason):** because we render explicit/NSFW content on
  an NSFW-trained family, we DO carry a heavier anatomy/style guard set
  (`SdxlSceneImagePromptBuilder.DefaultNegativePrompt`). This is a deliberate, commented deviation
  from "start minimal" (rule 6). Keep the guard set tight and justified — do not grow it ad hoc.
- Negations ("no X") belong in the negative, never as "no X" in the positive.
- SDXL-family finetunes can carry **BOORU anatomical tokens**; if any appear, they belong in the
  **negative** to avoid accidental outputs (Juggernaut guide, "Keep it safe for work").

### 3.3 SFW best practices (from the Juggernaut guide — applies to any NSFW-trained finetune)

- Filter anatomical tokens into the **negative**.
- **Describe clothing in the positive** to anchor safe imagery.
- Use realism tokens carefully (`detailed skin`, `natural`, `realistic texture` enhance anatomical
  fidelity — pair with a hard SFW clamp when the provider is SFW-filtered).

---

## 4. Family quick reference (do not mix vocabularies)

| Family | Prompt language | Count/quality tokens | Camera | Sampler / steps / CFG | Negative | Doc |
|---|---|---|---|---|---|---|
| **SDXL / Juggernaut / Big Lust** | natural-language photography brief | none — state gender+number in prose | name it (`wide shot`, `from behind`, distance) | DPM++ 2M SDE / 30–40 / 3–6 | minimal-ish; anatomy guards for NSFW | this doc + `sdxl-juggernaut-prompting.instructions.md` |
| **Pony V6 XL** | dense comma tags | `score_9…score_4_up` (full string, first) + `rating_*` + `1boy/1girl/2people` | explicit (`front view, eye level`) | `euler_ancestral` / 25 / 7 · CLIP skip 2 | short (~6 terms) | `pony-v6-prompting.instructions.md` |
| **Qwen Image Edit** | edit instruction over a source image | n/a | n/a (edit) | see its doc | see its doc | `qwen-image-edit-2511.instructions.md` |
| **FLUX.2** | natural language or structured JSON | explicit ordered subjects | explicit structured camera | variant/profile-specific | **unsupported; field forbidden** | `flux2-prompting.instructions.md` |
| **Qwen Image 2512** | descriptive natural language | explicit subject clauses | explicit framing/lighting | 50 steps / true CFG 4 in official recipe | persisted profile value | `qwen-image-generation.instructions.md` |
| **Qwen Image Edit 2511** | explicit transformation/preservation instruction | ordered input-image roles | preserve/change contract | 40 steps / true CFG 4 / guidance 1 | persisted profile value | `qwen-image-edit-2511.instructions.md` |
| **API image models** (GPT-Image-2 / Seedream / Imagen) | natural language | n/a | n/a | n/a | n/a (neutral SFW clamp) | `SceneImagePromptCompilers.cs` (`ApiSceneImagePromptCompiler`) |

### 4.1 FLUX.2 exact production boundary

- BFL documents 64px minimum dimensions, dimensions divisible by 16, and at most 4MP output.
- FLUX.2 has no negative prompt; the compiler rejects that field at any nesting level.
- `[flex]` guidance is 1.5-10 and steps are at most 50. `[pro]`/`[max]` must not inherit those
   fields unless the exact selected provider profile documents them.
- BFL `[pro]` permits at most 9MP total input plus output and up to eight API references at 1MP
   output. API reference counts remain provider/variant-specific; playground limits never transfer.
- Production uses pinned fixed endpoints, ordered role-bearing references, and deterministic
   structured prompts. Prompt upsampling is not part of compilation.

### 4.2 Qwen exact production boundary

- `Qwen/Qwen-Image-2512` generation uses `QwenImagePipeline`; the official recipe uses 50 steps,
   `true_cfg_scale = 4.0`, and seven exact published aspect-ratio dimensions.
- `Qwen/Qwen-Image-Edit-2511` uses `QwenImageEditPlusPipeline`; the official recipe uses 40 steps,
   `true_cfg_scale = 4.0`, `guidance_scale = 1.0`, one output, and an ordered image list.
- Generation and edit have different compiler/profile identities. Official prompt-enhancement
   utilities are not runtime dependencies; compilation consumes validated structured facts only.

---

## 5. Change procedure (mandatory for ANY compiler/prompt-builder change)

1. **State the target family** and which documented rule/source justifies the change.
2. **Check this document + the family instruction file first.** If the change contradicts a
   documented rule, stop and surface the conflict — do not silently override.
3. **If a new technique/setting is involved, do the external research (§6), cite it in the PR/commit
   and in the family instruction file.**
4. **Implement structurally.** Update the system prompt so the LLM preprocessor *enforces* the rule
   (e.g. the 4 canonical rules of §2.3), not just a one-off prompt string for one render.
5. **Add/update tests** that assert the structure (forbidden tokens absent, POV exclusion, length
   cap, component presence) — `DreamGenClone.Tests/RolePlay/**`.
6. **Generate + visually verify** every new prompt on the real pod, and record per-image pass/fail —
   never rubber-stamp.
7. **Record an audit event** (`SceneImageRequestSubmitted`) so the exact submitted positive /
   negative / seed / checkpoint is always recoverable.

---

## 6. External research sources (fetched 2026-09-01)

| Source | What it provides |
|---|---|
| **RunDiffusion — Juggernaut XIII Ragnarok Prompt Guide** (Team Juggernaut / Adam) — `https://www.rundiffusion.com/prompt-guide-for-juggernaut-xiii-ragnarok-by-rundiffusion` | The 17 prompt components, settings table (DPM++ 2M SDE, 30–40 steps, CFG 3–6, 832×1216, VAE baked in, HiRes recipe), token budget ≤75, first-sentence rule, NSFW-trained-model clothing anchor, SFW best practices, BOORU-token handling. |
| **Civitai — Juggernaut XL model page (author: Kandoo/Team Juggernaut)** — `https://civitai.com/models/133005/juggernaut-xl` | Author's recommended settings (identical to the guide) and the explicit SDXL limitation note ("faces at a distance", weak text). Model identity: `JuggernautXL_Ragnarok_ByRunDiffusion.safetensors`, RAIL++-M, Overwhelmingly Positive. |
| **Civitai — Big Lust model page + API** — `https://civitai.com/models/575395/big-lust` and `/api/v1/models?query=biglust` | Big Lust = bigASP × LUSTIFY merge, SDXL 1.0, non-Pony NSFW checkpoint; `bigLust_v16.safetensors` (v1.6 = bigASP 2, 2024-11-20, fp16; v1.5 = LUSTIFY 4.0, darker); **no author prompt guide, no trigger words**; community settings DPM++ 2M (DPM2 A) / CFG 3.5–5 + hires-fix; Sunburned companion LoRA 0.25–0.4. |
| **Stable Diffusion Art — "15 Stable Diffusion XL prompts + tips"** — `https://stable-diffusion-art.com/sdxl-prompt/` | SDXL reads natural language (stronger text encoder), describe in detail, easy on negative prompts, easy on weights (≤1.4), native resolutions (1536×1024 / 1216×832 / 1024+), fewer anatomy issues than v1. |
| **RunDiffusion — Juggernaut X Prompt Guide** (Adam Stewart / Team Juggernaut) — `https://www.rundiffusion.com/prompting-guide-for-juggernaut-x/` | The 17-component anatomy with worked example prompts (coffee-shop reader, urban fashion portrait); NSFW/SFW settings (DPM++ 2M Karras, 30–40 steps, CFG 6–7, ≤75 tokens); token-bleed guidance; clothing + negative-token handling for the NSFW-trained model. |
| **Stable Diffusion Art — "How to generate realistic people"** — `https://stable-diffusion-art.com/realistic-people/` | Photorealistic-people prompt construction: clothing terms in the positive suppress explicit output on NSFW-prone models; camera/lighting/facial-detail keywords; minimal negative prompt approach. |
| **Stable Diffusion Art — "Regional Prompter: Control image composition"** — `https://stable-diffusion-art.com/regional-prompter/` | Multi-subject attribute bleed ("self-attention incorrectly associates the hair color and the person"); the common count+gender prompt requirement; per-region BREAK prompting (~75% reliable, batch it); ControlNet OpenPose as the structural pose/composition fix for multiple people. |

Re-verify before relying on a source older than ~6 months; model cards and guides are updated by
their authors.

---

## 7. Related documents

- `sdxl-juggernaut-prompting.instructions.md` — practical SDXL/Juggernaut rules validated on the pod.
- `pony-v6-prompting.instructions.md` — Pony V6 tag vocabulary + workflow rules.
- `qwen-image-edit-2511.instructions.md` — Qwen source-image editing.
- `specs/Planning/B-103-production-studio-composition-transparency/NOTES.md` — the B-103 failure
  analysis that produced the four canonical rules (§2.3).
- `specs/Planning/B-100-progressive-scene-beat-pipeline/` — the B-100 pipeline the compilers serve.
