"""Crop and magnify a region of H3 output frames for close inspection (B-150).

Uses the frames extract-frames.py already wrote. Default region is the upper
band of the frame, where faces sit in these scenes.

    python helpers/h3-local-host/crop-region.py v2-real10 --top 0.5 --scale 1.6
    python helpers/h3-local-host/crop-region.py v2-base v2-real10 --compare face-c1.png
"""
import argparse
import glob
import os
import sys

import cv2
import numpy as np

ROOT = os.path.join('artifacts', 'tmp', 'h3-nsfw-proof')


def crop(frame, left, top, right, bottom, scale):
    h, w = frame.shape[:2]
    patch = frame[int(top * h):int(bottom * h), int(left * w):int(right * w)]
    if scale != 1.0 and patch.size:
        patch = cv2.resize(patch, (round(patch.shape[1] * scale), round(patch.shape[0] * scale)),
                           interpolation=cv2.INTER_CUBIC)
    return patch


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('tags', nargs='*')
    ap.add_argument('--left', type=float, default=0.0)
    ap.add_argument('--top', type=float, default=0.0)
    ap.add_argument('--right', type=float, default=1.0)
    ap.add_argument('--bottom', type=float, default=0.55)
    ap.add_argument('--scale', type=float, default=1.5)
    ap.add_argument('--compare', default=None)
    args = ap.parse_args()

    if not args.tags:
        args.tags = sorted(os.path.basename(os.path.dirname(p))
                           for p in glob.glob(os.path.join(ROOT, '*', '*.mp4')))

    rows = []
    for tag in args.tags:
        frames = sorted(glob.glob(os.path.join(ROOT, tag, 'frames', '*.png')))
        if not frames:
            print(f'{tag}: no frames (run extract-frames.py first)')
            continue
        picks = [frames[0], frames[len(frames) // 2], frames[-1]]
        patches = [crop(cv2.imread(p), args.left, args.top, args.right, args.bottom, args.scale)
                   for p in picks]
        height = max(p.shape[0] for p in patches)
        width = sum(p.shape[1] for p in patches) + 6 * (len(patches) - 1)
        row = np.full((height, width, 3), 24, dtype=np.uint8)
        x = 0
        for patch in patches:
            row[:patch.shape[0], x:x + patch.shape[1]] = patch
            x += patch.shape[1] + 6
        cv2.putText(row, tag, (10, height - 12), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 0, 0), 4, cv2.LINE_AA)
        cv2.putText(row, tag, (10, height - 12), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (255, 255, 255), 2, cv2.LINE_AA)
        out = os.path.join(ROOT, tag, f'crop-t{args.top}-b{args.bottom}.png')
        cv2.imwrite(out, row)
        print(f'{tag}: {out}')
        rows.append(row)

    if args.compare and rows:
        sheet = np.vstack([r for r in rows])
        dest = os.path.join(ROOT, args.compare)
        cv2.imwrite(dest, sheet)
        print(f'comparison -> {dest}')


if __name__ == '__main__':
    sys.exit(main())
