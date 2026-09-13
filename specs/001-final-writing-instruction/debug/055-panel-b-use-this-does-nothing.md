# 055 — Panel B "Use this" did nothing

## Report

- **Reported:** 2026-09-13, by user, on the Character Studio Faces tab: *"Use this in panel B does not work."*
- **Reproduced:** character `Sam` (`a9137ebf…`), build `c6dd9a6c…` (Front `Complete`, GarmentRemoval `Running`).
  Clicking **Use this** on an edited image in Panel B left the card unchanged.
- **Observed failure:** after the click the page showed
  `Step Front is out of order; the current step is GarmentRemoval.` — and that text was rendered inside
  **Panel A**, so with Panel A collapsed (or the user working in Panel B) the click looked like a no-op.

## Analysis

Two independent defects compounded:

1. **The wrong operation was invoked.** Panel B's button called `FrontService.SelectFrontAsync(imageId)`,
   which exists to pick the candidate that *completes the Front step*. That path is
   `CharacterIdentityBuildService.CompleteStepAsync(Front, …)`, and `EnsureCurrent` correctly refuses it once
   the pipeline has moved on: `Step Front is out of order; the current step is GarmentRemoval.` Approving a
   canonical front is not a step transition at all — the Front step has long been complete and re-completing
   it would be wrong.
2. **The message was rendered where the action was not.** The exception was assigned to `_frontMessage`,
   which is rendered inside Panel A's body. An action taken in Panel B reported its failure in a collapsed
   panel above it.

There was also no durable place to record the choice: `CharacterIdentityBuild` had
`FrontContainerAssetId` (the *container* holding every front image) but no field for the *chosen* image, so
the only writable path was the step machine.

## Plan

1. Record the choice on the build: `CharacterIdentityBuild.CanonicalFrontAssetId` (domain), persisted with
   the build (repository INSERT / ON CONFLICT UPDATE / SELECT / reader / CREATE TABLE / `ALTER TABLE`
   migration guard).
2. `ICharacterIdentityBuildService.SetCanonicalFrontAsync(buildId, imageId)`: validate the image exists, is
   `Complete`, and belongs to the build's own front container, then upsert the build. **No step transition.**
3. Repoint Panel B's button at it, with `IsCanonicalFront(image)` for the card state and the delete guard.
4. Hoist `_frontMessage` above both panels so an action's result is always visible.

## Resolution

- `DreamGenClone.Domain/RolePlay/CharacterIdentityBuildModels.cs` — `CanonicalFrontAssetId`, documented as
  deliberately separate from the Front step's output artifact.
- `DreamGenClone.Infrastructure/RolePlay/CharacterIdentityBuildRepository.cs` — `CanonicalFrontAssetId` in
  INSERT, ON CONFLICT UPDATE, both SELECTs (read at ordinal 8, matching `ReadBuild`), CREATE TABLE, and the
  `ALTER TABLE … ADD COLUMN` migration for existing DBs.
- `ICharacterIdentityBuildService` / `CharacterIdentityBuildService` — `SetCanonicalFrontAsync`; the service
  now takes `ISceneAssetService` (the image must be resolved and checked) and
  `ILogger<CharacterIdentityBuildService>` (the approval is logged with the step it happened at).
- `DreamGenClone.Web/Components/Pages/CharacterStudio.razor` — Panel B's card button is
  `ApproveCanonicalFrontAsync(edited)` showing `Use this` / `Canonical front`; delete is refused for the
  canonical image with an explanatory message; `_frontMessage` renders as an alert above Panel A and Panel B;
  `_garmentMessage` uses the same alert styling.
- Tests: `CharacterIdentityBuildServiceTests.SetCanonicalFront_RecordsChoiceWithoutReopeningTheFrontStep`
  (asserts the Front step row is untouched — status `Complete`, output artifact unchanged — and `CurrentStep`
  does not move) and `SetCanonicalFront_RejectsAnImageFromAnotherAsset`. The three fixtures that construct
  the service were updated for the new dependencies; `CharacterIdentityBuildServiceTests` gained a
  scene-asset stub whose unimplemented members throw rather than returning empty data.

## Verification

```
DB     CharacterIdentityBuilds.CanonicalFrontAssetId  ->  91433c5e69964c1a990c9804cc283533   (new column, migrated)
Build  c6dd9a6c…  Front=Complete(27ca8569…)  Validate=Complete  GarmentRemoval=Running  CurrentStep=Crop
UI     Panel B card 91433c5e…  "Canonical front"  (Delete disabled); the other five still "Use this"
Msg    "Canonical front approved: 91433c5e… — the next steps work from this image."
Log    Canonical front approved: BuildId=c6dd9a6c…, ImageId=91433c5e…, Step="Crop"
```

No `out of order` message is produced, and no step row changes state.

Tests: 135 passed / 0 failed (`CharacterIdentity|SceneAsset|MediaEdit`); Web project builds with 0 errors.

## Validated

- [ ] pending — awaiting user confirmation
