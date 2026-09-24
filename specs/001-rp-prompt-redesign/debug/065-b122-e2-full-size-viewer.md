# 065 — B-122 E-2: viewing a body view at full size

**Status:** Delivered and verified live; 60 panel/body/studio tests green; app restarted on the dev DB
**Date:** 2026-09-22
**Items:** B-122 E-2 follow-up (operator: *"a thumbnail view is not good enough"*)

## Report

The grid showed a 56 px thumbnail with no way to inspect the render — the operator could see that an image existed,
but not whether the body, the marks or the anatomy were right, which is the whole point of reviewing a reference.

## What changed (`BodyViewsPanel.razor`)

- Every view with an artifact now has **View image** (and the thumbnail itself is a zoom-in button) opening a
  **full-size viewer**: a `modal-xl` dialog with the image fitted to the window (`max-height:78vh`).
- **Click the image to zoom to 100 %** (it renders at its natural pixel size; click again to fit). Verified live:
  fitted 428 px → zoomed **1024x1024**, the image's natural size.
- The viewer shows what the operator is actually judging:
  - **the source image** when the view was produced as an edit of an accepted view ("this view was produced as an
    edit of it"), so the drift between source and result is visible rather than remembered;
  - **the prompt it was rendered from**, collapsible;
  - **Open the raw file** for the untouched file in a new tab.
- The modal is the same pattern the studio already uses for front attempts, so the two review surfaces behave alike.

## Verification

- Browser, Becky's `Clothed Front`: **View image** opens the dialog titled `Clothed Front · Complete`, showing
  `/scene-images/assets/3d71ae259e8d4260b782513f3e5e24cd.png`; fit-then-zoom measured 428 px → 1024x1024 natural;
  the prompt block and the raw-file link are present.
- Tests **60/60** (`~BodyViewsPanel|~CharacterIdentityBody|~CharacterStudio`), including a new contract that pins the
  full-size path (the modal, the 100 % toggle, the source-beside-result, the prompt and the raw link) so a future
  change cannot quietly reduce it back to a thumbnail.
