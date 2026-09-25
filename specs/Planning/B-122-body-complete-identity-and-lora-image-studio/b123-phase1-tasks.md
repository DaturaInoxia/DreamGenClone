# B-123 Phase 1 — Coverage plan, prompt/caption templates, plan generator, two-pane tab, review deck, gates

**Execution rule:** in order. Each task ends with the affected focused tests green and evidence recorded.
Authoritative design: `lora-dataset-design.md` (this folder). Evidence base: `pose-pack-survey.md`,
`specs/image-generator-tests/flux-character-lora/DATASET-CAPTURE-LIST.md`,
`specs/Planning/identity-lora-program-map.md`.
**Not in Phase 1:** the render/edit pipeline and pose conditioning (Phase 2, and the pose plumbing is a
separate session). Renders/prompts are *composed and shown* here; nothing is dispatched.

**Repo rules that bind every task:** no fallback branches; no hardcoded runtime defaults; missing
required configuration fails fast naming the item; every prompt is a seeded, editable row (never a code
string); no `git restore`; all affected tests green before a task is checked.

---

## The order (operator-chosen)

`typed schemas` → `seeded prompt + caption templates` → `plan generator` → `two-pane tab` → `review deck` → `gates`.

---

## Phase 0 — verification carried over (no code)

- [x] **B123-000a** *Verified 2026-09-25.* What Phase 1 replaces is the hand-written JSON: `LoraDatasetGeneration.razor`
  asks the operator to type `Coverage plan JSON` and `Curation policy JSON` into textareas, plus
  `Candidate plan JSON`.
- [x] **B123-000b** *Verified.* `CharacterLoraDataset.CoveragePlanJson` / `CurationPolicyJson` are the persistence
  targets (`CharacterLoraModels.cs`, lines 86–87). Phase 1 makes those two columns typed; it adds no dataset table.
- [x] **B123-000c** *Verified.* The reference vocabulary to project onto is `SceneImageReferenceFaceView`,
  `SceneImageReferenceBodyView` (+`Back`), `SceneImageReferenceBodyState` and `ReferenceViewDescriptor`.

---

## P1-01 — Typed schemas (`CoveragePlan`, `CoverageRecord`, `CurationPolicy`, `CurationFindings`) — **DONE 2026-09-25**

*Files:* `DreamGenClone.Domain/RolePlay/CharacterLoraCoverageModels.cs`,
`CharacterLoraCoverageWorkflowKeys.cs`, `DreamGenClone.Tests/RolePlay/CharacterLoraCoverageSchemaTests.cs`
**Evidence:** 28 tests green. `CoverageRecord` carries `FaceVisible` explicitly (not derived from the angle) and
validates the face reference against it; `CurationPolicy` uses `required` members so a policy missing a value
fails to deserialize by name; findings gained a `NotScorable` severity so "could not measure" is neither a pass
nor a failure, plus a single `IsPassing` definition.

## P1-02 — Seeded prompt + caption templates — **DONE 2026-09-25**

*Files:* `DreamGenClone.Infrastructure/RolePlay/ImageWorkflowRepository.cs` (seed block),
`DreamGenClone.Domain/RolePlay/CharacterLoraCoverageWorkflowKeys.cs`,
`DreamGenClone.Tests/RolePlay/CharacterLoraCellTemplateSeedTests.cs`,
`DreamGenClone.DbQuery/queries/b123-lora-cell-templates.sql`
**Evidence:** 26 tests green. 12 render templates + caption + edit + references + 42 vocabulary rows
seeded and resolvable. The caption is proved to carry no invariant term; the render templates are proved to be
slot-driven and to name no character; re-seeding neither duplicates a row nor overwrites an edited body.
**Corrected 2026-09-25 — there is no negative-prompt row, by operator instruction.** The first cut seeded
`lora.cell.negative` with a guard list. That contradicts the prompt research the pipeline already follows: SDXL /
Juggernaut / BigLust resolve to an **empty** negative by model-author research, Pony takes only the short guard set
its own compiler authors, and FLUX has no negative field at all — so a per-cell negative would fight three of the
four documents. It was also dead: a cell render sets no compiler id, so the handler compiles the prompt and authors
no negative itself, meaning the value never reached the model. The key is deleted from
`LoraCellWorkflowKeys`, the seed row is deleted, `RenderCellAsync` passes none, and a contract test
(`CellRender_CarriesNoNegativePrompt`) forbids any of the three returning. The stale row was removed from the dev DB
(`artifacts/tmp/dbquery/queries/delete_lora_cell_negative.sql`, 1 row).
**Read-back:** the dev DB has none of these rows yet — the running app predates the change. They are inserted on
app startup, so a restart makes them readable with
`helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/b123-lora-cell-templates.sql`.

## P1-03 — Coverage plan generator — **DONE 2026-09-25**

*Files:* `DreamGenClone.Web/Application/RolePlay/CharacterLoraCoveragePlanGenerator.cs` (+ interface),
`LoraCellPromptComposer.cs`, `DreamGenClone.Infrastructure/RolePlay/CharacterLoraRepository.cs`
(`CharacterLoraCurationPolicies` table + seed + resolve/save/reset),
`DreamGenClone.Application/RolePlay/ICharacterLoraRepository.cs`,
`DreamGenClone.Tests/RolePlay/CharacterLoraCoveragePlanGeneratorTests.cs`,
`LoraCellPromptComposerTests.cs`
**Evidence:** 27 generator tests + 16 composer tests green. The matrix lands exactly (30 core / 6 variation, per
angle 6/10/12/2, per distance 8/11/11), seeds are unique inside the configured range, wardrobe is 18/18 with both
states in every angle group, every diversity minimum is met, the holdout is 8 cells (variation + behind), two runs
produce byte-identical plans, and a face-only pack / empty trigger token / missing policy row / impossible minimum
all fail fast. A source contract proves the generator exposes exactly one method and no batch primitive.
**Notable decisions taken while building:**
- The pose class advances on a **per angle-and-distance cursor**, so two frames of the same view never share a
  stance; a single global cycle produced repeats at series boundaries (caught by test, fixed).
- A `lora.vocabulary.facing.*` axis was added, derived from `FaceVisible` + yaw by one function, rather than
  duplicating each render template per side.
- `DeleteGlobalPolicyRow` in the test world proves the resolver fails fast naming `global`.

## P1-04 — Two-pane LoRA images tab — **DONE 2026-09-25**

*Files:* `DreamGenClone.Web/Components/Editing/LoraDatasetWorkspace.razor` (new),
`DreamGenClone.Web/Components/Pages/CharacterStudio.razor`,
`DreamGenClone.Web/Program.cs` (generator registration),
`DreamGenClone.Tests/RolePlay/LoraDatasetWorkspaceContractTests.cs`
**Evidence:** 19 contract tests green. Two panes (`col-12 col-lg-4` / `col-12 col-lg-8`) with a collapse control;
readiness header
(cells accepted / captions / clothed-nude / backgrounds / pose classes / seeds) plus every `DescribeGaps` gap;
dataset creation through the generator with no JSON textarea; the owner resolved and **every identity read through
the resolved key**; no batch action and no render loop; the component carries no prompt text of its own.
**Corrected 2026-09-25:** the panes were first written `col-xl-5` / `col-xl-7`. Bootstrap only applies `col-xl-*` at
≥1200px, so below that the halves stacked and the operator reported "the cells is supposed to be a left pane that is
collapsible". Now `col-lg-*`, collapsing to `col-lg-auto`, and a contract test forbids `col-xl-5` returning.
**Deferred to Phase 2 by design**: source, pose frame, gates. The render half of the cell workspace landed under
P1-05.

## P1-05 — Review deck

**Render half done 2026-09-25** (operator report: "the cells is supposed to be a left pane that is
collapsible, there is not model picker or Render Image actions").
- [x] **Layout.** The left pane was `col-xl-*`, which only becomes a pane above 1200px — below that the two halves
  stacked, so the cell list was not a left pane at all. Now `col-lg-4` / `col-lg-8`, collapsing to `col-lg-auto`.
  Pinned by a contract test that also forbids `col-xl-5` returning.
- [x] **Model picker.** `ReferenceWorkflowSettings.LoraCellModelId` (character override → global), seeded column +
  ALTER migration, read/written through `ICharacterLoraCellService`, options from
  `IModelResolutionService.ListSceneImageModelsAsync`. Saving it reads the resolved row and writes back one field
  changed, so choosing a LoRA model cannot reset the front model or a gate threshold (pinned by test).
- [x] **Render image.** `ICharacterLoraCellService.RenderCellAsync` → `ISceneAssetService.AddGeneratedImageAsync`
  with the cell's derived candidate batch id: **one image per action**, an ordinary queued generation job.
  **Corrected 2026-09-25 (pre-flight re-read of the code):** an attempt is a `SceneAssetImage` inside the dataset's
  **one container asset** (`AddGeneratedImageAsync(container.Id, …)`), NOT one Scene Asset per attempt. The dataset
  has a single `ContainerAssetId` created on the first shot; each cell contributes images to it under its own
  candidate batch. This is why member registration needs an explicit promotion step (see P1-05a). The button is disabled with
  the reason shown (no body card / no model) rather than dispatching a substitute.
  **Corrected 2026-09-25 (operator: "i needs to use the indentity")** — the first cut used
  `CreateFromPromptAsync`, which has **no conditioning parameters at all**, so a "cell of this character" was
  rendered as a stranger. It now goes through `AddGeneratedImageAsync` with
  `options.Identity = new SceneAssetIdentityConditioning(packId, faceAssetId)`, resolved from the cell's own rule
  by the pure static `ResolveIdentityConditioning(record, packId, packAssets)`: face-less (behind) cells → null, a
  missing angle-matched approved reference → fails fast naming the pack, the slot and the Faces tab. The workspace
  shows which reference the cell will use before it shoots. Contract tests assert the identity-aware call, the
  `Identity = conditioning` assignment, and that `CreateFromPromptAsync` cannot return to this file.
  Needs a container asset: `CharacterLoraDataset.ContainerAssetId` + `SetDatasetContainerAsync` (sticky, Draft-only).
- [x] **Attempt deck.** `ListCellAttemptsAsync` returns only this cell's attempts (newest first) with preview,
  open-full-size and discard; `DiscardAttemptAsync` deletes the whole attempt asset. Refresh re-reads, because a
  render is queued and appears as Pending until the worker lands it. The deck is the shared
  **`CandidateGrid`** + `CandidateGridItem` component, not a second thumbnail grid: two decks in one app drift apart
  (operator: "are you going to create a new forms, is there nor common compoenet that has it already?").
- [x] Tests: `CharacterLoraCellServiceTests` (21) + the workspace contract suite (19) — a cell only ever sees its
  own batch, a render is refused on a missing prompt/model/aspect before anything is queued, the settings
  round-trip leaves other values alone, the identity decision is correct for every face/angle case, and the service
  exposes exactly one singular render method.

- [ ] **P1-05a (remaining)** Accept into the dataset: the per-cell surface that promotes an accepted attempt to a
  dataset member. It needs the image approved for `CharacterLoraTraining`, and that approval carries required
  consent / licence / content-policy facts. **Do not write a new form and do not invent those facts** — the shared
  **`Components/Assets/ProductionApprovalForm.razor`** already collects provenance, consent, licence + licence
  label, **use scope (it includes `CharacterLoraTraining`)**, content-policy key and compatibility, and approves the
  exact image. Sourcing the values from the identity pack the cell was projected from is the work; a button that
  cannot finish is not.
  **One structural catch to resolve first:** `CharacterLoraRepository.RequireApprovedAssetAsync` reads the
  **`SceneAssets`** row (`ApproveForProductionAsync(assetId, …)`), whereas `ProductionApprovalForm` approves the
  **`SceneAssetImage`**. Those are different rows, and the asset-level approval currently has **no UI call site
  anywhere**. Either the form approves the asset as well (one form, one set of facts — preferred), or the membership
  check moves to the image. Decide before writing code.
  **Resolution designed 2026-09-25 — see "Pre-flight for the next step" below for the evidence behind each
  constraint.** Awaiting the operator's go-ahead plus the two open decisions.
- [ ] **P1-05b** Register the member through `ICharacterLoraRepository` (`SceneAssetId` + version + SHA-256,
  caption, `CoverageJson`, `CurationFindingsJson`, `ReviewedBy/Utc`) — no direct DB write, no second path.
- [ ] **P1-05c** Tests: a non-accepted cell cannot register; accepting twice is refused; the deck lists both
  renders and edits.

## P1-06 — Gates

- [ ] **P1-06a** Evaluate the configured gates against a cell's attempts and persist the result as a
  `CurationFindings`: identity (delegated to the existing measurement capability — not re-implemented),
  direction/sharpness for frames that carry them, **similarity/near-duplicate** naming the colliding
  cell, **body-invariant continuity** inside the plan, and adherence for pose/expression cells.
- [ ] **P1-06b** One favourable metric is never a pass: a blocking finding blocks accept. A manual
  override requires a reason + author and is persisted.
- [ ] **P1-06c** Tests: green-field gates on a clean plan; a near-duplicate pair blocks with the partner
  named; insufficient data returns a not-scorable finding rather than a pass; every limit is read from
  the policy (assert by changing the policy and seeing the verdict change); a single favourable metric is
  not a pass.

*Files:* `DreamGenClone.Web/Application/RolePlay/CharacterLoraCurationService.cs` (+ interface),
`LoraDatasetWorkspace.razor`, tests

### P1-06 plan for go-ahead (prepared 2026-09-25)

**The measurement already exists as an approved tool.** `tools/consistency-scoring/` (git-tracked, pinned
`requirements.txt`, isolated uv venv because the app venv is 3.14) exposes exactly the metrics the gates need, as
JSON from six subcommands. Delegation map — nothing is re-implemented:

| Gate (P1-06a) | Existing capability | Note |
|---|---|---|
| identity | `score.py identity --reference --render` | facenet-pytorch MTCNN + InceptionResnetV1 `vggface2` cosine. **No face → `similarity=null`**, which is precisely the `NotScorable` case the schema already models |
| similarity / near-duplicate (naming the colliding cell) | `score.py subject --image-a --image-b` | DINOv2 authoritative, CLIP-I secondary. Compare the attempt against the plan's other cells' accepted attempts and name the partner |
| adherence for pose/expression cells | `score.py adherence --render --prompt` | CLIP-T |
| presence | `score.py presence --render --expected` | MTCNN face count vs the cell's expected count |
| direction | `ICharacterIdentityMeasurementService.MeasureFileAsync` | The approved eye tool (`tools/eye-validation/measure_iris.py`), already wrapped for the identity pipeline — the same one `RenderCellAsync`'s identity path uses |
| **sharpness** | **no existing metric** | The one gate with no tool. Decide: add it to the eye-validation tool (a registered-tool change), or drop it from the policy |
| **body-invariant continuity** | `subject` / `identity` across cells | The least-defined gate: needs a stated definition before it can be asserted |

Shape: a `CharacterLoraCurationService` that (a) resolves the policy for the character through
`ResolveCurationPolicyAsync` — never a code default, (b) runs the tools through a runner abstraction (mirroring
`ICharacterIdentityMeasurementRunner`, so tests need no Python), (c) parses their JSON into the typed
`CurationFindings`, (d) returns a verdict where **one favourable metric is never a pass**. The scorer interpreter
path must be **configured and UI-backed** (like `EyeToolPythonPath`), not hardcoded — the no-fallback rule
applies to a tool path exactly as it does to a threshold.

Thresholds: every limit comes from `CurationPolicy` (already `required`-member typed, so a missing one fails to
deserialize by name). Any threshold the policy lacks is added to the seeded policy row — never to code.

Blast radius: a new service + its interface + a runner abstraction + policy seed rows + one settings column and
its UI + `LoraDatasetWorkspace.razor` + focused tests. **No render-path change, no dataset schema change.**

Two things to settle before coding: **sharpness** (extend the approved tool, or drop the gate) and the exact
definition of **body-invariant continuity**. Both are policy facts, so both belong in `CurationPolicy` either way.

---

## Handover — state at end of session 2026-09-25 (read this first)

**Where the work actually is.** P1-01 → P1-04 are done and green. The **render half of P1-05** is done and green:
a cell can be shot, the shot uses the character's identity, and its attempts land in a deck. **P1-05a/b/c (accept
into the dataset) and all of P1-06 (gates) are NOT written.** Pose plumbing is explicitly **not** this workstream.

**Uncommitted files** (all of this is one uncommitted change set):

| File | State |
|---|---|
| `DreamGenClone.Domain/RolePlay/CharacterLoraCoverageModels.cs` | new — typed plan / policy / findings |
| `DreamGenClone.Domain/RolePlay/CharacterLoraCoverageWorkflowKeys.cs` | new — every `lora.*` key. **No negative key** |
| `DreamGenClone.Domain/RolePlay/CharacterLoraModels.cs` | + `CharacterLoraDataset.ContainerAssetId` (payload field, no schema change) |
| `DreamGenClone.Infrastructure/RolePlay/ImageWorkflowRepository.cs` | seeded 12 render + caption + edit + references + 42 vocabulary rows; + `ReferenceWorkflowSettings.LoraCellModelId` (column, ALTER, upsert SET, `ReadSettings` index **19**) |
| `DreamGenClone.Infrastructure/RolePlay/CharacterLoraRepository.cs` | `CharacterLoraCurationPolicies` table + seed; `Resolve/Save/ResetCurationPolicyAsync`; `SetDatasetContainerAsync` (sticky, Draft-only) |
| `DreamGenClone.Application/RolePlay/ICharacterLoraRepository.cs` | those three new members |
| `DreamGenClone.Web/Application/RolePlay/CharacterLoraCoveragePlanGenerator.cs` | new |
| `DreamGenClone.Web/Application/RolePlay/LoraCellPromptComposer.cs` | new |
| `DreamGenClone.Web/Application/RolePlay/ICharacterLoraCellService.cs` / `CharacterLoraCellService.cs` | new — the render half |
| `DreamGenClone.Web/Components/Editing/LoraDatasetWorkspace.razor` | new — the LoRA images tab |
| `DreamGenClone.Web/Components/Pages/CharacterStudio.razor` | `case "LoRA images"` now hosts the workspace |
| `DreamGenClone.Web/Program.cs` | generator + cell-service registrations |

**Tests** (`DreamGenClone.Tests/RolePlay/`): `CharacterLoraCoverageSchemaTests` 28, `CharacterLoraCellTemplateSeedTests`
26, `CharacterLoraCoveragePlanGeneratorTests` 29, `LoraCellPromptComposerTests` 16, `CharacterLoraCellServiceTests`
21, `LoraDatasetWorkspaceContractTests` 19. **All green.** (`SdxlSceneImagePromptBuilderTests` has 3 pre-existing
failures from another workstream — leave them alone.)

**Three operator corrections already folded in** — do not undo them:
1. **The identity.** A cell render must go through `AddGeneratedImageAsync` with `options.Identity`;
   `CreateFromPromptAsync` carries no conditioning and renders a stranger. Pinned by a contract test that forbids it.
2. **The layout.** `col-xl-*` only applies at ≥1200px — a "left pane" written that way silently stacks. Use
   `col-lg-4` / `col-lg-8`. Pinned by a contract test.
3. **No negative prompt.** See P1-02. Pinned by `CellRender_CarriesNoNegativePrompt`.

**The body-reference question is ANSWERED (2026-09-25, second pass) — do not ask it again.** The operator chose
**option (c): add real body conditioning now**, so it is in scope and is planned in "Pre-flight for the next step"
below, together with the verified trap (a reference image copies its clothing state, and the canonical body pointer
is the *unclothed* one, so the body reference must be state-matched per cell). `SceneAssetImageGenerationOptions`
has exactly five fields (`NegativePrompt`, `PromptCompilerId`, `Pose`, `Identity`, `BodyAngle`) and none is a body
reference; `BodyAngle` is not it: it means "render this canonical body angle *from* that accepted body image", a
different operation. The remaining unresolved item is the **approval mismatch** recorded under P1-05a.

**Reusing rather than rebuilding is now a standing instruction** from the operator. Before writing any UI, check
`Components/Shared/`, `Components/Assets/` and `Components/Editing/`: `CandidateGrid`, `ProductionApprovalForm`,
`ReferencePicker`, `PoseLibraryPicker`, `PoseAuthorPanel`, `EditIterateWorkbench`, `AssetRunTray`,
`SceneAssetPreview`, `ResolvedRenderBadge`, `BodyViewsPanel` already exist.

**To see any of it, the app must be restarted.** The seeded templates and the curation policy row are written on
startup, and the running instance predates all of this. Then read them back with
`helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/b123-lora-cell-templates.sql`.

---

## Pre-flight for the next step — verified 2026-09-25 (no code written)

Everything below was read out of the code and the live dev DB, not inferred from the notes above. It exists so the
next session starts from facts and from two decisions only the operator can make.

### Verification performed

- **Baseline tests green.** `CharacterLoraCoverageSchemaTests`, `CharacterLoraCellTemplateSeedTests`,
  `CharacterLoraCoveragePlanGeneratorTests`, `LoraCellPromptComposerTests`, `CharacterLoraCellServiceTests`,
  `LoraDatasetWorkspaceContractTests` → **136 passed, 0 failed**, run with
  `-p:OutDir=artifacts\testout\b123-baseline\` (the default `bin\Debug` output is locked by the running app).
- **The app HAS been restarted since the seed work**, contrary to the note above. The dev DB holds the seeded rows.
- **New read-back query:** `DreamGenClone.DbQuery/queries/b123-lora-cell-state.sql` (companion to
  `b123-lora-cell-templates.sql`, which dumps wording). Run:
  `helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/b123-lora-cell-state.sql`.

### Live dev DB state (read 2026-09-25)

| Fact | Value |
|---|---|
| `lora.cell.render.*` rows | **12** (one per view × distance) |
| `lora.cell.caption` | **1** |
| `lora.cell.negative` | **0** — the deleted row is confirmed absent |
| `lora.vocabulary.*` rows | **45** |
| `CharacterLoraCurationPolicies` | **1** (global, 503 chars; seed copy identical) |
| `CharacterLoraDatasets` | **1** — `8d639aa57dcc47bf89bbf6243379ea3f`, character **Becky** (`de351eb3-…`, a character **template** id — not a roleplay `CharacterProfiles` row), pack `2d13c667-…`, Draft, v1, family SDXL, plan 19,181 chars, policy 503 chars |
| `…containerAssetId` | **none** — no cell has ever been shot |
| Cell attempts (`lora-cell-%`) | **0 images, 0 batches** |
| `CharacterLoraDatasetMembers` | **0** |
| Image-level production approvals | **0** |
| Asset-level production approvals | **0** |

So the operator is parked exactly at P1-05a: a real dataset exists, nothing has been shot, nothing accepted.

### Why an accept step needs a promotion (the constraints, each with its evidence)

1. **A member is a Scene Asset, not an image.** `CharacterLoraDatasetMembers.SceneAssetId` is
   `FOREIGN KEY … REFERENCES SceneAssets(Id)` with `UNIQUE (DatasetId, SceneAssetId)`, and the member carries
   `SceneAssetVersion` + `AssetSha256`.
2. **A cell attempt is an image inside the container asset.** `RenderCellAsync` calls
   `AddGeneratedImageAsync(container.Id, …)`; `SceneAssetImages` is where the bytes, `Sha256`, `Status` and the
   approval facts live. The container asset itself is container-only: no bytes, no checksum.
3. **`RequireApprovedAssetAsync` reads the `SceneAssets` row** (`Status`, `Sha256`, `ProductionApprovalStatus`,
   `ApprovedUseScope & CharacterLoraTraining`, `ProductionVersion`, `SourceProvenanceJson`, `ConsentState`,
   `LicenseState`). It is called from **`FreezeDatasetAsync`**, not from `AddDatasetMemberAsync` — so acceptance is
   what must satisfy it, and freezing is where it is finally enforced.
4. **The existing promotion boundary cannot be reused.** `SceneAssetRepository.CreatePromotedAsync` →
   `ValidatePromotedAsset` **requires `SourceApprovalDecisionId`** (a roleplay `ApprovedSceneFrameDecision`), and
   its caller `SceneImageProductionService.PromoteApprovedFrameAsync` sources a roleplay `SceneImage`. A LoRA cell
   attempt has no approval decision, so this path is the *pattern* to copy, not the method to call.
5. **The asset-level approval has no UI call site.** `ISceneAssetService.ApproveForProductionAsync` is called only
   from `ProductionWorkloadService.ApproveAsync` (Production Studio). The single UI approval in the app is
   `AssetReview.razor` → `ProductionApprovalForm` → **image**-level `ApproveImageForProductionAsync`.
6. **The image row already carries every fact the asset check needs** — `SourceProvenanceJson`, `ConsentState`,
   `LicenseState`, `LicenseLabel`, `ApprovedUseScope`, `ContentPolicyKey`, `CompatibilityMetadataJson`,
   `ProductionVersion`. Nothing has to be re-typed, so nothing has to be invented.

### Proposed accept path (for approval)

One judgement, recorded at both layers; one form; no new facts.

1. **Approve the exact attempt image once** with the existing shared `ProductionApprovalForm` (it already offers
   `CharacterLoraTraining` in use scope). Additive change to that component only: an `EventCallback` on success, so
   the cell pane can re-read and enable the accept control. No new form, no new field.
2. **Accept** = one new service call that (a) re-reads the image and requires `Complete` + checksum + an **Approved**
   image whose `ApprovedUseScope` includes `CharacterLoraTraining` (fails fast naming whichever is missing),
   (b) creates the promoted `SceneAsset` **sharing the image's file and checksum** (no copy) with
   `Kind = PromotedApprovedFrame`, `SourceSceneImageId`/`SourceSha256`/provenance naming the cell, dataset and plan
   hash, and the approval facts **copied from the approved image row**, (c) registers the member through
   `ICharacterLoraRepository.AddDatasetMemberAsync` with caption, `CoverageJson` (the cell's `CoverageRecord`
   snapshot), findings, `ReviewedBy`/`ReviewedUtc`.
3. **The promotion lives with `SceneAssets`** — a new `ISceneAssetService`/`Repository` member beside
   `ApproveForProductionAsync`. The LoRA repository keeps to `AddDatasetMemberAsync` only, as the plan requires;
   nothing writes `SceneAssets` from the LoRA side and the UI never touches a repository.
4. **Double-accept is refused by the LoRA side** (P1-05c): a second accept for a cell that already has an accepted
   member must fail on the dataset's own table, not by promoting twice.

Property this buys: the freeze check can only fail if a fact was changed *after* acceptance, because the promoted
asset's approval is a copy of the approval the operator already gave on the exact pixels (identical checksum).

### Decisions taken by the operator (2026-09-25)

1. **The body reference: option (c) — add real body conditioning now.** Not deferred to Phase 2.
2. **Gates before accept: option (i)** — build P1-06 first, so the accept path is written once against real
   verdicts instead of a placeholder findings document.
3. **The accept-path design above is approved** ("proceed as designed").

That re-orders the remaining work to: **P1-06 gates → body conditioning (c) → P1-05a/b/c accept.**

### Body conditioning (option c) — what was found before planning it

- **The reference exists and resolves.** The dataset's pack is Becky's **v9, `Approved`, `BodyComplete`**, with
  `CanonicalFaceAssetId` = `282f5b91…` and `CanonicalFullBodyAssetId` = `37f0fa3f…`. The pack's body vocabulary
  (`SceneImageReferenceBodyView` + `Back`, `SceneImageReferenceBodyState`) already distinguishes view *and*
  clothed/unclothed state, so a cell can name the body reference that matches its own rule the same way
  `ResolveIdentityConditioning` already names its face.
- **No cell model is chosen yet.** `ReferenceWorkflowSettings.LoraCellModelId` is **NULL** in every row, so
  `RenderCellAsync` cannot run at all today (it fails fast on a missing model). The mechanism below depends on
  which family the operator picks, so this is the first thing to settle.
- **The mechanism is family-dependent, and this is the real cost of option (c).**
  `ReferenceStrategyResolver` gives a model two ways to carry identity: `ReferenceConditioning` (an IP-Adapter/
  PuLID graph applied to the sampler's model input — SDXL/Juggernaut) or `NativeMultiReference` (the model's own
  reference-image slots — Qwen-Image-2.1). The render path already names the single place a second reference
  would be added, and it is the **native** branch:
  `SceneAssetGenerationJobHandler.RenderIdentityConditionedAsync` → the `references` list ("This list is the
  single place a further reference would be added … when a pack carries a canonical BODY reference as well").
  - **Native-reference model** → wiring plus a proof. Small, and the designated spot already exists.
  - **IP-Adapter/PuLID graph model** → **not expressible today.** The proven regional mechanism
    (`IPAdapter` + `attn_mask`, two-character proof 2026-08-26) separates references *spatially*; a face and the
    same person's body occupy the same region of the frame, so a mask cannot isolate "identity" from "build".
    The unmasked variant must be measured, because the one time a second IP-Adapter mechanism was pushed at
    identity (FaceID v2, same proof session) it **degraded** identity: "different face per angle … selected
    mechanism stays PLUS FACE regional".
- **A verified data trap, straight from this repo's own history.** The plan sets `CanonicalFullBodyAssetId` to the
  approved **unclothed** Front body. Conditioning a render on a reference **image** copies the reference's clothing
  *state*, not just its subject — verified 2026-09-23 on pack v5, where bare-shouldered refs made a clothed render
  come out fully nude, fixed only by regenerating the refs clothed (memory:
  `native-reference-identity-refs-must-be-clothed.md`). This coverage plan is **18 clothed / 18 unclothed cells**,
  so one unclothed canonical body reference used for all 36 would corrupt the clothed half. The body reference a
  cell uses must therefore be **state-matched from the cell's own rule**, not fixed to the canonical pointer.

### Body conditioning (option c) — **BUILT AND HOST-PROVEN 2026-09-25**

The character's BUILD now travels beside her face as a **second native reference**. Files:

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/ISceneAssetService.cs` | `SceneAssetBodyReferenceConditioning(PackId, BodyAssetId)` + `SceneAssetImageGenerationOptions.BodyReference` |
| `DreamGenClone.Web/Application/RolePlay/SceneAssetJobPayloads.cs` | `BodyReferencePackId` / `BodyReferenceAssetId` |
| `DreamGenClone.Web/Application/RolePlay/SceneAssetService.cs` | maps the option onto the payload |
| `DreamGenClone.Web/Application/RolePlay/IdentityFaceReferenceResolver.cs` | +`ResolvedIdentityBodyReference` and `IdentityBodyReferenceResolver.ResolveExactBodyAsync` (a separate class so the proven face path is untouched) |
| `DreamGenClone.Web/Application/RolePlay/SceneAssetGenerationJobHandler.cs` | strategy first; face and body resolved conditionally; native `references` = face, body, then skeleton; a body reference on a single-slot mechanism **fails fast naming the model** |
| `DreamGenClone.Web/Application/RolePlay/CharacterLoraCellService.cs` (+ interface) | `ResolveBodyConditioning` (pure static, slot **and** state matched, newest wins, fails fast naming pack/slot/state), `DescribeBodyReferenceAsync`, `BodyReference` on the render |
| `DreamGenClone.Web/Components/Editing/LoraDatasetWorkspace.razor` | shows the body reference beside the face before shooting |
| `DreamGenClone.Web/Program.cs` | registers `IdentityBodyReferenceResolver` |

**A routing defect was fixed while doing it:** the render choice now enters the reference path when the face **or**
the body reference is named. Before, a view from directly behind (no face in frame, so no face reference) fell
through to the prompt-only branch and was rendered with **no** conditioning while still reporting success.

**Pre-existing bug fixed (not introduced here):** `ImageWorkflowRepository.UpsertSettingsAsync` bound
`$loraCellModelId` in its SQL but never added the parameter, so **every** settings write threw
`Must add values for the following parameters: $loraCellModelId` — the operator could not save the cell model at
all, and 10 tests were failing on it. One-line forward fix.

**Proof:** `specs/image-generator-tests/qwen-21-native-reference/CASE-20-body-build-reference.md`. The app's own
emitted two-reference graph (`LoadImage` 20 = face, 21 = body) was submitted unchanged to the local 5080 with the
same prompt and seed, A = face only, B = face + the approved FullBody/Front/**Clothed** reference (real pack assets,
checksums verified). Verdict: **PASS** — B differs materially (framing widens to the torso and hips, the silhouette
follows the body reference), so the second reference is genuinely not dropped, and identity holds.
**Observed, honest:** the reference's *garment look* is copied (olive tee becomes the reference's white tee). The
clothing **state** did not leak wrongly — it stayed clothed, which is the defect the state-matching exists to
prevent — but a clothed cell's body reference should depict a neutral garment, or the prompt must be treated as
authoritative for colour only. Proportions were **not** measured; this proves transport and direction, not a
head/body ratio.

**Tests:** `CharacterLoraCellServiceTests` 31 (10 new: slot+state matching, the clothed/unclothed trap, the back
slot, unapproved and face-kind refusals, newest-wins, and two behavioural render tests asserting the options the
render is queued with), `SceneAssetGenerationJobHandlerIdentityReferenceTests` 13 (4 new: face-then-body order, a
body-only render still travelling as a reference, the single-slot refusal naming the model, the unapproved
refusal), plus two workspace contract tests. **Focused sweep: 380 green.** The 3
`SdxlSceneImagePromptBuilderTests` failures remain pre-existing (another workstream).

---

## Done criteria for Phase 1

- Every artefact above exists, every focused test is green, and the app builds.
- The dev DB contains the seeded LoRA templates and vocabulary, readable with `dbq.ps1`.
- A dataset can be created from the generator for a real character, and the two-pane tab shows its 36
  cells, the readiness header and the gaps — with no render dispatched and no batch action anywhere.
