# 054 — Crop/Enhance rows report the wrong image size

## Report

- **Reported:** 2026-09-12, by user, after reviewing the crop → enhance pair from debug 053
  (`c4774276` crop → `63d72010` enhance). The stored size said `1024x1024` while the files are
  `826x611` and `1024x757`.
- **Symptom:** the size shown for an operation's result (editor badge, gallery, studio lineage) describes
  the source image, not the file that exists.

## Analysis

`SceneImageRecord.ImageSize` is written when a row is **queued**, and operation rows inherit it:

```
SceneImageService.EnqueueCropAsync / EnqueueEnhanceAsync   → ImageSize = source.ImageSize
SceneImageMediaEditSubjectWriter.CompleteAsync             → never touched ImageSize
```

An operation changes the size — a crop trims, an enhance resamples — so the inherited value is a
placeholder that stayed wrong forever. The completion transition did not persist the column either
(`TryCompleteImageAsync` / `TryCompleteOperationImageAsync` had no `ImageSize` in their UPDATE), so even a
corrected value would not have been stored.

The asset path was already correct: `ISceneAssetStorageService.SaveAsync` returns ingest metadata
(`Width`, `Height`, `ByteLength`, `Sha256`) and `SceneAssetMediaEditSubjectWriter` records it. The scene
path is the outlier: `ISceneImageStorageService.SaveAsync` returns only a path and the writer computes the
SHA itself.

Readers affected: `SceneImageEditWorkspaceService.ToSource` (editor badge), `SceneImageGallery` /
`SceneImageStudio`, `IdentityControlledRequestCompiler` (canvas size for a later identity edit), and the
`ImageSize = source.ImageSize` inheritance for anything derived from the row.

## Plan

1. New helper `MediaEditProducedImage.SizeOf(byte[])`: the produced bytes' `width x height`, failing fast
   when the bytes are not an identifiable image.
2. `SceneImageMediaEditSubjectWriter.CompleteAsync`: record `image.ImageSize` from the produced bytes, for
   operations and edits alike.
3. `SceneImageRepository`: persist `ImageSize` in **both** completion transitions (an unchanged value for
   the render/legacy-edit callers, which pass the row they loaded).
4. Tests: helper (real PNG, non-square, garbage) and repository (the corrected size round-trips).
5. Backfill the three pre-fix rows from their measured files.

## Resolution

- `DreamGenClone.Web/Application/RolePlay/Editing/MediaEditProducedImage.cs` — new.
- `SceneImageMediaEditSubjectWriter.cs` — sets `ImageSize` from `output.Bytes` on completion.
- `SceneImageRepository.cs` — `ImageSize = $imageSize` added to both `TryComplete*ImageAsync` UPDATEs.
- Tests: `MediaEditProducedImageTests` (3) and an `ImageSize` assertion in
  `SceneImageRepositoryTests.TryCompleteOperationImage_CompletesAPendingRow`.
- The first run of `SizeOf_BytesWithoutAnIdentifiableImage_FailsInsteadOfGuessing` failed because ImageSharp
  throws `UnknownImageFormatException` rather than returning null; the helper now converts both cases to the
  documented `InvalidOperationException` (one message, one place) instead of leaking the decoder's type.
- Backfill (`artifacts/tmp/dbquery/queries/backfill_imagesize_053.sql`):
  `20d6b11b` → `819x1024`, `c4774276` → `826x611`, `63d72010` → `1024x757` (all measured from the files).

### Verification

```
12a95317  Crop     ImageSize=1024x576   file 1024x576   (new, post-fix, 16:9 crop)
20d6b11b  Crop     ImageSize=819x1024   file 819x1024   (backfilled)
c4774276  Crop     ImageSize=826x611    file 826x611    (backfilled)
63d72010  Enhance  ImageSize=1024x757   file 1024x757   (backfilled)
```

Tests: 158 passed / 0 failed on the affected set (enhance, crop, media-edit, repository, service).

### Collateral and follow-up found while verifying

1. Restarting the app to build killed an in-flight asset enhance (`c930d6a5…`, source `ecfd13a2…`). Startup
   recovery marked it `Failed` with *"Processing was interrupted before the exact model request was
   persisted"*. Re-run it from the editor.
2. That message is **wrong for operations**: a crop/enhance has no model request, and its parameters
   (upscaler, target edge, crop window) are persisted on the row, so an interrupted operation is
   deterministic and re-runnable rather than unrecoverable. `SceneAssetPendingJobRecovery` treats every
   non-`PromptGenerated` row as needing a compiled edit request. Not fixed — needs its own approval.

## Validated

- [ ] pending — awaiting user confirmation

