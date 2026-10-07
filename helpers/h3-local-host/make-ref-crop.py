"""Derive a tighter reference crop from a scene image, using MediaPipe FaceMesh.

H3 Ref2VA takes two reference images. A second reference framing the subjects
(or their faces) more tightly gives the model more pixels per identity feature,
which is the lever we want for the "faces not holding up" problem.

Face boxes come from MediaPipe FaceMesh landmarks (the same detector family the
repo's canonical eye-validation tool uses), not Haar boxes or dark-region
centroids, which are known-bad on photoreal faces.

    python helpers/h3-local-host/make-ref-crop.py <in.png> <out.png> [--mode head|subject] [--pad 0.6]
"""
import argparse
import sys

import cv2
import numpy as np


def face_boxes(image):
    """Bounding boxes (x, y, w, h) per detected face, largest first."""
    import mediapipe as mp

    with mp.solutions.face_mesh.FaceMesh(
            static_image_mode=True, max_num_faces=4, refine_landmarks=True,
            min_detection_confidence=0.3) as mesh:
        result = mesh.process(cv2.cvtColor(image, cv2.COLOR_BGR2RGB))

    if not result.multi_face_landmarks:
        return []

    h, w = image.shape[:2]
    boxes = []
    for landmarks in result.multi_face_landmarks:
        xs = [lm.x * w for lm in landmarks.landmark]
        ys = [lm.y * h for lm in landmarks.landmark]
        x0, x1 = max(0.0, min(xs)), min(float(w), max(xs))
        y0, y1 = max(0.0, min(ys)), min(float(h), max(ys))
        boxes.append((x0, y0, x1 - x0, y1 - y0))
    boxes.sort(key=lambda b: b[2] * b[3], reverse=True)
    return boxes


def clamp_box(box, width, height):
    x, y, w, h = box
    x0, y0 = max(0, int(round(x))), max(0, int(round(y)))
    x1, y1 = min(width, int(round(x + w))), min(height, int(round(y + h)))
    return x0, y0, max(1, x1 - x0), max(1, y1 - y0)


def expand(box, factor, width, height):
    """Grow a box by `factor` of its own size on every side, then clamp."""
    x, y, w, h = box
    dx, dy = w * factor, h * factor
    return clamp_box((x - dx, y - dy, w + 2 * dx, h + 2 * dy), width, height)


def subject_box(box, width, height):
    """Head box grown downward to roughly a head-and-shoulders portrait."""
    x, y, w, h = box
    cx = x + w / 2
    side = max(w, h) * 2.6
    return clamp_box((cx - side / 2, y - side * 0.30, side, side * 1.15), width, height)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('source')
    ap.add_argument('dest')
    ap.add_argument('--mode', choices=['head', 'subject'], default='subject')
    ap.add_argument('--pad', type=float, default=0.6, help='head mode: grow the face box by this factor')
    ap.add_argument('--size', type=int, default=1024, help='output square size')
    args = ap.parse_args()

    image = cv2.imread(args.source)
    if image is None:
        raise SystemExit(f'Could not read {args.source}')
    height, width = image.shape[:2]

    boxes = face_boxes(image)
    if not boxes:
        print(f'{args.source}: no face detected', file=sys.stderr)
        return 1

    print(f'detected {len(boxes)} face(s); largest w={boxes[0][2]:.0f} h={boxes[0][3]:.0f} of {width}x{height}')

    if args.mode == 'head':
        box = expand(boxes[0], args.pad, width, height)
    else:
        box = subject_box(boxes[0], width, height)

    x, y, w, h = box
    crop = image[y:y + h, x:x + w]
    print(f'crop x={x} y={y} w={w} h={h} -> {args.size}x{args.size} ({args.mode})')

    side = max(crop.shape[0], crop.shape[1])
    canvas = np.zeros((side, side, 3), dtype=np.uint8)
    oy, ox = (side - crop.shape[0]) // 2, (side - crop.shape[1]) // 2
    canvas[oy:oy + crop.shape[0], ox:ox + crop.shape[1]] = crop
    canvas = cv2.resize(canvas, (args.size, args.size), interpolation=cv2.INTER_CUBIC)

    cv2.imwrite(args.dest, canvas)
    print(f'wrote {args.dest}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
