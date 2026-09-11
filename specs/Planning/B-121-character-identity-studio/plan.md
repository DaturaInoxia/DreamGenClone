# B-121 Implementation Plan — Character Identity Studio

**Status:** Plan only — handoff document. No code written under this item.
**Spec:** [spec.md](spec.md) · **Tasks:** [tasks.md](tasks.md) · **UI:** [ui-contract.md](ui-contract.md)

## Program context

This item is the **second stage of the identity → body → LoRA spine** (after B-124's foundation —
the reference model and Asset Manager shell it consumes) and is the producer of most of the shared
machinery the later items consume. Read `specs/Planning/identity-lora-program-map.md`
before changing its scope.

- **Feeds:** B-122 Phase 0 (body-complete pack) and B-123 (LoRA image studio).
- **Delivers downstream:** the resumable step pipeline, the editable prompt-template store, the
  eye/face-landmark validation capability (with manual override), reference quality gating, the
  same-image identity-edit primitive, and **view-tagged** promotion into identity packs.
- **Bounded by the map:** B-121 **owns** face/eye measurement, reference quality gating, the edit
  primitive and the template store. B-122/B-123 **consume** them and must not build second
  implementations (see map §3 for the ownership table and the corrections it applies to B-122/B-123).
- **Hard design consequence:** the pipeline must stay **target-extensible** (FR21-035). B-122
  Phase 0 must be expressible as a new target kind with new seeded templates — if this item hardcodes
  the five face views, B-122 is forced to fork the studio and the two will drift.

## Guiding constraint

Extend the reference-bootstrap pipeline that already exists; do not rebuild it. The genuinely new
things are (1) a persisted, editable prompt-template store, (2) four new pipeline steps around a
front image, and (3) a yaw/eye gated promotion that carries the correct view tag. Everything else —
generation, curation, review deck, promotion targets, storage — is already implemented and must be
reused as-is.

## Verified current state (read this before planning any work)

The item's original design docs (B-108 `tasks.md`) are **obsolete**. B-108 was absorbed into
B-111 Phase 2 (`superseded-map.md`: "B-108 — Reference Bootstrap Studio | Designed | **P2**"), and
most of P2 is **already implemented**. Verified by reading the code:

| Exists | Path |
|---|---|
| Batch + location profile/reference domain records | `DreamGenClone.Domain/RolePlay/ReferenceBootstrapModels.cs` |
| Batch + location persistence (SQL) | `DreamGenClone.Infrastructure/RolePlay/ReferenceBootstrapRepository.cs` (table `ReferenceBootstrapBatches`) |
| Repository contract | `DreamGenClone.Application/RolePlay/IReferenceBootstrapRepository.cs` |
| Workflow service | `DreamGenClone.Web/Application/RolePlay/ReferenceBootstrapService.cs` |
| Service contract | `DreamGenClone.Web/Application/RolePlay/IReferenceBootstrapService.cs` |
| Candidate generation (durable jobs) | `ReferenceBootstrapService.GenerateCandidatesAsync` → `BackgroundJobTypes.ProducedImageGeneration` |
| Curation | `ReferenceBootstrapService.SetCandidateDecisionAsync` (`Accepted`/`Shortlisted`/`Rejected` + notes) |
| Promotion (face, body, wardrobe, location) | `PromoteAccepted{CharacterFace,CharacterBody,Wardrobe,Location}Async` |
| UI panel | `DreamGenClone.Web/Components/Assets/ReferenceBootstrapPanel.razor`, mounted in `AssetStudio.razor` |
| Candidate workspace | `CandidateGrid` + Review Deck (`/review-deck/{batchId}`) |
| Tests | `DreamGenClone.Tests/RolePlay/ReferenceBootstrapRepositoryTests.cs` |
| Additive `SceneAsset` candidate fields + batch queries | `SceneAssetModels.cs`, `SceneAssetRepository.cs`, `SceneAssetService.cs` |
| Quality rating | `ReferenceImageQualityAnalyzer.cs` (ImageSharp, Lanczos3) |

**Therefore these B-108 tasks are already done and must NOT be re-implemented:** B108-001…B108-010,
B108-015…B108-023 (modulo the Asset-Manager migration tracked in
`specs/001-final-writing-instruction/debug/020-b111-reference-bootstrap-not-in-asset-manager.md`,
which records the move as *pending implementation* — confirm its true state before starting).

**Does not exist anywhere in the codebase:**

- Any prompt-template store. Prompts are hardcoded constants (e.g. `AngleEdits`) or inline strings.
- Any face-landmark / iris measurement capability in .NET.
- Any crop step.
- Any upscaling step in the app (only the *test runner* `identity-two-character/runners/run_upscale.py`).
- Any build/step pipeline record tying a front image to a sequence of curated steps.
- Any view tag on promotion — `PromoteAcceptedCharacterFaceAsync` hardcodes
  `SceneImageReferenceFaceView.Front`.

**Known duplication risk (do not worsen):** two candidate mechanisms coexist —
(a) `ProducedImage`-based (`ReferenceBootstrapService`, Review Deck), and (b) `SceneAsset.Candidate*`
fields (used by `PromptAssetCreator.razor` / `ImageEditWorkbench.razor`, routed to
`/review-deck/asset/{batchId}`). This item uses **(a)** only (FR21-031). Reconciling (b) is out of
scope and should be recorded, not silently merged.

## Reuse map (verified)

| Concern | Reuse | Notes |
|---|---|---|
| Batch creation + candidate jobs | `ReferenceBootstrapService.CreateBatchAsync` / `GenerateCandidatesAsync` | Batch requires `Description`, `TargetAssetType`, `RequestedCandidateCount` |
| Candidate listing | `ListCandidatesAsync(batchId)` → `ProducedImage` | `Kind == ProducedImageKind.ReferenceCandidate` |
| Decision + notes | `SetCandidateDecisionAsync` | Statuses: `Accepted`/`Shortlisted`/`Rejected` |
| Promotion | `PromoteAccepted*Async` | **Face path hardcodes `Front` view — must be extended** |
| Frozen text block | `FrozenTextBlockEditor` component | Promotion *requires* a non-empty `FrozenTextBlock` — fail-fast already enforced |
| Same-image edit | `IImageEditingClient.EditAsync`; `IEditorModelResolver`/`_editorModelResolver` pattern from `SceneAssetProfilePackJobHandler` | Confirm the enqueue path via `SceneAssetImageEditCompilationService` + `SceneAssetImageEditJobPayloads` before writing |
| Prompt compilation for generation | `SceneAssetPromptCompiler.Compile(description, assetType, model)` | Used by `SceneAssetProfilePackJobHandler` |
| Identity pack mechanics | `ICharacterImageIdentityService.CreateDraftPackAsync` / `UploadAssetAsync` / `SetProvenanceAsync` | Already used by `ReferenceBootstrapService` |
| Image manipulation | `SixLabors.ImageSharp` 3.1.11 (**already a Web dependency**) | Lanczos3 already used by the analyzer |
| Quality rating | `ReferenceImageQualityAnalyzer` | Non-blocking today; this item gates on it |
| Eye measurement | `tools/eye-validation/measure_iris.py` | Canonical; must be invoked, not reimplemented |

## Design decisions

### D1 — Prompt templates are persisted rows, and the seed *is* the default

New store `ImageWorkflowPromptTemplate`: `Key`, `Scope` (`Global`/`Character`),
`CharacterProfileId`, `WorkflowStep`, `Body`, `SeedBody`, `UpdatedUtc`. Seeded idempotently at
migration with the prompts validated in `dean-front17-shirt-removal.md`. Resolution is
character-override → global → **fail fast naming the key**. `SeedBody` is written once and never
editable; "Reset to default" copies it back.

*Why this satisfies the repo's no-fallback rule:* the working default is **configuration data in the
database**, not a `?? "default prompt"` branch in code. Removing the row produces an explicit failure,
which is the required behaviour (FR21-007/FR21-008).

*Naming:* `TemplateDefinition` already exists (`DreamGenClone.Domain/Templates/`) for *character seed*
templates (Persona/Character/Location/…). This store is a different concern and must not reuse that
name or table.

### D2 — Behaviour controls are persisted settings, not constants

A `ReferenceWorkflowSettings` row (global, optional per-character override) holds: editor model id,
upscaler model name, target long edge, crop parameters, mirror/derive flags, eye-tool Python path,
eye-gate threshold (`|irisDy%| ≤ x`, seeded 1.5), quality-gate threshold (seeded 250). Only **model
ids** are stored — sampling parameters stay in Model Manager (FR21-011). Every field is resolved
from persisted configuration; missing values fail fast by key. The seeded values are migration data,
never runtime fallback constants.

### D3 — The existing `AngleEdits` constants are deleted, not copied

`SceneAssetProfilePackJobHandler.AngleEdits` is the source of both defects this item exists to fix
(the word "clothing" in the preserve-list → invents clothing on a shirtless front; anatomical
"to their left/right" wording → direction ignored). Its **corrected text becomes the seed** for the
angle templates, and the constants are removed so the broken text cannot be reused. B-111 `P2-tasks.md`
unit P2-U4 references "reuse `AngleEdits` pattern" — that reference is superseded by this decision and
must be updated when the change lands.

### D4 — Direction correctness is enforced by measurement + mirror, not by wording

Qwen's rotation-direction compliance is unreliable — prompting produced four same-direction renders,
and image-space wording fixed it for some sources but not all. Therefore:
1. Render the left-facing view (reliable).
2. **Measure the yaw sign** (nose offset from face centre via landmarks).
3. If the convention is violated, apply the configured remedy — default: mirror the left render into
   the right slot — and record `MirrorDerived = true` on the artifact.
4. If the sign is still wrong and the remedy is disabled, block the view with a reason.

Never gate an angle on `irisDy%` or interocular distance: iris metrics are invalid under yaw
(3/4 = −5…−8 %, profiles ≈ −36 %) and interocular distance shrinks naturally with yaw. MediaPipe
returns **no face mesh on full profiles**, so profile orientation is a manual visual confirmation.

### D5 — Eye validation is a subprocess call to the canonical tool

`measure_iris.py` is invoked with the repo venv Python and its output parsed; raw stdout is persisted.
A manual visual gate always exists and is **required** when the tool reports no face mesh. Rejected
alternative: porting to .NET — the repo explicitly forbids re-deriving an eye checker from weaker
methods, and a port would be a second implementation of the same validator.

### D6 — Crop in-process, enhance via local ComfyUI

Crop uses ImageSharp (already a dependency, no round-trip, deterministic). Enhance uses local ComfyUI
`UpscaleModelLoader` + `ImageUpscaleWithModel` with the configured upscaler, then Lanczos to the
target long edge — the path the existing test runner already proves. Bundling the realesrgan binary
was rejected: it exists today only as a git-ignored artifact under `artifacts/tmp/`.

### D7 — Promotion carries the view; it must stop hardcoding `Front`

The current face promotion path uploads one asset tagged `Front`. The studio must pass the intended
`SceneImageReferenceFaceView` per canonical artifact and `ViewDescriptorJson` for every canonical or
extended artifact (FR21-027). B-121 creates an explicitly `FaceOnly` pack under B-124's `PackScope`
contract. This is a change to existing behaviour and must keep the legacy caller working: the
current panel promotes a single face candidate as `Front` into an explicit `FaceOnly` pack.

### D8 — Build state is a first-class resumable record

`CharacterIdentityBuild` (target character, batch id, current step, status) plus per-step rows
(`Step`, `Status`, `InputArtifactId`, `OutputArtifactId`, `ResolvedPromptText`, `ResolvedModelId`,
`FailureReason`, `MirrorDerived`, `ManualOverride*`). Rationale: the manual session lost track of
which pass produced which file, and a 4-angle run takes ~11 minutes — a failure at the last angle must
not require redoing the first three.

## Implementation phases

**Phase A — Prompt templates and settings persistence**
New `ImageWorkflowPromptTemplate` + `ReferenceWorkflowSettings` (domain records, SQLite tables,
repositories), the idempotent seed migration carrying the validated prompt texts, and the
resolve-or-fail-fast service. *Exit:* a seeded template resolves globally, a character override wins,
removing a row fails fast with the key named.

**Phase B — Build pipeline record**
`CharacterIdentityBuild` + per-step rows, repository, and the step-status state machine.
*Exit:* a build persists, advances step by step, resumes after interruption, and each step is
independently re-runnable.

**Phase C — Front acquisition**
Generation path (via `SceneAssetPromptCompiler` + the generation client, reusing the profile-pack
handler's front logic) and upload path (`SceneAssetService.CreateFromUploadAsync`); both converge on
one artifact type. *Exit:* acceptance scenarios 1 and 2 reach the validate step by both routes.

**Phase D — Validate step**
Subprocess invocation of the canonical tool, parsing, persistence, manual override UI, and the
advance-blocking gate. *Exit:* acceptance scenario 6.

**Phase E — Garment removal, crop, enhance**
Three steps reusing the edit client, ImageSharp, and local ComfyUI respectively, each reading its
prompt/settings from the store. *Exit:* acceptance scenario 3; each step re-runnable and non-destructive.

**Phase F — Angle step**
Ordered 3/4-L → 3/4-R → profile-L → profile-R, yaw measurement, convention assertion, configure-driven
mirror remedy, per-view gate. *Exit:* acceptance scenario 7.

**Phase G — Promotion with view tagging**
Extend face promotion to carry the canonical slot plus descriptor; gate on validate/yaw/quality;
write the five canonical and any accepted extended views into an explicit `FaceOnly` draft pack.
*Exit:* acceptance scenarios 8, 9, 11 — and the existing reference-bootstrap tests stay green.

**Phase H — UI**
The studio surface per [ui-contract.md](ui-contract.md), including the prompt editor with scope
switching and Reset to default, inside Asset Manager. *Exit:* acceptance scenarios 4, 5, 10; Razor
diagnostics clean.

**Phase I — Validation**
Focused tests, affected project builds, all eleven scenarios live, the grep proofs from the exit gate.

## Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | The implementing agent re-implements already-built B-108 work from the stale tasks file | This plan's "Verified current state" is authoritative; B-108 `tasks.md` gets a superseded banner |
| R2 | A third candidate pipeline is introduced | FR21-031; Phase G extends the existing service rather than adding a path |
| R3 | Seed prompts get re-embedded in code as a "tidy" refactor, re-violating the no-fallback rule | FR21-008 states the rule explicitly; the exit-gate grep checks for it |
| R4 | Yaw gating is implemented on iris/interocular metrics (the metrics that misled this session) | FR21-023 names the forbidden metrics; D4 states the reason |
| R5 | Promotion view-tag change breaks the legacy single-face promote path | D7 requires the legacy caller to keep working; Phase G asserts it |
| R6 | Subprocess dependency on the repo venv is assumed present | Fail fast naming the interpreter path; the venv path is configurable (D2), not hardcoded |
| R7 | Razor regressions extending `AssetStudio.razor` / `ReferenceBootstrapPanel.razor` | Follow `.github/instructions/razor-editing.instructions.md`: full-context read, micro-steps, diagnostics after each edit |
| R8 | Enhancement is applied before likeness is judged, degrading the chosen face | FR21-021 requires the warning in the UI; D6 documents the reason (`front_16` was rejected for exactly this) |

## Points the implementing agent must VERIFY, not assume

1. Whether the Asset-Manager migration of `ReferenceBootstrapPanel` (debug doc 020) is complete or
   still pending — it changes whether Phase H is a move or an extension.
2. The exact enqueue path for same-image edits (`SceneAssetImageEditCompilationService` /
   `SceneAssetImageEditJobPayloads`) and which lane/retry settings apply.
3. Whether `IReferenceBootstrapService` needs extending for per-view promotion, or whether a new
   service should own the build pipeline while calling it.
4. Which venv Python path is available at runtime and how the app should locate it.
5. The current state of `AssetStudioUiContractTests` (it asserts `EnqueueProfilePackAsync` is absent).

## Definition of done

All eleven acceptance scenarios pass live; a full Sam Winchester build completes in-app with no
hand-run scripts and no DB writes; templates are editable/resettable/fail-fast; the pre-existing
reference-bootstrap behaviour and tests are unchanged; focused tests and affected builds are green;
Razor diagnostics are clean; and the exit-gate greps show no hardcoded pipeline prompt and no new LoRA
coupling.
