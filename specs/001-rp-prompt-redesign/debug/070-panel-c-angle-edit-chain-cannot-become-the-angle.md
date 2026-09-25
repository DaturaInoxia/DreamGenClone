# 070 — Panel C: an angle's edit chain could not become the angle

**Status:** Done (328 tests green on the studio/identity/asset/media-edit filter; solution builds with 0 errors)
**Date:** 2026-09-24
**Report:** "Similar issue with three-quarter left: I did the edit, crop and enhance, the review deck allows me to
Accept the final enhanced one but it does not show as a 3/4 Panel as the accepted, it does not show the cropped edit
either."
**Follows:** `debug/069` (the same class of defect on the front candidate, Panel A/B).

## What the live records said

Face build `c05b2f5661804e0aab865ffafdc3c6c0` (`CurrentStep` = Angles), view `ThreeQuarterLeft`, container
`6563517022834e80893d987810446f24`:

| image | Kind | decision | source | batch |
|---|---|---|---|---|
| `aa726797…` | Edited | Undecided | canonical front | `angles-…-threequarterleft` |
| `9ea464b9…` | Edited | Undecided | canonical front | `angles-…-threequarterleft` |
| `496155b9…` | Edited | Undecided | canonical front | `angles-…-threequarterleft` |
| `7f6beb71…` | Edited | Undecided | `9ea464b9…` (**the crop**) | `angles-…-threequarterleft` |
| `666adf68…` | Edited | **Accepted** | `7f6beb71…` (**the enhance**) | `angles-…-threequarterleft` |

`CharacterIdentityAngles`: `Status = NotStarted`, `OutputArtifactId` **empty**, `AcceptedAttemptId` **empty**.
`CharacterIdentityAngleAttempts`: three rows, one per render, all `Complete(3)`.

The chain the operator built was complete, decided in the review deck, and had no route into the view.

## Defect — the attempt list can only accept a RENDER

`AcceptAttemptAsync` is the only acceptance path and it accepts an *attempt*: it writes
`angle.OutputArtifactId = attempt.OutputArtifactId`. The chain's images are not attempts — they are ordinary images
of the same candidate batch — so:

- the card's "attempts" list showed the three renders and nothing else ("it does not show the cropped edit either");
- no control could make the enhanced image the view, because the card's image (and the Promote readiness) read
  `angle.OutputArtifactId`, which stayed empty ("it does not show as the 3/4 panel accepted");
- the review deck's Accept only sets `CandidateDecision` on the image row — a decision the Faces page never read.

## Resolution

**One acceptance path for any image of the view** — `CharacterIdentityAnglesService.AcceptCandidateAsync(buildId,
view, imageId, manualConfirmed)`:

- **The batch IS the flow.** Every image of a view — render, upload, crop, enhance — joins
  `CandidateBatchIdFor(buildId, view)`, so membership is checked against that record and an image from another view's
  batch is refused by name. Nothing is inferred from looks.
- Requires `Status == Complete`; keeps the profile views' explicit visual confirmation; and applies the same gate to
  the attempt the image descends from (Complete or overridden) so a chain built on a failed render cannot sneak past.
- The attempt is resolved by walking `SourceImageId` upwards (bounded at 64 hops, and a non-terminating lineage is
  refused by name): the chain's acceptance still records **which render it came from**. When the link is lost by an
  older operation the acceptance is explicit and the link is left empty rather than invented.
- Exactly one attempt per angle stays `Accepted`; a previously selected render is demoted to `Complete`, so no two
  rows claim "Selected".
- The Angles step completes through one shared writer (`CompleteAnglesStepIfAllAcceptedAsync`), naming the accepted
  artifact — the same transition the attempt path used.

**Panel C lists the view's images.** Under the open card: every image of the view's batch, newest first, with the
review deck's decision badge, the recorded pipeline (`de-clothe ✓ · crop ✓ · enhance ✓`), the accepted marker, an
**Accept this image** action, **Edit image** (the media-edit page), and **Delete** (refused for the accepted one).
The per-view lists are read with the angle records (`LoadAngleViewImagesAsync`), so a card shows the chain the
moment it opens instead of only after an action.

## Validation

- `CharacterIdentityAnglesDirectionGateTests` +2: a two-hop chain (crop → enhance) built on an attempt is accepted as
  the view (artifact = the chain's image, attempt link resolved two hops up, source recorded from the render), and an
  image of another view's batch is refused with the batch named while the angle is left untouched.
- `CharacterStudioFacesContractTests` +1: Panel C lists the batch and calls the acceptance path, reading the batch
  from the one helper that defines it.
- `dotnet build DreamGenClone.sln` → **0 errors**. Targeted angle/Faces/build-service run → **52 passed / 0 failed**;
  the wide `CharacterStudio|CharacterIdentity|SceneAsset|MediaEdit` filter → **328 passed / 0 failed**.

## Open (unchanged from 069)

1. Edit sessions left non-terminal (`f64c66e4…`, `39ac15a7…`) with `database is locked` faults in the log.
2. `SourceImageId` still null on the first edit of a chain (the open finding from
   `001-final-writing-instruction/debug/056`) — the angle acceptance survives it (it treats an unresolvable link as
   "no link" and says so), and the lineage walk in the promotion path uses checksums.
3. Whether the review deck's edit action should be offered before the build reaches the edit/angle steps.
