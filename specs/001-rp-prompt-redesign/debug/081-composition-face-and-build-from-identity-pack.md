# 081 — The Composition step cannot bind a character's face or build from their identity pack

**Session:** 2026-10-02 · **State:** implemented, build + targeted tests green
**Scope:** Composition Composer (`/roleplay/studio/.../production/{groupId}/composition`) reference slots,
`ImageStepBlueprintFactory.ForPackIdentityComposition`, the native-reference render path, and the
identity-pack picker's place in the shared composer.

---

## Report

Operator, on the Composition Composer with the native-reference model (Qwen-Image-2.1) selected:

> "2.1 should allow for picking becky face and body but it says 'No approved build reference exists for
> this character yet'"

then, after the first read: "recent changes broke it" and "fix the App".

URL: `http://localhost:5177/roleplay/studio/8bc36efb-b235-485b-8675-b98ad7754e59/32244bb9-d0e7-4943-a8cb-739b9fb4d1a8/production/7627eb78-0919-4ca7-b53b-0c238c2b9e22/composition`

---

## Analysis

### 1. The message is honest about the wrong store

The text the operator saw comes from `ReferencePickerEmptyReason.Build` with
`assetType = CharacterBody`, `characterProfileId = <Becky's template id>`, `assetCount = 0`. Measured in the
dev store (`dreamgenclone.dev.db`, 2026-10-02):

| query | result |
|---|---|
| Becky's template id | `de351eb3-69d3-421a-a762-79ae8ee183ed` |
| her approved pack | `2d13c667-a690-4b5a-992a-93359591aa41`, **v9**, scope `BodyComplete` |
| approved assets in that pack | **5 faces + 12 full-body references** |
| `SceneAssets` of type `CharacterBody` owned by Becky | **0** (all 11 `CharacterBody` rows have `CharacterProfileId = NULL`) |
| Becky's `CharacterFace` scene assets | 10 rows, **all** with `CharacterProfileId = NULL` |

So the character-scoped approved-asset dropdown is legitimately empty: the character's curated face/build
references live in her identity **pack** (`SceneImageReferenceAssets`), which is a different store.

### 2. The Composition blueprint never offered the pack, and pointed at a control it does not render

`ImageStepBlueprintFactory.ForPackIdentityComposition` declared, per character, only a `Body` slot and a
`Wardrobe` slot, both sourced from `[ApprovedSceneAsset, ScratchImage]`, and **no `Face` slot at all**. The
shared composer renders `IdentityPackPicker` only when a slot allows `IdentityPackAsset`
(`ImageStepComposer.OffersIdentityPack`), so on this host the empty dropdown's own advice — "Or bind one of
the character's approved identity-pack references, **above**" — described a picker that was below it and
disabled anyway.

That is the twin of the defect the LoRA cell (commit `6e57549`, 2026-09-26) and the asset creator
(`75ac3d4`, 2026-10-01) were fixed for. B130-012 recorded the Composition host as the deliberate
remainder — see `specs/Planning/B-130-unified-image-step-composer/tasks.md`:

> Moving the pack from CHANNEL to SOURCE (pick the character → `IdentityPackSlotPrefill` fills face and
> body slots → the pack channel is retired) is the remaining step … so it is scoped rather than folded into
> this migration.

### 3. Allowing the source was not sufficient — the render path could not carry a pack BUILD

On a scene render, packs were reachable only as a **channel**: `SceneImageService.EnqueueRenderAsync` →
`ResolveNativeReferenceIdentityBindingsAsync` writes `canonicalFaceAssetId` / `faceView` /
`fileRelativePath` / `sha256`, i.e. **faces only** — and
`SceneImageRenderingJobHandler.BuildNativeReferencesAsync` handed the applied bindings to
`MediaEditReferenceResolver`, which reads approved `SceneAssets` by `SceneAssetImageId`.
`ReferenceApplicationSelection.UsesReference` is **false** for a pack binding (no scene asset id), so a
pack binding was filtered out there and a pack **build** had no route to a scene render at all. A
blueprint-only change would have produced a control whose pick was silently dropped — the exact outcome
the reference rules forbid.

---

## Plan

1. `ImageStepBlueprintFactory.ForPackIdentityComposition`: declare a per-character **Face** slot first,
   then **Body**, both accepting `IdentityPackAsset` (face also keeps `ScratchImage`); Wardrobe/Location
   stay shared-library-only, Pose unchanged.
2. `ImageStepComposer.razor`: render the pack picker **before** the approved-asset dropdown in the
   unbound branch, so the empty reason's "above" is true (ordering only; affects all hosts visually).
3. `SceneImageRenderingJobHandler`: resolve pack-sourced bindings (`Source == IdentityPackAsset`) into
   ordered reference images via the existing `IdentityFaceReferenceResolver.ResolveExactFaceAsync` and
   `IdentityBodyReferenceResolver.ResolveExactBodyAsync`; refuse a pack source on any other element by
   name; hand only the asset-backed bindings to `MediaEditReferenceResolver`.
4. `CompositionComposer.razor`: the native route receives the face from the step's own binding (the exact
   view the operator picked), so the channel is handed packs only for the graph route
   (`IdentityPacks = identityPacks`); the two identity mechanisms are refused together rather than stacked.
5. Tests + this record.

**Not changed:** `IdentityPackSlotPrefill` (still the pre-fill seam, unused by this host), the identity
channel's own resolver, the pose route, and the "Identity on create" graph mechanism.

---

## Resolution

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/ImageStep/ImageStepBlueprintFactory.cs` | `ForPackIdentityComposition`: Face slot added first per character, `characterSources` (ApprovedSceneAsset + IdentityPackAsset + ScratchImage) for the face, `buildSources` (ApprovedSceneAsset + IdentityPackAsset) for the build, `sharedSources` for wardrobe/location/pose. Doc comment rewritten to the two-route rule. |
| `DreamGenClone.Web/Components/Shared/ImageStepComposer.razor` | `IdentityPackPicker` moved above `ReferencePicker` in the unbound branch. |
| `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs` | `IdentityBodyReferenceResolver` injected (optional); `IsIdentityPackBinding`; `AddIdentityPackReferencesAsync` (face/build, ordinal order, refusal by name for any other element); `BuildNativeReferencesAsync` splits pack bindings from asset-backed ones and passes only the latter to `MediaEditReferenceResolver`. |
| `DreamGenClone.Web/Components/Pages/CompositionComposer.razor` | `IdentityPacks = identityPacks` (channel is the graph route only); explicit refusal when 'Identity on create' and a bound pack face are both present. |
| `DreamGenClone.Tests/RolePlay/ImageStepBlueprintFactoryTests.cs` | `PackIdentityComposition_CanBindItsFaceAndBodyFromTheCharacterPack`, `…_DeclaresTheFaceBeforeTheBuild`, `…_DoesNotOfferThePackForSharedElements`. |
| `DreamGenClone.Tests/RolePlay/SceneImageRenderingJobHandlerNativeReferenceTests.cs` | `HandleAsync_BoundPackFaceAndBuild_TravelAsTheirOwnReferenceImages`, `HandleAsync_PackBindingOnAnElementAPackCannotSupply_FailsFastByName` (+ `IdentityBodyReferenceResolver` wired into the fixture). |
| `DreamGenClone.Tests/RolePlay/CompositionComposerNativeReferenceContractTests.cs` | Retargeted to the one-route contract, keeping its guarantee; new `TheTwoIdentityMechanismsAreRefusedTogether`. |
| `DreamGenClone.Web/Components/Shared/IdentityPackPicker.razor` | `pack v@Owner.PackVersion` → `pack v@(Owner.PackVersion)`. The bare form reads to the Razor parser as an e-mail address and was emitted literally; the label was invisible on this host until this change surfaced the picker here, so it is fixed with the change that surfaced it. (The same pattern exists in other, unrelated pickers and is left alone.) |

**Evidence**

- `dotnet build DreamGenClone.Web/DreamGenClone.csproj` — **0 errors, 0 warnings**.
- New tests by name: **8/8 passed** (`BoundPackFaceAndBuild`, `PackBindingOnAnElementAPackCannotSupply`,
  `PackIdentityComposition_*`, `CompositionComposerNativeReferenceContractTests`).
- Targeted suites: `ImageStepBlueprintFactoryTests` + `CompositionPoseConsolidationContractTests` +
  `ImageStepReferenceLabellingTests` + `SceneImageStudioUiContractTests` + `IdentityPackSlotPrefillTests` +
  `ReferenceSlotPlannerTests` + `ImageStepComposerUsageContractTests` — **117/117 passed**.
- `SceneImageRenderingJobHandler*` + `SceneImageServiceJobTests` + `SceneImageStudioUiContractTests` —
  **77/77 passed**.
- Full RolePlay area: **3397 passed / 4 failed (3401 total)**. All four are **pre-existing failures from
  other in-flight worktree work**, not from this change:
  - `SdxlSceneImagePromptBuilderTests` × 3 — the documented pre-existing set (the worktree's
    `SceneImageCompilerSystemPrompts.cs` is modified by other work; that file is untouched here).
  - `SceneLoraSelectionWireTests.Read_WithAMalformedStack_Throws` — its subject
    (`SceneLoraSelectionWire.cs`) is a new, untracked file from other work; untouched here.

  The one failure this change DID cause
  (`CompositionComposerNativeReferenceContractTests.TheNativeReferenceModeIsDerivedFromTheBoundPacks`,
  which pinned the old `IdentityPacks = identityPacks ?? nativePacks` line) was retargeted, keeping its
  guarantee, and now passes.

**Verified in the running app** (clean rebuild + restart from `DreamGenClone.Web` with
`ASPNETCORE_ENVIRONMENT=Development`, `http://localhost:5177`, the reported session/production) — the
Composition step's reference tabs now read `Face Body Wardrobe · Face Body Wardrobe · Location · Pose` and:

| tab | observed |
|---|---|
| Face · Becky | pack picker present and **above** the approved-asset dropdown, offering **Front, 3/4 Left, 3/4 Right, Profile Left, Profile Right** (`Becky · pack v9`) |
| Body · Becky | offering **12 builds**: Front / 3/4 Left / 3/4 Right / Profile Left / Profile Right / Back, each Clothed and Unclothed |
| picking "Front · Clothed" | tab becomes `Body · supplied`, the binding reads `Body for Becky: Front · Clothed`, strategy `NativeMultiReference` |

The approved-asset dropdown keeps its own (now accurate) empty reason underneath.

---

## Validated

- [ ] pending — operator to confirm on the Composition Composer: with Qwen-Image-2.1 selected, Becky's
      Face tab offers her pack's 5 faces and her Body tab offers its 12 builds, and a render carries both.
