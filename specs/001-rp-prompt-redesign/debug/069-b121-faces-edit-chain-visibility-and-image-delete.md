# 069 — B-121/B-122 Faces: a finished edit chain the Faces page could not show, and a source image that could not be deleted

**Status:** Done (325 tests green on the asset/media-edit/identity filter; solution builds with 0 errors)
**Date:** 2026-09-24
**Report:** "I started over doing Becky's face … I did all the remove-background, crop and enhance edits but the images
do not show in the list so that I can choose to accept the final enhanced one. The review deck shows it and I can
approve it there but it does not show in the faces page, also I can not delete the uploaded image `77a0862a…` —
SQLite Error 19: 'FOREIGN KEY constraint failed'."

## What the live records said

Character template `de351eb3-69d3-421a-a762-79ae8ee183ed`, face build `c05b2f5661804e0aab865ffafdc3c6c0`
(`CurrentStep` = **Front**, then **Validate** after the operator's "Use this"), front container
`6563517022834e80893d987810446f24`:

| image | Kind | decision | source | created |
|---|---|---|---|---|
| `77a0862a…` | Uploaded | Rejected | — | 01:10:05Z |
| `94b23b23…` | Edited | Undecided | *(null)* | 01:11:01Z |
| `95b3b976…` | Edited | Undecided | `94b23b23…` | 01:12:11Z |
| `3b8f7dd9…` | Edited | **Accepted** | `95b3b976…` | 01:12:19Z |

The operator's chain was complete and accepted; the page showed none of it.

## Defect 1 — Panel B was gated on the step index, so the chain had no surface

`Panel B · 3 De-clothe · 4 Crop · 5 Enhance` — the only place edited images are listed and one can be accepted as the
canonical front (`ApproveCanonicalFrontAsync` → `SetCanonicalFrontAsync`) — was wrapped in
`@if (_build is not null && (int)_build.CurrentStep >= (int)CharacterIdentityBuildStep.GarmentRemoval)`.
Panel A deliberately lists non-edited candidates only (`Kind != SceneAssetKind.Edited`). For a build sitting on
Front/Validate the two rules together **hid every edited image in the container**: the review deck showed them (it
reads the container's candidate batch) and the Faces page showed nothing at all — not even a line saying the panel
existed.

Verified live before the fix (browser, the operator's page): rail `Front · Complete`, `Validate · NotStarted`,
candidate `77a0862a…` carrying `Fail · irisDy -5.45% · Eye gate failed`, and **no Panel B in the DOM**.

**Resolution.** Panel B is scoped to the **front container** (`!string.IsNullOrWhiteSpace(_build.FrontContainerAssetId)`)
rather than to the step index; the actions keep their own prerequisites:

- `RefreshEditedImagesAsync` reads the container (`AssetService.ListImagesAsync(_build.FrontContainerAssetId)`)
  instead of `_garmentSource.AssetId`, and is called with the front attempts, so the canonical-front choice never
  depends on the edit workspace resolving its own source (it cannot resolve at Front/Validate —
  `ResolveCurrentInputArtifactId` refuses any step outside De-clothe/Crop/Enhance/Angles, by name).
- The canonical-front button is disabled with `|| !_frontStepComplete` and says why ("Choose a front candidate in
  Panel A first — the canonical front is recorded as the result of that candidate's edit chain").
- The empty state and the missing-workspace state are spoken: "No edited images in this build's container yet." and
  `EditWorkspaceUnavailableReason` (which names the eye gate when the step index has not reached the edit steps).
- An empty list is no longer silently absent, and the step-index expression is asserted **absent** by a contract test
  over the comment-stripped markup, so it cannot come back.

The operator's order of work is now a supported order: the chain is visible and acceptable as soon as a front
candidate is recorded, without waiting for Validate (the eye gate measures the *candidate*, not the accepted chain).

## Defect 2 — deleting an image whose edits were recorded always failed

`SceneAssetImageEditSessions` carries `FOREIGN KEY (SourceImageId) REFERENCES SceneAssetImages(Id) ON DELETE RESTRICT`
(and so do its compilation attempts and prompt revisions). `SceneAssetRepository.DeleteImageAsync` detached the
derived `SceneAssetImages` rows but left the session rows alone, so the delete raised the raw
`SQLite Error 19: 'FOREIGN KEY constraint failed'` — and *every* source of a recorded operation was held forever by
the record of that operation, including the intermediate images of a chain.

Confirmed in the live DB: session `006bd411…` (`SourceImageId = 77a0862a…`, Completed 01:11:25Z) referenced the
upload the operator was trying to delete.

**Resolution.** `DeleteImageAsync` now clears the image's edit history in the same transaction, bottom-up
(prompt revisions → compilation attempts → sessions), for each edit store that exists: the asset-image tables and,
after the media-edit cutover, the shared store's `SubjectKind = 'AssetImage'` rows. The images the sessions
**produced are kept**: their own operation provenance carries the source checksum, which is what the canonical-front
lineage walk reads (`ResolveLineageAsync`, B-121 note 011) — so the accepted chain survives its source being deleted.

## Validation

- `SceneAssetRepositoryTests` +1: the cascade removes session/attempt/revision rows, keeps the produced image
  (detached), and — to stop the test passing against a schema that enforces nothing — first proves the constraint is
  live by requiring a direct row delete to throw `SqliteException`.
- `CharacterStudioFacesContractTests` +3: the container scope and the absence of the step-index gate, the
  container-scoped list read, the named prerequisites.
- `dotnet build DreamGenClone.sln` → **0 errors**. Targeted run (asset repo + Faces contracts + media-edit repo +
  asset service) → **50 passed / 0 failed**; the wider
  `SceneAsset|MediaEdit|CharacterStudio|CharacterIdentity` filter → **325 passed / 0 failed**.

## Follow-ups (open, not fixed here)

1. **Edit sessions left non-terminal.** In the live DB, sessions `f64c66e4…` (source `94b23b23…`) and `39ac15a7…`
   (source `95b3b976…`) are `Active` with `CompletedUtc` null although the images they produced exist, and the log
   shows `SQLite Error 5: 'database is locked'` faults in the `ImageEdit` / `PromptCompilation` lanes at the same
   timestamps. Needs its own diagnosis (state machine left non-terminal on a write fault).
2. **`SourceImageId` still null on the first edit of this chain** (`94b23b23…`): the open finding from
   `001-final-writing-instruction/debug/056`, reproduced tonight. The lineage walk survives via the recorded source
   checksum, but the editor's lineage view does not.
3. **Two surfaces, one container.** The review deck lets an image be edited before the build's step index reaches the
   edit steps, so edits join the build's container while the build is still on Front. That is now *visible*, but the
   design question — whether the review deck's edit action should be offered before Validate — is unresolved.
