"""Extract comparison frames from H3 Ref2VA output videos (B-150 tuning sweep).

For each tag: pull evenly spaced frames out of the mp4, save them individually
under <tag>/frames/, and build a single contact strip (<tag>/strip.png) so a
whole clip can be judged from one image.

    python helpers/h3-local-host/extract-frames.py v2-base v2-real10
    python helpers/h3-local-host/extract-frames.py --all
    python helpers/h3-local-host/extract-frames.py v2-base v2-real10 --compare compare-1.png

Read-only with respect to the videos; writes only under artifacts/tmp/h3-nsfw-proof/.
"""
import argparse
import glob
import os
import sys

import cv2
import numpy as np

ROOT = os.path.join('artifacts', 'tmp', 'h3-nsfw-proof')


def find_video(tag):
    matches = sorted(glob.glob(os.path.join(ROOT, tag, '*.mp4')))
    return matches[0] if matches else None


def extract(tag, count):
    """Return `count` evenly spaced BGR frames from the tag's first mp4."""
    path = find_video(tag)
    if path is None:
        return None, None

    cap = cv2.VideoCapture(path)
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    fps = cap.get(cv2.CAP_PROP_FPS) or 24.0
    if total <= 0:
        cap.release()
        return None, None

    indices = [round(i * (total - 1) / (count - 1)) for i in range(count)] if count > 1 else [0]
    frames = []
    for index in indices:
        cap.set(cv2.CAP_PROP_POS_FRAMES, index)
        ok, frame = cap.read()
        if ok:
            cv2.putText(frame, f'{index / fps:.1f}s', (16, 44),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.2, (255, 255, 255), 3, cv2.LINE_AA)
            cv2.putText(frame, f'{index / fps:.1f}s', (16, 44),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.2, (0, 0, 0), 1, cv2.LINE_AA)
            frames.append(frame)
    cap.release()

    outdir = os.path.join(ROOT, tag, 'frames')
    os.makedirs(outdir, exist_ok=True)
    for index, frame in zip(indices, frames):
        cv2.imwrite(os.path.join(outdir, f'frame_{index:04d}.png'), frame)

    return frames, (total, fps, path)


def resize_to_height(frame, height):
    scale = height / frame.shape[0]
    return cv2.resize(frame, (max(1, round(frame.shape[1] * scale)), height), interpolation=cv2.INTER_AREA)


def strip(frames, height=256, gap=6):
    tiles = [resize_to_height(f, height) for f in frames]
    width = sum(t.shape[1] for t in tiles) + gap * (len(tiles) - 1)
    canvas = np.full((height, width, 3), 24, dtype=np.uint8)
    x = 0
    for tile in tiles:
        canvas[:, x:x + tile.shape[1]] = tile
        x += tile.shape[1] + gap
    return canvas


def label(image, text, height=34):
    band = np.full((height, image.shape[1], 3), 0, dtype=np.uint8)
    cv2.putText(band, text, (10, height - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2, cv2.LINE_AA)
    return np.vstack([band, image])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('tags', nargs='*', help='run tags to extract (default: all with an mp4)')
    ap.add_argument('--all', action='store_true')
    ap.add_argument('--count', type=int, default=5, help='frames per clip')
    ap.add_argument('--height', type=int, default=256)
    ap.add_argument('--compare', default=None, help='also write a stacked comparison image to this name')
    args = ap.parse_args()

    if args.all or not args.tags:
        args.tags = sorted(os.path.basename(os.path.dirname(p))
                           for p in glob.glob(os.path.join(ROOT, '*', '*.mp4')))

    rows = []
    for tag in args.tags:
        frames, info = extract(tag, args.count)
        if not frames:
            print(f'{tag}: no video found')
            continue
        total, fps, path = info
        print(f"{tag}: {total} frames @ {fps:.0f} fps = {total / fps:.1f}s -> {os.path.basename(path)}")
        run_strip = strip(frames, height=args.height)
        cv2.imwrite(os.path.join(ROOT, tag, 'strip.png'), run_strip)
        rows.append((tag, run_strip))

    if args.compare is not None and rows:
        sheet = np.vstack([label(run_strip, tag) for tag, run_strip in rows])
        dest = os.path.join(ROOT, args.compare)
        cv2.imwrite(dest, sheet)
        print(f'comparison -> {dest}')


if __name__ == '__main__':
    sys.exit(main())
