# 064 — B-122 E-2 follow-up: "Generate does nothing" (completion + settings-driven size)

**Status:** Fixed and verified live on Becky's card; 67 tests green; app restarted on `dreamgenclone.dev.db` (Development)
**Date:** 2026-09-22
**Items:** B-122 E-2, reported by the operator as *"clicking generate nothing happens"* and *"user should not have to type the size"*

## Root cause of "nothing happens" — two separate defects

**1. Nothing ever completed a body view (the real one).** `CharacterIdentityBodyService.GenerateAsync` enqueues a render on
the build's container asset and leaves the view at `Pending`. The render job runs and finishes
(`scene-asset-generation` job: created 18:38:29Z, **Complete** 18:39:07Z), and the finished image lands on the
container — but **no caller ever invoked the view's completion path**, `RecordResultAsync`. The face pipeline calls it
from the studio's edit-workspace callback (`OnAngleResultAsync`); body views had no equivalent, so the row stayed
`Pending` for ever and the operator saw a button that "did nothing". Verified before the fix: the view row read
`Status=2 (Pending)` while its `scene-asset-generation` job was `Complete`.

**2. My own panel defects, from yesterday's slice.** The extended-view `Generate`/`Open actions` called
`key.Describe()`, which *validates* and throws when the position key is empty — an exception out of an event handler, so
the button silently did nothing. And Generate/Re-run/Edit were clickable with no model and no size, answering only with
a row message that was easy to miss.

## What changed

- **Completion.** `BodyViewsPanel` reconciles on every refresh: a `Pending` view whose `OutputArtifactId` image row is
  `Complete` is recorded through `BodyService.RecordResultAsync(BuildId, view.Key(), view.OutputArtifactId!)`, then the
  views are re-read. This is a pragmatic caller for the service's existing completion path; a job-side finalizer would
  be better long-term and is noted as debt.
- **Self-refresh.** The panel now polls every 5 s while anything is `Pending` (a `Timer` disposed with the component),
  so the image appears without a manual refresh and the badge visibly moves.
- **No per-view typing.** The model and the render size moved out of the grid and into **persisted workflow settings**:
  `ReferenceWorkflowSettings.BodyImageSize` (new; column + ALTER guard + round-trip, seeded `1024x1536` as the global
  starting value) beside the existing `BodyModelId`. New service surface
  `ICharacterIdentityBodyService.ResolveViewSettingsAsync` / `SaveViewSettingsAsync` (validates a model is set and the
  size is a `WxH` pair, normalising case, and writes a character row carrying the other resolved settings so the
  override cannot blank them).
- **One place to set them.** The Body tab gained a **Body view settings** card (model picker + size + Save) above the
  grid; the grid takes `ModelId`/`ModelName`/`ImageSize` as parameters, shows them read-only, and keeps Generate /
  Re-run / Edit disabled with a named reason while either is unset.
- **Extended-view guards.** The extended actions validate their key *before* anything calls `Describe()`, refusing with
  *"An extended view needs a position key (for example 'standing')…"* at the panel level instead of throwing.

## Verification (live, Becky `de351eb3…`, body build `85441803e157429f9707dcde79bacb38`)

- Settings card reads **FLUX.1-dev fp8 (Local ComfyUI)** at **1024x1536**; the grid has **no size input and no model
  picker**, and shows `Model … · size 1024x1536 — both come from Body view settings above`.
- The previously stuck row is **`Clothed Front · Complete`** with its artifact
  `/scene-images/assets/3d71ae259e8d4260b782513f3e5e24cd.png` and the resolved prompt (373 chars, body-card line
  embedded). The panel completed it by itself on load — the exact path that was missing.
- I opened the produced image: a real full-body studio render, consistent with the card (curvy/full bust/soft
  waist/wide hips, dark hair, tree tattoo on the left calf).
  **Honest caveat for the operator:** the *Clothed Front* slot rendered as a near-unclothed body (bikini bottom) even
  though the prompt says "Wearing plain everyday clothing", and it came out square rather than the 1024x1536 portrait
  (the earlier attempt passed the size as `1024X1536`). Both are prompt/model-fidelity questions for the body set, not
  the panel wiring — worth a look now that views complete at all.
- Tests: **67/67** (`~BodyViewsPanel|~CharacterIdentityBody|~CharacterStudio|~ImageWorkflow`), including four new
  settings tests and three new grid contracts (settings-driven model/size, no per-view typing, completion +
  self-refresh present).

## Data applied (dev DB)

`ReferenceWorkflowSettings` global row: `BodyImageSize = '1024x1536'` (the seed's starting value, since
`INSERT OR IGNORE` cannot update an existing row) and `BodyModelId` set to the model the operator had already chosen
from the grid (`5814a47a-…`, FLUX.1-dev fp8). Both are editable in the new card, per character.
