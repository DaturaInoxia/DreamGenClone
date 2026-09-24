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
  - head extent, for framing crops: forehead-top (landmark 10) and chin (152), their
    vertical distance as head_height_px, and the full face_box (min/max over all
    landmarks). NOTE FaceMesh stops at the hairline, so landmark 10 is the highest
    FACIAL point, not the crown of the head (hair above it is not measured).
  - yaw evidence: nose_tip (landmark 1) and nose_offset_pct = (nose_tip_x -
    face_box_centre_x) / face_box_width * 100, SIGNED in image space: NEGATIVE means
    the nose points toward the LEFT of the image, POSITIVE toward the RIGHT. This is
    the one number the identity studio's angle gate asserts (Profile/ThreeQuarter
    Left must be negative, Right must be positive); it is deliberately NOT derived
    from iris/interocular distance, which is invalid under yaw.

Draws markers at iris + eye-corner centers on an annotated copy saved to
<repo>/artifacts/tmp/eye-output/<stem>_iris.png (GIT-IGNORED) so placement is
visually verified (never trust dy blind - ALWAYS check the marker sits on the
iris at high zoom before acting on the number).

Requires the repo venv: d:/src/DreamGenClone/.venv (see requirements.txt).
Run:
  d:/src/DreamGenClone/.venv/Scripts/python.exe tools/eye-validation/measure_iris.py <image...>

Usage: python measure_iris.py <image...> [--json]

  --json  print one JSON object per image instead of the fixed-width table
          (used by DreamGenClone's Character Identity Validate step).
"""
import json
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
# head-extent landmarks (10 is the hairline, not the crown - see module docstring)
FOREHEAD_TOP = 10
CHIN = 152
# nose tip, for the signed yaw evidence (image-space sign, see module docstring)
NOSE_TIP = 1


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
    # Head extent, for framing crops. 10 is the highest FACIAL point (hairline), not the crown.
    forehead = pt(FOREHEAD_TOP)
    chin = pt(CHIN)
    xs = [lm[i].x * w for i in range(len(lm))]
    ys = [lm[i].y * h for i in range(len(lm))]
    face_box = (round(min(xs)), round(min(ys)),
                round(max(xs) - min(xs)), round(max(ys) - min(ys)))
    # Yaw evidence: the nose tip's horizontal offset from the face-box centre, as a % of face-box
    # width. Signed in IMAGE space (negative = nose toward image-left, positive = image-right), so a
    # caller can assert the view convention without knowing how the model was prompted. Deliberately
    # not derived from iris/interocular distance: those are invalid under yaw.
    nose = pt(NOSE_TIP)
    face_centre_x = face_box[0] + face_box[2] / 2.0
    nose_offset_pct = (nose[0] - face_centre_x) / face_box[2] * 100.0
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
        "forehead_top": (round(forehead[0]), round(forehead[1])),
        "chin": (round(chin[0]), round(chin[1])),
        "head_height_px": round(chin[1] - forehead[1], 1),
        "face_box": face_box,
        "nose_tip": (round(nose[0]), round(nose[1])),
        "nose_offset_pct": round(nose_offset_pct, 2),
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
    # head extent, so the framing markers are visually verifiable at high zoom
    cv2.circle(img, (int(forehead[0]), int(forehead[1])), 9, (0, 165, 255), 3)
    cv2.circle(img, (int(chin[0]), int(chin[1])), 9, (0, 165, 255), 3)
    cv2.line(img, (int(forehead[0]), int(forehead[1])), (int(chin[0]), int(chin[1])),
             (0, 165, 255), 2)
    # yaw evidence: the face-box centre midline and the nose tip, so the sign can be checked by eye
    cv2.line(img, (int(face_centre_x), int(min(ys))), (int(face_centre_x), int(max(ys))),
             (255, 255, 0), 2)
    cv2.circle(img, (int(nose[0]), int(nose[1])), 9, (0, 255, 255), 3)
    cv2.line(img, (0, int(le[1])), (w - 1, int(le[1])), (0, 0, 255), 3)
    cv2.line(img, (int(re[0]) - 20, int(re[1])), (int(re[0]) + 20, int(re[1])),
             (255, 0, 255), 3)
    cv2.putText(img, f"iris dy {info['iris_dy_pct']}%  eye dy {info['eye_dy_pct']}%",
                (30, 60), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 0, 255), 3)
    cv2.putText(img, f"nose {info['nose_offset_pct']}% ({'image-left' if info['nose_offset_pct'] < 0 else 'image-right'})",
                (30, 100), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 255), 3)
    stem = os.path.splitext(os.path.basename(path))[0]
    out_p = os.path.join(OUT, f"{stem}_iris.png")
    cv2.imwrite(out_p, img)
    info["annot"] = out_p
    return info, None


def main():
    args = sys.argv[1:]
    as_json = "--json" in args
    paths = [a for a in args if a != "--json"]
    if not as_json:
        print(f"{'image':10} {'irisL':>11} {'irisR':>11} {'irisDy%':>9} {'eyeL':>11} "
              f"{'eyeR':>11} {'eyeDy%':>8} {'interoc':>8} {'noseOffset%':>12}")
    for path in paths:
        stem = os.path.splitext(os.path.basename(path))[0]
        info, err = analyze(path)
        if as_json:
            print(json.dumps({
                "image": path,
                "stem": stem,
                "error": err,
                "iris_dy_pct": info["iris_dy_pct"] if info else None,
                "eye_dy_pct": info["eye_dy_pct"] if info else None,
                "interoc": info["interoc"] if info else None,
                "forehead_top": info["forehead_top"] if info else None,
                "chin": info["chin"] if info else None,
                "head_height_px": info["head_height_px"] if info else None,
                "face_box": info["face_box"] if info else None,
                "nose_tip": info["nose_tip"] if info else None,
                "nose_offset_pct": info["nose_offset_pct"] if info else None,
                "annot": info.get("annot") if info else None,
            }))
            continue
        if err:
            print(f"{stem:10} {'-':>11} {'-':>11} {'-':>9} {'-':>11} {'-':>11} "
                  f"{'-':>8} {'-':>8} {'-':>12}  {err}")
            continue
        print(f"{stem:10} {str(info['iris_L']):>11} {str(info['iris_R']):>11} "
              f"{str(info['iris_dy_pct']):>9} {str(info['eyeL']):>11} "
              f"{str(info['eyeR']):>11} {str(info['eye_dy_pct']):>8} "
              f"{str(info['interoc']):>8} {str(info['nose_offset_pct']):>12}")


if __name__ == "__main__":
    main()
