# B-121 Specification — Character Identity Studio

**Status:** Plan only — for handoff to an implementing agent. No code written under this item.
**Supersedes:** B-108 `tasks.md` (obsolete — see README "Verified current state").
**Depends on:** B-111 P2 (`ReferenceBootstrapService`, `ProducedImage` reference candidates),
B-032 Phase 2 identity packs, Model Manager, durable job queue, `tools/eye-validation/measure_iris.py`.

## Goal

A user builds a complete, approved, multi-view character identity reference pack **entirely in the
app**, starting from nothing but a description (or an uploaded image), with every prompt used at each
step visible and editable. The workflow is the one proven by hand on Dean v8 / Becky v5; its value is
that it encodes what was learned expensively rather than re-deriving it.

Driving example: **Sam Winchester**, from description to approved 5-view pack, with no hand-run
scripts and no direct DB writes.

## Non-Goals

- LoRA dataset creation or training (B-107).
- Cross-image face grafting (needs B-106 Phase A). Every edit here is same-image.
- Wardrobe and Location targets — already implemented in `ReferenceBootstrapService`; this item
  consumes them but adds no new target.
- A generic image editor. Crop/garment/enhance are *fixed pipeline steps with editable parameters*,
  not a freeform canvas.
- Replacing `ReferenceImageQualityAnalyzer`'s rating model, or the Model Manager's sampling config.

## Requirements

### A. Step pipeline

- **FR21-001:** A character identity build is a persisted, resumable record: target character, the
  producing batch, the ordered step list, each step's status, and each step's produced image. A build
  interrupted at any step resumes at that step without repeating completed ones.
- **FR21-002:** Steps execute in fixed order: **Front → Validate → Garment removal → Crop → Enhance →
  Angles → Promote**. A step may be skipped only by an explicit user action recorded on the build;
  skipping is never implicit.
- **FR21-003:** Each step produces a new artifact row. No step overwrites its input. Every step is
  independently re-runnable, and re-running a step supersedes only that step's own output.
- **FR21-004:** The build records, per artifact: the resolved prompt text actually used, the resolved
  model id, the source artifact id, and the producing step.
- **FR21-005:** Front may be produced either by generation from a description or by user upload. Both
  routes converge on the identical step-2 onward pipeline.

### B. Editable prompts and settings

- **FR21-006:** Every prompt used by a pipeline step is a persisted, user-editable template. No prompt
  text is hardcoded in a component, service, or handler.
- **FR21-007:** Templates are keyed by a stable step/view key and resolvable at two scopes:
  **character-specific override** and **global default**. Resolution is: character override if
  present, else global. If neither exists the step **fails fast naming the missing key**.
- **FR21-008:** Global default rows are **seeded at migration** with the prompts validated in
  `dean-front17-shirt-removal.md`. Each global row retains an immutable `SeedBody`; a
  **Reset to default** action restores `Body = SeedBody`. Seed text is configuration data in the
  database — **there is no code-embedded prompt string to fall back to**. (This is what keeps the
  repo's no-fallback rule satisfied while still shipping working defaults.)
- **FR21-009:** Step *behaviour controls* are equally persisted and editable, not code constants:
  editor model id, upscaling model + target long edge, crop parameters, the direction/mirror flags,
  the eye-gate threshold, and the quality-gate threshold.
- **FR21-010:** The UI shows, for each step, the resolved prompt text and which scope supplied it
  (character override vs global), and allows editing either scope.
- **FR21-011:** Model ids resolve through Model Manager. Sampling parameters are never duplicated into
  this feature's tables — only the model id is stored.

### C. Validation step

- **FR21-012:** The validate step measures eye level/symmetry and interocular distance using the
  canonical `tools/eye-validation/measure_iris.py` executed as a subprocess. No second eye-measurement
  implementation is introduced (repo rule: the canonical tool is the only trusted method).
- **FR21-013:** The measured values and the raw tool output are persisted on the artifact.
- **FR21-014:** The step supports a **manual visual override**: a large preview with alignment guides
  where the user records pass/fail. The override is persisted, attributed to the user, and is
  **mandatory** when the tool returns no face mesh (which is its behaviour on full profiles).
- **FR21-015:** The pipeline cannot advance past Validate while the eye gate fails and no explicit
  override is recorded. The blocking reason is stated in the UI.

### D. Garment removal step

- **FR21-016:** Garment removal is a same-image edit through the configured editor model, using the
  seeded template whose text protects the neck and forbids adding clothing.
- **FR21-017:** The step is skippable for characters whose front is already correct.

### E. Crop step

- **FR21-018:** Crop normalises framing headroom and subject scale between views, using ImageSharp
  (already a Web dependency) — no new package and no ComfyUI round-trip.
- **FR21-019:** Crop parameters are persisted and previewable before commit.

### F. Enhance step

- **FR21-020:** Enhance runs a 4× upscale followed by a Lanczos resize to the configured target long
  edge, via local ComfyUI (`UpscaleModelLoader` + `ImageUpscaleWithModel`) using the configured
  upscaler model name. It is not applied automatically.
- **FR21-021:** The UI must state plainly that enhancement re-synthesises facial detail and therefore
  must not be applied to the chosen likeness front before likeness is judged.

### G. Angle step

- **FR21-022:** Angles are produced in the order 3/4 left → 3/4 right → profile left → profile right.
- **FR21-023:** Every angle render is **gated on measured yaw sign**, never on iris/interocular
  metrics (which are invalid under yaw) and never on a visual impression.
- **FR21-024:** The required convention is fixed and asserted: `ThreeQuarterLeft` / `ProfileLeft`
  face **image-left**; `ThreeQuarterRight` / `ProfileRight` face image-right.
- **FR21-025:** When a render's yaw sign violates the convention, the pipeline applies the configured
  remedy — by default, **mirroring the reliable left-facing render** to produce the right-side view —
  and records that the view was mirror-derived. The remedy is a persisted setting, not a branch
  chosen silently.
- **FR21-026:** A validation failure that cannot be remedied by the configured settings blocks that
  view and surfaces the reason; it is never silently accepted.

### H. Promotion

- **FR21-027:** Promotion writes the accepted view set into a draft identity pack, tagging each view
  with its correct `SceneImageReferenceFaceView`. **Promotion must not hardcode `Front`** — the
  current `PromoteAcceptedCharacterFaceAsync` does, and must be extended to carry the view.
- **FR21-028:** Promotion refuses a view whose validate gate failed, whose yaw is wrong, or whose
  quality rating is below the configured bar, naming the offending view.
- **FR21-029:** Promotion reuses the existing identity-pack mechanics (`CreateDraftPackAsync`,
  `UploadAssetAsync`, `SetProvenanceAsync`) and leaves the candidate rows untouched.
- **FR21-030:** After promotion the pack is visible at `/characters/identity` for approval, and the
  build records which pack it produced.

### I. Integration constraints

- **FR21-031:** The studio uses the existing `ProducedImage` reference-candidate pipeline
  (`ReferenceBootstrapService` + Review Deck + `CandidateGrid`). It **must not** introduce a third
  candidate mechanism, and must not extend the parallel `SceneAsset.Candidate*` fields for this
  workflow.
- **FR21-032:** The studio surface lives in Asset Manager, per B-111
  (`studio-reference-strategy-plan.md`: "/reference-bootstrap | **Retire** → into Asset Manager").
  The existing `ReferenceBootstrapPanel` becomes the studio's entry point rather than gaining a
  sibling.
- **FR21-033:** Every dispatch uses configured models. Missing or unqualified configuration fails
  fast naming the missing item. No fallback branch, no guessed value.
- **FR21-034:** Existing behaviour of `ReferenceBootstrapPanel` (describe → generate → curate →
  promote for face/body/wardrobe/location) keeps working unchanged.
- **FR21-035:** The pipeline is **target-extensible**. A target kind (character face, character body,
  wardrobe, location) selects the step set, the prompt-template key namespace, and the validation
  rules — it does not fork the pipeline. Adding a target kind must require only new seeded templates
  and new validation configuration, never a second copy of the build/step machinery. (This is what
  lets B-122 Phase 0 be a target kind rather than a parallel studio — see
  `specs/Planning/identity-lora-program-map.md` §4.1.)
- **FR21-036:** The face step produces a **view set**, not just five canonical slots. In addition to
  `Front`/`ThreeQuarterLeft`/`ThreeQuarterRight`/`ProfileLeft`/`ProfileRight` it supports **extended
  views** — looking up/down at several pitches and intermediate yaws between the canonical angles.
  Each view carries the `ViewDescriptorJson` model owned by **B-124 (stage 1)** — see
  `specs/Planning/identity-and-reference-model.md` §2. The canonical slots remain the multi-angle
  compiler's minimum contract and are unchanged. B-121 **consumes** this model; it does not define it.

## Acceptance Scenarios

1. Creating a build for a character with no pack, generating a front from a description, and running
   every step through to promotion produces a draft identity pack with five correctly angle-tagged
   views.
2. Uploading an existing image as the front enters the identical pipeline from the validate step, and
   reaches the same outcome.
3. Editing the garment-removal template text changes the actual prompt dispatched on the next run, and
   the artifact records the edited text.
4. **Reset to default** on an edited template restores the seeded text, and the restored text is
   byte-identical to the seeded value.
5. Deleting a required global template row makes the step fail fast naming the key — no prompt is
   invented and no default is silently substituted.
6. A front whose eye measurement fails the gate cannot advance to garment removal; recording a manual
   override allows it to proceed, and the override is persisted with its author.
7. Requesting all four angles yields `ThreeQuarterLeft`/`ProfileLeft` facing image-left and
   `ThreeQuarterRight`/`ProfileRight` facing image-right, verified by measured yaw sign, with any
   mirror-derived view recorded as such.
8. Promotion refuses a view that failed validation or yaw, naming it, and promotes successfully once
   remedied.
9. The promoted pack exposes each view under its correct `SceneImageReferenceFaceView` (not four
   views all tagged `Front`).
10. Character-scoped template overrides affect only that character; the global template is unchanged.
11. The pre-existing reference-bootstrap flows for face, body, wardrobe, and location still pass their
    existing tests and behave unchanged.

## Exit Gate

- All eleven scenarios verified in the running application, with build/artifact ids recorded.
- Front-to-promotion completed for a real character (Sam Winchester) with no hand-run scripts and no
  direct DB writes.
- Prompt templates demonstrably editable, resettable, and fail-fast on removal.
- Focused tests + affected project builds green; Razor diagnostics clean on every touched component.
- Grep proof: no hardcoded prompt constant remains in any pipeline step path; no new reference to
  `CharacterLoraDataset`/`CharacterLoraDatasetMember`.
