#!/usr/bin/env python3
"""Precise iris/eye measurement via MediaPipe FaceMesh (refine_landmarks=True).

APPROVED TOOL (repo tools/ infrastructure) — the canonical eye/face validation
checker. See tools/README.md + .github/instructions/agent-tools.instructions.md.

WHY THIS TOOL: Haar box centers, dark-region centroids, and Hough circles all
fail to pinpoint irises on photoreal portraits (brow/hair are darker than the
pupil; Haar mis-centers boxes; Hough is noisy). FaceMesh `refine_landmarks`
returns true iris landmarks, which is the only trustworthy eye-level method here.

Reports, per image:
  - iris-center dy  (landmarks 468 / 473 - printed and visually confirmed)
  - eye-center dy   (midpoints of eye corners: L 33/133, R 362/263) - independent
    of iris semantics, useful cross-check
  - dy in px and as % of interocular distance
  - interocular px (eye-corner centers)

Draws markers at iris + eye-corner centers on an annotated copy saved to
<repo>/artifacts/tmp/eye-output/<stem>_iris.png (GIT-IGNORED) so placement is
visually verified (never trust dy blind - ALWAYS check the marker sits on the
iris at high zoom before acting on the number).

Requires the repo venv: d:/src/DreamGenClone/.venv (see requirements.txt).
Run:
  d:/src/DreamGenClone/.venv/Scripts/python.exe tools/eye-validation/measure_iris.py <image...>

Usage: python measure_iris.py <image...>
"""
import os
import sys
import cv2
import numpy as np

# Output to a GIT-IGNORED location (artifacts/ is ignored), never into tools/.
_REPO_ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(_REPO_ROOT, "artifacts", "tmp", "eye-output")
os.makedirs(OUT, exist_ok=True)

import mediapipe as mp  # noqa: E402
mp_face_mesh = mp.solutions.face_mesh
FACEMESH = mp_face_mesh.FaceMesh(static_image_mode=True, max_num_faces=1,
                                 refine_landmarks=True)

# eye corner landmarks (mesh indices)
L_OUT, L_IN = 33, 133   # subject-left eye outer/inner corners
R_OUT, R_IN = 362, 263  # subject-right eye outer/inner corners
IRIS_L = [468, 469, 470, 471, 472]
IRIS_R = [473, 474, 475, 476, 477]


def mid(a, b):
    return ((a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0)


def analyze(path):
    img = cv2.imread(path)
    if img is None:
        return None, f"cannot read {path}"
    h, w = img.shape[:2]
    rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
    res = FACEMESH.process(rgb)
    if not res.multi_face_landmarks:
        return None, "no face mesh"
    lm = res.multi_face_landmarks[0].landmark
    pt = lambda i: (lm[i].x * w, lm[i].y * h)  # noqa: E731
    # iris centers 468 / 473 (refine_landmarks) - semantics verified visually
    li = pt(468)
    ri = pt(473)
    # eye-corner midpoints
    le = mid(pt(L_OUT), pt(L_IN))
    re = mid(pt(R_OUT), pt(R_IN))
    d_iris_y = ri[1] - li[1]
    d_eye_y = re[1] - le[1]
    interoc = re[0] - le[0]
    info = {
        "iris_L": (round(li[0]), round(li[1])),
        "iris_R": (round(ri[0]), round(ri[1])),
        "eyeL": (round(le[0]), round(le[1])),
        "eyeR": (round(re[0]), round(re[1])),
        "iris_dy_px": round(d_iris_y, 1),
        "iris_dy_pct": round(d_iris_y / interoc * 100.0, 2),
        "eye_dy_px": round(d_eye_y, 1),
        "eye_dy_pct": round(d_eye_y / interoc * 100.0, 2),
        "interoc": round(interoc, 1),
    }
    # annotate
    for i in IRIS_L:
        x, y = pt(i)
        cv2.circle(img, (int(x), int(y)), 3, (255, 255, 0), -1)
    for i in IRIS_R:
        x, y = pt(i)
        cv2.circle(img, (int(x), int(y)), 3, (255, 255, 0), -1)
    cv2.circle(img, (int(li[0]), int(li[1])), 9, (0, 255, 0), 3)
    cv2.circle(img, (int(ri[0]), int(ri[1])), 9, (0, 255, 0), 3)
    cv2.circle(img, (int(le[0]), int(le[1])), 9, (255, 0, 0), 3)
    cv2.circle(img, (int(re[0]), int(re[1])), 9, (255, 0, 0), 3)
    cv2.line(img, (0, int(le[1])), (w - 1, int(le[1])), (0, 0, 255), 3)
    cv2.line(img, (int(re[0]) - 20, int(re[1])), (int(re[0]) + 20, int(re[1])),
             (255, 0, 255), 3)
    cv2.putText(img, f"iris dy {info['iris_dy_pct']}%  eye dy {info['eye_dy_pct']}%",
                (30, 60), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 0, 255), 3)
    stem = os.path.splitext(os.path.basename(path))[0]
    out_p = os.path.join(OUT, f"{stem}_iris.png")
    cv2.imwrite(out_p, img)
    info["annot"] = out_p
    return info, None


def main():
    print(f"{'image':10} {'irisL':>11} {'irisR':>11} {'irisDy%':>9} {'eyeL':>11} "
          f"{'eyeR':>11} {'eyeDy%':>8} {'interoc':>8}")
    for path in sys.argv[1:]:
        stem = os.path.splitext(os.path.basename(path))[0]
        info, err = analyze(path)
        if err:
            print(f"{stem:10} {'-':>11} {'-':>11} {'-':>9} {'-':>11} {'-':>11} "
                  f"{'-':>8} {'-':>8}  {err}")
            continue
        print(f"{stem:10} {str(info['iris_L']):>11} {str(info['iris_R']):>11} "
              f"{str(info['iris_dy_pct']):>9} {str(info['eyeL']):>11} "
              f"{str(info['eyeR']):>11} {str(info['eye_dy_pct']):>8} "
              f"{str(info['interoc']):>8}")


if __name__ == "__main__":
    main()
