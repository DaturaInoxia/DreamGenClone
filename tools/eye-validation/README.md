# eye-validation — MediaPipe iris/eye-level checker

**Approved tool.** Measures whether a face's eyes are level, even, and symmetric
with real iris landmarks (MediaPipe FaceMesh `refine_landmarks=True`).

## Why this exists
Every naive OpenCV approach fails to pinpoint irises on photoreal portraits:
- Haar eye-box centers are mis-centered (they often wrap the brow + eye together)
  and box sizes are asymmetric, so dy from box centers is garbage.
- Dark-region centroids lock onto the **brow/hair**, which are darker than the
  pupil (a marker can sit visibly off the iris).
- Hough circles return ~14 false positives everywhere on a face.

FaceMesh `refine_landmarks` returns true iris landmarks (468/473) plus eye-corner
landmarks, giving a trustworthy iris/eye vertical offset. Verified by tight 4x
zoom that the markers sit on the iris.

## Outputs
Per image prints:
- `irisDy%` = (right iris y − left iris y) / interocular × 100 (iris landmarks)
- `eyeDy%`  = same using eye-corner midpoints (33/133 vs 362/263) — independent
  cross-check that should agree in sign/magnitude
- `interoc` = eye-corner-center distance in px

And writes an annotated copy to the **git-ignored** output dir
`artifacts/tmp/eye-output/<stem>_iris.png` (green ring = iris landmark, blue =
eye-corner center, red = level line through the left eye). **Always verify the
markers sit on the irises before acting on the number.**

## Interpretation
- `|dy| < ~1.5%` ≈ level (natural). `~2%+` is visibly uneven.
- Positive = image-right eye lower; negative = image-left eye lower.
- To tell **head roll** from **true eye asymmetry**, also measure the mouth
  (corners 61/291), brow (70/300), and nostril (98/327) lines. Uniform tilt
  across all = head roll (harmless). Eye/brow tilted but mouth/nostril near 0 =
  real left-or-right-eye-lower asymmetry.

## Run
```powershell
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/eye-validation/measure_iris.py <image1.png> [image2.png ...]
```

Dependencies: `requirements.txt` in this folder (`mediapipe==0.10.21`,
`opencv-python-headless==4.10.0.84`, `numpy`) — install into the repo venv.

## Adding/editing
- Keep output to git-ignored paths (`artifacts/tmp/**`) — never write artifacts
  into `tools/`.
- Pin versions; note why in this README.
