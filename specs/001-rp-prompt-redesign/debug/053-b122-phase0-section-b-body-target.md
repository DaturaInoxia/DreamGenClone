# 053 — B-122 Phase 0 Section B: the body target is a plan, and every body view is one request

**Status:** Done (71 focused tests green; both step plans verified in the dev DB; app restarted and clean)
**Date:** 2026-09-22
**Items:** B-122 B122-005 … B122-010 (Section B), following `debug/050` (Section A)

## Report

Section A gave the body target a BodyCard and its seeded prompt/setting surface, but the target itself did not
exist: `CharacterIdentityStepPlans` held the Face plan only, `CharacterIdentityBuildHandlers.Known` had no body
handler, the template keys were unreachable, and nothing could produce a full-body reference. Section B makes the
body target real on B-121's machinery — no second pipeline, no second studio, no second edit primitive.

## Decisions

**The plan carries five steps, and that needed one new step member.** The task's step set is acquire base →
validate base → one requested view → validate views → promote. The existing enum is face-shaped (Front, Validate,
GarmentRemoval, Crop, Enhance, Angles, Promote), so `CharacterIdentityBuildStep.ValidateView = 8` was added: a
body build validates the base AND the views produced from it, while the face plan keeps gating its views inside
Angles. The Face plan's rows are untouched (it is seeded from `CharacterIdentityBuildSteps.Ordered`, which is
unchanged), and the plan's *order* is what drives the pipeline — the step ids happen to be 1, 2, 6, 8, 7 at orders
1…5, which is exactly what a plan separate from the enum buys.

**Per-view body state is a first-class row, not a JSON blob.** `CharacterIdentityBodyViews` is keyed by the
request — `(BuildId, State, View)` for a canonical view, `(BuildId, State, RotationDeg, PositionKey)` for an
extended one — with the usual lifecycle columns (status, input/output artifact, resolved prompt/model, failure
reason, attributed override). `SceneImageReferenceAsset` needs `BodyView`/`BodyState` at promotion time anyway,
and a row that cannot be misread as a canonical slot is what makes the pack unambiguous. The lifecycle reuses
`CharacterIdentityAngleStatus` (same states, no second state machine).

**A body view is never invented.** The prompt is the ONE template store's, with the body card line pasted in
(`{BodyCard}`) and the character name filled; an incomplete card fails fast naming the missing `[DECIDE]` item.
Every view is a same-image edit of an accepted parent — the three-quarters of the accepted front base in the same
state, a profile of the accepted three-quarter on its own side, the unclothed base of the accepted clothed base —
through the shared `ISceneAssetService.EnqueueImageEditAsync`. The clothed front is the root: it is generated or
uploaded, and asking to *edit* it is refused naming that a base has no parent. One request produces exactly one
image, in the same build container, with its own candidate batch (`body-{buildId}-{state}-{view}`).

**Re-running replaces the row's artifact; the images stay as history.** Unlike the face angles there is no
per-attempt table for bodies — B-122 does not ask for one, and the container plus its batches already hold the
history. Recorded in the task list as a deliberate difference rather than an omission.

## What was built

| Piece | Where |
|---|---|
| `ValidateView` step; body handler keys (`front.body`, `validate.body.base`, `angle.body`, `validate.body.view`, `promote.bodyPack`) | `CharacterIdentityBuildModels.cs`, `CharacterIdentityStepPlanModels.cs` |
| Seeded Body plan (Kind=2), shared row upsert, startup ensure | `CharacterIdentityBuildRepository.cs`, `Program.cs` |
| Request key with axis validation + prompt mapping | `CharacterIdentityBodyModels.cs` |
| `CharacterIdentityBodyViews` table + upsert/list | `CharacterIdentityBuildRepository.cs`, `ICharacterIdentityBuildRepository.cs` |
| Acquisition service (container, prompt, generate, edit-from-accepted, record-source, upload, record-result, accept, override) | `ICharacterIdentityBodyService.cs`, `CharacterIdentityBodyService.cs` |
| Registration | `Program.cs` |

## Tests

`CharacterIdentityBodyServiceTests` (13) + the plan/build tests moved onto an unshipped kind (`(…Kind)99`),
because `Body` now has a seeded plan and two tests had been using it as the "no plan" case:

- generation writes one image, with the batch, size and the card line in the prompt; the base's request row holds
  `Clothed`/`Front` and the resolved model
- an incomplete body card refuses naming `Body hair pattern (a [DECIDE] item…)`; a missing card names the card -
  no image is produced in either case
- the service refuses a Face build, naming the target kinds
- the derivation chain: three-quarters need the accepted base; the right profile needs the RIGHT three-quarter;
  each edit carries its own batch and its key's prompt text
- the unclothed base is an edit of the accepted clothed base, while editing the clothed base is refused as a base
- extended views store rotation + position with no canonical slot, alongside the base row
- four axis-validation refusals (canonical + rotation, missing rotation, out-of-range rotation, missing position)
- accepting the base completes the acquisition step and moves the build to Validate

Runs: `CharacterIdentityBodyServiceTests|CharacterIdentityStepPlan|CharacterIdentityBuildServiceTests|CharacterBodyCard|CharacterStudioFacesContract|ImageEditWorkspace`
→ **71 passed / 0 failed**. Dev DB after restart: Face plan 7 rows, Body plan 5 rows, `CharacterIdentityBodyViews`
present.

## Next in Phase 0

Section C (body-invariant findings + explicit override, B122-011…014) then Section D (the `BodyComplete`
promotion, which the repository can already validate but nothing can reach: drafts are always `FaceOnly` and
`CanonicalFullBodyAssetId` is never set). Section E brings the BodyCard editor and the view grids, which is what
makes any of this user-drivable.
