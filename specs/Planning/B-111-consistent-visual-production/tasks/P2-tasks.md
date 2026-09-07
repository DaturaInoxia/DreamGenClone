# P2 — Reference Bootstrap — Task List

**Phase goal (spec O1):** a fictional character, an outfit, and a location each get an approved
canonical reference **entirely in-app** — text description → candidates → curate → expand → promote —
with **no LoRA dataset and no uploaded photo**.
**Absorbs:** B-108 (which is already fully designed and implementation-ready).

> **This phase EXECUTES B-108.** Do not redesign. Authoritative artifacts:
> - `specs/Planning/B-108-reference-bootstrap-studio/spec.md` (requirements)
> - `specs/Planning/B-108-reference-bootstrap-studio/plan.md` (reuse map + design decisions D1–D4)
> - `specs/Planning/B-108-reference-bootstrap-studio/tasks.md` (the task breakdown to dispatch)
> - `specs/Planning/B-108-reference-bootstrap-studio/ui-contract.md`
> B-108's design is consistent with B-111 contracts C1 (Reference Library) and C2 (Reference Bootstrap).

## B-108 design anchors (verified, do not re-litigate)
- **D1** — new `ReferenceBootstrapBatch`, decoupled from `CharacterLoraDataset` (which hard-requires
  `DatasetId`). Reuses the durable production graph types. (FR-C1-05, FR-C2-06)
- **D2** — candidate curation = additive nullable `SceneAsset` fields (`CandidateBatchId`,
  `CandidateDecision`, `CandidateNotes`, `CandidateSourceAssetId`). No new join table.
- **D3** — promotion is always a **copy** into the existing identity-pack / wardrobe storage; the
  candidate is never mutated/deleted (audit history).
- **D4** — location gets a minimal, distinctly-named `ReferenceBootstrapLocationProfile` /
  `ReferenceBootstrapLocationReference` (NOT `LocationProfile` — Phase 3/P5 reserves that name).

## B-111 additions layered on B-108
- **Mandatory frozen text block** on every reference kind before approval (FR-C1-02) — the
  `FrozenTextBlockEditor` component (`ui/FrozenTextBlockEditor.md`).
- **Face reference is a view set**, each view angle-tagged (FR-C1-04) — P3 selects between them.
- **Candidate batches execute through the Run** (PATH B / `ProductionWorkload`), reusing P1's
  `CandidateGrid` + `RunTray`.
- **Grep proof at gate:** no bootstrap path references `CharacterLoraDataset` (FR-C2-06).

## Dispatch discipline (MANDATORY — see tasks/README.md + memory speed rules)
Every subagent dispatch to `GPT-5.6 Luna (copilot)` MUST:
- FIRST `Get-Process DreamGenClone | Stop-Process -Force`.
- Build ONLY the affected project(s), NEVER `dotnet build DreamGenClone.sln`.
- Test ONLY a tight `--filter`, NEVER the full `dotnet test`.
- Stop and report if any command exceeds 90s.
Coordinator verifies with fast greps/reads, not full builds.

## Ordered dispatch units (from B-108 tasks.md, grouped for one-task-per-dispatch)
1. **P2-U1 — Domain + persistence:** `ReferenceBootstrapBatch` record + `SceneAssetCandidateDecision`
   enum + the 4 additive `SceneAsset` fields + `ReferenceBootstrapLocationProfile/Reference`;
   additive SQLite schema; repository. (B-108 domain/persistence tasks)
2. **P2-U2 — Candidate generation:** batch → staged Run (PATH B) → candidates as `SceneAsset` rows
   tagged with batch id + `Undecided`. Reuses `SceneAssetPromptCompiler` + `IImageGenerationClient`.
3. **P2-U3 — Curation:** accept/reject/notes persisted; `CandidateGrid` (virtualized, thumbnails).
4. **P2-U4 — Expansion:** face angles / location framings from an accepted candidate (reuse
   `SceneAssetProfilePackJobHandler.AngleEdits` pattern).
5. **P2-U5 — Promotion:** copy accepted set → identity pack (Face view set) / wardrobe look / location
   profile; mandatory `FrozenTextBlockEditor` before approve; candidate untouched.
6. **P2-U6 — UI screens:** the four Bootstrap screens (Describe / Curate / Expand / Promote) +
   Reference Library, each single-purpose, flowing with preserved state (C7 §3). Reuse shared
   components; do not build a mega-form.
7. **P2-U7 — Gate:** acceptance A1/A2 (fictional character + outfit + location reach approved
   references, no LoRA, no photo); scorer confirms the promoted face view set is the same person
   (first real golden-set identity scoring, deferred from P0); grep proof no `CharacterLoraDataset`;
   `tools/e2e/` flow; targeted build+tests green; **your usability verdict** (P9).

> Each unit is hardened to file-level detail (from B-108 tasks.md) immediately before its dispatch,
> per G3. Start with P2-U1.
