"""
Measure how faithfully each C1 variant reproduces the reference ARRANGEMENT.

Not a face/identity check and not a "did it make a person" check: it compares the two-person
SILHOUETTE of a render against the reference silhouette, so we can say numerically whether the
layout was inherited (`text` baseline vs controlled variants) instead of eyeballing it.

Metrics per render (all at the run canvas):
  iou            intersection-over-union of the reference mask and the render mask
  row_corr       Pearson correlation of the vertical (row) mass profiles - arrangement up/down
  col_corr       Pearson correlation of the horizontal (col) mass profiles - arrangement left/right
  centroid_dx/dy silhouette centroid offset in pixels (render - reference)
  area_ratio     render mask area / reference mask area

Usage:
  python measure-arrangement-match.py <reference.png> <run-dir> [--canvas 1216x832]
Outputs a markdown table on stdout and arrangement-match.json in the run dir.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image


def silhouette_mask(image_path: Path, canvas: tuple[int, int]) -> np.ndarray:
    from rembg import new_session, remove

    session = new_session("isnet-general-use")
    with Image.open(image_path) as im:
        rgba = remove(im.convert("RGB"), session=session)
    alpha = np.asarray(rgba.split()[-1], dtype=np.uint8)
    if alpha.shape[::-1] != canvas:
        alpha = np.asarray(
            Image.fromarray(alpha).resize(canvas, Image.Resampling.BILINEAR), dtype=np.uint8
        )
    return alpha > 128


def profiles(mask: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    return mask.sum(axis=1).astype(np.float64), mask.sum(axis=0).astype(np.float64)


def correlation(a: np.ndarray, b: np.ndarray) -> float:
    if a.std() == 0 or b.std() == 0:
        return 0.0
    return float(np.corrcoef(a, b)[0, 1])


def centroid(mask: np.ndarray) -> tuple[float, float]:
    ys, xs = np.nonzero(mask)
    if len(xs) == 0:
        return float("nan"), float("nan")
    return float(xs.mean()), float(ys.mean())


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("reference", type=Path)
    parser.add_argument("run_dir", type=Path)
    parser.add_argument("--canvas", default="1216x832")
    parser.add_argument("--prefix-filter", default="")
    args = parser.parse_args()

    width, height = (int(v) for v in args.canvas.lower().split("x"))
    canvas = (width, height)

    ref_mask = silhouette_mask(args.reference, canvas)
    ref_rows, ref_cols = profiles(ref_mask)
    ref_cx, ref_cy = centroid(ref_mask)
    ref_area = int(ref_mask.sum())

    results = []
    for png in sorted(args.run_dir.glob("**/*.png")):
        if args.prefix_filter and args.prefix_filter not in png.name:
            continue
        if "_0" not in png.name:
            continue
        try:
            mask = silhouette_mask(png, canvas)
        except Exception as exc:  # noqa: BLE001 - report, do not silently skip
            print(f"FAILED {png.name}: {exc}", file=sys.stderr)
            continue
        rows, cols = profiles(mask)
        cx, cy = centroid(mask)
        inter = int(np.logical_and(ref_mask, mask).sum())
        union = int(np.logical_or(ref_mask, mask).sum())
        results.append(
            {
                "variant": png.parent.name,
                "file": str(png),
                "iou": round(inter / union, 4) if union else 0.0,
                "row_corr": round(correlation(ref_rows, rows), 4),
                "col_corr": round(correlation(ref_cols, cols), 4),
                "centroid_dx": round(cx - ref_cx, 1),
                "centroid_dy": round(cy - ref_cy, 1),
                "area_ratio": round(int(mask.sum()) / ref_area, 3) if ref_area else 0.0,
            }
        )

    results.sort(key=lambda r: -r["iou"])
    print(f"\nreference: {args.reference}")
    print(f"canvas   : {width}x{height}\n")
    print("| variant | IoU vs ref | row corr | col corr | centroid dx,dy | area ratio |")
    print("|---|---|---|---|---|---|")
    for r in results:
        print(
            f"| {r['variant']} | {r['iou']:.3f} | {r['row_corr']:.3f} | {r['col_corr']:.3f} | "
            f"{r['centroid_dx']:+.1f}, {r['centroid_dy']:+.1f} | {r['area_ratio']:.2f} |"
        )

    out = args.run_dir / "arrangement-match.json"
    out.write_text(
        json.dumps(
            {"reference": str(args.reference), "canvas": f"{width}x{height}", "results": results},
            indent=2,
        ),
        encoding="utf-8",
    )
    print(f"\nwrote {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
