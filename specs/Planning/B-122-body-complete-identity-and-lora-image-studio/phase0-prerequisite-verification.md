# B-122 Phase 0 — prerequisite verification (B122-000a … B122-000d)

**Date:** 2026-09-21 · **Method:** read-only code investigation (no files modified)
**Verdict:** three of four prerequisites hold **as schema**, and two hard gaps must be closed inside Phase 0 —
there is **no BodyCard owner** and **no `Body` target kind** (no plan rows, no handlers, no keys, and the angle
service is typed to the face view axis).

---

## B122-000a — body/descriptor schema round-trip ✅ present (with one precision note)

**Symbols**

| Symbol | File |
|---|---|
| `SceneImageReferenceBodyView { Front=1, ThreeQuarterLeft=2, ThreeQuarterRight=3, ProfileLeft=4, ProfileRight=5 }` | `DreamGenClone.Domain/RolePlay/CharacterImageIdentityModels.cs:37` |
| `SceneImageReferenceBodyState { Clothed=1, Unclothed=2 }` | same, `:49` (doc: an **asset-state contract, never inferred** from prompt/filename/pixels) |
| `CharacterImageIdentityPackScope { FaceOnly=1, BodyComplete=2 }` | same, `:56` |
| `SceneImageReferenceAssetKind { Face=1, FullBody=2, Wardrobe=3 }` | same, `:15` |
| `SceneImageReferenceAsset.BodyView` / `.BodyState` / `.ViewDescriptorJson` | same, `:205` / `:208` / `:211` |
| `CharacterImageIdentityPack.PackScope` / `.CanonicalFullBodyAssetId` | same, `:176` / `:170` (default scope `FaceOnly`) |
| `ReferenceViewDescriptor` (`Axis`, `YawDeg`, `PitchDeg`, `BodyRotationDeg`, `BodyPositionKey`, `Label`, `FaceCanonicalSlot`, `BodyCanonicalSlot`, `ToJson`/`FromJson`/`Validate`) | `DreamGenClone.Domain/RolePlay/ReferenceViewDescriptor.cs:19` |

**Store A — `CharacterImageIdentityRepository`** (the only store of `SceneImageReferenceAsset`, table
`SceneImageReferenceAssets`): all three columns are in CREATE TABLE (`:501–503`), have ALTER-if-missing guards
(`:553–570`), are written by INSERT (`:350`, `InsertAssetAsync :675`, params `:761–763`) and read by the asset
SELECT (`AssetSelect :608`, ordinals **16** `ViewDescriptorJson`, **17** `BodyView`, **18** `BodyState`, `:723–725`).
Supersede copies all three (`:262–283`). `ValidateAsset` (`:776–799`) enforces the contract: `FullBody`
**requires** `BodyState`, non-`FullBody` must not carry body fields, and a non-empty `ViewDescriptorJson` is
parsed by `ReferenceViewDescriptor.FromJson`.

**Precision note (not a gap, but binding):** there is **no UPDATE path** for the three columns — they are
insert-time only, i.e. immutable after upload (which matches the asset contract: replacing bytes creates a new
asset row). Also `ApproveAsync` (`:150–160`) writes only `Status`/`DescriptorSnapshotJson`/`CanonicalFaceAssetId`/
`ApprovedUtc` — **`PackScope` and `CanonicalFullBodyAssetId` must be set on the draft** (`UpsertDraftAsync`) before
approval, because approval will not set them (see B122-000d).

**Store B — `SceneAssetRepository`** (table `SceneAssets`) duplicates the same three columns (`:918–920`,
ALTER `:979–981`, INSERT `:394`/`:430–432`, ordinals **42/43/44**). No `UPDATE SceneAssets SET` exists at all and
no production code assigns them: the parallel copy is write-capable but unpopulated. Body data lives only in
`SceneImageReferenceAssets`. **Phase 0 must not start writing the second copy.**

**Tests today:** `CharacterImageIdentityRepositoryTests` (16 tests) cover packs, face-view round-trip,
quality, approve/supersede guards — **no test asserts `BodyView`/`BodyState`/`ViewDescriptorJson` round-trip and
no test mentions `PackScope`/`BodyComplete` at all.** Phase 0 writes those.

## B122-000b — B-121 pipeline readiness ⚠️ ready as machinery, missing every body-specific piece

**Present and reusable** (signatures verified): `CreateBuildAsync(characterProfileId, batchId, targetKind)`,
`CompleteStepAsync` / `FailStepAsync` / `SkipStepAsync` / `ReRunStepAsync` / `SetFrontContainerAsync` /
`SetCanonicalFrontAsync` / `ListStepsAsync` (`ICharacterIdentityBuildService`); the plan is resolved per kind via
`ICharacterIdentityStepPlanService.GetPlanAsync(kind)` → `ICharacterIdentityBuildRepository.ListStepPlanAsync`
(table `CharacterIdentityStepPlans`, PK `Kind+Step`); templates + settings via
`IWorkflowTemplateService.ResolveAsync(key, characterProfileId)` / `ResolveSettingsAsync` (character override →
global → fail fast naming the key); the shared same-image edit primitive
`IImageEditingClient.EditAsync` / `EditWithReferencesAsync(model, sourceImage, fileName, instruction, references)`;
promotion via `ICharacterIdentityPromotionService.GetReadinessAsync` / `PromoteAsync`.

**Missing (all inside Phase 0):**

| Gap | Evidence |
|---|---|
| **No `Body` step plan is seeded** | `SeedStepPlansAsync` seeds `Face` only (`CharacterIdentityBuildRepository.cs:528–537`); `GetPlanAsync(Body)` throws today (asserted by `CharacterIdentityStepPlanServiceTests.UnseededKind_FailsFastNamingTheKind`) |
| **No body handler keys** | `CharacterIdentityBuildHandlers.Known` holds face handlers only |
| **Template keys are hardcoded, not kind-namespaced** | `identity.front.generate` (`CharacterIdentityFrontService.cs:28`), `identity.garment.remove` (`CharacterIdentityGarmentService.cs:17`), `identity.angle.*` (`CharacterIdentityAnglesService.cs:494–501`). The plan's `TemplateKey` column is read **only by the plan-service test**; no production handler consults it |
| **No body settings** | `ReferenceWorkflowSettings` has no body columns, and no UI editor for any of its columns |
| **The angle service is typed to the face axis** | `ICharacterIdentityAnglesService` takes `CharacterIdentityAngleView` everywhere, so body per-view run/accept needs the body axis plumbed (a view-axis parameterisation), not a second service |

## B122-000c — the single owner for `BodyCard` ❌ does not exist; decision required

`BodyCard` exists **nowhere** in code — only the placeholder text in `CharacterStudio.razor:542` and spec prose.
The candidates found, with their real state:

| Candidate | State |
|---|---|
| `CharacterImageIdentityPack.DescriptorSnapshotJson` | **Live** but opaque free text (`CharacterIdentity.razor` edits it as a string) — no typed body schema |
| `CharacterBodyProfileVersion` + `CharacterBodyAssetBinding` (tables `CharacterBodyProfileVersions` / `CharacterBodyAssetBindings`) | Right shape (Draft/Approved/Superseded + snapshot + typed bindings) but **production-dead**: `CreateBodyProfileDraftAsync` / `AddBodyAssetBindingAsync` / `ApproveBodyProfileAsync` are called only from its own tests; the only production reader is `CharacterAssetCatalogService.LoadVersionsAsync`. `ProductionMediaRepository` FKs `BodyProfileVersionId` |
| `CharacterProfile.Description` | character-owned free text, no body schema |
| `SceneAsset.BodyView/BodyState/ViewDescriptorJson` | columns exist, no production writer |

**Decision needed (B122-000c):** which one becomes the BodyCard's single mutable owner — revive the appearance
version aggregate (`CharacterBodyProfileVersion`, typed fields inside its snapshot), extend the live pack
descriptor into a typed schema, or add a dedicated character-owned BodyCard. Phase 0 must not create two mutable
body sources; the three unowned/unreachable stores above stay untouched either way.

## B122-000d — pack approval/supersede for `BodyComplete` ⚠️ present but unreachable

`CharacterImageIdentityRepository.ApproveAsync` (`:115–166`) → `ValidateApprovalSet` (`:168–211`) **already
contains the whole `BodyComplete` contract** — the 5×2 matrix (both states × five canonical views), the
`CanonicalFullBodyAssetId` presence check, and the "must be the approved unclothed `Front`" check (`:185–210`).
It is dead only because of the early return at **`:182`**
(`if (pack.PackScope != CharacterImageIdentityPackScope.BodyComplete) return;`) and because **nothing in
production ever sets `PackScope = BodyComplete` or `CanonicalFullBodyAssetId`**: `CreateDraftPackAsync` takes no
scope, so packs are always `FaceOnly`; `ApprovePackAsync` cannot set either field.

**Exact change needed:** (1) let a caller create a draft with an explicit scope (and set the scope + canonical
full-body pointer on the draft through `UpsertDraftAsync` before approval), (2) supersede must keep carrying
scope + canonical id (it already does, `:236–245`), (3) the face half of the contract stays unconditional, so a
`BodyComplete` pack is a strict superset of today's five face slots.

---

## Consequences for the Phase 0 slice order

1. **Section A cannot start before B122-000c is decided** (B122-002 persists the BodyCard on that owner).
2. **Section B needs a view-axis parameterisation**, not just new rows: body views are
   `SceneImageReferenceBodyView`, so either the angle service gains a second axis or a body view service shares
   the same gate/attempt records. The existing "no sweep, one view per request" contract carries over unchanged.
3. **Section B also needs the template key to actually come from the plan** (`TemplateKey`) or explicit
   `identity.body.*` keys — today the key is a private const in each face service.
4. Everything else in Sections C–E reuses the B-121 machinery that item 3 (`debug/049`) just completed:
   the measurement service, the quality metric, the override discipline and the promotion readiness shape.
