# B-122 / B-123 — Body-Complete Identity + Character LoRA Image Studio (NSFW-inclusive)

Status: **planned** (design artifact — no code written under this item)
Prerequisite to production LoRA training for the RP scene-image pipeline.

> ### Program context — read `specs/Planning/identity-lora-program-map.md` first
>
> These two items are **stages 2–4** of the identity → body → LoRA spine:
> **B-121** (face pack + shared machinery) → **B-122 Phase 0** (body-complete pack) →
> **B-123 Phases 1–7** (training set) → **B-123 Phase 8** (LoRA usable at inference).
>
> **Ownership correction (map §3).** This plan previously said it "**owns** … prerequisites (B-117
> ControlNet render, B-118 pose store/DW extract, B-120 derived assets)". Building another item's
> scope here would duplicate B-119/B-120, which explicitly claim those areas. The correct reading:
> this plan **depends on and sequences** them, and **consumes** them per cell. They remain separate
> backlog items with their own plans.
>
> **B-121 is a hard prerequisite, not just "upstream".** Phase 0 needs the step pipeline, the
> editable prompt-template store, the eye/face-landmark capability, reference quality gating and
> **view-tagged** promotion. Those are delivered by B-121. Without it, Phase 0 re-implements machinery
> that already exists, and Phase 0 must be a new **target kind** in that pipeline (B-121 FR21-035),
> not a second studio.
>
> **B-123 does not own** the in-app eye check — B-121 builds it once; B-123 Phase 4 calls it.
>
> **Hard precondition:** no training cell may generate before the BodyCard `[DECIDE]` items are fixed.

**NSFW / nude cells are in scope.** The training set must cover the body in both clothed and
unclothed states so the LoRA learns the *person* (body shape, skin, body hair, marks), not a
wardrobe. This mirrors the capture list's ~50/50 rule: too much nude makes the model strip
clothes at inference; too much clothed makes nudity weak.

## Workflow orientation — interactive tools, not batch automation

Each capability in this plan is a **UI tool the user drives**, never a batch job. The training set is
assembled **cell by cell**, and each cell is an *exercise*: pick a source, apply an edit, attach or
extract a pose, pick a model, make attempts, keep or discard. **Nothing generates 30 images on its
own.** The coverage plan is an editable **plan**, not a queue that auto-runs. This is the same
discipline as B-121's studio (steps the user drives, gates the user overrides), extended to body
refs, poses and per-model cells.

**Explicit anti-pattern being avoided** (recorded from prior plans that shipped "wireframe forms"):
a screen that persists rows but gives the user no working workflow. Every tool below must end in a
visible image the user produced and judged — if a tool only writes a database row and produces no
working artefact in the UI, it is not done.

### Component / UI inventory (one row = one tool the user drives)

| Tool (UI) | What the user does, step by step | Item / Phase |
|---|---|---|
| Front source | generate from a description **or** upload; judge likeness; re-roll or re-upload | B-121 |
| Validate | run eye/proportion measurement; inspect the guides; record pass or manual override | B-121 |
| De-clothe / Crop / Enhance | run **one edit at a time**; before/after side-by-side; keep or discard | B-121 |
| Angles | render **one view**; check the yaw sign; mirror or re-run that view | B-121 |
| Promote (pack) | promote the accepted views into a draft pack; approve | B-121 |
| BodyCard editor | resolve every `[DECIDE]` item (tattoo design/placement, body-hair pattern) and save | B-122 |
| Body ref tools | full-body refs at angles, clothed **and** unclothed, same one-at-a-time loop | B-122 |
| **Pose Studio** | search the seeded pose library; extract a pose from an image; edit the 2D skeleton; **save new**; **apply with a chosen model** | B-118 |
| Pose render | pick pose + model + strength; render one image | B-117 |
| Derived-asset extract | pick a source → depth / canny / segmentation extract | B-120 |
| Layout render | depth/canny from a reference; regional identity | B-119 |
| Coverage plan editor | author or seed the cell matrix; edit cells; **never auto-run** | B-123 Ph1 |
| Cell workspace | per-cell: source / pose / model / multiple attempts; keep or discard | B-123 Ph2 |
| Normalize | face-only then body-normalize edit per cell; before/after | B-123 Ph3 |
| Gates | run the scorers; review the manual gates; record the verdict | B-123 Ph4 |
| Caption editor | template-filled, editable, versioned | B-123 Ph5 |
| Freeze / export | review the set; freeze; hand to training dispatch | B-123 Ph7 |
| LoRA use | pick the trained artifact for a base model; set strength | B-123 Ph8 |

**Pose tool requirements (explicit, from the user):** the Pose UI must let the user (a) browse/search
the downloaded pose library with skeleton previews, (b) **save a new pose**, (c) **extract a pose**
from an uploaded or existing image via DWPose, (d) **apply** any saved or extracted pose **with
different models** — always as a single render the user requests, never a sweep across models or poses.

## Scope in one paragraph

Turn the current LoRA dataset "wireframe" into a **fully functional, guided workflow** in
Asset Manager that produces a curated, captioned, frozen training image set for a character
(~30 core + ~6 variation cells ≈ 36 images, split across clothed and nude states), using the
app's existing image generation and Qwen image editing, the DW-pose extractor and the 472-pose
OpenPose library, with automated quality gates and auto-captioning — and hand the frozen set to
the already-implemented training dispatch, then make the trained LoRA **usable at inference**
via a LoraLoader graph node + trigger-token injection.

This depends on a **prerequisite**: the character identity pack must become **body-complete**.
Today the pack is five face views (front + 3/4L/R + profile L/R). The capture list
(`specs/image-generator-tests/flux-character-lora/DATASET-CAPTURE-LIST.md`) already defines what
"body-complete" means — a canonical **BodyCard** plus full-body references, in both clothed and
unclothed states — but no plan or UI produces those. That extension is tracked as **B-122** and
is Phase 0 below.

The parts the workflow needs that do not exist yet (DW-pose extraction client, ControlNet pose
render, pose store, LoRA inference wiring) are **sequenced by this plan** — with the LoRA inference
wiring **owned here** (Phase 8) and the pose/layout pieces **consumed from their own items**
(B-118 → B-117, B-120 → B-119) per `identity-lora-program-map.md` §3.

---

## Current state (verified 2026-09-11)

| Area | State |
|---|---|
| Domain model (`CharacterLoraModels.cs`) | ✅ complete — datasets, members, jobs, attempts, artifacts, strategy bindings, manifest hash |
| Repository + SQLite schema + strict validation | ✅ complete |
| Training profiles + durable training dispatch/reconcile/artifact | ✅ complete (adapter `runpod-serverless-lora-training-v1`) |
| Dataset **batch generation** (`CharacterAssetGenerationService`) | ✅ complete — but registers outputs only as draft `SceneAsset`s |
| Dataset **member registration** | ❌ missing — `AddDatasetMemberAsync` is only called from tests |
| Coverage-plan **generator** | ❌ missing — user hand-writes JSON |
| **Caption builder** | ❌ missing — captions typed by hand |
| Curation UI + freeze | ✅ complete (manual checkboxes + caption textarea) |
| Coverage plan / curation policy **JSON schemas** | ❌ missing — free-form strings, validated only as valid JSON |
| DWPose **pose extraction** client (in-app) | ❌ missing — only pose *consumption* scaffolding + external Python scripts |
| ControlNet **pose render** (B-117) | ❌ `planned`, not implemented |
| Quality scoring (identity / eye) | ✅ exists as external Python CLIs (`tools/consistency-scoring`, `tools/eye-validation`); ❌ not wired into the UI |
| Pose-adherence scorer | ❌ missing entirely |

The durable backbone is done and strict. The gap is entirely in the **interactive creation workflow**:
workflow is a raw-JSON form, and five automation pieces are absent (member registration,
coverage-plan generator, caption builder, pose/DWPose surface, automated quality gates).

---

## Prerequisites — separately owned and consumed here

The following do not exist in code today (verified 2026-09-11). This plan **sequences** them and
integrates them; ownership follows `identity-lora-program-map.md` §3. B-122 owns body-complete
identity; B-123 owns its dataset/scorer/inference work; B-117/B-118/B-120 remain separate owners.

| Prereq | State today | Built by (owner) | Backlog |
|---|---|---|---|
| ControlNet pose render | `designed`, no render code | OpenPose ControlNet workflow builder in `ComfyUIImageClient` (LoadImage pose → DWPreprocessor → ControlNetLoader/Apply → KSampler), model capability declaration, strength, render route | B-117 |
| Pose store + DW extract | `new`, none in-app | **nothing — consumed from B-118** (B-118 owns the store + extract + head-keypoint rule; this plan only builds the picker that reads it) | B-118 |
| Derived-asset store | `designed` | shared versioned/approved derived-asset store for pose/layout control assets | B-120 |
| Body-complete identity | missing | Phase 0 (BodyCard + full-body clothed + unclothed refs) | B-122 |
| LoRA **inference** path | missing entirely | `LoraLoader` graph node + trigger-token injection + strength UI (Phase 8) — so the trained data is actually consumable | net-new (this plan) |
| Pose-adherence scorer | missing | joint-geometry scorer vs library JSON keypoints (Phase 4) | net-new (this plan) |
| In-app face-landmark / eye check | missing (CLI only) | **nothing — consumed from B-121** (B-121 owns this capability and the manual-override gate; do not build a second implementation or a .NET port) | B-121 |

`B-121` (Character Identity Studio) remains the upstream for the face pack; Phase 0 extends it to
the body — as a **new target kind in B-121's pipeline** (B-121 FR21-035), not as a second studio.
B-121 also delivers the eye/face-landmark capability, the template store and view-tagged promotion
that Phase 0 and Phase 4 consume.

---

## Phase 0 — Body-complete identity pack (B-122)

Extend the B-121 identity-studio pack so "approved identity" includes the **whole person**, not
just the head.

1. **BodyCard as a first-class field.** Add a canonical body description to the identity pack /
   character descriptor: `{body shape}, {height/build}, {skin}, {body hair}, {tattoos: design +
   exact placement}, {scars/marks}, {grooming}`. This is the single source of truth pasted into
   every training-generation prompt. It must not be vague — `[DECIDE]` items from the capture
   list (tattoo design/placement, body-hair pattern) get resolved and recorded here.
2. **Full-body references at multiple angles.** In addition to the 5 face views, the pack gains
   full-body reference assets (front / 3/4 / profile, framed full-body, the same body across all
   of them). These become the identity source for full-body cells and for the body-normalization
  edit in Phase 3. Canonical views carry `SceneImageReferenceBodyView`; every body asset carries
  explicit `SceneImageReferenceBodyState` (`Clothed` or `Unclothed`); extended views carry
  `ViewDescriptorJson` (`BodyRotationDeg` + `BodyPositionKey`) without a canonical slot. This makes
  **base + angles + rotations + positions** first-class
   (`identity-and-reference-model.md` §2), not five fixed body shots.
3. **Wardrobe/body split — includes unclothed.** Two states of the same body, both captured at
   the same angles so the LoRA learns one invariant body:
   - **clothed** full-body references (front / 3/4 / profile) — clothing captioned, body uncaptioned;
   - **unclothed/nude** full-body references (front / 3/4 / profile) — the body's skin tone,
     body hair, pubic grooming, and any marks/tattoos are the *invariant* features that bind to
     the trigger token and are never captioned.
   Explicit anatomy in these references is the base model's output; the workflow only records and
   verifies the invariant body, it does not invent anatomy.
4. **Validation gates for the body.** Reuse the eye-validation discipline, extended to body
   invariants: tattoo present and in the exact spot, body shape/proportions consistent across all
   body refs, no drift between views.

Promotion creates or supersedes a draft with explicit `PackScope=BodyComplete`; it never mutates an
approved `FaceOnly` pack in place. Approval requires the canonical face set plus matching clothed and
unclothed canonical body sets, with `CanonicalFullBodyAssetId` pointing at the approved unclothed
front. B-123 rejects any pack that is not explicitly `BodyComplete`.

Reuses: `ICharacterImageIdentityService` (draft/supersede/approve), `IImageEditingClient`
(Qwen full-body edit to stamp marks/shape), `SceneAssetService.CreateFromUploadAsync`.

---

## Phase 1 — Coverage-plan generator (replaces the raw JSON textarea)

1. **Typed JSON schemas** (net-new classes, not free-form strings) for:
   - `CoveragePlan` — the FR2-047 axes: face angle, crop/distance, expression, body framing,
     pose, wardrobe state, lighting, background, aspect ratio.
   - `CoverageRecord` (the per-member `CoverageJson`) — which cell a member satisfied, with the
     assigned seed and the captured axis values.
   - `CurationPolicy` and `CurationFindings` — thresholds (duplicate similarity, identity floor)
     and the structured findings that currently exist only as checkboxes.
2. **Generator.** Build the ~30 core cells from the capture-list matrix
   (Angle × Distance: F 2/2/2, 34L 1/2/2, 34R 1/2/2, PL 2/2/2, PR 2/2/2, over-shoulder/behind 0/1/1)
   plus ~6 variation cells (2 outfits, 2 lighting extremes, 2 expressions). Assign one fixed seed
   per cell (Dean `40000..40099`, Becky `41000..41099`), recorded in the manifest. Profile and
   angled cells are weighted highest (the current identity failure mode).

   **Wardrobe-state axis (NSFW):** the generator also spans wardrobe state — `clothed` and
   `nude` — across the angle/distance matrix, balanced ~50/50 (per the capture-list rule), so
   the naked body is learned from multiple views and the clothed body stays separate. Nude cells
   are single-subject, no contact, and follow the same deterministic-seed policy.
3. **Dataset workspace (plan editor), not a queue.** A grid, one row per cell: axis values, seed,
   status, and the current accepted image thumbnail. Each row **opens a cell workspace** (Phase 2).
   There is no "generate all". The plan is authored or seeded from the capture-list matrix, but
   cells are produced one at a time by the user. Status reflects what the user has actually done:
   `draft → in-progress → accepted / rejected` — never a queued/auto state.

## Phase 2 — Cell workspace (one cell at a time, multiple attempts)

Each cell is a **user-driven exercise**, not a queued render. The user opens a cell from the plan
and, inside it: picks the source (generate fresh / reuse a reference / upload), attaches a pose
(from the B-118 library, a fresh DWPose extraction, or none), picks the model, renders, then applies
normalization (Phase 3) and gates (Phase 4). Any attempt can be kept as the cell's accepted image or
discarded; the workspace holds the attempt history. Several create / edit / pose / model attempts per
cell are expected and supported — never collapsed into one shot. Nothing generates more than the
single image the user just asked for.

**Model selection is the user's call.** Every cell carries its own model + route choice, defaulted
sensibly but freely overridable. The workflow records known characteristics as non-binding
guidance shown in the UI, never as hard blocks — a model is only rejected when it genuinely cannot
run the selected operation, not because of a preference. Options per cell:

- **Pose-conditioned cells** — default: attach a pose skeleton through the **B-117** ControlNet
  path. Pose sources: (a) the **472-pose library** via the **B-118** pose picker with skeleton
  preview and the **head-keypoint validation rule** (nose + neck + both shoulders must be present —
  a faceless skeleton leaves the head unconstrained); (b) a full-body reference from Phase 0 for
  angle cells. *Guidance, not a block:* OpenPoseXL2 holds standing/squatting/kneeling-feet-down
  reliably and re-poses lying/all-fours — the UI flags this; the user may still choose it or
  switch model/route.
- **Lying / all-fours cells** — the UI surfaces the known OpenPoseXL2 re-pose limitation and
  offers alternates (B-119 depth/canny from a reference, a reference-conditioned render, or a
  different model); the user decides.
- **Face-close cells** — default: identity-conditioned render (IP-Adapter `PLUS FACE`,
  angle-matched ref) or plain T2I + normalization.
- **Nude cells** — default: an uncensored local SDXL checkpoint (BigLust/Juggernaut/Pony),
  conditioned by the Phase-0 unclothed body reference and, where a pose applies, a single-person
  library pose. *Guidance:* Qwen image *edit* and hosted/censored APIs are unsuitable for explicit
  content and are flagged as such, but no model is hard-blocked — the user chooses and accepts the
  result.

Reuses `CharacterAssetGenerationService.CreateBatchAsync` only through a one-output adapter that
hard-validates requested count `= 1` (the existing API name does not authorize a multi-image UI)
and `SceneImageService` enqueue paths. Generation provenance stays exact (source render hash +
normalized hash + seed + prompt).

## Phase 3 — Normalization (face + body)

After each cell renders, the capture list's two-step fix:

1. **Face normalize** — Qwen identity edit, face-only instruction
   ("keep pose/bodies/position/lighting exactly unchanged except the face"), angle-matched
   reference (F→front, 34L→34l, 34R→34r, PL→profl, PR→profr). Reuses
   `IImageEditingClient.EditWithReferencesAsync`.
2. **Body normalize** — Qwen edit with the Phase-0 body reference to stamp body shape, tattoos,
   marks. This is what makes the training set identity-consistent even though raw T2I identity
   is weak, and it is the bootstrap for a tattoo/mark the text-to-image path cannot reliably draw.
   For **nude cells**, the body normalize uses the unclothed body reference so skin tone, body
   hair, pubic grooming and marks match; the Qwen editor is only asked to align the body, never
   to alter explicit anatomy (which the base model owns).

## Phase 4 — Quality gates (automated + manual)

Wire the external scorers into the workflow and add the missing one:

| Gate | Today | This plan |
|---|---|---|
| Exactly one subject / face count | manual checkbox | ✅ automate via MTCNN presence |
| Identity vs canonical | external CLI, not wired | wire `tools/consistency-scoring identity` (frame-normalized band) |
| Eye correctness | external CLI (`measure_iris.py`) | subprocess call in the workflow (face-close cells only) |
| **Pose adherence** | **missing** | **build it** — compare render joint geometry to the library JSON keypoints (the raster-IoU approach proved invalid; joint geometry is the ground truth) |
| Resolution / framing / aspect | manual | automate from cell axes |
| Near-duplicate | manual checkbox | automate via `subject`/CLIP similarity, threshold from `CurationPolicy` |
| Anatomy / leakage / permanent-trait drift | manual checkboxes | keep manual; score where possible. **For nude cells this is the primary gate** — anatomy plausibility is manual (no reliable automated detector exists), and the skin-fraction `sanitisation` scorer is explicitly **not** applied (it screens *out* explicit content, the opposite of intent) |

**Hard rule from prior work:** pose claims come from measured joint geometry, never from
auto-caption prose; identity scores are only comparable within a fixed face-size band; a single
favourable metric is never a pass — report identity + adherence + diversity.

**Pose-adherence contract:** compare only named COCO-18 joints present above the configured
confidence floor in both target and render. Require nose, neck and both shoulders plus the configured
minimum shared-joint count; otherwise return `NotScorable` and require manual review. Translate both
skeletons to the neck/shoulder-midpoint origin and scale by torso length (shoulder midpoint to hip
midpoint); do not rotate either skeleton. Mirror only when the cell's persisted pose-source transform
explicitly requests it. Report (a) confidence-weighted normalized joint RMSE and (b) mean absolute
connected-limb angle error. `CurationPolicy` stores the confidence floor, minimum joint count, RMSE
limit and limb-angle limit; missing values fail fast. A pass requires both limits. Calibrate and lock
the limits against versioned known-good and known-bad fixtures before the scorer gates a real cell.

## Phase 5 — Caption builder

Deterministic template per the capture list: `{trigger}, {pose/angle}, {distance}, {clothing},
{lighting}, {background}, {expression}`. Invariant identity (face, body shape, tattoos, body
hair, scars) is **never** captioned — the trigger token owns it. Captions are editable and
versioned (existing `CaptionRevision` optimistic-concurrency). Optional VLM assist (local Qwen VL)
behind a manual-review gate; the template is the safe default.

## Phase 6 — Member registration (the missing production path)

Close the gap where `AddDatasetMemberAsync` is only ever called from tests. When a reconciled
output is approved, register it as a `CharacterLoraDatasetMember`:

- `IdentitySeed` role for the seed cells, `Training`/`Validation` for coverage cells (both
  `Train` and `Validation` splits required at freeze — already enforced).
- `SceneAssetId` + version + SHA-256 (the shared asset catalog), `Caption`, `CoverageJson`,
  `CurationFindingsJson`, `ReviewedBy/Utc`.
- All members `Accepted` before freeze (already enforced by `ValidateFreezeMembers`).

## Phase 7 — Curation, freeze, export, train

- `LoraDatasetCuration.razor` gains the automated quality signals (Phase 4) alongside the manual
  checkboxes.
- Freeze already computes `ManifestSha256`. Add the training-format export (`.txt` sidecars /
  `dataset.toml`) as a download or a hand-off into the existing training dispatch — which already
  compiles the canonical request from the frozen manifest. No new training dispatch work.

---

## Service / model deltas (net-new)

- `CoveragePlan`, `CoverageRecord`, `CurationPolicy`, `CurationFindings` — typed DTOs + JSON schemas.
- `BodyCard` on the identity pack / character descriptor + full-body reference assets (Phase 0).
- `ICoveragePlanService` — generate + validate the checklist.
- `ICaptionBuilder` — build/revise captions from cell axes + trigger.
- `ILoraImageWorkflowService` — orchestrate generate → normalize → score → register → curate.
- `IPoseAdherenceScorer` — joint-geometry pose scorer (subprocess wrapper or .NET port).
- `IDwpPoseExtractionClient` — consumed from B-118; not implemented by B-122/B-123.
- OpenPose ControlNet workflow builder in `ComfyUIImageClient` — consumed from B-117; not
  implemented by B-122/B-123.
- `LoraLoader` node builder + trigger-token injection (Phase 8).
- Member-registration call site in the reconciliation/approval path.

## UI deltas

- `LoraDatasetGeneration.razor` → guided wizard: (1) dataset identity prefilled from character,
  (2) generated coverage checklist with pose picker + skeleton preview, (3) active-cell render
  progress, (4) normalization + quality-review gallery, (5) captions, (6) freeze.
- `LoraDatasetCuration.razor` → add automated quality signals.
- `LoraDatasetTraining.razor` → unchanged (works).
- `LoraTrainingProfiles.razor` → unchanged (works).

## Phase 8 — LoRA inference wiring (make the data consumable)

**First base model, generic infrastructure.** The first character is trained against **one** base
model, but every piece built here — the `LoraLoader` node, trigger-token injection, the
`IdentityStrategyBinding` strength control and the artifact picker — is base-model-agnostic. Adding a
second model later is a **data action** (a new training profile → a new artifact), not new code:
the infrastructure, UI, components and workflow already exist. See
`identity-lora-program-map.md` §6 G5.

A trained LoRA is useless without a path to load it. Add the two missing pieces (verified absent
repo-wide):

1. **`LoraLoader` graph node** in the ComfyUI render graph — inserted between checkpoint and the
   sampler, feeding both model and clip, at `IdentityStrategyBinding.LoraStrength` (already
   validated positive).
2. **Trigger-token injection** into the prompt builder — the dataset's `TriggerToken` is prepended
   to the character's prompt when the resolved identity strategy is `Lora`/`Combined`.
3. **UI surface** — the strength + artifact selection already modeled by `IdentityStrategyBinding`;
   surface it in the render/identity selection so a user picks the qualified LoRA artifact.

This is what turns B-123's frozen dataset into a working per-character identity strategy, and it
closes the gap flagged earlier (the app knows what a LoRA *is* but cannot *use* one).

## Explicit non-goals

- Not a two-person LoRA. One character per dataset (the blend problem stands); two-character
  frames stay on the staged/IP-Adapter path.
- Not a training service — that already exists and works.
- Not a rebuild of the pose store / pose editor (B-118), ControlNet render (B-117), or derived/layout
  tools (B-120/B-119). They are separately owned prerequisites and are consumed, not duplicated.

## Dispatch lists

- **B-122 Phase 0:** [b122-tasks.md](b122-tasks.md) — BodyCard, body target, explicit clothed/
  unclothed canonical and extended views, body validation, `BodyComplete` promotion and UI.
- **B-123 Phases 1–8:** [tasks.md](tasks.md) — coverage plan, cell workspace, normalization, gates,
  captions, registration, freeze/export and LoRA inference.

Neither dispatch list may absorb B-117/B-118/B-119/B-120 implementation. Their contracts must exist
before the consuming B-123 tasks are marked complete.

## Open questions / risks

- **Pose scorer correctness** — the recurring failure mode of this investigation. Must be validated
  against known-good and known-bad renders before it gates anything.
- **Lying / all-fours cells** — OpenPoseXL2 re-poses them (verified). Surfaced as guidance, not a
  gate: the user picks the route/model per cell (B-119 depth, reference, another model); nothing
  is silently blocked or excluded.
- **Synthetic training data** — the set is AI-generated; normalization mitigates identity drift
  but cannot add real-photo diversity. Consider mixing the real photos (`refs/originals/…`) where
  angle/lighting coverage allows.
- **Nude vs clothed balance** — RESOLVED: nude cells are in scope, balanced ~50/50 (the capture
  list's rule so the model does not default to one state). Anatomy comes from the base model;
  the workflow only records and verifies the invariant body. The open item is the automated
  anatomy gate — none exists; nude-cell anatomy review stays manual until one is built.
- **Circular identity** — normalization uses the same refs later used at inference; acceptable as
  bootstrap, but the LoRA must be qualified (P2-068, deferred to B-107) against an independent
  held-out set.
- **BodyCard `[DECIDE]` items** — tattoo designs/placements and body-hair pattern must be fixed
  before any cell generates, or the set will train an inconsistent body.

## Files

- This plan: `specs/Planning/B-122-body-complete-identity-and-lora-image-studio/plan.md` (this file).
- Capture list (source of truth for cells/body): `specs/image-generator-tests/flux-character-lora/DATASET-CAPTURE-LIST.md`.
- Upstream seams: `specs/Planning/B-117…`, `B-118…`, `B-120…`, `B-121…`.
- Implementation site: `DreamGenClone.Web/Application/RolePlay/CharacterLoraTrainingService.cs`,
  `CharacterAssetGenerationService.cs`, `DreamGenClone.Web/Components/Pages/LoraDataset*.razor`.
