# LoRA dataset design — what a good character LoRA needs, baked into the app (B-123 Phase 1/2)

**Status:** design (for operator sign-off before implementation)
**Date:** 2026-09-24
**Owner:** B-123. Extends `specs/image-generator-tests/flux-character-lora/DATASET-CAPTURE-LIST.md`
(the capture matrix, seeds, invariant/variable split) and `plan.md` Phases 1–8.
**Rule:** nothing here is left to the operator to know. The app generates the plan, the prompts,
the captions and the readiness verdicts; the operator renders, judges and accepts.

## 1. Research base (what the decisions below rest on)

| Source | Fact taken | Consequence in this app |
|---|---|---|
| kohya_ss — *LoRA training parameters* (translated manual) | LoRA needs only **2–3 epochs**; **network rank 32** is usually sufficient (default 8); **text-encoder LR is set lower than U-Net LR** (defaults 5e-5 vs 1e-4); **buckets** sort different aspect ratios automatically (`bucket_reso_steps` 64) and upscaling/squashing is the thing buckets exist to avoid; **“keep n tokens”** pins the first N comma-separated caption words so **“shuffle caption”** can reorder the tail without touching them; caption dropout (typical 0.05) and min-SNR gamma (5) exist to stop overfit; regularization/prior-loss is **optional** per dataset | The dataset must ship **bucketed, un-squashed** images (two native sizes: 1024², 832×1216) and captions whose **first token is the trigger** and whose tail is comma-separated variables (so shuffle is safe). Training profile defaults follow these numbers |
| Stable Diffusion Art — *How to train LoRA models* | A character LoRA works from **~15+ images**; the **trigger word replaces the class word** (“a man” would make every man look like him); **overcooking shows up as identical backgrounds**; captions are auto-generated then corrected | 36 cells is comfortably in range and the **diversity requirement is not optional** — near-identical backgrounds are the visible symptom of an overfit set, so a **similarity gate** and background minimums are part of the plan, not a nicety |
| Repo, `DATASET-CAPTURE-LIST.md` | The Angle × Distance matrix (30 core + 6 variation), one fixed seed per cell, profile/angled weighting, ≥4 outfits, lighting rotation, ~50/50 clothed/nude, **invariants uncaptioned / variables captioned**, body-card as the single source, face-normalization by angle-matched reference | Kept verbatim as the plan's basis; this document adds the app-baked layer on top |

## 2. The invariant / variable split (the rule everything else serves)

- **Invariant** — face, body shape and proportions, skin tone, body hair, pubic grooming, scars,
  marks, tattoos (design + placement). These are **learned**, never captioned: they must bind to the
  trigger token.
- **Variable** — angle, framing/distance, wardrobe state, pose, expression, lighting, background,
  aspect ratio. These are **captioned** and controlled by the prompt at use time.

Consequence: the cell's axis record is the **single input** for both the render prompt and the
caption. One record, two projections.

## 3. The plan (generated, not typed)

`CoveragePlan` → `CoverageRecord` per cell. Operator never edits JSON.

**36 cells per character** (seeds `41000–41099` for Becky, `40000–40099` for Dean style; one seed
per cell, never reused):

- **30 core cells** — Angle × Distance: Front 2/2/2, 34L 1/2/2, 34R 1/2/2, PL 2/2/2, PR 2/2/2,
  over-shoulder/behind 0/1/1 (CU/HB/FB).
- **6 variation cells** — 2 outfits, 2 lighting extremes (hard rim, very dim), 2 expressions
  (laughing, surprised) at CU/HB.

**Every core cell carries** (auto-assigned by the generator, balanced, never random):

| Field | Rule |
|---|---|
| `Angle` | from the matrix |
| `Distance` | CU / HB / FB |
| `WardrobeState` | `Clothed` / `Unclothed`, alternated so each angle group lands ~50/50 overall |
| `PoseClass` | assigned per cell from the matrix (standing / sitting / kneeling / lying / hands-raised), so the set is not 30 standing frames. A concrete pose **frame** is chosen in the workspace from the library inside that class |
| `Expression`, `Lighting`, `Background`, `Outfit` | rotated from a fixed cycle so no two adjacent cells repeat and the minimums below hold |
| `Aspect` | CU → 1024×1024; HB/FB → 832×1216 |
| `Seed` | one per cell |
| `ReferenceRule` | derived from Angle + WardrobeState (exact-angle face ref; clothed or unclothed body ref) |
| `SourceRole` | `train` (default) or `val` |

**Holdout (my decision):** the 6 variation cells + the `over-shoulder/behind` pair are
`val`; everything else is `train`. That gives 8 val images that are *not* the core matrix, so a
trained LoRA can be judged on cells it never saw, without hollowing out coverage.

**Diversity minimums the generator enforces** (and the readiness header reports as gaps):
≥4 distinct outfits · ≥4 distinct backgrounds · ≥4 distinct lighting setups · ≥3 pose classes ·
all 5 angles · both wardrobe states · both aspect ratios.

## 4. Prompts and captions live in the app (template store, seeded, editable)

All of these are seeded rows in the existing `ImageWorkflowPromptTemplate` store — the same store
the front/angle prompts already use — so they are editable in the UI, revisioned, and never code
strings:

| Key | Purpose |
|---|---|
| `lora.cell.render.{front\|threequarter\|profile\|behind}.{close\|half\|full}` | the render prompt per angle family × framing (12 rows) |
| `lora.cell.edit.tweak` | minor edit wrapper used when the operator tweaks an accepted render |
| `lora.cell.caption` | the **caption template** (one template, driven by the record) |
| `lora.cell.references` | the reference-selection rule text shown next to the picker |
| `lora.vocabulary.*` | the wording of every variable axis, one row per value (42 rows) |

**No negative-prompt row, deliberately** (operator instruction, 2026-09-25). The families this pipeline
renders carry no negative: SDXL / Juggernaut / BigLust resolve to an **empty** negative by model-author
research, Pony takes only the short guard set its own compiler authors, and FLUX has no negative field at
all. A per-cell negative would also be dead config — a cell render sets no compiler id, so the render path
compiles the prompt and authors no negative itself, and the value could never reach the model.

**Render prompt composition (deterministic):**
`<template body>` + `, ` + `<BodyCard line, verbatim>` + `, ` + `<angle/framing/wardrobe tags>`
+ pose clause (when a pose frame is attached) + identity references (pack assets, per
`ReferenceRule`). The BodyCard line is the invariant source; the axis tags are the variables.

**Caption composition (deterministic, no hand-writing):**
```
<trigger token>, <wardrobe state>, <angle>, <framing>, <pose class>, <expression>, <lighting>, <background>, <aspect>
```
- The **trigger token is the first token** — kohya's *keep n tokens* pins it while *shuffle caption*
  reorders the rest, so the identity token never gets shuffled into the tail.
- **Invariants never appear.** A unit test asserts the caption builder cannot emit any invariant
  field (body shape, skin, body hair, grooming, marks, tattoo) even if a template asks for it.
- Long captions are allowed but every tag is comma-separated (SDXL's commas-per-concept behaviour).
- The caption is written into `CharacterLoraDatasetMember.Caption` with a revision number and is
  **editable** — but the app's output is already complete, so editing is a correction, not a task.

## 5. Gates (all measured; no prose verdicts)

| Gate | Applies to | Source of truth |
|---|---|---|
| Identity (eye/face landmarks) | CU/HB cells with a measurable face | existing measurement service + `EyeGateMaxAbsIrisDyPercent` |
| Direction (three-quarter views face the right way) | 34L/34R cells | existing angle yaw gate + `AngleYawMinAbsPercent` |
| Sharpness | every cell | existing `QualityGateMinSharpness` metric |
| **Similarity / near-duplicate** | the whole set | new: perceptual hash per accepted member; a candidate within the configured similarity of an accepted member is **blocked with the colliding cell named** (the "identical backgrounds" symptom, caught at the door) |
| **Body-invariant continuity** | the whole set | new: the accepted member's body reference is compared across cells (marks/tattoo region presence + shape metrics); a mismatch flags the cell rather than the pack |
| Adherence (pose/expression actually present) | pose/expression cells | measured joint geometry for pose; expression stays a manual verdict with a required reason (no prose auto-pass) |

Failed gates block **accept**, never the render: an attempt may be kept as evidence, and a manual
override must carry a reason + author (the same override discipline as the face/body flow).

## 6. Training profile (Phase 7) — what this dataset is built to feed

Recorded per profile (BaseModelId/Version/Sha256, never a code default): rank **32** / alpha **16**
(usage strength 0.5), U-Net LR **1e-4**, text-encoder LR **5e-5**, optimizer AdamW8bit, **2–3
epochs**, batch 1–2, `enable_bucket` on with `bucket_reso_steps` 64 and **no bucket upscale**,
`min_snr_gamma` 5, `noise_offset` 0.1, `shuffle_caption` on with `keep_tokens` **1**,
`caption_dropout_rate` 0.05, `clip_skip` per base model (2 for the Pony/NovelAI-derived family, 1
otherwise), caption extension `.txt`.

**Regularization images: not used** (decision): a character LoRA for a *specific* person has no
usable class set, and paying for one costs a second dataset. The overfit levers used instead are
caption discipline, diversity minimums, the similarity gate and a low caption dropout. The profile
keeps the option open if evidence later says otherwise.

## 7. UI (Character Studio → **LoRA images**, two panes)

```
LoRA images · Becky      dataset [BigLust v1.6 · v1 ▾]  [+ new dataset]
cells 12/36 accepted · captions 9/12 · clothed/nude 6/6 · backgrounds 4 (min 4) ·
poses 3/5 classes · gaps: no lying cells · [open review deck] [freeze…]
┌── cells ────────────────────────┐ ┌── workspace: cell #3 (34L · half · nude) ───┐
│ ¤ 1 F·CU·clothed  accepted  ✓✓  │ │ SOURCE  generate·reuse·upload               │
│ ¤ 2 F·CU·nude     in review     │ │ REFS    face 34L ✓  body unclothed ✓       │
│ ¤ 3 34L·HB·nude   working  ◀    │ │ POSE    kneel-114 ✓ head rule ✓            │
│ ¤ 4 34L·HB·clothed              │ │ MODEL   Qwen 2.1 ▾  seed 41012   [Render]    │
│ ¤ 5 34R·FB·nude                 │ │ ATTEMPTS/deck: t1 t2 t3* (delete · state ·   │
│ …                               │ │   accept) — every render and edit of the cell│
│ (collapse « )                   │ │ GATES   identity ▢ adherence ▢ similar ▢    │
└─────────────────────────────────┘ │ CAPTION “ohwx-becky, nude, three-quarter…”  │
```
- The left list is the working item; **collapse** (chevron, like Panels A–C) gives the workspace the
  full width.
- The grid is flat with filters (wardrobe / angle / distance / status / pose), not grouped cards;
  each row shows seed, accepted thumbnail and gate verdicts.
- **The review deck is the attempt surface**: every render and edit of the cell, each with delete,
  state change and accept — the same deck semantics the Faces/Body panels already use.
- Nothing here renders more than the one image the operator asked for. No “generate all”.

## 8. Boundary with the pose work (a separate session)

Pose frames are **consumed, not built here**: both routes (pose-as-reference for Qwen, ControlNet for
SDXL-family) come from that work. This item owns only: the pose **picker** in the cell workspace, the
`PoseClass`/`PoseKey` fields on the cell and attempt, and the guarantee that **every composition —
render or edit — can attach a pose frame**. The conditioning implementation is theirs.

**Pose source, settled by the market survey** (`pose-pack-survey.md`): no pack on the market is built for
LoRA training datasets, so the app owns the plan and treats packs as interchangeable input. Three
consequences:
- **Two input routes are required** — the bundled pack (`pose-packs/openpose-nsfw`, 472 real 18-joint
  COCO JSON) **and** extraction from an image (`Save Pose Keypoints` / DWPose), because the bundle is
  thin or empty for dynamic/walking, fashion/editorial and arms-raised classes.
- **No pack encodes camera or head orientation** — pose-only. So an angled cell can never be satisfied by
  a pose frame alone; the angle must come from the reference and prompt. `PoseClass` and `Angle` stay
  independent axes.
- **Lying / all-fours / feet-tucked-kneel re-pose on SDXL** with 2D OpenPose (already on record in the
  repo's scorecard: lying silhouette ≈ standing silhouette). Cells in those classes need a pose-adherence
  gate at accept, never a silent pass.
- The **"character sheet" shortcut** (one prompt → a sheet of views → auto-crop) is rejected: it shares one
  scene and seed across 30+ crops, which is exactly what the similarity gate is there to catch.
