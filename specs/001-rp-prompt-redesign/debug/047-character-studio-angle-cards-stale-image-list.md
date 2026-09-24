# 047 — Character Studio: the four uploaded face angles looked like a single image

**Status:** Fixed (code + targeted tests green; verified live in the browser; awaiting user confirmation)
**Date:** 2026-09-21
**Character:** `f58f959a-8050-4388-a219-99d2df3446a1` (Becky) · build `b8adc0e742e645f7a4a7100ff4796a94`

## Report

> "the uploaded angles are not beeing sepearated by the each angle, lastest one overwrites all the others"

Four angle images were uploaded through the Panel C cards (18:04–18:05 local). The user saw one image at a
time and concluded the newest upload replaced the others.

## Analysis

**The data was correct.** Four rows in `CharacterIdentityAngles` (views 1–4), each `Accepted`, each naming
its own output image; four distinct files on disk with distinct checksums and sizes; the chain correct
(3/4L → Profile left source, 3/4R → Profile right source). Nothing was overwritten in storage.
Queries: `b121-f58-angles.sql`, `b121-f58-angle-outputs.sql`.

**The display was wrong.** Panel C's card image is resolved as
`_angleImages.FirstOrDefault(image => image.Id == angle.OutputArtifactId)`, and `_angleImages` was filled
by scanning the canonical front's asset container — **inside `RefreshAnglesAsync`, which is called only from
`OnInitializedAsync`**. Every angle action (upload, accept, edit result, delete) refreshed the records and
the selected card's attempts, but never that list. So an image created during the session could never appear
on its card, and because the attempts block renders only for the open card, the only visible picture was the
one belonging to whatever card was last touched — reading exactly as "the latest one overwrote the others".

Confirmed live with the browser: at that point the DOM showed the four uploaded angle images listed as
**Panel A front candidates** (they are stored in the shared "Becky front" container, since
`UploadAsync` adds the upload to the *source image's* container) while Panel C carried the stale list.

`RefreshPromotionAsync` had the same shape of defect: it too ran only at init, so Panel D's Promote
readiness could not follow an acceptance made in the session.

## Resolution

`DreamGenClone.Web/Components/Pages/CharacterStudio.razor` only:

- `RefreshAnglesAsync` now builds the card image list from **the ids the angle records name**
  (`AssetService.GetImageAsync(outputId)`), not from a container scan that cannot see anything created after
  the page loaded.
- New `RefreshAnglePanelAsync(attemptsView)` — the one refresh every angle action ends in: the four records,
  each card's image, the open card's attempts and their images, and (since it reads the same records) the
  Promote readiness.
- Routed through it: `SelectAngleAsync`, `UploadAngleAsync` (via select), `OnAngleResultAsync`,
  `AcceptAngleAsync`, `AcceptAngleAttemptAsync`, `DeleteAngleAttemptAsync`, `RecordAngleOverrideAsync`.

### Verification

- Contract test `CharacterStudioFacesContractTests.PanelC_EveryAngleActionEndsInTheOnePanelRefresh`: exactly
  one place reads the attempts list, and the container scan is gone.
- Targeted run: **93 passed / 0 failed** (`CharacterStudioFacesContractTests|CharacterIdentity|SceneAsset`).
- Live (browser, after rebuild + restart): all four cards render their own distinct image
  (`93c4c9dc`, `632a5e22`, `edfb0b69`, `41c85cc2`), and they still do **after** clicking two cards — i.e.
  after the refresh path rebuilt the image list from the records. Panel D reported all five views `Ready`.
- The upload case itself was not re-run against this build: `UploadAsync` sets the record's
  `OutputArtifactId` before returning, and the same helper then resolves that id, so it is covered by the
  mechanism proven above. (A live upload would have knocked the accepted angle out of `Accepted` and
  blocked promotion, so it was deliberately not done on the user's build.)

## Not changed (reported to the user)

- Angle uploads still live in the shared "Becky front" container, so Panel A's candidate grid lists them
  alongside the front candidates. The user chose to leave this as-is for now (fixing it means per-angle
  containers in the angle service).
- Pre-existing: at the Promote step, `RefreshGarmentAsync` asks the garment service for a source image and
  receives `The image-edit workspace is only available for the De-clothe, Crop, and Enhance steps; current
  step is Promote.`, which surfaces as an alert above Panel B. Not touched in this fix.

## Validated

- [ ] pending — user to confirm: upload a face angle and see it appear on its own card immediately, with the
      other three unchanged.
