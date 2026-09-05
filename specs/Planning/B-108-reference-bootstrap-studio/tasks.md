# B-108 Tasks — Reference Bootstrap Studio

**Execution rule:** complete in order unless marked `[P]`. Check a task only after its tests and
evidence are recorded. Every task ends with the affected tests green.

**Mandatory reading before the first Razor edit:** `.github/instructions/razor-editing.instructions.md`.

**Repo rules that bind every task:** no fallback branches, no hardcoded runtime defaults, missing
configuration fails fast with an explicit diagnostic, no `git restore`/reset, all tests green before
a task is checked. This package must never reference `CharacterLoraDataset` or
`CharacterLoraDatasetMember` (D1) — grep for both before closing Phase H.

---

## A. Batch And Candidate Persistence

- [ ] B108-001 Add `ReferenceBootstrapBatch` (target character/location id, target asset type,
  description, requested count, capability profile/cell, provider endpoint, dispatch policy, cost
  basis, created time) — no `DatasetId` field, no reference to LoRA types.
  *File:* `DreamGenClone.Domain/RolePlay/ReferenceBootstrapModels.cs` (new)
- [ ] B108-002 Add `SceneAssetCandidateDecision` enum (`Undecided`/`Accepted`/`Rejected`) and the
  four additive nullable fields on `SceneAsset`: `CandidateBatchId`, `CandidateDecision`,
  `CandidateNotes`, `CandidateSourceAssetId`.
  *File:* `DreamGenClone.Domain/RolePlay/SceneAssetModels.cs`
- [ ] B108-003 Add the additive SQLite columns and read/write mapping for B108-002, following the
  existing `SceneAsset` repository schema pattern.
  *File:* `DreamGenClone.Infrastructure/RolePlay/` (scene asset repository)
- [ ] B108-004 Add repository queries: candidates by batch id, by target (character+type or
  location profile), and filtered by decision.
  *File:* same as B108-003
- [ ] B108-005 [P] Add real-SQLite tests for batch persistence, additive-field round-trip, and the
  three query shapes.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## B. Candidate Generation

- [ ] B108-006 Add `IReferenceBootstrapService.CreateBatchAsync` — compiles the description via
  `SceneAssetPromptCompiler.Compile` for the resolved model, dispatches N generation calls through
  the existing image-generation client stack, and persists N `SceneAsset` rows with
  `CandidateDecision = Undecided` sharing the new batch id.
  *File:* `DreamGenClone.Web/Application/RolePlay/ReferenceBootstrapService.cs` (new)
- [ ] B108-007 Fail fast when no enabled qualified image model resolves for the target's asset
  type, naming the missing configuration.
  *File:* same as B108-006
- [ ] B108-008 [P] Add tests: N candidates produced with correct target/decision/batch linkage;
  missing-model failure; compiled prompt/model recorded per candidate (FR8-018).
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## C. Curation

- [ ] B108-009 Add accept/reject/note operations that mutate only the additive fields — the asset
  row and bytes are never deleted or altered otherwise.
  *File:* `ReferenceBootstrapService.cs`
- [ ] B108-010 [P] Add tests: decision transitions, note persistence, rejected candidates remain
  queryable and unchanged.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## D. Expansion

- [ ] B108-011 Add same-image expansion: given an `Accepted` candidate and a requested view,
  dispatch one `IImageEditingClient.EditAsync` call (source = candidate bytes, instruction = the
  view-specific canned prompt) and persist the result as a new candidate in the same batch with
  `CandidateSourceAssetId` set to the source.
  *File:* `ReferenceBootstrapService.cs`
- [ ] B108-012 Define the per-target-type view/instruction sets: face angles (reuse the existing
  `SceneAssetProfilePackJobHandler.AngleEdits` prompts), body pose options, wardrobe angle/lighting
  options.
  *File:* same as B108-011
- [ ] B108-013 Reject expansion requests against a non-`Accepted` source candidate explicitly.
  *File:* same as B108-011
- [ ] B108-014 [P] Add tests: expansion produces a correctly-linked new candidate; non-accepted
  source rejected; each supported view/instruction set covered.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## E. Promotion — Character Face/Body And Wardrobe

- [ ] B108-015 Add Face/FullBody promotion: read the accepted candidate's bytes, call the existing
  `ICharacterImageIdentityService.UploadAssetAsync` with the correct
  `SceneImageReferenceAssetKind`, leaving the source `SceneAsset` untouched.
  *File:* `ReferenceBootstrapService.cs`
- [ ] B108-016 Add Wardrobe promotion: create a `CharacterWardrobeAssetBinding` on the character's
  open Draft `CharacterWardrobeLookVersion` (creating one if none is open), reusing the existing
  repository's draft-only mutation rule.
  *File:* same as B108-015
- [ ] B108-017 Reject promotion of a non-`Accepted` candidate explicitly, for both targets.
  *File:* same as B108-015
- [ ] B108-018 [P] Add tests: Face/FullBody promotion copies bytes correctly and leaves the
  candidate row intact; Wardrobe promotion creates the binding on the correct draft version;
  non-accepted rejection for both.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## F. Minimal Location Domain And Promotion

- [ ] B108-019 Add `ReferenceBootstrapLocationProfile` (id, name, description, status,
  supersession lineage) and `ReferenceBootstrapLocationReference` (id, profile id, ordinal,
  semantic role, `SceneAssetId`) — no coordinate frame, dimensions, or landmark fields.
  *File:* `DreamGenClone.Domain/RolePlay/ReferenceBootstrapModels.cs`
- [ ] B108-020 Add repository support: create/list profiles, draft-only reference mutation,
  supersede.
  *File:* `DreamGenClone.Infrastructure/RolePlay/` (new repository or extension)
- [ ] B108-021 Add Location promotion: create a `ReferenceBootstrapLocationReference` on the
  selected profile from an `Accepted` candidate.
  *File:* `ReferenceBootstrapService.cs`
- [ ] B108-022 [P] Add tests: profile creation, reference binding, supersede lineage, non-accepted
  rejection.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## G. Asset Manager UI

**All tasks in this section implement [ui-contract.md](ui-contract.md).**

- [ ] B108-023 Add the `Bootstrap` tab to the existing Asset Manager tab row without disturbing
  `Browse`/`Versions`/`Lineage` selection state (§1).
  *File:* `DreamGenClone.Web/Components/Pages/AssetStudio.razor`
- [ ] B108-024 Implement the target picker and generation form per §1, with disabled-state reasons
  stated inline.
  *File:* same as B108-023
- [ ] B108-025 Implement the candidate gallery grouped by batch per §2: decision badges, source
  tags for expansions, per-thumbnail actions, rejected candidates dimmed but visible.
  *File:* same as B108-023
- [ ] B108-026 Implement compare mode per §3, matching the B-106 compare-mode interaction pattern.
  *File:* same as B108-023
- [ ] B108-027 Implement the expand panel per §4 with target-scoped view options and the
  same-image-only expectation note.
  *File:* same as B108-023
- [ ] B108-028 Implement the four target-specific promote actions per §5, including the
  post-promotion confirmation and destination link.
  *File:* same as B108-023
- [ ] B108-029 Implement the minimal location profile create/select control per §6.
  *File:* same as B108-023
- [ ] B108-030 Add `CandidateBatchFilter`/`CandidateDecisionFilter` to the existing filter set per
  §7, applying across all target kinds.
  *File:* same as B108-023
- [ ] B108-031 Implement the empty/loading/failure states per §8 for every region.
  *File:* same as B108-023
- [ ] B108-032 Implement accessibility/focus per §9 and the state keys per §10.
  *File:* same as B108-023
- [ ] B108-033 [P] Add source-contract/component tests and Razor diagnostics for the tab, gallery,
  compare selection, expand panel, and promotion gating.
  *File:* `DreamGenClone.Tests/RolePlay/` (new Bootstrap UI contract test file)
- [ ] B108-034 [P] Run Playwright acceptance at 1440x1000 and 390x844 covering generate → curate →
  expand → promote for at least one Character and the Location target. Assert no horizontal
  overflow, no overlapping controls, no console/page errors.
  *File:* established Playwright harness

---

## H. Validation

- [ ] B108-035 Run affected focused tests, the full solution build, and the full test suite. Record
  exact counts.
- [ ] B108-036 Run Razor diagnostics on every touched component and record a clean result.
- [ ] B108-037 Execute all nine acceptance scenarios from `spec.md` in the running application.
  Record session/batch/candidate ids and outcomes.
- [ ] B108-038 Grep the full new/changed code for `CharacterLoraDataset` and
  `CharacterLoraDatasetMember`; confirm zero matches (D1).
- [ ] B108-039 Record completion evidence and note the deliberate location-domain naming boundary
  (FR8-015) for whoever scopes B-032 Phase 3.

---

## Dependency notes

- Phase A blocks Phase B; Phase B blocks C/D/E/F; Phase G can begin once B–F produce at least one
  candidate and one promotion path each.
- Cross-image face-graft (feeding an already-approved face into an edit of a separately generated
  body) is explicitly out of scope here and depends on B-106 Phase A (B106-001→007). Nothing in
  this package is blocked by that dependency — same-image expansion and independent generation both
  work without it.
