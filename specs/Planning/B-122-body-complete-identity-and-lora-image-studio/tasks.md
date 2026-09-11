# B-123 Tasks — Character LoRA Image Studio (cell-workspace granularity)

**Execution rule:** complete in order unless marked `[P]`. Check a task only after its tests and
evidence are recorded. Every task ends with the affected tests green.

**This is the dispatch list for B-123** (B-122 Phase 0 has its own backlog item; tasks here that
touch the body declare it explicitly). Authoritative documents, in order:
`identity-lora-program-map.md` (sequencing + ownership) →
`identity-and-reference-model.md` (the reference model) →
`plan.md` in this folder (Phases 1–8) → `B-121` (`plan.md` + `tasks.md`, the machinery this consumes).

**Mandatory reading before the first Razor edit:** `.github/instructions/razor-editing.instructions.md`.

**Repo rules that bind every task:** no fallback branches; no hardcoded runtime defaults; missing
configuration fails fast naming the missing item; no `git restore`/reset; all tests green before a
task is checked. This is the LoRA item — it *does* reference `CharacterLoraDataset`, and that is
correct here.

**Workflow orientation (the whole point of this item):** every capability is a UI tool the user
drives, never a batch. The training set is assembled **cell by cell**; each cell is an exercise
(create / edit / pose / model / several attempts). A "generate 30" action must never exist. If a task
only writes a database row and produces no working artefact the user judged, it is not complete.

## Architecture review points (first pass = an architecture agent, before any coding)

Validate these before writing code; each is a design decision already made in the plans:

1. **The cell workspace is the centerpiece** — `plan.md` Phase 2, not a queued batch. The coverage
   plan is an editable plan; nothing auto-runs.
2. **Coverage axes read from the view sets** produced by B-121 (face, incl. pitch/intermediate yaw)
   and B-122 (body, incl. rotation/position) via the `ViewDescriptorJson` model owned by B-124. The
   matrix is a *projection* of the reference model, never a separately invented list.
3. **Normalization reuses B-121's edit primitive + prompt-template store** — it does not build a
   second edit path or a second template store.
4. **Gates:** eye/face-landmark measurement is consumed from B-121 (subprocess, no .NET port); a
   single favourable metric is never a pass (report identity + adherence + diversity); pose claims
   come from **measured joint geometry**, never auto-caption prose (raster-IoU is known-bad).
5. **LoRA inference is base-model-agnostic:** one dataset, N training profiles, N artifacts;
   inference selects the artifact whose `BaseModelId`/`Sha256` matches the render model and **fails
   fast** when none matches. First character targets **one** model; model #2 is a data action.
6. **B-122 vs B-123 ownership** is declared per task below; the two items share this folder.

---

## 0. Verify before writing code (no code in this phase)

- [ ] B123-000a Confirm the current `LoraDatasetGeneration.razor` / `LoraDatasetCuration.razor` /
  `LoraDatasetTraining.razor` structure and exactly what the "raw-JSON wireframe" is that Phases 1–7
  replace.
  *Evidence:* the files + the raw-JSON inputs they currently collect.
- [ ] B123-000b Confirm `CharacterAssetGenerationService.CreateBatchAsync` output (draft
  `SceneAsset`s) and that `AddDatasetMemberAsync` is called only from tests.
  *Evidence:* call sites quoted.
- [ ] B123-000c Confirm `CharacterLoraModels.cs` fields (`TargetModelFamily`, `BaseModelId/Version/
  Sha256`) and `CharacterLoraTrainingProfile`, and the freeze `ManifestSha256` flow.
  *Evidence:* fields quoted.
- [ ] B123-000d Confirm the B-124 view-set model (`ViewDescriptorJson`) and B-121/B-122 outputs are
  consumable, and record the exact service methods the cell workspace will read them through.
  *Evidence:* method names + file paths.

---

## A. Coverage plan + typed schemas (plan.md Phase 1)

- [ ] B123-001 Add typed schemas: `CoveragePlan` (angle/crop/expression/framing/pose/wardrobe-state/
  lighting/background/aspect), `CoverageRecord` (per-member), `CurationPolicy`, `CurationFindings`
  — real classes + validation, not free-form strings.
  *File:* `DreamGenClone.Domain/RolePlay/`
- [ ] B123-002 Add the coverage-plan editor UI: a grid seeded from the capture-list matrix, editable,
  with fixed per-cell seeds — **authored/seeded, never auto-run**.
  *File:* `DreamGenClone.Web/Components/Pages/LoraDatasetGeneration.razor`
- [ ] B123-003 [P] Tests: schema round-trip + validation failures; editor seeding produces the
  expected cells; no "generate all" action exists in the source contract.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## B. Cell workspace (plan.md Phase 2) — the centerpiece

- [ ] B123-004 Add the cell-workspace route + per-cell state (source / pose / model / attempts /
  status `draft → in-progress → accepted / rejected`).
- [ ] B123-005 Source selection: generate fresh, reuse a reference from the view set, or upload.
- [ ] B123-006 Pose attach: from the B-118 library, a fresh DWPose extraction, or none.
- [ ] B123-007 Model selection + single render (one image per request; no sweep).
- [ ] B123-008 Attempt history: keep an attempt as the cell's accepted image or discard; never
  collapse several attempts into one shot.
- [ ] B123-009 [P] Tests: per-cell state transitions; a render produces exactly one image; attempts
  are kept/discarded independently; a cell cannot be accepted without an image.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## C. Normalization (plan.md Phase 3)

- [ ] B123-010 Face-only normalize: Qwen identity edit with the angle-matched reference
  (F→front, 34L→34l, …), reusing B-121's edit primitive + template store.
- [ ] B123-011 Body normalize: Qwen edit with the Phase-0 body reference; the unclothed reference for
  nude cells. Alignment only — never alters explicit anatomy (the base model owns that).
- [ ] B123-012 [P] Tests: normalization dispatches through the shared edit primitive (not a new path);
  the angle-matched reference resolves correctly; nude cells use the unclothed body reference.

---

## D. Quality gates (plan.md Phase 4)

- [ ] B123-013 Wire the existing scorers into the workspace: face count (MTCNN), identity vs
  canonical (`tools/consistency-scoring identity`, frame-normalised band), eye check (subprocess from
  B-121, face-close cells only), near-duplicate (subject/CLIP similarity, threshold from
  `CurationPolicy`).
- [ ] B123-014 Build the pose-adherence scorer: joint geometry vs the library skeleton JSON. Never
  raster-IoU, never auto-caption prose.
- [ ] B123-015 Manual gates (anatomy — the primary gate for nude cells; no reliable automated
  detector) with persisted verdicts. The skin-fraction `sanitisation` scorer is **not** applied.
- [ ] B123-016 [P] Tests: a single favourable metric is never a pass; pose scorer validated against
  known-good and known-bad renders before it gates anything; the eye check is delegated to B-121's
  capability (no second implementation).

---

## E. Captions (plan.md Phase 5)

- [ ] B123-017 Caption builder: template `{trigger}, {pose/angle}, {distance}, {clothing}, {lighting},
  {background}, {expression}`; invariant identity never captioned; editable + versioned via the
  existing `CaptionRevision` optimistic concurrency. Optional VLM assist behind manual review.
- [ ] B123-018 [P] Tests: the trigger token owns identity and is never added to captions; template
  round-trips; edits are versioned.

---

## F. Member registration (plan.md Phase 6)

- [ ] B123-019 Register an accepted output as a `CharacterLoraDatasetMember` (role `IdentitySeed` /
  `Training` / `Validation`, `SceneAssetId` + version + SHA-256, `Caption`, `CoverageJson`,
  `CurationFindingsJson`, `ReviewedBy/Utc`). Both train and validation splits required at freeze
  (already enforced).
- [ ] B123-020 [P] Tests: registration from an accepted cell; split enforcement; non-accepted cells
  cannot register.

---

## G. Freeze / export / training hand-off (plan.md Phase 7)

- [ ] B123-021 Freeze (computes `ManifestSha256`) + export (`.txt` sidecars / `dataset.toml`) into the
  existing training dispatch — no new training dispatch work.
- [ ] B123-022 [P] Tests: freeze requires all members accepted; export contents match the manifest.

---

## H. LoRA inference wiring (plan.md Phase 8)

- [ ] B123-023 Add the `LoraLoader` graph node (between checkpoint and sampler, feeding model and
  clip) at `IdentityStrategyBinding.LoraStrength`.
- [ ] B123-024 Add trigger-token injection (dataset `TriggerToken` prepended when the resolved
  identity strategy is `Lora`/`Combined`) + the strength/artifact picker UI.
- [ ] B123-025 [P] Tests: **base-model-agnostic** — one dataset, N profiles, N artifacts; inference
  selects the artifact matching the render model's `BaseModelId`/`Sha256` and **fails fast** when
  none matches.

---

## I. UI + validation

- [ ] B123-026 `LoraDatasetGeneration.razor` → guided wizard: dataset identity prefilled from the
  character → coverage checklist → cell workspace → quality review → captions → freeze.
- [ ] B123-027 `LoraDatasetCuration.razor` → add the automated quality signals alongside the manual
  checkboxes.
- [ ] B123-028 Empty / loading / failure states for every region (no silent gaps).
- [ ] B123-029 [P] Source-contract/component tests + Razor diagnostics: the wizard, the cell
  workspace, the gate verdicts, and the no-batch rule.
- [ ] B123-030 [P] Playwright at 1440×1000 and 390×844: open a cell → generate → normalize → gate →
  accept; assert no auto-batch, no horizontal overflow, no console/page errors.

---

## J. Cleanup, validation, evidence

- [ ] B123-031 Run affected focused tests and build only the affected projects (never
  `dotnet build DreamGenClone.sln`). Record exact counts. See B-111 `P2-tasks.md` dispatch discipline.
- [ ] B123-032 Run Razor diagnostics on every touched component; record a clean result.
- [ ] B123-033 Live run with a real character (Sam Winchester): cell-by-cell to a frozen set and a
  trained artifact, with no hand-run scripts and no direct DB writes. Record the dataset, the
  training profile and the artifact ids.
- [ ] B123-034 Grep proofs: (a) no "generate N" / batch-sweep path exists anywhere in the new code;
  (b) no hardcoded pipeline prompt; (c) the eye check is delegated to B-121's capability, not
  re-implemented. Record commands + results.
- [ ] B123-035 Record completion evidence and update the backlog states for B-122/B-123.

---

## Dependency notes

- Phase 0 blocks everything. Phase A blocks B. Phase B blocks C–G. Phase H (inference) depends only
  on a training profile existing.
- **B-123 depends on stages 1–3 of the program map:** B-124 (the `ViewDescriptorJson` model),
  B-121 (the edit primitive, template store, eye-check capability, face view set) and B-122 Phase 0
  (the BodyCard + body view set). Do not start B before those exist; the cell workspace degrades
  without B-118/B-117 (pose) and B-119/B-120 (layout) — it must run without them, just with weaker
  pose/layout control.
- **B-122 Phase 0** (the BodyCard + body refs) is its own backlog item; the tasks above that touch
  the body (B123-006, B123-011, B123-026) consume it and must not build it.
