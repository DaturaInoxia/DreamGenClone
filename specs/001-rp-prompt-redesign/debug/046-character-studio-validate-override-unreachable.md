# 046 — Character Studio: Validate override control unreachable for a deleted recorded front

**Status:** Fixed (code + targeted tests green; awaiting user confirmation in the browser)
**Date:** 2026-09-21

## Report

Character Studio, character `f58f959a-8050-4388-a219-99d2df3446a1` (Becky), route
`/characters/f58f959a-8050-4388-a219-99d2df3446a1`:

> "the manual override text boxes don't show allowing to override the validations issue"

The Faces panel sits at **Panel A · 1 Front · 2 Validate** and the eye gate blocks, but no override
inputs appear anywhere on the page. Clicking "Use this" answers with

> `Validate is Fail: Eye gate failed: |irisDy%| 5.45 exceeds the configured 1.50. Record a manual
> override with a reason to continue.`

i.e. the page instructs the user to record an override it offers no control for.

## Analysis

### DB state (dev DB, read at 2026-09-21)

Build `b8adc0e742e645f7a4a7100ff4796a94`:

| Fact | Value |
|---|---|
| `CurrentStep` / `Status` | `Validate` (2) / `InProgress` |
| Front step | `Complete`, `OutputArtifactId = 93eae67af55d417ca3a69da8bcc19f0a` |
| Does that image exist? | **No** — 0 rows in `SceneAssetImages` (the row was deleted; `UpdatedUtc` of the Front step is 2026-09-21T20:56:35) |
| `FrontContainerAssetId` | `c04a3531496a4b4c9d2f3408762cbb90` ("Becky front") |
| Images in that container | 1: `7c3a6c8dda004c398904b1fd92fb6ba7` (Uploaded, `Rejected`) |
| Its validation | `verdict: Fail`, `irisDyPercent: -5.45`, `thresholdPercent: 1.5` |
| Validate step row | `NotStarted`, `MeasurementJson` empty, `ManualOverrideApplied = 0` |

**The build names a front image that no longer exists.** Queries used (all permanent, in
`DreamGenClone.DbQuery/queries/`): `b121-character-f58-builds.sql`, `b121-f58-steps.sql`,
`b121-f58-front-attempts.sql`, `b121-f58-front-images.sql`, `b121-find-93eae.sql`,
`b121-f58-container-detail.sql`.

### Code paths

`DreamGenClone.Web/Components/Pages/CharacterStudio.razor`:

- The override inputs were rendered **only** inside the card that is the build's recorded front *and*
  only for a `Fail` verdict:
  `@if (IsChosenFront(attempt) && validation?.Verdict == CharacterIdentityValidationVerdict.Fail)`.
- `IsChosenFront` (line ~716) compares against the Front step's `OutputArtifactId`. The recorded id
  (`93eae…`) matches **no** image in the container, so the condition is false for every card: neither
  the override inputs nor the **Continue** button ever render.
- `SelectFrontAsync` then refused the selection itself — the "different candidate" branch required
  `Verdict == Pass` — so the user could not even re-record a front that would expose the override.
- `DeleteImageAsync` protected only the *canonical* front, so the recorded Panel A front could be
  deleted at will; that is how the dangling reference arises.
- The same inputs were also suppressed for `NoFaceMesh`, although
  `CharacterIdentityValidationService` explicitly supports overriding it (test
  `Measure_NoFaceMesh_BlocksUntilOverrideIsRecorded`) and `ui-contract.md` §3 says the override
  control **is always visible**.

`DreamGenClone.Web/Application/RolePlay/CharacterIdentityBuildService.cs`: `ReRunStepAsync` reset
every step from the re-opened one onward but left `ManualOverride*` intact, so a newly chosen front
would inherit an override recorded about a different image.

## Plan (approved by user: "All 4 items" + clear the stale override)

1. Render the override inputs for every candidate whose verdict is `Fail` **or** `NoFaceMesh`.
2. Let "Use this" record a different candidate regardless of the eye verdict; measure it, advance only
   if the gate passes, and otherwise leave the build blocked on that candidate's own verdict (where the
   override control now is).
3. Name the deleted-recorded-front state in the UI instead of silently showing no chosen card.
4. Refuse to delete the build's recorded front (card control disabled + handler guard).
5. Clear `ManualOverride*` when a step is re-run, so no candidate inherits another image's override.

## Resolution

- `DreamGenClone.Web/Components/Pages/CharacterStudio.razor`
  - Panel A: override block now gated on `validation is not null && (Fail || NoFaceMesh)` — verdict
    specific wording, no `IsChosenFront` requirement.
  - Panel A: new notice when a manual override is recorded (`_validateStepRecord`) and a warning when
    the recorded front artifact is missing (`_recordedFrontArtifactMissing` / `_recordedFrontArtifactLabel`).
  - `SelectFrontAsync`: the "different candidate" branch no longer requires `Pass`; it re-opens Front,
    records the candidate, measures it, and advances only when `CanAdvance`.
  - `DeleteImageAsync`: refuses `IsChosenFront(image)`; card delete button disabled for it.
- `DreamGenClone.Web/Application/RolePlay/CharacterIdentityBuildService.cs`
  - `ReRunStepAsync` clears `ManualOverrideApplied/Reason/Author/Utc` on reset rows.
- Tests: new `DreamGenClone.Tests/RolePlay/CharacterStudioValidateOverrideContractTests.cs` (4 tests,
  source-contract: control not gated on the recorded front, offered for both blocking verdicts,
  records against the build, recorded front protected) and
  `CharacterIdentityBuildServiceTests.ReRunStep_ClearsTheManualOverrideRecordedOnTheResetStep`.
- Targeted run: `CharacterStudioValidateOverrideContractTests`, `CharacterIdentityBuildServiceTests`,
  `CharacterIdentityValidationServiceTests`, `ImageEditWorkspaceContractTests` → **36 passed, 0 failed**.

## Follow-up in the same session — steps 3-5 had no path to the face angles

**Report (same character, after the fix above):** *"it should now allow the user to continue on with the
face angles … currently it is stuck, it thinks the front face needs to be clothing removed, cropped,
enhanced."*

**Analysis.** Panel B only ever offered **"Use this"** (→ `SetCanonicalFrontAsync`) on **edited** images
(`_editedImages`, i.e. `Kind == Edited`). With no edits made, `_editedImages` is empty, so the build sat at
`GarmentRemoval` with no control that could finish steps 3-5 — while nothing *needs* an edit: the front here
is an uploaded image.

The service already supports the no-edit case: approving the Front artifact itself makes
`ResolveLineageAsync` return an empty chain, so `DeriveFrontPipeline` records **de-clothe, crop and enhance
as `Skipped`** (the design's own recorded state for "this image never went through it") and
`FirstIncomplete` puts the build at `Angles`. The face angles themselves cannot start without a canonical
front: `CharacterIdentityAnglesService.UploadAsync` / `PrepareAsync` / `RunAsync` all require
`build.CanonicalFrontAssetId`. So the missing piece was purely the UI entry point.

**Resolution.**

- `CharacterStudio.razor` Panel B: new **"Use the front as-is — skip 3 · 4 · 5"** control calling
  `ApproveFrontAsIsAsync`, which approves `_garmentSource` (the candidate chosen in Panel A, resolved by
  `CharacterIdentityGarmentService.ResolveCurrentInputArtifactId` at `GarmentRemoval`). It renders only while
  no canonical front is approved — offering it later would rewrite the recorded lineage of steps 3/4/5.
- `ApproveCanonicalFrontAsync` now reports what was **actually recorded**, read from the approved image's own
  pipeline record (`de-clothe — skipped · crop — skipped · enhance — skipped`) instead of implying edits
  happened.
- Test: `CharacterIdentityBuildServiceTests.SetCanonicalFront_WithoutAnyEdit_RecordsTheThreeStepsAsSkipped_AndMovesToAngles`;
  contract tests moved to `CharacterStudioFacesContractTests` with
  `PanelB_OffersTheNoEditPathToTheFaceAngles`.
- Targeted run: **95 passed / 0 failed** (`CharacterIdentity|SceneAsset|ImageEditWorkspaceContractTests`),
  then **58 passed / 0 failed** after the rename.

**State of the Angles step (verified by reading, for the same session):** uploads (`UploadAsync`) and edit
results (`RecordResultAsync`) are recorded `Complete`, and `AcceptAttemptAsync` accepts any `Complete` attempt,
so nothing blocks a manual flow there. The per-attempt override inputs render only for a `Failed` attempt and
nothing in the code sets an angle attempt to `Failed`, so that control stays dormant — reported to the user
rather than pre-emptively wired.

## Validated

- [ ] pending — user to confirm in the browser: the Fail card now shows the override inputs, recording
      an override then "Use this" advances Panel A to step 3.

**Recovery note for this build:** the user can simply click "Use this" on the only candidate, record the
override, then Continue; the dangling `93eae…` front is replaced by `7c3a6c8d…` in the same action.
