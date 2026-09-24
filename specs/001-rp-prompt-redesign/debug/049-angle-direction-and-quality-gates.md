# 049 — B-121 Phase F/G: the angle direction gate and the promotion gates

**Status:** Done (code + tests green; 207 targeted tests pass) — the live acceptance run is B121-048 (item 2)
**Date:** 2026-09-21
**Items:** B-121 B121-024, 026, 027 (Phase F) and B121-030 (Phase G) — the gating prerequisite for B-122 Phase 0.4

## Report

Phase F and G were the last unbuilt gates of the identity studio, and both were the "persisted but unread"
kind of hole the repo forbids:

| Control (persisted) | Where it was | What read it |
| --- | --- | --- |
| `DeriveByMirrorThreeQuarterLeft/Right`, `DeriveByMirrorProfileLeft/Right` | `ReferenceWorkflowSettings` row | **nothing** — `MirrorDerived` was never written anywhere |
| `QualityGateMinSharpness` | same row | **nothing** — `QualityGateMinSharpness` was persisted and never applied |
| `ManualConfirmationRequired` (profiles) | angle record | the record-level accept only; `AcceptAttemptAsync` let a `Complete` profile attempt through without it |

Nothing measured a render's direction, so a wrong-sided 3/4 or profile could be accepted and promoted; and
promotion itself re-checked nothing beyond "an attempt was accepted" and "the Angles step is complete".

## Decision

The direction gate reads **one number**: the measured nose offset (signed, image space) from the approved
MediaPipe tool. It never reads `irisDy%` or interocular distance — those are invalid under yaw (FR21-023, D4),
and the accepted *Becky* set is the evidence: on the very images where the nose offset called the sides
correctly (−45.12 % for 3/4 left, +27.70 % for 3/4 right), iris dy read −13.63 % and −4.29 %.

Full profiles are deliberately **never asserted**: the tool returns no face mesh on a true profile (profile
left measured literally "no face mesh" on the accepted set). A profile's direction stays the user's explicit
confirmation, which is why B121-027's enforcement lands with the same change rather than as a later task.

The remedy is synchronous. A queued mirror operation would leave the angle attempt `Pending` with no link to
the job that completes it, so the wrong-facing render would block the card forever. Mirroring is deterministic
and local (ImageSharp flip), so it runs inline through the shared `IImageMirrorEngine` and lands as its own
`SceneAssetImage` via the new `AddDerivedImageAsync`. The queued plumbing (`MediaEditOperation.ForMirror`,
`EnqueueMirrorAsync`, `MirrorOperationExecutor`, `EnqueueSceneAssetImageMirrorRequest`) was written first and
then **removed as dead code** once the synchronous path was chosen.

## Resolution

- **Tool** (`tools/eye-validation/measure_iris.py`): the canonical tool now emits `nose_tip` and a signed
  `nose_offset_pct = (nose_tip_x − face_box_centre_x) / face_box_width × 100` (negative = image-left), in the
  table and in JSON, with a cyan midline + yellow nose marker annotation; README documents the convention and
  the evidence table. **Verified on real images** (3/4L −45.12 %, 3/4R +27.70 %, profile L "no face mesh",
  profile R +48.60 %), annotation visually checked.
- **Gate** (`CharacterIdentityAngleYawGate`): `RequiresAssertion` (3/4 views only), `RequiredSign` (Left −1 =
  image-left, Right +1), `Evaluate(view, measurement, minAbsPercent)` → `CharacterIdentityYawVerdict`
  (`Passed`/`Asserted`/`NoseOffsetPercent`/`BlockReason`). Fails fast on a non-positive deadband; throws when an
  asserted view has no measurement to judge.
- **Setting**: `ReferenceWorkflowSettings.AngleYawMinAbsPercent` (seeded `5.0`) — column, DDL, ALTER guard,
  SELECT/INSERT/UPDATE, `ReadSettings` ordinal, and the front-service copy.
- **Service** (`CharacterIdentityAnglesService`): `UploadAsync` and `RecordResultAsync` both end in
  `ApplyDirectionGateAsync` — measure (via the one `ICharacterIdentityMeasurementService`), store
  `attempt.MeasurementJson` as evidence, then pass / mirror / block. `AcceptAttemptAsync` now enforces the
  profile confirmation (B121-027).
- **Mirror**: `IImageMirrorEngine`/`ImageMirrorEngine` (`Flip(Horizontal)`), `MediaEditOperationKind.Mirror = 4`,
  `MediaEditProvenance.MirrorValue`, and `ISceneAssetService.AddDerivedImageAsync` (refuses `Unknown`/`Edit`,
  requires the source in the same asset, writes a real file + provenance). A mirrored render is a NEW attempt
  with `MirrorDerived = true`; the wrong render stays as `Failed` evidence.
- **Promotion** (`CharacterIdentityPromotionService`): three gates, each re-using the ONE owner of its rule —
  the Validate gate via `ICharacterIdentityValidationService.GetGateAsync`, the direction convention via
  `CharacterIdentityAngleYawGate` over the accepted attempt's **recorded** measurement (never a second
  measurement, so promotion agrees with the angle panel), and the sharpness floor via the ONE
  `IReferenceImageQualityAnalyzer` metric (now exposed as `ComputeSharpness`). Each block names the view. A
  recorded manual override on the attempt defers the direction gate, exactly as it does in the panel.
- **UI** (`CharacterStudio.razor`, Panel C): the card now shows the gate's block reason (`attempt.FailureReason`
  and `angle?.FailureReason`), the `Mirror-derived` attribution, and a `DescribeDirection(attempt)` line with the
  measured nose offset (or the tool's reason for a view that is not asserted). The profile confirmation control
  now renders whenever that confirmation is outstanding (it used to require `Pending`, which the upload path
  never sets — a dead end the new enforcement would otherwise have created).

## Tests

| File | Covers |
| --- | --- |
| `CharacterIdentityAngleYawGateTests` (14) | both passing sides; facing-camera block; wrong-side block with the measured value and the deadband; profiles never asserted (with or without a measurement); **iris/interocular nonsense cannot move the verdict** (the required regression guard); no-face-mesh block naming the override; missing measurement throws; non-positive deadband throws; the sign convention per view |
| `CharacterIdentityAnglesDirectionGateTests` (5) | the mirror remedy end-to-end on **real bytes** (the derived file must be the horizontal flip: left↔right swapped AND the bottom band still at the bottom), the attempt bookkeeping (`MirrorDerived`, attempt 2, failed evidence), the disabled remedy blocking with no image written, the passing render keeping its measurement as evidence, the queued-render path going through the same gate, and a profile needing (and then accepting) the explicit confirmation |
| `CharacterIdentityPromotionGateTests` (7) | ready when every view clears both gates; a blurry front blocked with the measured sharpness and the configured floor; a wrong-facing accepted attempt blocked naming the view; the recorded override deferring it; the validate gate quoted verbatim; a non-positive floor failing fast; and the five promoted views carrying five distinct correct `SceneImageReferenceFaceView` values (D7) |
| `CharacterStudioFacesContractTests` (+2) | the card exposes the verdict/measurement/mirror attribution; the profile confirmation is not hidden behind `Pending` |

Run: `CharacterIdentity|CharacterStudio|Reference|MediaEdit|ImageWorkflow` filter → **207 passed / 0 failed**.

## Two findings from the tests themselves (worth keeping)

1. **The direction gate must not be judged on the wrong number.** The first version of the service test asserted
   the mirror on one pixel row and failed, which looked like an engine defect; widening the check to both axes
   proved `FlipMode.Horizontal` in ImageSharp 3.1.11 *is* the left↔right mirror, and that the test's own
   expectation was wrong. The final assertion pins both axes so a vertical flip can never pass as a mirror.
2. **A returned service record is a snapshot.** `AcceptAngleAsync`/`AcceptAttemptAsync` re-read the record, so
   asserting acceptance on the object `UploadAsync` returned reads a stale `Complete`. The tests now read the
   state back through `ListAsync` — the same trap the UI avoids by refreshing the panel after every action.

## Open (not this item)

- `ReferenceWorkflowSettings` has **no UI editor** for any of its columns (eye gate, yaw deadband, quality floor,
  crop, mirror flags, tool path). `AngleYawMinAbsPercent` is persisted, seeded and editable through the DB tool,
  like its siblings, but no page exposes the table. Named as a follow-up.
- `PackScope` is still `CreateDraftPackAsync`'s default rather than explicit `FaceOnly` (B121-031 note); it
  becomes explicit when B-122 Phase 0 needs `BodyComplete`.
- B121-025a (single extended view with `ViewDescriptorJson`) is untouched — it is not part of Phase F/G's gates.
