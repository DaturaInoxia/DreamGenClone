# B-121 Tasks — Character Identity Studio

**Execution rule:** complete in order unless marked `[P]`. Check a task only after its tests and
evidence are recorded. Every task ends with the affected tests green.

**This is a handoff task list.** Authoritative documents, in order:
[spec.md](spec.md) (requirements) → [plan.md](plan.md) (reuse map + decisions D1–D8) →
[seed-prompts.md](seed-prompts.md) (the seed text and thresholds) → [ui-contract.md](ui-contract.md).

**Mandatory reading before the first Razor edit:** `.github/instructions/razor-editing.instructions.md`.

**Repo rules that bind every task:** no fallback branches; no hardcoded runtime defaults; missing
configuration fails fast naming the missing item; no `git restore`/reset (forward-only); all tests green
before a task is checked. This package must never reference `CharacterLoraDataset` /
`CharacterLoraDatasetMember`.

**Do NOT re-implement B-108.** Its `tasks.md` is obsolete; most of it is already built (see
`plan.md` → "Verified current state"). Read that section before starting.

---

## 0. Verify before writing code (no code in this phase)

- [ ] B121-000a Confirm whether the Asset-Manager migration of `ReferenceBootstrapPanel` is complete or
  still pending — `specs/001-final-writing-instruction/debug/020-b111-reference-bootstrap-not-in-asset-manager.md`
  records it as *pending implementation*, but the panel is already mounted in `AssetStudio.razor`.
  Record which is true; it decides whether Phase H is a move or an extension.
  *Evidence:* the exact file/line that settles it.
- [ ] B121-000b Trace and record the same-image edit enqueue path
  (`SceneAssetImageEditCompilationService`, `SceneAssetImageEditJobPayloads`, the job type, lane and
  retry settings) so the new steps enqueue edits the same way.
  *Evidence:* file/line references and the job type constant.
- [ ] B121-000c Record the current behaviour of `ICharacterImageIdentityService.CreateDraftPackAsync`
  when a draft pack already exists, and compare it with
  `SceneAssetProfilePackJobHandler.EnsureDraftPackAsync` (which prefers draft → supersedes approved →
  creates). Note which one the studio must use for promotion, and whether
  `ReferenceBootstrapService.PromoteAcceptedCharacterFaceAsync` (which calls `CreateDraftPackAsync`
  directly) diverges.
  *Evidence:* the two code paths quoted.
- [ ] B121-000d Confirm the runtime Python interpreter available to the app and how it is configured
  (repo venv) for invoking `tools/eye-validation/measure_iris.py`.
  *Evidence:* the resolved path and how it will be configured rather than hardcoded.
- [ ] B121-000e Record the current state of `AssetStudioUiContractTests` (it asserts
  `EnqueueProfilePackAsync` is absent) so that Phase H does not trip it.
  *Evidence:* the assertion quoted.

---

## A. Prompt templates and settings

- [ ] B121-001 Add `ImageWorkflowPromptTemplate` (Key, Scope `Global`/`Character`,
  `CharacterProfileId`, `WorkflowStep`, `Body`, `SeedBody`, `UpdatedUtc`). No prompt string is
  embedded in code (FR21-006/008). **Do not reuse the name `TemplateDefinition`** — it already exists
  for character seed templates.
  *File:* `DreamGenClone.Domain/RolePlay/` (new records file)
- [ ] B121-002 Add `ReferenceWorkflowSettings` (global row + optional per-character override) with the
  settings in `seed-prompts.md` §2, including `EyeToolPythonPath`. Store model **ids** only — no
  sampling parameters (FR21-009/011). Every required value resolves from persisted configuration;
  missing values fail fast by key and no machine path is inferred.
  *File:* same as B121-001
- [ ] B121-003 Add the SQLite tables, additive schema and repository read/write mapping for B121-001/002.
  *File:* `DreamGenClone.Infrastructure/RolePlay/` (new repository)
- [ ] B121-004 Add the idempotent seed migration inserting the template rows from
  `seed-prompts.md` §1, writing `SeedBody = Body`. Re-running must not duplicate rows and must not
  overwrite a user-edited `Body`.
  *File:* same as B121-003
- [ ] B121-005 Add the resolver: character override → global → **fail fast naming the key**.
  No code-embedded default (FR21-007). Add `ResetToSeedAsync(key, scope)`.
  *File:* `DreamGenClone.Web/Application/RolePlay/` (new service)
- [ ] B121-006 [P] Add tests: seed → resolve global; character override wins; character override does
  not alter the global row; missing row fails fast with the key in the message; `Reset to seed`
  restores `SeedBody` byte-identically; re-running the seed does not overwrite an edited `Body`.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## B. Build pipeline record

- [ ] B121-007 Add `CharacterIdentityBuild` (character profile id, batch id, current step, status,
  created/updated) and the per-step record (`Step`, `Status`, `InputArtifactId`, `OutputArtifactId`,
  `ResolvedPromptText`, `ResolvedModelId`, `FailureReason`, `MirrorDerived`, manual-override fields)
  per D8.
  *File:* `DreamGenClone.Domain/RolePlay/`
- [ ] B121-008 Add persistence for B121-007.
  *File:* `DreamGenClone.Infrastructure/RolePlay/`
- [ ] B121-009 Add the step state machine: fixed order Front → Validate → GarmentRemoval → Crop →
  Enhance → Angles → Promote; explicit skip recording; resume from the first incomplete step; per-step
  re-run that supersedes only its own output (FR21-001/002/003).
  *File:* `DreamGenClone.Web/Application/RolePlay/` (build service)
- [ ] B121-010 Fail fast when a step is requested out of order, when a required input artifact is
  missing, or when a required template key does not resolve.
- [ ] B121-011 [P] Add tests: ordering enforced; resume after interruption does not repeat completed
  steps; re-running a step leaves earlier artifacts untouched; skip is recorded and distinguishable
  from completion; out-of-order request fails fast.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## C. Front acquisition

- [ ] B121-011a Make the step pipeline **target-extensible** (FR21-035): the target kind selects the
  step set, the template-key namespace and the validation rules, so B-122 Phase 0's body pack is a
  new target kind rather than a second studio. Test: adding a target kind requires no change to the
  build/step machinery.
  *File:* `DreamGenClone.Web/Application/RolePlay/` (build service)
- [ ] B121-012 Add the generate route: compile via `SceneAssetPromptCompiler.Compile` and dispatch
  through the existing generation client stack, reusing the front logic in
  `SceneAssetProfilePackJobHandler` (including the empty-description degradation, expressed as a
  template variant rather than an embedded string).
  *File:* `DreamGenClone.Web/Application/RolePlay/` (front step handler)
- [ ] B121-013 Add the upload route via `SceneAssetService.CreateFromUploadAsync`, converging on the
  same artifact type as B121-012 (FR21-005).
- [ ] B121-014 Record the resolved prompt text and resolved model id on the step record (FR21-004).
- [ ] B121-015 [P] Add tests: both routes produce a front artifact with the same shape; the recorded
  prompt equals the resolved template text (including after the template is edited); missing model
  configuration fails fast.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## D. Validate step

- [ ] B121-016 Add the subprocess runner for `tools/eye-validation/measure_iris.py`: invoke, parse
  `irisDy%` / `eyeDy%` / interocular, persist the values and the raw stdout, and fail fast naming the
  configured interpreter path when it is missing (FR21-012/013).
- [ ] B121-017 Add the manual override: persisted value, reason, author, timestamp; required when the
  tool returns no face mesh (FR21-014).
- [ ] B121-018 Add the advancement gate: blocked while the eye gate fails and no override is recorded;
  the blocking reason is exposed for the UI (FR21-015).
- [ ] B121-019 [P] Add tests: pass/fail/no-face-mesh classification against the configured threshold;
  no-face-mesh blocks without an override; an override with an author and reason unblocks; the raw
  output is persisted; missing interpreter fails fast.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## E. Garment removal, crop, enhance

- [ ] B121-020 Add the garment-removal step: same-image edit through the configured editor model using
  the resolved `identity.garment.remove` template; new artifact, never overwriting the input; skippable
  (FR21-016/017).
- [ ] B121-021 Add the crop step using ImageSharp (already a Web dependency), with persisted parameters
  and a preview-before-commit path (FR21-018/019).
- [ ] B121-022 Add the enhance step: local ComfyUI `UpscaleModelLoader` + `ImageUpscaleWithModel` with
  the configured upscaler name, then Lanczos to the configured target long edge. Never automatic
  (FR21-020).
- [ ] B121-023 [P] Add tests: each step writes a new artifact and leaves its input intact; the
  dispatched prompt equals the resolved template (proving editability reaches dispatch — acceptance
  scenario 3); crop parameters round-trip; enhance fails fast when the upscaler name is unset.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## F. Angle step

- [ ] B121-024 Add yaw measurement (nose offset from face centre via landmarks) returning a signed
  value, and assert the convention `ThreeQuarterLeft`/`ProfileLeft` = image-left,
  `ThreeQuarterRight`/`ProfileRight` = image-right (FR21-024).
- [ ] B121-025 Add angle orchestration in the fixed order 3/4L → 3/4R → ProfileL → ProfileR, each view
  independently re-runnable (FR21-022).
- [ ] B121-025a Add a user-driven **single extended view** action for configured pitch/intermediate-
  yaw descriptors. One request produces one artifact with `ViewDescriptorJson` and no canonical
  `FaceView`; there is no multi-angle sweep or generate-all action (FR21-036).
- [ ] B121-026 Add the gate-and-remedy: measure yaw sign; when it violates the convention apply the
  configured remedy (default: mirror the left render into the right slot) and record
  `MirrorDerived = true`; when the remedy is disabled, block the view with a stated reason
  (FR21-023/025/026).
- [ ] B121-027 Make profile views require explicit visual confirmation (no face mesh) — never an
  automatic pass (D4).
- [ ] B121-028 [P] Add tests: convention asserted per view; a deliberately wrong-sided render is either
  mirrored (flag set) or blocked with a reason; **the gate is never evaluated on `irisDy%` or
  interocular** (a regression test should fail if someone adds such a check); re-running one view
  leaves the other three untouched; one extended-view request produces exactly one descriptor-only
  artifact and cannot dispatch a sweep.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## G. Promotion with view tagging

- [ ] B121-029 Extend face promotion to carry the intended `SceneImageReferenceFaceView` canonical
  slot plus `ViewDescriptorJson` per artifact instead of hardcoding `Front` (FR21-027, D7). Extended
  views carry a descriptor and no canonical slot. **The legacy single-face promote path must keep
  working and continue to produce `Front` in an explicit `FaceOnly` pack.**
  *File:* `DreamGenClone.Web/Application/RolePlay/ReferenceBootstrapService.cs`
- [ ] B121-030 Add promotion gates: refuse a view whose validate gate failed, whose yaw is wrong, or
  whose quality is below the configured bar, naming the offending view(s) (FR21-028).
- [ ] B121-031 Write the accepted view set into an explicit `FaceOnly` draft pack, reusing the verified pack-resolution
  sequence and the per-view `SceneAsset` + `UploadAssetAsync(..., view, ...)` pattern from
  `SceneAssetProfilePackJobHandler`; record the produced pack id on the build (FR21-029/030).
- [ ] B121-032 [P] Add tests: five canonical views land with five distinct correct `FaceView` values;
  configured extended views land with descriptor data and no canonical slot; pack scope is explicitly
  `FaceOnly`; refusing a failed/yaw-wrong/low-quality view names it; the legacy promote path still
  writes `Front` with explicit scope; candidate rows are unmodified by promotion (FR21-029); the
  existing reference-bootstrap test file still passes unchanged (acceptance scenario 11).
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## H. UI

**All tasks in this section implement [ui-contract.md](ui-contract.md).**

- [ ] B121-033 Add the studio route `/asset-studio/identity/{buildId}` and the `Build full identity
  pack…` entry action on `ReferenceBootstrapPanel`, leaving the existing controls intact (§0).
- [ ] B121-034 Implement the step rail + step panel + artifacts strip with per-step status and inline
  block reasons (§1).
- [ ] B121-035 Implement the Front panel with both sources (§2).
- [ ] B121-036 Implement the Validate panel: preview with eye-level guides, measured values, gate
  verdict, override control, raw-output disclosure (§3).
- [ ] B121-037 Implement the De-clothe / Crop / Enhance panels, including the **mandatory** enhance
  warning (§4–§6).
- [ ] B121-038 Implement the Angles panel: four view cards with yaw sign, convention verdict,
  `mirrored from …` attribution, per-view re-run, and profile visual confirmation (§7).
- [ ] B121-039 Implement the shared prompt editor: scope toggle, resolved-text display with
  **Supplied by** attribution, placeholder validation, Save, Reset to default with confirmation, and
  the `Missing required template: <key>` disabled state (§8).
- [ ] B121-040 Implement the Promote panel with per-view gate summary, disabled-state reasons, and the
  post-promotion destination link (§9).
- [ ] B121-041 Implement empty/loading/failure states (§10) and accessibility/focus (§11), including
  state keys (§12).
- [ ] B121-042 [P] Add source-contract/component tests and Razor diagnostics for the rail, the prompt
  editor scoping, the gate disabled-states, and the mirror-derived labelling.
  *File:* `DreamGenClone.Tests/RolePlay/` (new studio UI contract test file)
- [ ] B121-043 [P] Run Playwright acceptance at 1440×1000 and 390×844 covering
  front → validate → de-clothe → crop → enhance → angles → promote for one character. Assert no
  horizontal overflow, no overlapping controls, no console/page errors.
  *File:* established Playwright harness

---

## I. Cleanup, validation, evidence

- [ ] B121-044 Delete the hardcoded `AngleEdits` constants from
  `SceneAssetProfilePackJobHandler.cs`, ensuring the corrected text now lives only in the seeded
  templates (D3). Confirm nothing else reads them.
- [ ] B121-045 Update the B-111 `P2-tasks.md` reference ("reuse `AngleEdits` pattern", unit P2-U4) to
  point at the template store, and add a superseded banner to
  `specs/Planning/B-108-reference-bootstrap-studio/tasks.md` pointing at B-111 P2 and this item.
- [ ] B121-046 [P] Run affected focused tests and build only the affected projects (never
  `dotnet build DreamGenClone.sln`). Record exact counts. See B-111 `P2-tasks.md` dispatch discipline.
- [ ] B121-047 Run Razor diagnostics on every touched component; record a clean result.
- [ ] B121-048 Execute all eleven acceptance scenarios from `spec.md` in the running application with
  a real character (Sam Winchester), recording build/artifact/pack ids. Front-to-promotion must
  complete with no hand-run scripts and no direct DB writes.
- [ ] B121-049 Grep proofs: (a) no hardcoded pipeline prompt remains in any step path; (b) no new
  reference to `CharacterLoraDataset`/`CharacterLoraDatasetMember`; (c) no new third candidate
  mechanism. Record the commands and results.
- [ ] B121-050 Record completion evidence, update the backlog state, and note the deliberate
  `TemplateDefinition` naming boundary.

---

## Dependency notes

- Phase A blocks B–H (every step resolves its prompt through the store).
- Phase B blocks C–G. Phase F depends on C (a front must exist). Phase G depends on D and F (the gates).
- Phase H can begin once A–G expose a working service surface; do not build UI against unfinished
  service contracts.
- Phase I's B121-044 must not land before A is seeded, or the angle step will have no prompt to resolve.
- The parallel `SceneAsset.Candidate*` pipeline (`PromptAssetCreator` / `ImageEditWorkbench`) is
  **out of scope**; do not merge it into this work (FR21-031).
