# 081 — An app image (Studio render) used as a Location reference was dropped by the first re-plan

**Reported:** 2026-10-18
**Surface:** Composition Composer → reference composer, using a Studio render as a Location reference
**URL:** `/roleplay/studio/8bc36efb…/460f8394…/production/0f98cb6f…/composition?reloadImageId=b91598c2-59da-46b4-8670-9a49f6de3209`

## Report

Operator: *"trying to use studio image as reference image … getting this error: Reference application 'Location'
declares strategy 'NativeMultiReference' but names no reference image."* Plus: *"the delete action on the Location Tab
deletes all added location references not just the one row selected."*

## Analysis

`ReferenceApplicationSelection` carries FOUR reference channels, and a binding is valid only while the channel it
declares still names its image:

| Channel | Fields |
|---|---|
| approved scene asset | `SceneAssetId`, `SceneAssetImageId`, `SceneAssetVersion`, `SceneAssetSha256` |
| **app image (Studio render)** | **`ReferenceImageId`, `ReferenceImageSha256`** |
| identity pack | `IdentityPackId`, `ReferenceAssetId` |
| pose | `PosePresetId`, `SkeletonRelativePath` |

`ImageStepComposer`'s private `ToAssignment` rebuilt an `ImageStepSlotSource` from each binding on **every re-plan**
(adding another reference, removing one, changing a strategy) and did not copy the two app-image fields. So:

1. Binding a Studio render as a Location reference succeeded — that path builds the source directly.
2. The **first** re-plan converted it back through `ToAssignment`, which produced a source with
   `SourceKind = AppImage` and no image.
3. The planner emitted a `Location` binding with `strategy = NativeMultiReference` and nothing to condition on, and
   validation refused it with exactly the reported message.

The second symptom is the same defect. The per-row trash button calls `RemoveBindingAsync`, which removes ONE binding
and re-plans the rest — but with the app-image fields dropped, the re-plan threw, so the delete could not complete and
the slot's remaining rows were not preserved. The separately-labelled "Clear" button (`ClearSlotAsync`) genuinely
removes every binding for the slot, which is where "deletes all references" came from as a recovery step.

## Plan

1. Move the binding→source conversion out of the component into `ImageStepSlotSource.FromBinding`, beside the shape it
   must preserve, so a dropped field is a compile-visible, test-covered omission instead of a silent one.
2. Have the composer's `ToAssignment` delegate to it.
3. Rename the bulk control to "Clear all" with an explicit tooltip, so it cannot be mistaken for the row delete.
4. Add a round-trip test over ALL FOUR channels.

## Resolution

- `DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceSlotPlanner.cs` — added
  `ImageStepSlotSource.FromBinding(ReferenceApplicationSelection)`; the record now has a body.
- `DreamGenClone.Web/Components/Shared/ImageStepComposer.razor` — `ToAssignment` delegates to it (the local copy of
  the field list is gone); the bulk button reads "Clear all" with
  `title="Remove every image bound to this slot, not just one"`.
- `DreamGenClone.Tests/RolePlay/ImageStepAppImageReferenceTests.cs` —
  `FromBinding_RoundTripsEveryChannelSoARePlanCannotDropAnImage`: for approved asset, **app image**, identity pack and
  pose it plans a re-plan from `FromBinding` and asserts every channel field survives, the binding still
  `SuppliesImage`, and `ReferenceApplicationSelectionValidation.Validate` accepts the result.

Build: 0 errors. Suite: 4415 passed / 27 pre-existing `CharacterLoraCoverage*` failures / 0 skipped.

## Validated

- [ ] pending — requires a republish. Bind a Studio render as a Location reference, then touch any other control (add
  a prop, change a strategy, delete one location row) and confirm the Location reference survives with its image.
