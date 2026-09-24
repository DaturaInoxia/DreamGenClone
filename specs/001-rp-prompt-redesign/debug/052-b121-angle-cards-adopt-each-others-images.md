# 010 — Face-angle cards adopted each other's images, and a profile could not be confirmed

**Status:** Fixed (root cause + my own regression + the confusing ownership display)
**Date:** 2026-09-22
**Reported:** "trying to complete face profiles and is very confusing, still images for different angles show in all
the cards not just the images created for that angle, for profile left Use This I get 'Angle ProfileLeft requires
explicit visual confirmation…' which there is no tick the confirmation on the ui, i cant even give direct what is
wrong, the images and cards keeps changing, I do one then move to next and the previous one is gone, or included in
the next card, left in the right profiles and vise versa"

## Report

Three symptoms on build `c6dd9a6c…` (character `a9137ebf…`, "Sam"): angle images appeared under the wrong cards
(left/right swapped), `Use this` on ProfileLeft demanded a confirmation control that was not on screen, and the
cards appeared to change their contents as the user moved between them.

## Analysis

### 1. The cards adopted each other's images (root cause)

`SceneAssetImageEditWorkspaceService.ResolveResultAsync` (the Asset Manager adapter of the shared edit workspace)
resolved a result by *guessing*:

```csharp
// Asset images are not stamped with the edit session, so the newest derived image of the
// source is the session's result.
var latest = images
    .Where(image => string.Equals(image.SourceImageId, subject.ImageId, StringComparison.Ordinal))
    .OrderByDescending(image => image.CreatedUtc).FirstOrDefault();
```

`ImageEditWorkspace.RefreshAsync` fires `ResultChanged` for whatever this returns, and `CharacterStudio.razor`
records it with `AnglesService.RecordResultAsync(buildId, selectedView, imageId)` — so **opening a card recorded
the newest image in the shared front container as that card's attempt**. The four angle cards share one asset
container, so every view adopted another view's render. The scene-image adapter resolves by `EditSessionId` and
was already correct; only the asset adapter guessed.

Database proof (table `CharacterIdentityAngleAttempts`, one row per adopted image):

| time | event | row |
|---|---|---|
| 01:52:16 | 3/4L edit → `7dd8b33a` (batch `…-threequarterleft`) | view 1 attempt 1 — correct |
| 01:54:35 | **3/4R opened** → adopted `7dd8b33a` (3/4L's image) | view 2 attempt 1, failed "faces image-left", mirror → `4630b03c` |
| 03:49:48 | **ProfileLeft opened** → adopted `4630b03c` (3/4R's mirror) | view 3 attempt 1 |
| 03:51:19 | ProfileLeft edit → `4d4220e9` (batch `…-profileleft`) | view 3 attempt 2 — correct |
| 03:54:03 | **3/4R re-opened** → adopted `4d4220e9` (ProfileLeft's image) | view 2 attempt 3 → failed → mirror `792d79ff` |

The batches confirm the mechanism: images carry the batch of the card that *created* them, while the resolver
looked only at `SourceImageId + CreatedUtc`.

### 2. The confirmation control was unreachable (regression from debug record 049)

`CharacterStudio.razor` offered the profile confirmation only when the record already had an `OutputArtifactId`.
ProfileLeft held an *attempt* (`f554f4ce` → `4d4220e9`) with no record output, so the control was hidden while
`AcceptAttemptAsync` still demanded it. The 049 change that made the confirmation reachable had been narrowed in
`debug/051`; that narrowing is what created this dead end.

### 3. Ownership was invisible (confusion)

The attempts list renders only for the open card and was titled "Attempts (n)" with no view name, and no card said
how many attempts it owned. An adopted image therefore looked like a legitimate one, and moving between cards
looked like the images were moving.

## Plan

1. `SceneAssetImageEditWorkspaceService.ResolveResultAsync`: when the subject names a `CandidateBatchId`, the
   first resolution must come from that batch. The tracked-result path stays first, so a run started in this
   session still reports its own image; subjects without a batch (Panel B's step workspace) keep the old rule.
2. `CharacterStudio.razor`: restore the confirmation's reachability; label the attempts list with its view; show a
   per-card attempt count (and fix the "waiting for previous view" wording for three-quarter cards, which chain to
   the front).
3. Clean the adopted rows so the user can finish the set (dev DB backup first).

## Resolution

- **`SceneAssetImageEditWorkspaceService.ResolveResultAsync`** — added the batch guard with the reasoning in a
  comment naming this incident. One rule, no fallback, still a single resolver.
- **`CharacterStudio.razor`** — confirmation condition back to `ManualConfirmationRequired && Status != Accepted`;
  attempts block titled `@AngleLabel(view) attempts (n)`; new `AngleAttemptCount` + `_angleAttemptCounts` filled in
  `RefreshAnglesAsync` (so the first render has it); new `AngleSourceLabel` naming what each card waits for.
- **Data cleanup** (dev DB, backup `artifacts/tmp/db-backups/dreamgenclone.dev.db.20260922-001707`): deleted the 5
  adopted attempts (view 2's four, view 3's one), reset 3/4R to `NotStarted` with cleared ids, repointed
  ProfileLeft at its own render (`4d4220e9`, renumbered attempt 1, not yet accepted), cleared ProfileRight's stale
  source. 3/4L's accepted attempt was untouched.
  *Note: the first delete list mistakenly included 3/4L's accepted attempt; it was restored byte-for-byte from the
  backup taken minutes earlier and the cleanup re-run with the correct five ids.*
- **Tests**: `ImageEditWorkspaceContractTests.AssetResultResolution_MustStayInTheSubjectsCandidateBatch`;
  `CharacterStudioFacesContractTests.PanelC_LabelsOwnershipOfAttemptsAndCountsThemPerCard` plus the confirmation
  test now asserting the condition is NOT narrowed by `OutputArtifactId`. Run:
  `CharacterStudio|ImageEditWorkspace|CharacterIdentity|Reference|MediaEdit` → **219 passed / 0 failed**.
- **Verified live** (browser, character `a9137ebf…`, build `c6dd9a6c…`): Panel C renders 3/4L Accepted (1 attempt),
  3/4R NotStarted (0), ProfileLeft Complete (1) with its confirmation checkbox, ProfileRight NotStarted (0) with a
  checkbox; no error UI; app restarted on `data/dreamgenclone.dev.db` (Development, `:5177`).

## What the user can now do

The build is left in a truthful state: 3/4L accepted, 3/4R to redo, ProfileLeft ready to confirm-and-accept,
ProfileRight waiting on 3/4R. Opening any card resolves only that card's own batch, so images can no longer
migrate between cards.
