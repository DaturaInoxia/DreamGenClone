# B-148 Tasks — Location creation from the moment (in-flow Stage 0)

**Execution rule:** complete in order unless marked `[P]`. Check a task only after its tests and
evidence are recorded.

**Repo rules that bind every task:** no fallback branches, no hardcoded runtime defaults, missing or
invalid configuration fails fast naming the exact value, no `git restore`/reset, all tests green
before a task is checked. RP engine change control applies to every `Application/RolePlay/**`
change — this task list is the approved plan; execute it to completion without stopping between
tasks for re-approval.

**Design authority:** `plan.md` (D1-D19 + §6 UI plan) and `spec.md` (FR-B148-01 … FR-B148-15).
Any task that would contradict a decision is a defect in the task, not a new decision.

---

## 0. Verify before writing code (no code in this phase)

- [ ] B148-000a Confirm the settled blast radius in `plan.md` §5 is still accurate against the
  current tree — in particular that `SceneAssetTreeService` still builds location roots from
  `ReferenceBootstrapLocationProfile` (D19) and that `SceneImageProductionGroupRepository` still has
  no pragma-guarded ALTER mechanism (D18). Record any drift here.
- [ ] B148-000b Confirm `Setting.WorldLocation` does not collide with an existing
  `Setting`/`WorldDescription` consumer and that `Scenarios.PayloadJson` deserialization tolerates a
  new property on old payloads (null = not configured).
  *Evidence, no code change.*

---

## A. Domain model (D7, D8, D9, D13, D17)

- [ ] B148-001 Add `ParentAssetId`, `ScenarioLocationId`, and `ScenarioId` to `SceneAsset`.
  *File:* `DreamGenClone.Domain/RolePlay/SceneAssetModels.cs`
- [ ] B148-002 Add `WorldLocation` (a small `WorldLocationSetting` record carrying
  `AssetContainerId` + `RenderingDescription`) to `Setting`.
  *File:* `DreamGenClone.Web/Domain/Scenarios/Setting.cs`
- [ ] B148-003 Add the per-POV backdrop fields to `SceneImageProductionGroup` — the image id **and**
  its checksum, production version, and operator label (a bare id is not enough for render
  revalidation; mirror the fields `ReferenceApplicationSelection` carries).
  *File:* `DreamGenClone.Domain/RolePlay/SceneImageProductionGroup.cs`
- [ ] B148-004 Add the moment→location link record — keyed on **`MomentId`** (not
  `MomentEnrichmentId`, which changes per revision), carrying the container asset id, the resolved
  `ScenarioLocationId` (null = ad-hoc), and the link origin. One **current** link per moment.
  *File:* `DreamGenClone.Domain/RolePlay/` (new record, alongside the production group model)
- [ ] B148-005 [P] Add domain tests: `WorldLocation` deserializes null from a payload that lacks it;
  the link record is one-per-moment on upsert.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## B. Persistence (D18)

- [ ] B148-006 Add the three `SceneAssets` columns to the repository's existing pragma-guarded
  `ALTER TABLE` list, the SELECT/INSERT/ON CONFLICT projections, and the read mapping. No new table.
  *File:* `DreamGenClone.Infrastructure/RolePlay/SceneAssetRepository.cs`
- [ ] B148-007 Add the backdrop column to `SceneImageProductionGroups` — **and add the
  pragma-guarded ALTER TABLE migration mechanism itself** (the repository currently only has
  `CREATE TABLE IF NOT EXISTS`, so a new column never reaches an existing DB).
  *File:* `DreamGenClone.Infrastructure/RolePlay/SceneImageProductionGroupRepository.cs`
- [ ] B148-008 Add the moment→location link table + repository (`Get`, `Upsert` keyed on
  `MomentId`, one current row).
  *File:* `DreamGenClone.Infrastructure/RolePlay/` (new repository, schema alongside the group repo)
- [ ] B148-009 [P] Add repository tests: new columns round-trip on `SceneAssets`; the backdrop
  column survives against an already-migrated DB; the link upsert is idempotent per moment.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## C. Retire the B-108 location path (D3, D19)

- [ ] B148-010 Remove `PromoteAcceptedLocationAsync` from the interface and implementation, remove
  the `Location` arm of `MapReferenceKind`, and remove the location-batch plumbing
  (`ReferenceBootstrapBatch.LocationProfileId` handling on the location path).
  *Files:* `DreamGenClone.Web/Application/RolePlay/IReferenceBootstrapService.cs`,
  `DreamGenClone.Web/Application/RolePlay/ReferenceBootstrapService.cs`
- [ ] B148-011 Remove the location profile/reference methods from the bootstrap repository
  interface and implementation. **Leave the DB tables in place** (dead, harmless — D18; no
  destructive migration).
  *Files:* `DreamGenClone.Application/RolePlay/IReferenceBootstrapRepository.cs`,
  `DreamGenClone.Infrastructure/RolePlay/ReferenceBootstrapRepository.cs`
- [ ] B148-012 Drop the location branch from `ReferenceBootstrapPanel` (character/wardrobe only).
  *File:* `DreamGenClone.Web/Components/Assets/ReferenceBootstrapPanel.razor`
- [ ] B148-013 **Re-source the asset tree's location roots** from `SceneAsset(Type=Location)`
  containers, rendered with the new hierarchy (world parent → location children) and linking
  `/locations/{containerId}`. This is a hard dependency of the retirement (D19) and fixes the
  latent `/locations/{profileId}` break.
  *File:* `DreamGenClone.Web/Application/RolePlay/SceneAssetTreeService.cs`
- [ ] B148-014 [P] Update tests: remove location-promotion coverage from
  `ReferenceBootstrapRepositoryTests` (~line 523); re-source location roots in
  `SceneAssetTreeServiceTests` (drop the location-profile repository fakes).
  *Files:* `DreamGenClone.Tests/RolePlay/ReferenceBootstrapRepositoryTests.cs`,
  `DreamGenClone.Tests/RolePlay/SceneAssetTreeServiceTests.cs`

---

## D. Services (D14, D16)

- [ ] B148-015 Add `LocationBackdropSlotPrefill` — a sibling of `IdentityPackSlotPrefill` that fills
  the blueprint's **declared `Location` slot** from the group's backdrop fields, and **refuses by
  name** a blueprint that declares no location slot (never a silent drop).
  *File:* `DreamGenClone.Web/Application/RolePlay/ImageStep/` (new)
- [ ] B148-016 Add the resolution rule (D14): prefix-match the moment's `location` string against
  `Scenario.Location.Name`; token-suggest the spot's named image; no match → ad-hoc. Return a
  suggestion (never auto-applied) with the match reason and the `Sightline` context.
  *File:* `DreamGenClone.Web/Application/RolePlay/` (new resolver, alongside the moment-enrichment
  contracts)
- [ ] B148-017 Add the create-from-moment and bind services: create container
  (`CreateAssetAsync` + place-only seed), write the group backdrop + the moment link on bind.
  *File:* `DreamGenClone.Web/Application/RolePlay/` (new)
- [ ] B148-018 [P] Add tests: prefill fills the declared slot; prefill refuses an undeclared slot;
  resolution prefix-matches / token-suggests / falls to ad-hoc; bind writes backdrop + link and is
  operator-only; the link is keyed on `MomentId` and survives an enrichment-revision change.
  *File:* `DreamGenClone.Tests/RolePlay/`

---

## E. UI (D15, §6 UI plan)

- [ ] B148-019 Add the page-local `Location` stage tab (before `1 Composition`) to the Production
  POV workbench stepper — tablist semantics (`role="tab"`, `aria-selected`, `tabindex`,
  `HandleProductionStageKeyDown`), page-local stage list only (persisted `SceneImageProductionStage`
  untouched).
  *File:* `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`
- [ ] B148-020 Build the Stage-0 panel's three zones: suggestion card, search-as-cards (approved +
  named selectable; other cards disabled with reason + "Finish in Location Studio" link), and
  create/skip actions.
  *File:* `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` (or a new sibling component)
- [ ] B148-021 Wire the create-from-moment handoff: `CreateAssetAsync` with place-only seed →
  `/locations/{asset.Id}?return=<pov url>`; add the `?return=` back-link to `LocationStudio`.
  *Files:* `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`,
  `DreamGenClone.Web/Components/Pages/LocationStudio.razor`
- [ ] B148-022 Add the `WorldLocation` editor to the Scenario Editor's Setting/World section
  (container picker + rendering-description textarea, separate from the narrative
  `WorldDescription`).
  *File:* `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor`
- [ ] B148-023 Add the Composition auto-seed: on composer open, seed the declared `Location` slot
  from the group backdrop via the prefill; a model change re-seeds only after re-validation against
  the new model's slot plan (a model declaring no location slot shows a notice, never a silent
  drop).
  *File:* `DreamGenClone.Web/Components/Pages/CompositionComposer.razor`
- [ ] B148-024 [P] Add UI-contract tests: stage array now leads with `Location`; the search cards
  render disabled for unnamed/unapproved images; the "finish in Location Studio" link is present;
  the composer seeds the location slot from a bound backdrop and shows the notice when the model
  declares no location slot.
  *File:* `DreamGenClone.Tests/RolePlay/SceneImageStudioUiContractTests.cs`

---

## F. Full-suite validation and snapshot refresh

- [ ] B148-025 Run the full test suite (at minimum the RolePlay project) and confirm green —
  NFR-B148-04. Record the failing-then-fixed trajectory for the retirement + tree re-source in
  particular.
- [ ] B148-026 Refresh `dreamgenclone.snapshot.db` from the migrated dev DB so the additive columns
  are represented in the git-tracked snapshot (db-snapshot workflow).
- [ ] B148-027 Mark B-148 `implemented` in `specs/Planning/backlog.md` only after B148-025 is green
  and the smoke test on moment `a607b7bc` (search → create → name → approve → bind → compose)
  passes in the running app.
