# B-119 — Multi-character layout toolbox (depth/canny + regional identity + workflow): tasks

Contract: `plan.md` + `workflow.md` in this folder. **Scope boundary:** this item does NOT duplicate canonical B-117 (OpenPose pose-control composition render) or B-118 (Pose Studio) — coordinate with those instead. B-119 covers depth/canny-from-reference ControlNet, per-char regional identity in structured renders, and the decision workflow.
Repo rules that always apply: no `git restore`; no fallback branches (capability-gated, fail-fast); test suite green; no skipped tests; `.razor` edits follow `.github/instructions/razor-editing.instructions.md`; additive only — never repoint defaults, don't touch Generate/Edit/Compose/identity paths.

Legend: `[ ]` not started, `[X]` done. Order within a phase is dependency-ordered.

---

## Phase 1 — Host: install + verify depth/canny ControlNet weights (local ComfyUI)
- [ ] **T01** Install SDXL **depth** + **canny** ControlNet weights on the local host (visible to `ControlNetLoader`). **Document the change** in `docs/local-comfyui-model-manager-setup.md` + an idempotent helper `helpers/flux-local-host/install-sdxl-controlnets.ps1`. Acceptance: `ControlNetLoader` lists the weights; each smoke-renders through a minimal SDXL ControlNet workflow on BigLust/Juggernaut.
- [ ] **T02** Verify depth preprocessors (DepthAnything/Zoe/MiDaS) produce a usable depth map from a reference photo on the host (write to git-ignored `artifacts/tmp/`). Acceptance: depth map written + visually reviewed; feeds T04.
- [ ] **T03** Coordinate with canonical B-117: confirm the OpenPose ControlNet composition path (B-117) is separate from this depth/canny path; do not duplicate its workflow builder or UI. Acceptance: no overlapping code; this item references B-117's route in `workflow.md` C2.

## Phase 2 — App: depth/canny ControlNet render capability ("layout reference" operation)
- [ ] **T04** Add a layout-reference input to the scene-image render path: request variant carrying `{ LayoutReferenceImageId, StructureType (Depth|Canny), ControlStrength }`; persist on the image record for provenance. Acceptance: round-trip test; ValidateImage requires a structure source when structure type set.
- [ ] **T05** `ComfyUIImageClient`: add a ControlNet workflow builder (SDXL-family) — `LoadImage(layout) → <preprocessor per StructureType> → ControlNetApplyAdvanced → KSampler`, checkpoint by family. Acceptance: workflow-builder unit tests (nodes, strength, structure type).
- [ ] **T06** Model/capability gate: visual-strategy token (e.g. `ControlNet`) declared on SDXL local rows (BigLust/Juggernaut/Pony Realism) via an idempotent dbq command (`controlnet-configure`); resolver fail-fast (no fallback to plain render when a structure type is requested and unsupported). Acceptance: gating tests (≥5 no-fallback cases).
- [ ] **T07** Service/job: enqueue path + job handler executing the depth/canny ControlNet render (mirror scene-image render handlers), registered in `Program.cs`. Acceptance: service validation tests + stub handler test.
- [ ] **T08** (Optional, recommended) Combine **regional IP-Adapter identity** into the depth/canny ControlNet render (per-character mask + ref regions) so identity lands per region in the structured shot. Acceptance: two-character identity+structure proof (visual review).

## Phase 3 — UI
- [ ] **T09** Surface in Studio / image-editor (legacy + composer): a "Layout from structure (Depth/Canny)" action — layout-reference image picker, structure type, strength slider, model dropdown filtered to `ControlNet`-capable models. Disabled w/ explanatory note when unsupported. Keep the OpenPose path (B-117) as its own action. Acceptance: UI contract test; razor rules followed.

## Phase 4 — Workflow + docs + validation
- [ ] **T10** Confirm `workflow.md` reflects shipped app surface (mark routes Executable/B-116/B-117/B-119 as they land); update `plan.md` Status and the backlog B-119 row on completion. Acceptance: docs match reality.
- [ ] **T11** Full suite green; fix forward. Then live validation on a real beat (Route C1: dictated 2-char reclining blocking from a reference → depth ControlNet → visual review per image, honest pass/fail). Update tasks/status when validated.
