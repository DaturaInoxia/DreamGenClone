# B-121 Tasks — Character Identity Studio

**Execution rule:** complete in order unless marked `[P]`. Check a task only after its tests and
evidence are recorded. Every task ends with the affected tests green.

**Status (2026-09-21):** Phases 0, A, B, C, D, E, H are **done and verified**; F and G are **partial**
(orchestration/promotion done, the yaw and quality gates not built); I is **open**. The authoritative,
evidence-backed phase table is [plan.md](plan.md) → *Implementation status (2026-09-21)*. Boxes below are
checked only for what was verified by reading the code and running the tests named in that table.

**Authoritative documents, in order:**
[spec.md](spec.md) (requirements) → [plan.md](plan.md) (reuse map + decisions D1–D8) →
[seed-prompts.md](seed-prompts.md) (the seed text and thresholds) → [ui-contract.md](ui-contract.md),
with [ui-notes-plan.md](ui-notes-plan.md) superseding the UI **layout** (`ui-contract.md` §1, §2, §7, §9)
and the studio route.

**Mandatory reading before the first Razor edit:** `.github/instructions/razor-editing.instructions.md`.

**Repo rules that bind every task:** no fallback branches; no hardcoded runtime defaults; missing
configuration fails fast naming the missing item; no `git restore`/reset (forward-only); all tests green
before a task is checked. This package must never reference `CharacterLoraDataset` /
`CharacterLoraDatasetMember`.

**Do NOT re-implement B-108.** Its `tasks.md` is obsolete; most of it is already built (see
`plan.md` → "Verified current state"). Read that section before starting.

---

## 0. Verify before writing code (no code in this phase)

- [x] B121-000a Confirm whether the Asset-Manager migration of `ReferenceBootstrapPanel` is complete or
  still pending — `specs/001-final-writing-instruction/debug/020-b111-reference-bootstrap-not-in-asset-manager.md`
  records it as *pending implementation*, but the panel is already mounted in `AssetStudio.razor`.
  Record which is true; it decides whether Phase H is a move or an extension.
  *Evidence:* the exact file/line that settles it.
- [x] B121-000b Trace and record the same-image edit enqueue path
  (`SceneAssetImageEditCompilationService`, `SceneAssetImageEditJobPayloads`, the job type, lane and
  retry settings) so the new steps enqueue edits the same way.
  *Evidence:* file/line references and the job type constant.
- [x] B121-000c Record the current behaviour of `ICharacterImageIdentityService.CreateDraftPackAsync`
  when a draft pack already exists, and compare it with
  `SceneAssetProfilePackJobHandler.EnsureDraftPackAsync` (which prefers draft → supersedes approved →
  creates). Note which one the studio must use for promotion, and whether
  `ReferenceBootstrapService.PromoteAcceptedCharacterFaceAsync` (which calls `CreateDraftPackAsync`
  directly) diverges.
  *Evidence:* the two code paths quoted.
- [x] B121-000d Confirm the runtime Python interpreter available to the app and how it is configured
  (repo venv) for invoking `tools/eye-validation/measure_iris.py`.
  *Evidence:* the resolved path and how it will be configured rather than hardcoded.
- [x] B121-000e Record the current state of `AssetStudioUiContractTests` (it asserts
  `EnqueueProfilePackAsync` is absent) so that Phase H does not trip it.
  *Evidence:* the assertion quoted.

---

## A. Prompt templates and settings

- [x] B121-001 Add `ImageWorkflowPromptTemplate` (Key, Scope `Global`/`Character`,
  `CharacterProfileId`, `WorkflowStep`, `Body`, `SeedBody`, `UpdatedUtc`). No prompt string is
  embedded in code (FR21-006/008). **Do not reuse the name `TemplateDefinition`** — it already exists
  for character seed templates.
  *File:* `DreamGenClone.Domain/RolePlay/` (new records file)
- [x] B121-002 Add `ReferenceWorkflowSettings` (global row + optional per-character override) with the
  settings in `seed-prompts.md` §2, including `EyeToolPythonPath`. Store model **ids** only — no
  sampling parameters (FR21-009/011). Every required value resolves from persisted configuration;
  missing values fail fast by key and no machine path is inferred.
  *File:* same as B121-001
- [x] B121-003 Add the SQLite tables, additive schema and repository read/write mapping for B121-001/002.
  *File:* `DreamGenClone.Infrastructure/RolePlay/` (new repository)
- [x] B121-004 Add the idempotent seed migration inserting the template rows from
  `seed-prompts.md` §1, writing `SeedBody = Body`. Re-running must not duplicate rows and must not
  overwrite a user-edited `Body`.
  *File:* same as B121-003
- [x] B121-005 Add the resolver: character override → global → **fail fast naming the key**.
  No code-embedded default (FR21-007). Add `ResetToSeedAsync(key, scope)`.
  *File:* `DreamGenClone.Web/Application/RolePlay/` (new service)
- [x] B121-006 [P] Add tests: seed → resolve global; character override wins; character override does
  not alter the global row; missing row fails fast with the key in the message; `Reset to seed`
  restores `SeedBody` byte-identically; re-running the seed does not overwrite an edited `Body`.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## B. Build pipeline record

- [x] B121-007 Add `CharacterIdentityBuild` (character profile id, batch id, current step, status,
  created/updated) and the per-step record (`Step`, `Status`, `InputArtifactId`, `OutputArtifactId`,
  `ResolvedPromptText`, `ResolvedModelId`, `FailureReason`, `MirrorDerived`, manual-override fields)
  per D8.
  *File:* `DreamGenClone.Domain/RolePlay/`
- [x] B121-008 Add persistence for B121-007.
  *File:* `DreamGenClone.Infrastructure/RolePlay/`
- [x] B121-009 Add the step state machine: fixed order Front → Validate → GarmentRemoval → Crop →
  Enhance → Angles → Promote; explicit skip recording; resume from the first incomplete step; per-step
  re-run that supersedes only its own output (FR21-001/002/003).
  *File:* `DreamGenClone.Web/Application/RolePlay/` (build service)
- [x] B121-010 Fail fast when a step is requested out of order, when a required input artifact is
  missing, or when a required template key does not resolve.
- [x] B121-011 [P] Add tests: ordering enforced; resume after interruption does not repeat completed
  steps; re-running a step leaves earlier artifacts untouched; skip is recorded and distinguishable
  from completion; out-of-order request fails fast.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## C. Front acquisition

- [x] B121-011a Make the step pipeline **target-extensible** (FR21-035): the target kind selects the
  step set, the template-key namespace and the validation rules, so B-122 Phase 0's body pack is a
  new target kind rather than a second studio. Test: adding a target kind requires no change to the
  build/step machinery.
  *File:* `DreamGenClone.Web/Application/RolePlay/` (build service)
  **DONE 2026-09-21.** `CharacterIdentityTargetKind` + a persisted, seeded step plan
  (`CharacterIdentityStepPlans`: Kind, Step, OrderIndex, HandlerKey, TemplateKey) read through
  `ICharacterIdentityStepPlanService`; `CharacterIdentityBuilds.TargetKind` added by additive migration;
  `CharacterIdentityBuildService` resolves the plan from the build's kind and never reads a fixed array
  (terminal step = the plan's last). Evidence: `CharacterIdentityStepPlanServiceTests` (5) +
  `CharacterIdentityBuildServiceTests.SecondTargetKind_WalksItsOwnPlan_WithNoMachineryChange`, which builds
  a four-step non-face plan and walks it with no machinery change; live dev DB shows the seeded Face plan
  and the Faces page rendering its badges from it. Debug record 048.
  **Deliberately deferred to the body handlers:** the *face* step handlers still hold their template-key
  constants (`identity.front.generate`, `identity.garment.remove`) rather than reading the plan's
  `TemplateKey`, because the front service is face-specific beyond its key (it also fixes
  `SceneAssetType.CharacterFace` for the container). Parameterising key + asset type + view set belongs
  with the first non-face handler that needs it; the plan rows already carry the keys for that step.
- [x] B121-012 Add the generate route: compile via `SceneAssetPromptCompiler.Compile` and dispatch
  through the existing generation client stack, reusing the front logic in
  `SceneAssetProfilePackJobHandler` (including the empty-description degradation, expressed as a
  template variant rather than an embedded string).
  *File:* `DreamGenClone.Web/Application/RolePlay/` (front step handler)
  *Done as:* `CharacterIdentityFrontService.GenerateFrontAttemptAsync` →
  `ISceneAssetService.AddGeneratedImageAsync` (the shared create primitive + durable generation job),
  with the prompt resolved from the seeded template store rather than copied from the profile-pack
  handler. B121-044 still has to retire that handler's constants.
- [x] B121-013 Add the upload route via `SceneAssetService.CreateFromUploadAsync`, converging on the
  same artifact type as B121-012 (FR21-005).
  *Done as:* `CharacterIdentityFrontService.UploadFrontAttemptAsync` → `AddUploadedImageAsync`;
  covered by `CharacterIdentityFrontServiceTests`.
- [x] B121-014 Record the resolved prompt text and resolved model id on the step record (FR21-004).
  *Done as:* `FrontService.SelectFrontAsync` → `CompleteStepAsync(..., resolvedPromptText, resolvedModelId)`.
- [x] B121-015 [P] Add tests: both routes produce a front artifact with the same shape; the recorded
  prompt equals the resolved template text (including after the template is edited); missing model
  configuration fails fast.
  *File:* `DreamGenClone.Tests/RolePlay/`
  *Evidence:* `CharacterIdentityFrontServiceTests` (green 2026-09-21).

---

## D. Validate step

- [x] B121-016 Add the subprocess runner for `tools/eye-validation/measure_iris.py`: invoke, parse
  `irisDy%` / `eyeDy%` / interocular, persist the values and the raw stdout, and fail fast naming the
  configured interpreter path when it is missing (FR21-012/013).
  *Done as:* `CharacterIdentityMeasurementService` (`EyeToolPythonPath` resolved from settings).
- [x] B121-017 Add the manual override: persisted value, reason, author, timestamp; required when the
  tool returns no face mesh (FR21-014).
  *Done as:* `CharacterIdentityValidationService.RecordOverrideAsync` on the Validate step row; the UI
  control is now always reachable for a `Fail` or `NoFaceMesh` candidate (`debug/046`).
- [x] B121-018 Add the advancement gate: blocked while the eye gate fails and no override is recorded;
  the blocking reason is exposed for the UI (FR21-015).
  *Done as:* `CharacterIdentityValidationResult.CanAdvance` / `BlockReason`; `ReRunStepAsync` now clears a
  withdrawn override, so a newly chosen front cannot inherit one recorded about another image.
- [x] B121-019 [P] Add tests: pass/fail/no-face-mesh classification against the configured threshold;
  no-face-mesh blocks without an override; an override with an author and reason unblocks; the raw
  output is persisted; missing interpreter fails fast.
  *File:* `DreamGenClone.Tests/RolePlay/`
  *Evidence:* `CharacterIdentityValidationServiceTests`, `CharacterIdentityMeasurementParserTests`.

---

## E. Garment removal, crop, enhance

- [x] B121-020 Add the garment-removal step: same-image edit through the configured editor model using
  the resolved `identity.garment.remove` template; new artifact, never overwriting the input; skippable
  (FR21-016/017).
  *Done as:* `CharacterIdentityGarmentService` (source, prompt and editor model resolved from the store)
  + the shared `ImageEditWorkspace`; the step's outcome is recorded from the approved image's lineage.
- [x] B121-021 Add the crop step using ImageSharp (already a Web dependency), with persisted parameters
  and a preview-before-commit path (FR21-018/019).
  *Done as:* `MediaEditOperations` / `MediaEditOperationExecutors` + the workspace crop preview
  (`ImageEditWorkspaceContractTests` asserts the preview window and corner handles exist).
- [x] B121-022 Add the enhance step: local ComfyUI `UpscaleModelLoader` + `ImageUpscaleWithModel` with
  the configured upscaler name, then Lanczos to the configured target long edge. Never automatic
  (FR21-020).
  *Done as:* `ComfyUIImageUpscaleClient` + `MediaEditOperationExecutors`; `UpscalerModelName` unset fails
  fast (`ImageEnhanceTests`).
- [x] B121-023 [P] Add tests: each step writes a new artifact and leaves its input intact; the
  dispatched prompt equals the resolved template (proving editability reaches dispatch — acceptance
  scenario 3); crop parameters round-trip; enhance fails fast when the upscaler name is unset.
  *File:* `DreamGenClone.Tests/RolePlay/`
  *Evidence:* `CharacterIdentityGarmentServiceTests`, `ImageEnhanceTests`, `MediaEdit*` tests.

---

## F. Angle step

- [x] B121-024 Add yaw measurement (nose offset from face centre via landmarks) returning a signed
  value, and assert the convention `ThreeQuarterLeft`/`ProfileLeft` = image-left,
  `ThreeQuarterRight`/`ProfileRight` = image-right (FR21-024).
  *Done as:* the canonical tool emits a signed `nose_offset_pct` (negative = image-left) plus `nose_tip`, and
  `CharacterIdentityAngleYawGate` asserts the convention per view (`RequiredSign`: left −1, right +1). The
  descriptor write (`ViewDescriptorJson`) lands with B121-025a, which is the extended-view task.
  See `debug/049`.
- [x] B121-025 Add angle orchestration in the fixed order 3/4L → 3/4R → ProfileL → ProfileR, each view
  independently re-runnable (FR21-022).
  *Done as:* `CharacterIdentityAnglesService` — `PrepareAsync`/`RunAsync`/`UploadAsync`/`RecordResultAsync`,
  per-view attempts, `ResolveSource` chains each profile to its own 3/4 view; the Panel C cards drive it.
- [ ] B121-025a Add a user-driven **single extended view** action for configured pitch/intermediate-
  yaw descriptors. One request produces one artifact with `ViewDescriptorJson` and no canonical
  `FaceView`; there is no multi-angle sweep or generate-all action (FR21-036).
  **OPEN** — the identity pipeline writes no `ViewDescriptorJson`.
- [x] B121-026 Add the gate-and-remedy: measure yaw sign; when it violates the convention apply the
  configured remedy (default: mirror the left render into the right slot) and record
  `MirrorDerived = true`; when the remedy is disabled, block the view with a stated reason
  (FR21-023/025/026).
  *Done as:* `ApplyDirectionGateAsync` runs on upload **and** on a recorded result: it measures
  (`ICharacterIdentityMeasurementService`), stores `attempt.MeasurementJson` as evidence, then passes, mirrors
  through `IImageMirrorEngine` into a new `MirrorDerived = true` attempt (the `DeriveByMirror*` flags now drive
  it, and the mirror lands as its own `SceneAssetImage` via `AddDerivedImageAsync`), or marks the view `Failed`
  with the measured reason. The gate reads the nose offset only — never `irisDy%`/interocular (FR21-023).
  See `debug/049`.
- [x] B121-027 Make profile views require explicit visual confirmation (no face mesh) — never an
  automatic pass (D4).
  *Done as:* `AcceptAttemptAsync` now refuses an unconfirmed profile attempt, and Panel C renders the
  confirmation checkbox whenever it is still outstanding (it used to require `Pending`, which the upload path
  never sets — the enforcement would otherwise have introduced a dead end).
  See `debug/049`.
- [ ] B121-028 [P] Add tests: convention asserted per view; a deliberately wrong-sided render is either
  mirrored (flag set) or blocked with a reason; **the gate is never evaluated on `irisDy%` or
  interocular** (a regression test should fail if someone adds such a check); re-running one view
  leaves the other three untouched; one extended-view request produces exactly one descriptor-only
  artifact and cannot dispatch a sweep.
  *File:* `DreamGenClone.Tests/RolePlay/`
  **PARTIAL** — the first three cases are green: `CharacterIdentityAngleYawGateTests` (convention per view,
  wrong-side/camera-facing blocks, and the iris/interocular regression guard) and
  `CharacterIdentityAnglesDirectionGateTests` (mirror flag + evidence, blocked reason, profile confirmation).
  The extended-view case waits on B121-025a. See `debug/049`.

---

## G. Promotion with view tagging

- [x] B121-029 Extend face promotion to carry the intended `SceneImageReferenceFaceView` canonical
  slot plus `ViewDescriptorJson` per artifact instead of hardcoding `Front` (FR21-027, D7). Extended
  views carry a descriptor and no canonical slot. **The legacy single-face promote path must keep
  working and continue to produce `Front` in an explicit `FaceOnly` pack.**
  *File:* `DreamGenClone.Web/Application/RolePlay/ReferenceBootstrapService.cs`
  *Done as:* the studio promotes through `CharacterIdentityPromotionService` (a separate path, so the
  legacy reference-bootstrap caller is untouched): the canonical front lands as `Front` and each accepted
  angle as its own `ThreeQuarterLeft/Right`, `ProfileLeft/Right` asset.
- [x] B121-030 Add promotion gates: refuse a view whose validate gate failed, whose yaw is wrong, or
  whose quality is below the configured bar, naming the offending view(s) (FR21-028).
  *Done as:* `CharacterIdentityPromotionService.GetReadinessAsync` asks the Validate gate of
  `ICharacterIdentityValidationService` (quoting its reason), re-runs the direction convention over the accepted
  attempt's recorded measurement (a recorded override defers it), and measures each promoted image against the
  configured `QualityGateMinSharpness` with the one `IReferenceImageQualityAnalyzer` metric
  (`ComputeSharpness`). Every block names its view. See `debug/049`.
- [x] B121-031 Write the accepted view set into an explicit `FaceOnly` draft pack, reusing the verified pack-resolution
  sequence and the per-view `SceneAsset` + `UploadAssetAsync(..., view, ...)` pattern from
  `SceneAssetProfilePackJobHandler`; record the produced pack id on the build (FR21-029/030).
  *Done as:* `PromoteAsync` → `CreateDraftPackAsync` + per-view `UploadAssetAsync(..., faceView)` +
  provenance, `build.ProducedIdentityPackId` recorded, idempotent on a second call.
  *To confirm with B-122:* the pack scope currently comes from `CreateDraftPackAsync`'s default rather
  than being passed explicitly; `BodyComplete` must be explicit when that work lands.
- [ ] B121-032 [P] Add tests: five canonical views land with five distinct correct `FaceView` values;
  configured extended views land with descriptor data and no canonical slot; pack scope is explicitly
  `FaceOnly`; refusing a failed/yaw-wrong/low-quality view names it; the legacy promote path still
  writes `Front` with explicit scope; candidate rows are unmodified by promotion (FR21-029); the
  existing reference-bootstrap test file still passes unchanged (acceptance scenario 11).
  *File:* `DreamGenClone.Tests/RolePlay/`
  **PARTIAL** — `CharacterIdentityPromotionGateTests` (7) now covers: five canonical views landing with five
  distinct correct `FaceView` values on promotion (D7), readiness naming the view it refuses (low sharpness,
  wrong/absent direction evidence, a validate gate that cannot advance), readiness accepting a recorded override,
  and the configured floor failing fast at 0. Still open: explicit `FaceOnly`/`BodyComplete` pack scope (moves
  with B-122 Phase 0), the extended-view descriptor case (B121-025a), and the "candidate rows unmodified" plus
  "legacy bootstrap unchanged" assertions. See `debug/049`.

---

## H. UI

**All tasks in this section implement the Faces section of the Character Studio** — the panel shape
approved 2026-09-12 in [ui-notes-plan.md](ui-notes-plan.md) (Panels A–D at `/characters/{id}`), which
supersedes `ui-contract.md`'s step-rail layout, its `/asset-studio/identity/{buildId}` route and
`studio-navigation-and-layout.md`'s per-type studio routes. Tasks below are re-read against that shape.

- [ ] B121-033 Add the studio route `/asset-studio/identity/{buildId}` and the `Build full identity
  pack…` entry action on `ReferenceBootstrapPanel`, leaving the existing controls intact (§0).
  **SUPERSEDED** — the studio is the Faces section of `/characters/{CharacterId}` (owner-index Asset
  Studio), and `ReferenceBootstrapPanel`'s controls are untouched. Do not add the second route.
- [x] B121-034 Implement the step rail + step panel + artifacts strip with per-step status and inline
  block reasons (§1).
  *Done as:* the pipeline-state badge line (per-step status, current step outlined) plus Panels A–D with
  per-candidate verdicts, block reasons and the artifact lineage record.
- [x] B121-035 Implement the Front panel with both sources (§2).
  *Done as:* Panel A — editable prompt, model + size, Generate, upload, per-candidate cards, decisions.
- [ ] B121-036 Implement the Validate panel: preview with eye-level guides, measured values, gate
  verdict, override control, raw-output disclosure (§3).
  **PARTIAL** — verdict, `irisDy%`, threshold, block reason, re-validate and the (now always reachable)
  override control are present; the eye-level guide overlay and the raw-tool-output disclosure are not.
- [x] B121-037 Implement the De-clothe / Crop / Enhance panels, including the **mandatory** enhance
  warning (§4–§6).
  *Done as:* Panel B hosts the shared `ImageEditWorkspace` (de-clothe / crop / enhance), the mandatory
  warning is the workspace's *Assess likeness first* block (`ImageEditWorkspace.razor`), and the
  no-edit path ("use the front as-is — skip 3 · 4 · 5") records the three as skipped.
- [ ] B121-038 Implement the Angles panel: four view cards with yaw sign, convention verdict,
  `mirrored from …` attribution, per-view re-run, and profile visual confirmation (§7).
  **PARTIAL** — four cards, per-view prompt + edit workspace, upload, accept-per-view, review-deck links,
  inherited source and profile direction confirmation are present, and the card now shows the gate's verdict
  (block reason), the measured direction (`DescribeDirection`) and the `Mirror-derived` attribution
  (`debug/049`). Still open: the yaw *sign* as a signed descriptor on extended views (B121-025a).
- [ ] B121-039 Implement the shared prompt editor: scope toggle, resolved-text display with
  **Supplied by** attribution, placeholder validation, Save, Reset to default with confirmation, and
  the `Missing required template: <key>` disabled state (§8).
  **PARTIAL** — Panel A's prompt editor (editable text, saved per character, *Reload default*, and a
  `character prompt` / `default prompt` scope badge) is in place; the *Supplied by* attribution, a
  confirmation on reset and the missing-template disabled state are not.
- [x] B121-040 Implement the Promote panel with per-view gate summary, disabled-state reasons, and the
  post-promotion destination link (§9).
  *Done as:* Panel D — five readiness cards, joined blocking reasons, disabled Promote until ready, and
  the *Open identity packs* destination link.
- [ ] B121-041 Implement empty/loading/failure states (§10) and accessibility/focus (§11), including
  state keys (§12).
  **PARTIAL** — failure and empty states exist throughout (alerts, "no attempts yet", disabled
  controls); the state-key and focus/accessibility pass has not been done.
- [x] B121-042 [P] Add source-contract/component tests and Razor diagnostics for the rail, the prompt
  editor scoping, the gate disabled-states, and the mirror-derived labelling.
  *File:* `DreamGenClone.Tests/RolePlay/` (new studio UI contract test file)
  *Done as:* `CharacterStudioFacesContractTests` (override reachability, recorded-front protection,
  no-edit path, one Panel C refresh, the gate verdict/measurement/mirror attribution, and the profile
  confirmation not being hidden behind `Pending`) + `ImageEditWorkspaceContractTests`; Razor diagnostics clean.
- [ ] B121-043 [P] Run Playwright acceptance at 1440×1000 and 390×844 covering
  front → validate → de-clothe → crop → enhance → angles → promote for one character. Assert no
  horizontal overflow, no overlapping controls, no console/page errors.
  *File:* established Playwright harness
  **OPEN** — the flow was exercised live in a desktop browser (builder verification), not as the
  two-viewport assertion run.

---

## I. Cleanup, validation, evidence

- [ ] B121-044 Delete the hardcoded `AngleEdits` constants from
  `SceneAssetProfilePackJobHandler.cs`, ensuring the corrected text now lives only in the seeded
  templates (D3). Confirm nothing else reads them.
  **OPEN** — `AngleEdits` is still declared at `SceneAssetProfilePackJobHandler.cs:26`; B121-049's
  grep proof (a) fails until this lands.
- [ ] B121-045 Update the B-111 `P2-tasks.md` reference ("reuse `AngleEdits` pattern", unit P2-U4) to
  point at the template store, and add a superseded banner to
  `specs/Planning/B-108-reference-bootstrap-studio/tasks.md` pointing at B-111 P2 and this item.
  **OPEN** — both references still present.
- [x] B121-046 [P] Run affected focused tests and build only the affected projects (never
  `dotnet build DreamGenClone.sln`). Record exact counts. See B-111 `P2-tasks.md` dispatch discipline.
  *Evidence (2026-09-21):* `DreamGenClone.Tests` built with 0 errors and targeted runs at
  94 → 95 → 93 → 58 → 36 passed / 0 failed across `CharacterStudioFacesContractTests`,
  `CharacterIdentity*`, `SceneAsset*`, `ImageEditWorkspaceContractTests` (counts vary with the filter).
- [x] B121-047 Run Razor diagnostics on every touched component; record a clean result.
  *Evidence:* `CharacterStudio.razor` reported no errors after every edit set this session.
- [ ] B121-048 Execute all eleven acceptance scenarios from `spec.md` in the running application with
  a real character (Sam Winchester), recording build/artifact/pack ids. Front-to-promotion must
  complete with no hand-run scripts and no direct DB writes.
  **PARTIAL** — the flow was proven end-to-end on *Becky* (`f58f959a…`, build `b8adc0e742e645f7a4a7100ff4796a94`)
  from an uploaded front through the eye-gate override, the skipped steps 3/4/5, four uploaded angles and
  a Promote panel reporting all five views Ready. The formal eleven-scenario pass is still to be executed.
- [ ] B121-049 Grep proofs: (a) no hardcoded pipeline prompt remains in any step path; (b) no new
  reference to `CharacterLoraDataset`/`CharacterLoraDatasetMember`; (c) no new third candidate
  mechanism. Record the commands and results.
  **OPEN** — blocked on B121-044 for proof (a).
- [x] B121-050 Record completion evidence, update the backlog state, and note the deliberate
  `TemplateDefinition` naming boundary.
  *Evidence:* `plan.md` → *Implementation status (2026-09-21)* + *Next phase*; `backlog.md` B-121 set to
  `implemented`; `identity-lora-program-map.md` stage status added. Naming boundary unchanged and
  deliberate: `ImageWorkflowPromptTemplate` (this item) is **not** `TemplateDefinition`
  (`DreamGenClone.Domain/Templates/`, character seed templates).

---

## Dependency notes

- Phase A blocks B–H (every step resolves its prompt through the store).
- Phase B blocks C–G. Phase F depends on C (a front must exist). Phase G depends on D and F (the gates).
- Phase H can begin once A–G expose a working service surface; do not build UI against unfinished
  service contracts.
- Phase I's B121-044 must not land before A is seeded, or the angle step will have no prompt to resolve.
- The parallel `SceneAsset.Candidate*` pipeline (`PromptAssetCreator` / `ImageEditWorkbench`) is
  **out of scope**; do not merge it into this work (FR21-031).
