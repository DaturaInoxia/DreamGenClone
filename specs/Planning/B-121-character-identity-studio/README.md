# B-121 — Character Identity Studio (in-app end-to-end reference-pack pipeline)

**State:** `planned` — **plan only; for handoff to an implementing agent. No code written under this item.**
**Created:** 2026-09-11
**Owner surface:** Asset Manager (`/asset-studio`) → studio sub-route
**Driving example:** build a full 5-view pack for **Sam Winchester** entirely in the app.

## Document index

| Document | Purpose |
|---|---|
| [spec.md](spec.md) | Requirements (FR21-001…034) and eleven acceptance scenarios |
| [plan.md](plan.md) | Verified current state, reuse map, decisions D1–D8, phases, risks, DoD |
| [tasks.md](tasks.md) | Ordered agent-executable tasks B121-000a…B121-050 |
| [seed-prompts.md](seed-prompts.md) | The exact seed prompt texts and settings a fresh install ships with |
| [ui-contract.md](ui-contract.md) | Studio layout, prompt editor, states, keys, accessibility |

## Why this exists

Building Dean v8 / Becky v5 (2026-09-10) was done by hand, outside the app: a dozen ad-hoc
scripts, direct DB writes to create the draft pack, and a DB-injected background job because the
UI entry point for pack generation had been removed. It worked, but it is not repeatable by the
user and every step had to be re-derived. This item turns that exact sequence into a guided,
first-class workflow.

## The workflow to support (the sequence actually proven on Dean v8 / Becky v5)

1. **Front** — either *generate* candidates from a prompt, or *upload* an existing image.
2. **Validate** — eyes (level/symmetry) and proportions; reject/regenerate before investing further.
3. **Remove clothes** — replace garment+collar with background, **without** touching the neck.
4. **Crop** — optional normalise (framing / head scale between views).
5. **Enhance** — 4× upscale → clean 1024 conditioning file.
6. **Angles** — 3/4 left and right first, then profiles.
7. **Promote** — write the 5 views into a new draft pack, sync the test refs, approve.

**Every prompt above is a user-editable persisted template.** The texts validated in this session
ship as *seeds* — starting points the user can change in the app, with a Reset-to-default that
restores the seed exactly. There is no prompt string embedded in code to fall back to; a missing
template fails fast naming its key. See [seed-prompts.md](seed-prompts.md).

## Verified current state — read before planning any work

**The original B-108 design docs are obsolete.** B-108 was absorbed into B-111 Phase 2
(`superseded-map.md`: "B-108 — Reference Bootstrap Studio | Designed | **P2**"), and most of P2 is
already implemented: `ReferenceBootstrapBatch` + location records, `ReferenceBootstrapRepository`
(table `ReferenceBootstrapBatches`), `ReferenceBootstrapService` (batch creation, durable candidate
generation, `SetCandidateDecisionAsync` accept/shortlist/reject, four `PromoteAccepted*Async`
paths), `ReferenceBootstrapPanel.razor` mounted in `AssetStudio.razor`, `CandidateGrid` + Review
Deck, and `ReferenceBootstrapRepositoryTests.cs`.

So **do not re-implement** B108-001…010 or B108-015…023. What genuinely does not exist anywhere:
a prompt-template store, any .NET face-landmark capability, a crop step, an in-app upscaler, a
build/step pipeline record, and any view tag on face promotion (`PromoteAcceptedCharacterFaceAsync`
hardcodes `Front`). Full detail, with file paths, is in [plan.md](plan.md) → "Verified current
state".

## What already exists (reuse, unchanged)

| Concern | Existing capability |
|---|---|
| Candidate generation | `IImageGenerationClient` + dispatcher; `SceneAssetPromptCompiler.Compile` |
| Same-image view/pose edits | `IImageEditingClient.EditAsync`; `ComfyUIImageEditingClient` (local ComfyUI, split Qwen workflow) |
| Upload → asset | `SceneAssetService.CreateFromUploadAsync` |
| Pack lifecycle | `ICharacterImageIdentityService` (draft / supersede / upload asset / approve) |
| Pack faces + provenance | `SceneImageReferenceAssets`, `SceneAssets` |
| Non-pose quality rating | `ReferenceImageQualityAnalyzer` (resolution, density, aspect, Laplacian sharpness) |
| Job execution | Durable background jobs (`DurableBackgroundJobs`, lanes, retries) |
| Test-ref sync | `specs/image-generator-tests/identity_refs.py` + `refs/versions.json` |

## What is genuinely new / missing

| Gap | Detail |
|---|---|
| **Upload-as-front as a first-class entry** | B-108 is description→generation only; the upload path exists as a service but has no guided entry point. |
| **Eye / proportion validation** | The only trustworthy checker is `tools/eye-validation/measure_iris.py` (MediaPipe FaceMesh iris landmarks). The app has **no** face-landmark capability — `ReferenceImageQualityAnalyzer` is resolution/sharpness only. Invoked as a subprocess (Decision 1). |
| **Garment removal step** | No app operation. Needs the neck-preserving prompt shape (below). |
| **Crop / framing normalise** | No app operation. |
| **Enhance / upscale** | **No app-side upscaler exists.** Only a dev binary (`artifacts/tmp/realesrgan/…`, git-ignored) and a *test runner* workflow (`identity-two-character/runners/run_upscale.py`, 4x-UltraSharp via ComfyUI). See Decision 2. |
| **Angle generation reachable** | `SceneAssetProfilePackJobHandler.AngleEdits` exists and is DI-registered, but **nothing in the UI calls it** (removed in commit `930374f`; `AssetStudioUiContractTests` asserts its absence). |
| **Pack creation without the job** | Supersede is only reachable from `/characters/identity`; the studio needs it as a service call (`ICharacterImageIdentityService.SupersedePackAsync`). |

## Hard-won constants that MUST be baked in (not rediscovered)

Learned the expensive way on Dean v8 / Becky v5 (see repo memory `dean-front17-shirt-removal.md`):

- **Engine:** local `Qwen Image Edit 2511` at **40 steps / CFG 4 / euler / simple / shift 3.1 /
  CFGNorm 1**. The RunPod Rapid-AIO variant (8 steps / CFG 1 / euler_ancestral / beta) is a
  distilled checkpoint and produced worse results; its endpoint also went `throttled` and stalled a
  pack job for 15 minutes.
- **Garment prompt must protect the neck:** *"Keep his/her neck, throat, collarbones and shoulders
  exactly as they are — do not remove, shorten, hide or cover the neck."* Without it the model
  deletes the neck and every angle then invents an elongated one.
- **Angle prompts must not name "clothing"** in the preserve-list — with a shirtless front the model
  invents clothing. Name *"bare neck and bare shoulders"* and add *"do not add any clothing"*.
- **Direction:** anatomical wording ("turn to their left") is ignored — Qwen produced four identical
  directions. Image-space wording ("nose points toward the RIGHT side of the image") works for some
  sources and not others. **Gate every angle on the yaw sign** and mirror the reliable left-facing
  render when the model refuses the other direction.
- **View convention (ground truth = the accepted packs):** `ThreeQuarterLeft`/`ProfileLeft` face
  **image-left**; `ThreeQuarterRight`/`ProfileRight` face image-right.
- **Quality bar:** the app's own analyzer — variance of Laplacian on a 256 px grayscale resize;
  **≥250 = Good**, 60–250 = Ok. Generative edits soften images, so an enhance pass is usually
  required to clear it.
- **Eye gate:** `|irisDy%| ≤ 1.5` via the canonical tool. Profiles have no face mesh — validate
  visually.
- **Refs to sync on promote:** `refs/<char>/<version>/` (all 5 views), `refs/versions.json`, the
  legacy `identity-two-character/refs/multiangle/<char>_*.png`, and the legacy canonical face
  `identity-two-character/refs/<char>_face.png`. Miss one and runs keep using old refs.

## Proposed shape

A **step pipeline with persisted state**, not a one-shot job:

- A `CharacterIdentityBuild` record (character, target pack, current step, per-step artifact refs,
  settings snapshot) so a build is resumable and auditable — the manual session lost track of which
  pass produced which file.
- One durable job per step (lane `ImageEdit`), each writing a new `SceneAssets` row so every step is
  inspectable and re-runnable in isolation.
- **Gates that block, with reasons:** cannot advance past *front* while the eye check fails; cannot
  advance past *validate* while the app quality rating is `NotGood`; cannot promote while any view
  is missing or the yaw sign is wrong.
- Reuse `SceneAssetProfilePackJobHandler`'s angle-edit *mechanics* but move the prompt/view sets out
  of hardcoded constants into the step configuration so they are correctable without a code change.

## Decisions (locked 2026-09-11)

1. **Eye/proportion check = (A + C).** The app invokes the approved Python tool
   (`tools/eye-validation/measure_iris.py`) as a subprocess for machine numbers, **and** provides a
   manual visual gate (large preview + guides) so the user's verdict can override. Rationale: the
   repo forbids re-deriving an eye checker from weaker methods, so the tool's numbers are the only
   machine signal — but the tool returns nothing on full profiles, so the human override is
   mandatory rather than optional.
2. **Enhance = (A) local ComfyUI `UpscaleModelLoader` + `ImageUpscaleWithModel` (4x-UltraSharp).**
   No new binary to ship; the existing test runner (`identity-two-character/runners/run_upscale.py`)
   already proves this exact path. To be surfaced in the UI: enhancement re-synthesises facial
   detail, so it must never be applied to the chosen likeness front *before* likeness is assessed.
3. **Sequencing = B-108 first.** Implement the Reference Bootstrap Studio (candidate generation +
   curation, already `designed` as tasks B108-001…B108-039), then layer this item's
   identity-specific steps (validate → de-clothe → crop → enhance → sequenced angles) on top.
   Rationale: generation and curation are the front of the same pipeline and B-108 is already fully
   specified; building B-121's upload-first path first would strand the generation path.

## Blocking conflict found while reading B-108 (must be resolved inside B-108)

Task **B108-012** says: *"Define the per-target-type view/instruction sets: face angles (reuse the
existing `SceneAssetProfilePackJobHandler.AngleEdits` prompts)"*. Those prompts are defective in two
ways, both fixed by hand in this session and documented in repo memory
`dean-front17-shirt-removal.md`:

| Defect in `AngleEdits` (lines 26–38) | Observed effect | Correction |
|---|---|---|
| `"Keep the exact same face, hair, facial features, identity, **clothing**, and lighting unchanged."` | On a shirtless front, Qwen **invents clothing** — 3 of 4 angles came back clothed (botDark 0.660/0.329) | Drop the word "clothing"; name *"bare neck and bare shoulders"*; add *"do not add any clothing"*; lock framing. Proven → botDark 0.000 |
| `"Rotate the person's head … **to their left** / **to their right**"` | The direction instruction is **ignored** — all four angles rendered facing the same way (3/4L −0.387 vs 3/4R −0.415 = *same* direction; profiles correlated 0.954 direct vs 0.427 mirrored) | Use image-space wording, **gate on the yaw sign**, and mirror the reliable left-facing render for the right slot |

Implementing B108-012 verbatim would import both bugs into the new studio. Options: (1) correct the
`AngleEdits` constants first (small, contained — also fixes the still-registered handler), or
(2) define fresh instruction sets in B-108 that supersede them while leaving the old constants dead.
**Recommendation: (1)** — the constants are the single place the corrected prompt shapes belong, and
leaving knowingly-broken prompts in a registered handler invites their reuse.
B-111 `P2-tasks.md` line 49 points at the same pattern and would inherit the same fix.

## Decisions still open

- **Where the studio lives.** Asset Manager tab (B-108 precedent) vs `Character Identity`
  (`/characters/identity`, where packs are approved). *Recommendation: a dedicated
  `/characters/identity/studio/{buildId}` route reached from Character Identity, so the pack being
  built is never ambiguous.* Deferred until B-108's Bootstrap tab lands.
- **The orphaned pack job.** `SceneAssetProfilePackJobHandler` is registered and does real work but
  has no caller. Either delete it or make it the studio's angle step.

## Out of scope

- LoRA training (B-107) and cross-image face grafting (needs B-106 Phase A).
- Wardrobe / location targets (B-108).
- Fixing the removed-pack-generation-button regression: partially covered here (the studio needs
  `EnqueueProfilePackAsync` or an equivalent step), but the *orphaned handler* itself is a
  separate small fix — see Follow-ups.

## Follow-ups (independent of this item)

- **Orphaned pack job:** `SceneAssetProfilePackJobHandler` is registered and does real work but has
  no caller; its `AngleEdits` prompts contain the two defects above. Either delete the handler or
  make it the studio's angle step. Until then, re-running it reproduces the clothing/direction bugs.
- **Two stale rows blocked by a RESTRICT FK** from the failed RunPod attempt
  (`SceneAssets` 22876854…, bdbef7dd…).
- **`manifest.json` / `build_manifest.py`** point at `refs/becky_face.jpg`, which does not exist
  (only `.png` is on disk).
