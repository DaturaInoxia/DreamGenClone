# 069 — Body 3/4 left and right render the same direction (duplicate angle skeleton)

- **Reported by**: operator — "3/4 left and right are same angle", character `de351eb3-69d3-421a-a762-79ae8ee183ed` (Becky)
- **Route**: `/characters/de351eb3-…` (Body views)
- **Date**: 2026-09-23

## Report

The Clothed `ThreeQuarterLeft` and `ThreeQuarterRight` body views showed the same facing direction.
Body build `8333698fb13244d2ba40b3d6c2dc17c1`; view rows `46341b9d…` (BodyView 2) and `bf505eee…` (BodyView 3).

## Analysis

### The prompts were correct

All 8 body angle templates in the live DB are distinct and unedited (`Body = SeedBody`), including
`identity.body.angle.render.three-quarter` ("…facing left of frame") and `…three-quarter.right`
("…facing right of frame"). The two stored `ResolvedPromptText` values are correct and differ.
The output artifact files are genuinely distinct (different SHA-256, 2,093,168 vs 1,939,581 bytes).

### The renders both faced LEFT — measured

`tools/eye-validation/measure_iris.py`, `nose_offset_pct` (negative = nose toward image-left):

| View | Prompt says | Measured | Verdict |
|---|---|---|---|
| Front base `ab27c7ca` | facing camera | −2.03 % | straight-on |
| `ThreeQuarterLeft` `65027ac1` | facing left | **−16.21 %** | left — correct |
| `ThreeQuarterRight` `f245afd5` | facing right | **−14.52 %** | **left — WRONG** |

For reference, the correct Becky face angles measured −45.12 % / +27.70 % (opposite signs).

### Root cause: the two 3/4 skeletons are the same pose

`BodyAngleSkeletons` maps `ThreeQuarterRight → angle-34-right.png`. Chamfer distance between the two
figures after scale/position normalisation:

| Pair | same orientation | mirrored | Verdict |
|---|---|---|---|
| **three-quarter** | **7.13 px** | 14.58 px | **same pose** |
| profile (control) | 33.19 px | **1.43 px** | true mirror |

Overlays confirmed it: left+right coincide (solid yellow); left + mirrored-right shows the limbs crossing.
So every 3/4-right render was conditioned on a left-facing pose — the skeleton, not the prompt, sets the turn.

### The failure was baked into the proof set

`RUNBOOK.md` case 13 verdict claimed *"pass (mirrored turn; the skeleton is the only changed input)"* — that
claim was **never measured**. Contrast cases 14/15, which cite a real discriminator (tattoo visible on near
leg vs correctly hidden on far leg). Proof `images/13-…png` is visibly the same direction as case 12.

### The annotation was not stale — the plate was wrong

Re-running the committed `plate34Right.json` (seed 20260922) and DWPose-annotating it reproduced the broken
skeleton **exactly** (`chamfer = 0.00 px` vs the original). Annotation is deterministic, so the plate itself
never turned right — the camera-position phrasing in the runbook was not sufficient for a 3/4 *body* turn.

### Why nothing caught it

The **face** angle path has a direction gate (`CharacterIdentityAnglesService.ApplyDirectionGateAsync` →
`CharacterIdentityAngleYawGate` → mirror remedy via `IImageMirrorEngine`). The **body** path has none:
`CharacterIdentityBodyService` never references the gate, the mirror engine, or `AngleYawMinAbsPercent`; it
copies `DeriveByMirror*` into the settings row (lines 116–123) but never consumes it. Nothing could flag two
same-direction views.

## Plan (operator approved: "do a then b")

- **A** — replace `angle-34-right.png` with a horizontal mirror of the left: immediate, deterministic stopgap.
- **B** — fix the plate wording/seed, re-annotate with DWPose, and install the genuine annotation.

## Resolution

**A** (stopgap, 2026-09-23): exact mirror installed — `chamfer(mirrored) = 0.00 px`, `IoU = 1.0000`.

**B** (proper fix):
- `prompts/plate34Right.json` — prompt rewritten to state the turn as a **view** and force an asymmetric
  occlusion (*"…her left arm largely hidden behind her torso, her body and head facing toward the right side
  of the frame"*); seed `20260922 → 20260924`.
- Re-annotated with `dwpose-annotate.json` → `angle-34-right.png` measures `16.63 px` same-orientation vs
  **`7.19 px` mirrored**: a genuine right turn, and *not* a clone of the left.
- All three copies updated together (`wwwroot/pose-library/`, `skeletons/`, runner staging) plus the pinned
  hash in `manifest.json` and `run-qwen-2-1-proof.ps1`; both scripts parse; all pins verified consistent.
- **Case 13 re-run and re-verified**: render now measures `nose_offset_pct` **+38.11 %** (was −15.73 %).
  Proof image replaced, manifest `result: passed` with the measured verdict, runbook case-13 row corrected.
- Originals preserved under `artifacts/tmp/dbquery/backup/pose-library/`.

Tests: `~BodyStanceSkeletons|~BodyAngleSkeleton|~CharacterIdentityBodyService|~SceneAssetGenerationJobHandlerIdentityReference` **79/79**.

## Open / follow-ups

- **The body view path still has no direction gate and does not consume `DeriveByMirror*`.** This skeleton fix
  makes the 3/4 pair correct, but the class of defect remains open — the same "declared but unwired" state
  debug `049` recorded for the face path before it was fixed.
- **Existing renders are not repaired.** Becky's `ThreeQuarterRight` (and anything else rendered from the old
  skeleton) must be re-generated.
- **A pair-guard test is cheap and would have caught this**: assert the 3/4/…/profile skeleton pairs are
  mirror-consistent (the profile pair gives a `1.43 px` reference value).
- **The proof package is UNTRACKED in git** — `specs/image-generator-tests/qwen-21-native-reference/`,
  `DreamGenClone.Web/wwwroot/pose-library/` and `helpers/local-comfyui-host/run-qwen-2-1-proof.ps1` are all
  untracked, so these fixes are not durable until committed.
- Skeleton regeneration belongs to `specs/Planning/B-128-dwpose-studio/`.

## Validated

- [x] Skeleton pair now measures as a genuine right turn (7.19 px mirrored vs 16.63 px same).
- [x] Case 13 re-rendered: `nose_offset_pct` +38.11 % (RIGHT), was −15.73 % (LEFT).
- [x] All hash pins consistent; 79/79 targeted tests green.
- [ ] Operator to re-generate the affected body views in the app and confirm the pair visually.
