# 056 — Picking the canonical front does not finish the de-clothe/crop/enhance part

## Report

- **Reported:** 2026-09-13, by user, after a full manual run on `/characters/a9137ebfa4df43d08c3347242aaa2261`
  (character `Sam`, build `c6dd9a6c…`).
  > "i did the full workflow of picking a created image, removed clothing, crop, enhance, and Use this
  > Image, but the overall workflow is only validation complete … if the user picks a Use This in the edit
  > then it means this first part of the workflow is done, it also should know that the selected front
  > profile image did have clothes removed, cropped and enhanced."
- **Symptom:** the pipeline status line reads `Front · Complete`, `Validate · Complete`,
  `De-clothe · Skipped`, `Crop · NotStarted`, `Enhance · NotStarted` — even though the approved image was
  in fact de-clothed, cropped and enhanced.

## Analysis

**DB state after the run** (`CharacterIdentityBuilds` / `CharacterIdentityBuildSteps`):

```
build c6dd9a6c…  CurrentStep=Crop(4)  CanonicalFrontAssetId=ad01d7e8…
  1 Front        Complete    out 27ca8569
  2 Validate     Complete    out 27ca8569
  3 GarmentRemoval Skipped   in  27ca8569   out 72d5d4af     <- skipped, but it has a de-clothe output
  4 Crop         NotStarted
  5 Enhance      NotStarted
  6 Angles       NotStarted
  7 Promote      NotStarted
```

**The approved image's lineage** (`SceneAssetImages`, front container `4ff9a68c…`):

```
27ca8569  PromptGenerated  1024x1024  Accepted          Front step output = the candidate chosen in Panel A
72d5d4af  Edited          1024x1024  editSessionId…    de-clothe    (SourceImageId = 27ca8569)
babaa71c  Edited           742x911   operation: crop   crop         (SourceImageId = 72d5d4af)
ad01d7e8  Edited           834x1024  operation: enhance enhance     (SourceImageId = babaa71c)  = canonical front
```

So every operation *is* recorded — on the image rows, as `SourceImageId` + `SourceProvenanceJson`. Two
things are missing:

1. **Nothing advances steps 3/4/5 from the edits.** The only writers are
   `CharacterIdentityGarmentService.RecordResultAsync` / `AdvanceAsync` / `SkipAsync`, driven by the
   Panel B buttons one step at a time. The embedded `ImageEditWorkspace` creates images through the
   shared operation pipeline, which knows nothing about the build, so a crop or enhance performed in the
   form changes no step row. `SetCanonicalFrontAsync` deliberately performs no step transition (debug 055).
2. **Nothing records on the chosen image what it went through.** The chain is only derivable by walking
   `SourceImageId`; no image carries the summary the user expects it to "know".

Two further faults visible in the data:

3. `SkipStepAsync` keeps whatever output was previously recorded, so step 3 can be `Skipped` *and* carry a
   de-clothe output — a state that contradicts itself.
4. There is no shared reader for "which operation produced this image": crop/enhance rows write
   `operation: "crop" | "enhance"` in their provenance, while a plain edit (de-clothe) writes
   `editSessionId`/`compilationAttemptId` and **no** `operation` key. Any consumer must currently infer the
   kind from a missing key.

## Plan

**Making the approval the one place that finishes steps 3/4/5.**

1. **One provenance reader.** New `Editing/MediaEditProvenance.cs` exposing the operation kind of an image
   row, used by the derivation (and by the lineage-label fix already owed from debug 053). To give the
   reader a single rule, the plain-edit writer (`SceneAssetImageEditCompilationService`, ~line 217) starts
   emitting `operation = "edit"`, and the historical rows are backfilled with the same key — no
   "missing key means edit" branch anywhere.
2. **Per-image record.** `SceneAssetImage.PipelineStepsJson` (mirroring the existing `ValidationResultJson`
   pattern: property, repository INSERT/UPDATE/SELECT/reader/params, `CREATE TABLE`, `ALTER TABLE`
   migration, `ISceneAssetService.SetImagePipelineStepsAsync`) holding the ordered steps this image went
   through with the artifact of each, e.g.
   `{"frontArtifactId":"27ca…","steps":[{"step":"GarmentRemoval","artifact":"72d5…"},{"step":"Crop","artifact":"babaa71c…"},{"step":"Enhance","artifact":"ad01d7e8…"}],"recordedUtc":"…"}`.
3. **Approval finishes the part.** `CharacterIdentityBuildService.SetCanonicalFrontAsync` additionally:
   walks the approved image's lineage back to the Front step's output artifact; derives De-clothe, Crop and
   Enhance as **Done** (with the artifact for that operation nearest the approved image) or **Skipped**
   (no such operation in its chain); writes the three step rows; stores the per-image record; sets
   `CanonicalFrontAssetId`; moves `CurrentStep` to `Angles`. Fails fast when the image is not `Complete`,
   is not in the build's front container, or its lineage never reaches the Front artifact.
4. **Remove the duplicate writers.** `RecordResultAsync`, `AdvanceAsync`, `SkipAsync` (+ interface) and the
   Panel B "Skip this step" / "Recorded result" UI (plus the already-dead `AdvanceGarmentAsync`). Skipping
   stops being a button and becomes what the derivation records for an operation the approved image never
   had — exactly one source of truth for steps 3/4/5.
5. **Show it.** Panel B cards gain the pipeline line (`de-clothe ✓ · crop ✓ · enhance ✓`, `— skipped`
   where absent), so the chosen image visibly carries what was done to it.
6. **Verify.** Re-approving `ad01d7e8` on the existing build must yield De-clothe/Crop/Enhance `Complete`
   (outputs `72d5d4af`, `babaa71c`, `ad01d7e8`) and `CurrentStep=Angles`; the top line then reads
   `Front ✓ Validate ✓ De-clothe ✓ Crop ✓ Enhance ✓ · Angles · Promote`.

**Blast radius:** `Domain/RolePlay/SceneAssetModels.cs`; `Infrastructure/RolePlay/SceneAssetRepository.cs`
(+ schema + migration); `ISceneAssetService`/`SceneAssetService` (new setter → 5 test stubs updated);
`CharacterIdentityBuildService` (+ interface); `CharacterIdentityGarmentService` (+ interface, three methods
removed → garment tests pruned); `SceneAssetImageEditCompilationService` (provenance key);
`Components/Pages/CharacterStudio.razor`; new `Editing/MediaEditProvenance.cs`. No prompt or engine
behaviour changes.

## Resolution

Approved 2026-09-13. The approval is now the one place that finishes steps 3/4/5, from the approved image's
own evidence.

- `Domain/RolePlay/SceneAssetModels.cs` — `SceneAssetImage.PipelineStepsJson` plus the
  `SceneAssetImagePipeline` / `SceneAssetImagePipelineStep` records (step, outcome, input artifact, artifact).
- `Infrastructure/RolePlay/SceneAssetRepository.cs` — the new column in INSERT / ON CONFLICT / SELECT /
  reader / parameters / CREATE TABLE, and an `ALTER TABLE` migration guard.
- `ISceneAssetService` / `SceneAssetService` — `SetImagePipelineStepsAsync` (same shape as the
  validation-result setter). Five test stubs updated.
- `Editing/MediaEditProvenance.cs` (new) — the single reader for operation provenance: the operation kind
  **and** the source checksum (`sourceImageSha256`). The three writers now take their `operation` values from
  this class, and the plain-edit writer emits `operation = "edit"` (it used to omit the key, which is why
  the reader needed a rule before).
- `CharacterIdentityBuildService.SetCanonicalFrontAsync` — walks the approved image's lineage to the Front
  step's output, derives De-clothe / Crop / Enhance as **Complete** (naming the artifact that operation
  contributed to this chain) or **Skipped** (the operation is absent from it), writes the three step rows and
  the per-image record, sets `CanonicalFrontAssetId`, and moves `CurrentStep` to `Angles`. Nothing is
  inferred: unreadable provenance, an unresolvable source, a circular chain, or a chain that does not start
  at the Front artifact are all refused by name.
- `CharacterIdentityGarmentService` (+ interface) — `RecordResultAsync`, `AdvanceAsync`, `SkipAsync` and
  `GetRecordedResultAsync` removed along with the build-service dependency; the step outcome now has exactly
  one writer. Panel B's "Skip this step" / "Recorded result" controls and the dead `AdvanceGarmentAsync`
  went with them, and each Panel B card shows its own `de-clothe ✓ · crop ✓ · enhance ✓` line.
- Tests: `CharacterIdentityBuildServiceTests` covers all-three-done (with the Front step left untouched),
  crop-only (the other two recorded as Skipped), the operation nearest the approved image when an operation
  ran twice, an unknown origin, a source outside the front container, and a chain that roots elsewhere. The
  five garment tests for the removed methods were deleted with the methods.

### Why the walk uses the recorded checksum, not `SourceImageId`

The approved image's `SourceImageId` chain could not be used: in this DB several asset crop/enhance rows have
`SourceImageId = NULL` while their sources still exist, which contradicts the only code that can clear it
(`SceneAssetRepository.DeleteImageAsync` detaches children of the row it deletes) — see the finding below.
Every operation row does record the checksum of the exact file it consumed, and that checksum resolves to
exactly one image of the container for every row checked, so the walk uses it. Where the same bytes exist
twice, the row's own `SourceImageId` picks between them; if it cannot, the approval fails rather than guessing.

## Verification

```
Steps  Front=Complete(27ca8569) Validate=Complete  De-clothe=Complete(27ca8569→72d5d4af)
       Crop=Complete(72d5d4af→babaa71c)  Enhance=Complete(babaa71c→ad01d7e8)  Angles NotStarted
Build  c6dd9a6c  CurrentStep=Angles(6)  CanonicalFrontAssetId=ad01d7e8
Image  ad01d7e8  PipelineStepsJson = {frontArtifactId 27ca8569, GarmentRemoval Complete 72d5d4af,
                                     Crop Complete babaa71c, Enhance Complete ad01d7e8}
UI     Front ✓ Validate ✓ De-clothe ✓ Crop ✓ Enhance ✓ · Angles · Promote
       Panel B card ad01d7e8: "Canonical front", "de-clothe ✓ · crop ✓ · enhance ✓"
```

Tests: 135 passed / 0 failed (`CharacterIdentity|SceneAsset|MediaEdit`); solution builds with 0 errors.
The approval is idempotent, which is how the user's existing build was brought up to date.

## Follow-up finding — asset operation rows lose their `SourceImageId`

Open, not fixed, needs its own diagnosis:

- Five `Edited` rows of front container `4ff9a68c` (`f808a5ad`, `a0673511`, `ecfd13a2`, `babaa71c`,
  `ad01d7e8`) have `SourceImageId = NULL` although the images they were produced from still exist.
- Their provenance's `sourceImageSha256` resolves to exactly the right source row in every case, and
  `SceneAssetMediaEditSubjectWriter.PrepareOperationAsync` *requires* `image.SourceImageId` before an
  operation runs — so the link existed when the operation ran and was cleared afterwards.
- The only writer that clears the column is `SceneAssetRepository.DeleteImageAsync`'s
  `UPDATE SceneAssetImages SET SourceImageId = NULL WHERE SourceImageId = $id`. There are no triggers on the
  table, no other `UPDATE`/`INSERT` touches the column, and the sources were not deleted. The mechanism is
  therefore unexplained.
- Consequence beyond this feature: the editor's lineage view, the "other attempts of this source" list and
  `ResolveResultAsync` all read `SourceImageId`, so they show the wrong chain for these images.

## Validated

- [ ] pending — awaiting user confirmation
