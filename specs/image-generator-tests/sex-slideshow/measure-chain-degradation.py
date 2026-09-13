"""Measure per-step image degradation across a chained edit run.

A chained slideshow re-encodes each frame to use it as the next step's reference, so loss can accumulate.
This quantifies that instead of eyeballing it.

Per step it reports:
  WxH        - output resolution. A changing resolution across steps means every link is also a RESAMPLE.
  KB         - file size (entropy proxy; falls as detail is lost to smoothness)
  gray_std   - luminance contrast
  edge_std   - standard deviation of a FIND_EDGES pass: a sharpness / high-frequency-energy proxy.
               Falling edge_std across steps == progressive softening.

Usage:
  python measure-chain-degradation.py <run-folder> [<run-folder> ...]
"""
import os
import sys

from PIL import Image, ImageFilter, ImageStat


def measure(path):
    """Return (width, height, kb, gray_std, edge_std, saturation, background_rgb, mean_rgb)."""
    with Image.open(path) as image:
        width, height = image.size
        rgb = image.convert("RGB")
        gray = rgb.convert("L")
        gray_std = ImageStat.Stat(gray).stddev[0]
        edge_std = ImageStat.Stat(gray.filter(ImageFilter.FIND_EDGES)).stddev[0]
        saturation = ImageStat.Stat(rgb.convert("HSV")).mean[1]

        # Background probe: the scene is shot against a flat studio backdrop, so a corner box is
        # background in EVERY step. Its RGB is what directly measures the reported "the background
        # starts to go different colours, and it gets worse with each edit".
        box_w, box_h = max(1, width // 10), max(1, height // 10)
        boxes = (
            (0, 0, box_w, box_h),
            (width - box_w, 0, width, box_h),
            (0, height - box_h, box_w, height),
            (width - box_w, height - box_h, width, height),
        )
        background = [0.0, 0.0, 0.0]
        for box in boxes:
            means = ImageStat.Stat(rgb.crop(box)).mean
            for channel in range(3):
                background[channel] += means[channel] / len(boxes)
        mean_rgb = ImageStat.Stat(rgb).mean

    return (width, height, os.path.getsize(path) / 1024.0, gray_std, edge_std,
            saturation, background, mean_rgb)


def main(folders):
    for folder in folders:
        if os.path.isfile(folder):
            # Single-file mode: measure one image (e.g. a CONTROL that isolates chain accumulation).
            width, height, kb, gray_std, edge_std, saturation, background, _ = measure(folder)
            print(f"{os.path.basename(folder):<46}{f'{width}x{height}':<11}{kb:>7.0f}"
                  f"{gray_std:>9.2f}{edge_std:>9.2f}{saturation:>7.1f}"
                  f"   bg=({background[0]:.0f},{background[1]:.0f},{background[2]:.0f})")
            continue

        files = sorted(
            os.path.join(folder, name)
            for name in os.listdir(folder)
            if name.startswith("step") and name.lower().endswith(".png")
        )
        if not files:
            print(f"{folder}: no step*.png found\n")
            continue

        print(f"=== {os.path.basename(folder.rstrip(os.sep))} ===")
        print(f"{'step':<6}{'WxH':<11}{'KB':>7}{'gray_std':>9}{'edge_std':>9}{'sat':>7}   background RGB")
        rows = []
        for path in files:
            width, height, kb, gray_std, edge_std, saturation, background, _ = measure(path)
            rows.append((os.path.basename(path)[:5], height, kb, gray_std, edge_std, saturation, background))
            print(f"{os.path.basename(path)[:5]:<6}{f'{width}x{height}':<11}{kb:>7.0f}"
                  f"{gray_std:>9.2f}{edge_std:>9.2f}{saturation:>7.1f}"
                  f"   ({background[0]:.0f},{background[1]:.0f},{background[2]:.0f})")

        first, last = rows[0], rows[-1]
        edge_delta = (last[4] - first[4]) / first[4] * 100 if first[4] else 0.0
        kb_delta = (last[2] - first[2]) / first[2] * 100 if first[2] else 0.0
        bg_drift = max(abs(last[6][c] - first[6][c]) for c in range(3))
        resolutions = {f"{r[1]}" for r in rows}
        print()
        print(f"  edge_std   {first[4]:.2f} -> {last[4]:.2f}   ({edge_delta:+.1f}%)")
        print(f"  KB         {first[2]:.0f} -> {last[2]:.0f}   ({kb_delta:+.1f}%)")
        print(f"  sat        {first[5]:.1f} -> {last[5]:.1f}")
        print(f"  background {tuple(round(v) for v in first[6])} -> {tuple(round(v) for v in last[6])}   max channel drift = {bg_drift:.0f}/255")
        print(f"  distinct heights: {sorted(resolutions)}")
        print()


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("usage: measure-chain-degradation.py <run-folder> [<run-folder> ...]")
    main(sys.argv[1:])
