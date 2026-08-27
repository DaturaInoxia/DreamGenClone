# Multi-Angle Reference Proof — Spec (2026-08-26)

## Goal

Complete the two-character mechanism investigation (Option 1). The single-frontal-reference
limitation broke Dean in the angled cells (C2 facing, C3 embrace): a frontal ref dominates and the
model has nothing to steer toward when the head turns. Hypothesis: if each cell conditions with a
reference photo whose **angle matches the target head angle**, the angled cells hold identity too.

This supersedes the FaceID probe (recorded FAIL — different face per angle, did not match the
PLUS FACE baseline). Mechanism stays **IP-Adapter `PLUS FACE (portraits)`** + regional `attn_mask`;
only the per-actor reference image now varies by angle.

## What the user provides (≥4 face images per character)

For **Dean** and **Becky** (ideally consistent hair/lighting; clear single face; no other people):

| View | File stem (Dean) | File stem (Becky) | Notes |
|---|---|---|---|
| Front (straight-on) | `dean_front` | `becky_front` | Already have both from existing packs |
| 3/4 left (head turned to their left) | `dean_34l` | `becky_34l` | **New** |
| 3/4 right (head turned to their right) | `dean_34r` | `becky_34r` | **New** |
| Profile (left or right) | `dean_profl` | `becky_profl` | **New** (choose the side the cells need) |

Minimum viable set: **Front + 3/4L + 3/4R + one profile** per character = 4 each. Files are dropped
into `artifacts/tmp/two-character-proof/multiangle-refs/` (png/jpg/webp); the runner uploads them
to the pod via `/upload/image` (no SSH/restart needed).

## Cell → angle mapping (Dean left, Becky right)

| Cell | Dean ref | Becky ref | Rationale |
|---|---|---|---|
| C1 side-by-side, facing camera | front | front | both near-frontal (control — expected to stay passing) |
| C2 facing each other | `34r` | `34l` | heads turn inward (Dean shows his right, Becky her left) |
| C3 embrace | `34r` | `34l` | same inward angle as C2 |
| C4 seated/standing | front | front | both near-frontal |
| C5 one behind | front | front | both near-frontal |
| C6 two-shot profile (both facing right) | `profr` | `profr` | right profile |

## Run

```powershell
# check which refs are present
& d:\src\DreamGenClone\.venv\Scripts\python.exe artifacts\tmp\two-character-proof\run_multi_angle_proof.py --list
# focused pass on the previously-failing cells + control
& d:\src\DreamGenClone\.venv\Scripts\python.exe artifacts\tmp\two-character-proof\run_multi_angle_proof.py --cell c2 --seed 1001 --strength 0.8
```

12 cases (6 cells × 2 seeds 1001/1002) → `artifacts/tmp/two-character-proof/outputs-multiangle/`.

## Gate (same as the matrix)

Pass if, per human review, both actors are recognizable in all 12 (Identity ≥ 4), cross-
contamination ≤ 2, and **no** case below Identity 3 — specifically Dean must now hold identity in
C2/C3. C1 remains the control (must not regress).

## Status

- [x] App-side multi-angle support implemented (`SceneImageReferenceFaceView` on assets,
      `/characters/identity` upload has a Face view selector, migration backfills existing faces as Front)
- [ ] User provides ≥4 angle-tagged images per character
- [ ] 12 multi-angle cases rendered
- [ ] Scorecard updated + mechanism gate decision
