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

With `--json` it also reports the **head extent**, used by the crop step so a
"headroom" percentage means head-relative space rather than arbitrary slack:
- `forehead_top` / `chin` = landmark 10 / 152 in pixels
- `head_height_px` = vertical distance between them
- `face_box` = `[x, y, w, h]` over all landmarks

> FaceMesh stops at the hairline: landmark 10 is the highest **facial** point, not the
> crown of the head. Hair above it is not measured, so `head_height_px` is slightly
> less than a true skull height. Do not read it as the top of the head.

And writes an annotated copy to the **git-ignored** output dir
`artifacts/tmp/eye-output/<stem>_iris.png` (green ring = iris landmark, blue =
eye-corner center, red = level line through the left eye, orange = forehead-top and
chin with the head extent drawn between them). **Always verify the markers sit on the
irises before acting on the number.**

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

Add `--json` to emit **one JSON object per image** on stdout instead of the fixed-width
table (used by the app's Character Identity *Validate* step, which cannot reliably parse
the table because the iris fields are tuples containing a space):

```powershell
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/eye-validation/measure_iris.py --json <image.png>
# {"image": "...", "stem": "...", "error": null, "iris_dy_pct": -0.2, "eye_dy_pct": 0.48, "interoc": 130.8, "forehead_top": [454, 364], "chin": [489, 1014], "head_height_px": 649.7, "face_box": [207, 361, 519, 653], "annot": "..."}
```

`error` is `"no face mesh"` (or `"cannot read <path>"`) when no measurement was possible, and the
numeric fields are then `null`. MediaPipe/absl progress messages go to **stderr**, so stdout stays
clean for parsing.

Dependencies: `requirements.txt` in this folder (`mediapipe==0.10.21`,
`opencv-python-headless==4.10.0.84`, `numpy`) — install into the repo venv.

## Adding/editing
- Keep output to git-ignored paths (`artifacts/tmp/**`) — never write artifacts
  into `tools/`.
- Pin versions; note why in this README.
