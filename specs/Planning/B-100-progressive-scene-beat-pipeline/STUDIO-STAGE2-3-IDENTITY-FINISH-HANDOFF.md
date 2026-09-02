# Handoff — Production Studio Stage 2 (Identity) & Stage 3 (Edit/Finish)

> **For:** the next planning agent. Produce the full detailed plan + implementation
> (spec → plan → tasks) for the two missing Production Studio stages.
> **Author:** Copilot session, 2026-09-02.
> **State of this doc:** handoff / scoping input. It is NOT the detailed plan.
> **Companion (approved design):** `production-studio-image-workflow.md` in this folder.

---

## 1. TL;DR / the gap

The B-100 **Production Studio** (`/roleplay/studio/{sessionId}/{interactionId}`) is only
half-built as a *staged* workflow:

- ✅ **Stage 1 Compose** — implemented (prompt-only or "+ Identity" one-pass).
- ✅ **Stage 4 Review/Approve** — implemented (shortlist / reject / approve / purge / promotion).
- ❌ **Stage 2 Identity** — designed + required by spec, **not wired** in the studio.
- ❌ **Stage 3 Edit/Finish** — designed + required by spec, **not wired**; studio says *"Unavailable:
  finish-stage source-image editing follows the identity boundary."*

The stepper shows `1 Composition (current) / 2 Identity / 3 Finish`, but rows 2 and 3 are inert.
There are **no** `CreateIdentity` / `CreateFinish` stage services, and no studio "Apply Identity",
"Run Edit", or "record identity skip" action exists. Persistence (stage enum, `IdentityPolicy`,
`IdentitySkipReason`, attempt lineage, approval decisions) is **already in place** — only the
stage *execution flow* is missing.

Target workflow (approved): **Compose a safe positional base → Apply required character
identities or record an explicit skip → Run one or more finishing edits → Approve one exact
version.** (B-100 README D-100-20; `production-studio-image-workflow.md`.)

---

## 2. What the requirements already say (authoritative — do not relax)

`specs/Planning/B-100-progressive-scene-beat-pipeline/spec.md` → **User Story 10 (P1)**,
acceptance scenarios #3/#4 and **FR-052 → FR-055**:

- **FR-052:** Every scene-image generation *or edit* is an immutable attempt assigned to exactly
  one production group and stage.
- **FR-053:** Stages distinguish `Composition`, `Identity`, `Finish`; successful execution is not
  approval.
- **FR-054:** Composition is safe + position-focused; identity and finishing edits **consume its
  exact stored output** rather than regenerate story semantics.
- **FR-055:** Identity is **required** for known visible characters unless an **explicit persisted
  user skip and reason** exist.
- Acceptance #3: *Visible known characters require approved identity packs before the identity
  stage, unless the user records an explicit skip decision and reason.*
- Acceptance #4: *Finishing edits create immutable child attempts and may branch from any
  completed eligible attempt.*

Approved design details: `production-studio-image-workflow.md` → **Stage 2**, **Stage 3**,
**Persistence model**, **First Implementation Boundary** ("Identity-reference editing and
automated stage progression follow as the next slice"), and **Acceptance**.

Ownership: B-100 owns the Beat/Moment semantics and the studio *workflow* (groups/stages/
attempts/approval). B-032 owns image *execution* (generation, identity application, source-image
edits, stored bytes, assets). The workflow doc's Ownership Boundary table is binding. The concrete
mechanism work is described in B-100 `plan.md` Phase 9A as *"the next B-032 slice"* — the planning
agent must reconcile that statement with the FRs above (the studio stage-flow is B-100/US10-scoped;
the mechanism behind it is B-032).

---

## 3. Verified current state (2026-09-02, branch `development` @ `72aa661`, merged + pushed)

### 3a. Studio page (implemented portion)
`DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` (+ `.razor.cs`):
- Beat Catalogue rail → Beat Production Plan → Moment set → Moment enrichment; a production group
  is pinned to one exact current Moment enrichment + POV.
- Composition stage controls: **Generate Prompt**, **Generate Composition** (prompt-only),
  **"+ Identity (one-pass)"** (regional IP-Adapter identity-conditioned T2I folded into the same
  render), **Regenerate Sibling**; editable prompt with word/token count; image-model selector.
- Attempt strip grouped by stage enum; per-attempt **shortlist / reject / approve / purge**;
  current-approval badge. Approval writes an append-only decision.
- Stage stepper rows 2/3 inert; Finish control hard-codes "Unavailable…".

**Important:** the "+ Identity one-pass" and the legacy "Render with Identity" card are
**identity-conditioned text-to-image**, NOT the required identity-after-composition source-image
edit stage. Every authoritative doc says do not substitute it
(`production-studio-image-workflow.md` Stage 2 + "First Implementation Boundary";
B-100 `plan.md` line ~125; roadmap item 7).

### 3b. Persistence / domain — already ready (reuse, do not re-model)
- `DreamGenClone.Domain/RolePlay/SceneImageProductionGroup.cs` — stage enum
  (`Composition`/`Identity`/`Finish`), `IdentityPolicy` (`Required`/`SkippedByUser`),
  `IdentitySkipReason`, POV + full B-100 lineage, `CurrentApprovedDecisionId`.
- `SceneImageRecord` — `ProductionGroupId`, `ProductionStage`, `Disposition`,
  lineage/typed-reference snapshot, `BytesPurgedUtc`.
- Append-only `ApprovedSceneFrameDecision` (Approved / Superseded / Revoked, version, checksum).
- Repositories + additive SQLite schema exist (B-100 tasks T066/T067/T069/T084-T087/T100 done).
- `SceneImageProductionService` currently creates groups with `IdentityPolicy.Required`; there is
  **no skip control surfaced** in the studio yet.

### 3c. Reusable mechanism components (verified present)
- **Identity packs:** `CharacterImageIdentityPack` (approved face + angled reference assets,
  consent/provenance, descriptor snapshot) via `CharacterIdentity.razor` (`/characters/identity`),
  `ICharacterImageIdentityService` / repository; storage under
  `DreamGenClone.Web/data/scene-images/identity/{profileId}/...`.
- **Identity-conditioned T2I client (one-pass mechanism):** `IIdentityConditionedImageClient` +
  `ComfyUIIdentityConditionedClient`, `RunPodServerlessIdentityClient`,
  `IdentityConditionedImageClientDispatcher`; used by `SceneImageRenderingJobHandler`. Regional
  IP-Adapter with a **near-frontal guardrail** (10/12 pass; angled identity NOT proven). This is
  the B-032 Phase 2 P2-019/020 result — see open tasks below.
- **Source-image edit (Qwen) stack:** `IImageEditingClient.EditAsync(model, source, fileName,
  instruction, ct)` with `ComfyUIImageEditingClient` / `RunPodServerlessEditingClient` /
  `ImageEditingClientDispatcher`; `SceneImageEditingJobHandler`; edit-session compilation
  (`SceneImageEditCompilationService`, `SceneImageEditCompilationJobHandler`,
  `SceneImageEditRepository`, `SceneImageEditCompilationAttempt`); UI = `SceneImageEditor.razor`
  (`/roleplay/image-editor/{sessionId}/{interactionId}/{sourceImageId}`).
- **Compilers / briefs:** `DeterministicMultimodalMediaCompiler` (brief-level appearance is B-105
  Option A — deferred until compiler has character access), `SdxlSceneImagePromptBuilder` +
  `BuildCanonicalCharacterAppearanceBlock` (B-105 Option B — implemented), prompt-compiler
  standards in `.github/instructions/scene-image-prompt-compiler-standards.instructions.md`.

### 3d. Verified-but-NOT-in-app (critical for Stage 2 mechanism choice)
A **reference-based Qwen face-edit** pipeline is proven **only via standalone proof harnesses**,
not wired in the current app:
- Repo memory `base-then-edit-pipeline.md` (2026-08-30): the **only** user-approved image came
  from: (1) SFW position base (Seedream/gpt-image-2), (2) **apply Becky/Dean faces via
  Qwen-Image-Edit WITH reference images**, (3) NSFW/ finish edit via Qwen. Includes hard-won
  rules: apply faces LAST in any chain; a later pose/angle edit washes the applied face; CFG 1.0 /
  8 steps correct for the AIO NSFW checkpoint; single-object phrasing to avoid duplicates.
- Proof harnesses: `specs/image-generator-tests/baseline/`,
  `specs/image-generator-tests/identity-edit/`, `specs/image-generator-tests/qwen/`.
- **The current app `IImageEditingClient` has NO reference-image parameter** and `SceneImageEditor`
  has no identity-reference UI. An earlier `EditWithReferencesAsync`/`SceneImageHeadAngleResolver`
  wiring (repo memory `identity-edit-option1.md`, 2026-08-30) appears to have been removed in the
  later serverless refactor — **verify before trusting that note**. This is a genuine open
  mechanism decision for the planning agent (see §5).

---

## 4. Hard constraints (bind the plan)

1. **No substitution:** identity-conditioned one-pass T2I must not stand in for the identity-
   after-composition source-image edit stage.
2. **No silent omission:** missing pack → explicit failure naming the character; only bypass is a
   persisted skip with reason (FR-055).
3. **Immutable children:** identity and finish edits create immutable child attempts of an exact
   parent; branch from any completed eligible attempt; never overwrite.
4. **No RP-prose re-analysis** in the image path; compile from the enriched Moment / production
   records only (FR-054).
5. **Snapshots:** per-attempt provider/model/settings, compiled prompt, source checksum, and
   identity references are persisted.
6. Repo-wide: **no fallback / no hidden defaults / fail-fast**, UI-backed persisted policy only,
   no hardcoded cleanup. Applies to any new behavior.
7. RP-engine files are out of scope; this work is scene-image/studio + mechanism only.

---

## 5. Open design/mechanism questions the planning agent must resolve (not answered here)

1. **Stage 2 mechanism:** How does the app apply identity-after-composition as a *source-image
   edit*? Candidates to evaluate against evidence:
   (a) re-introduce reference-based Qwen image editing into the app editing path (proven externally
   in `base-then-edit-pipeline.md` + `specs/image-generator-tests/identity-edit/`), or
   (b) Route A from `b032-phase2-identity-notes.md` (run the Qwen edit, then a face-fix pass
   through the regional IP-Adapter/PuLid identity path as i2i with denoise<1, canonical face as
   ref), or (c) another evidence-backed approach. Must produce Stage-2 child attempts via a real
   source-image edit — not a one-pass generation.
2. **Stage 3 Finish:** reuse the existing Qwen edit stack as Finish-stage attempts; how to gate
   **adult-capable** edits on the configured editor's capability; branch-from-composition when
   identity was explicitly skipped.
3. **Skip-with-reason UX:** where the `SkippedByUser` decision is recorded, how it is surfaced and
   persisted, and what downstream gating it enables.
4. **B-032 Phase 2 coupling:** whether Stage 2 should consume the (still-open) P2-023 identity
   request compiler / multi-actor assignment, and how the near-frontal-only identity proof limits
   Stage 2 acceptance.
5. **Split/ownership:** whether this is one new backlog item (e.g. B-106) that spans B-100 US10
   (workflow) + B-032 (execution), and how its spec/plan/tasks are sequenced against the open
   B-032 exit tasks in §6.

---

## 6. Dependencies / exit gates to sequence against (do not silently ignore)

- **B-032 Phase 1B (vision-aware editing) acceptance OPEN:**
  `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/tasks.md`
  P1B-009/010 (corpus quality/latency), P1B-036 → P1B-047 (deployment/manifest, Qwen-VL pod,
  capability cutover, frozen corpus through the app, adult-analysis acceptance, single compiler
  source, exit gate).
- **B-032 Phase 2 (identity) OPEN:**
  `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/tasks.md`
  P2-023 (identity request compiler, exact pack versions; full multi-actor assignment deferred),
  P2-027 (handler/provenance tests), P2-028 (matrix persistence/reporting), P2-029 (app path over
  frozen cases), P2-030 (LoRA decision), P2-031, P2-032 (exit gate).
- **B-100 open acceptance tasks** (can run in parallel; not blockers for Stage 2/3 planning):
  real-model corpus gates T003/T065/T082/T099/T119/T172, research-first Phases 0–2
  (T000/T005–T008, T021–T029, T041–T048), T153 legacy removal, T177 acceptance/backlog advance.

---

## 7. Expected deliverable shape (repo convention)

One coherent package the planning agent owns:
1. **Backlog item** (e.g. `B-106` — Production Studio Identity + Finish source-image stages) under
   `specs/Planning/backlog.md` with a scoped notes cell.
2. **spec.md** — mapped to US10/FR-052–055 + workflow-doc decisions; acceptance for Stage 2/3
   incl. skip-with-reason and adult-capable gating.
3. **plan.md** — slices, change surface (domain/app/infra/web), blast radius, sequencing against
   §6, explicit mechanism decision with evidence.
4. **tasks.md** — dependency-ordered ledger.
5. Update the roadmap (`multimodal-production-program-roadmap.md` Next-Work item 7) and B-103
   Part C notes when the slice is accepted/started.

---

## 8. Reference index

**Design / requirements**
- `specs/Planning/B-100-progressive-scene-beat-pipeline/production-studio-image-workflow.md`
  (approved design — starts here)
- `specs/Planning/B-100-progressive-scene-beat-pipeline/spec.md` (US10, FR-051–061)
- `specs/Planning/B-100-progressive-scene-beat-pipeline/plan.md` (Phase 9A + identity note)
- `specs/Planning/B-100-progressive-scene-beat-pipeline/tasks.md` (T066–T102 done)
- `specs/Planning/multimodal-production-program-roadmap.md` (Next-Work item 7)
- `specs/Planning/B-103-production-studio-composition-transparency/NOTES.md` (Part C)
- Backlog rows B-100, B-103, B-104, B-105

**Mechanism plans (B-032)**
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/` (editing)
- `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/` (identity packs + mechanism)
- `specs/Planning/B-032-scene-image-generator/README.md` (phase map + reconciliation)

**Verified evidence / memory (some notes may be stale — verify)**
- Repo memory: `base-then-edit-pipeline.md` (verified pipeline + ordering rules),
  `identity-edit-option1.md` (STALE re: app wiring — verify), `b032-phase2-identity-notes.md`
- Proof harnesses: `specs/image-generator-tests/baseline/`, `.../identity-edit/`, `.../qwen/`

**Key code**
- Studio: `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` (+ `.razor.cs`)
- Source editor: `DreamGenClone.Web/Components/Pages/SceneImageEditor.razor`
- Production service/domain: `Web/Application/RolePlay/SceneImageProductionService.cs`,
  `Domain/RolePlay/SceneImageProductionGroup.cs`, `SceneImageRecord.cs`
- Editing: `Application/Abstractions/IImageEditingClient.cs`,
  `Infrastructure/Models/{ComfyUIImageEditingClient,RunPodServerlessEditingClient,ImageEditingClientDispatcher}.cs`,
  `Web/Application/RolePlay/SceneImageEditingJobHandler.cs`,
  `Web/Application/RolePlay/SceneImageEditCompilationService.cs`, `Infrastructure/RolePlay/SceneImageEditRepository.cs`
- Identity: `Application/Abstractions/IIdentityConditionedImageClient.cs`,
  `Infrastructure/Models/{ComfyUIIdentityConditionedClient,RunPodServerlessIdentityClient,IdentityConditionedImageClientDispatcher}.cs`
- Compilers/prompt standards: `Web/Application/RolePlay/{DeterministicMultimodalMediaCompiler,SdxlSceneImagePromptBuilder,SceneImagePromptCompilers}.cs`,
  `.github/instructions/scene-image-prompt-compiler-standards.instructions.md`

---

*This handoff does not change any code. Stage advancement still requires the planned spec/plan/
tasks and their recorded exit evidence.*
