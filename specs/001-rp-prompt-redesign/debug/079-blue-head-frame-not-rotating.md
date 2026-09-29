# 079 — The head's blue frame does not turn, so the face skews against its own skull

## Report

Operator, 2026-09-28, verbatim: **"it is not rotating the main blue line 5 dot connection so the face is all skewed, it
needs to be kept in sync with the other dots."**

Follows 078: once the face pose's controls actually turned the face, the 70 white face dots rotated while the blue head
frame they belong to stayed where it was.

## Analysis

`PoseSkeletonRenderer.BodyLinks` opens with **five blue** links at `Rgb24(0, 0, 255)`:

```
(1, 0), (0, 14), (14, 16), (0, 15), (15, 17)
```

neck→nose, nose→right eye, right eye→right ear, nose→left eye, left eye→left ear. That IS the operator's "main blue
line 5 dot connection". Those five points — COCO 0, 14, 15, 16, 17 — live in the **BODY** channel.

`PoseFaceProxy.Project` returned `Body = pose.Body` **untouched**, rotating only `pose.Face`. So the face turned under a
head frame that did not.

### Measured (query: `DreamGenClone.DbQuery/queries/head-pose-blue-frame.sql`)

'34 Right Head', face box x 261..893 (width 632), y 468.9..1093.9:

| COCO | point | x, y | conf | lateral = \|x−centre\| / (width/2) |
|---|---|---|---|---|
| 0 | nose | 801, 723.5 | 1.0 | 0.71 |
| 14 | right eye | 587, 584.7 | 1.0 | 0.03 |
| 15 | left eye | 826, 567.5 | 1.0 | 0.79 |
| 16 | right ear | 238, 675.7 | 1.0 | **1.07** |
| 17 | left ear | 0, 0 | **0.0** | not detected |
| 1 | neck | 498, 1218.3 | 1.0 | 0.25, and **below the chin** (box ends y 1094) |

Two facts fall out of that:

1. **Five drawable points**, which is exactly the count the operator described. `Rasterize` skips a body link unless BOTH
   endpoints clear `VisibilityFloor`, so the undetected left ear at (0,0) is never drawn.
2. The right ear sits at lateral **1.07 — OUTSIDE** the face box the depth profile is measured across.

### Why that second fact mattered

`DepthRatio(lateral)` runs from the nose, out through the eyes, to the ears, where the depth is **zero by construction**.
Past lateral 1 the eye→ear segment keeps going:

```
eyeDepth * (1 - ((lateral - eyeLateral) / (1 - eyeLateral)))      // negative once lateral > 1
```

so an unclamped outside-ear is given a **negative** depth and swings to the far side of the rotation axis — a point
ending up forward of the nose. That is precisely the shear the mannequin approach exists to avoid (see the measured
rejection of the flat-cutout and monocular-lift methods). The clamp is therefore geometry, not a guard: *there is no
further back for a head to go than its own ears.*

## Plan

1. Extract the per-point mapping into ONE `Turn(x, y, confidence)` local function so the face and the head frame cannot
   be transformed differently.
2. Apply it to the face's visible points **and** to `HeadBodyIndices = [0, 14, 15, 16, 17]` — the head's own points in
   the body channel. The **neck is deliberately excluded**: it is the body's anchor and the pivot, not part of the head.
3. Clamp `lateral` to `[0, 1]`.
4. Skip any head point below `VisibilityFloor`, so an undetected one keeps the value it was given instead of being
   dragged in from the corner of the canvas.
5. Leave the LIMBS untouched, deliberately — they are the detector artifacts of a face crop, and moving them would be
   inventing motion for joints that are not in the picture.

## Resolution

| File | Change |
|---|---|
| `PoseFaceProxy.cs` | `HeadBodyIndices`; the shared `Turn` mapping; lateral clamped at the ears; invisible head points and all limbs untouched |
| `PoseFaceProxyTests.cs` | `AYaw_MovesTheHeadsOwnPointsInTheBodyChannel`, `AnUndetectedHeadPointAndTheLimbs_AreLeftExactlyWhereTheyWere`, `AtAProfile_AnEarOutsideTheFaceWidth_LandsOnTheCentreLine_NotBehindIt` |

### Verification

Rendered the **real** '34 Right Head' out of the dev DB at yaw 0 / 5 / 20 / 45 with one shared framing and looked at each
one: the blue frame tracks the white face cloud through the turn instead of staying put.

One test failed first and was **the fixture, not the code**: expected 577, got **577.164**. The fixture's 69-angle
ellipse never lands on the left-hand extreme (that needs index 34.5), so the face box came out `261.33..893` — centre
577.16 — while the assertion was written against 261..893. The proxy had done exactly what the clamp says. Fixed by
pinning the two extreme points in the fixture, which is recorded in the test's own comment.

**Build** 0 errors. **Pose suites** 253 passed / 2 failed, both pre-existing (`PoseTestCharacterConditioningContractTests`
asserts on `PoseLibraryPage.razor`).

## Validated

- [ ] pending — awaiting the operator restarting the app and confirming the blue frame turns with the face.
