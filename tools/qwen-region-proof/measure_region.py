#!/usr/bin/env python
"""Measure whether a Qwen-Image-2.1 edit was contained to a masked REGION.

WHY THIS EXISTS
---------------
`TextEncodeQwenImage21` has NO mask input (verified from the host's /object_info: clip, prompt,
negative_prompt, resolution, images, vae). The only way to limit an edit to a region is to mask the
LATENT the encoder already returns as output[2], via `SetLatentNoiseMask`. Whether that actually
contains the edit cannot be settled by looking at the output: a masked run and an unmasked run both
"look like an edit". It has to be measured.

THE MEASUREMENT
---------------
Three images are compared: the SOURCE that was edited, the MASKED run, and the CONTROL run (the
identical instruction and seed, emitted without the mask node).

The masked run is contained if, OUTSIDE the masked rectangle, it still sits on the SOURCE image -
because at denoise 1.0 the sampler blends the original latent back there. The control has no such
constraint, so it is free to drift everywhere. The decisive number is therefore:

    mean|diff| source vs masked, OUTSIDE the rectangle   <<   mean|diff| source vs control, OUTSIDE

A band around the rectangle is excluded from the "outside" set (the mask carries a blur radius, and
geometry rounding shifts the edge by a pixel or two), so the comparison cannot be flattered by the
soft edge. The same band is ALSO excluded from the "inside" set, so "inside" only counts pixels
unambiguously within the region.

This is a pixel measurement over an explicit rectangle. It is not a semantic judgement: it says the
edit was contained, not that the contained edit was good.

Usage
-----
  d:/src/DreamGenClone/.venv/Scripts/python.exe tools/qwen-region-proof/measure_region.py \
      --source artifacts/tmp/qwen-2-1/p0Base/result_0.png \
      --masked artifacts/tmp/qwen-2-1/p0RegionMasked/result_0.png \
      --control artifacts/tmp/qwen-2-1/p0RegionControl/result_0.png \
      --rect-pct 32,55,35,33 --blur 8 \
      --out artifacts/tmp/qwen-2-1/p0-region/measure.json

Outputs a JSON report and a human-readable summary. Exit code is 0 when the masked run is contained
and the control is not, 1 otherwise, so it can gate a proof run.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

# A pixel counts as "changed" beyond this per-channel delta. 16/255 is comfortably above codec and
# VAE-round-trip noise while still far below a visible colour change.
CHANGED_THRESHOLD = 16


def load_rgb(path: Path) -> np.ndarray:
    with Image.open(path) as image:
        return np.asarray(image.convert("RGB"), dtype=np.float64)


def diff_stats(a: np.ndarray, b: np.ndarray, region: np.ndarray) -> dict:
    """Mean absolute per-channel difference over `region` (a boolean mask), plus % pixels changed."""
    if a.shape != b.shape:
        raise SystemExit(f"Image shapes differ: {a.shape} vs {b.shape}. Compare renders of one source.")
    delta = np.abs(a - b)
    per_pixel = delta.max(axis=2)
    selected = per_pixel[region]
    if selected.size == 0:
        raise SystemExit("The selected region is empty; check --rect-pct and --band.")
    return {
        "pixels": int(selected.size),
        "meanAbsDiff": round(float(delta[region].mean()), 3),
        "meanMaxChannelDiff": round(float(selected.mean()), 3),
        "pctPixelsChanged": round(float((selected > CHANGED_THRESHOLD).mean() * 100.0), 3),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Measure whether a Qwen-2.1 edit stayed inside a masked region.")
    parser.add_argument("--source", required=True, type=Path, help="the image that was edited (the edit target)")
    parser.add_argument("--masked", required=True, type=Path, help="the run whose latent was masked")
    parser.add_argument("--control", required=True, type=Path, help="the same run emitted WITHOUT the mask node")
    parser.add_argument("--rect-pct", required=True, help="mask rectangle as x,y,width,height in PERCENT of the frame (what MaskRectArea takes)")
    parser.add_argument("--blur", type=int, default=0, help="the mask's blur radius, in the same percent units")
    parser.add_argument("--band", type=int, default=24, help="pixels of margin excluded on BOTH sides of the rectangle edge (default 24)")
    parser.add_argument("--out", type=Path, help="write the JSON report here (default: next to the masked image)")
    args = parser.parse_args()

    source = load_rgb(args.source)
    masked = load_rgb(args.masked)
    control = load_rgb(args.control)
    height, width = source.shape[:2]

    try:
        x_pct, y_pct, w_pct, h_pct = (float(v) for v in args.rect_pct.split(","))
    except ValueError:
        raise SystemExit("--rect-pct must be four numbers: x,y,width,height (percent)")

    # Same derivation the node performs: percent of the frame.
    x0 = int(x_pct / 100.0 * width)
    y0 = int(y_pct / 100.0 * height)
    x1 = x0 + int(w_pct / 100.0 * width)
    y1 = y0 + int(h_pct / 100.0 * height)

    # The blur radius is given in the node's own percent units, so convert it for the margin too.
    margin = args.band + int(args.blur / 100.0 * min(width, height))

    inside = np.zeros((height, width), dtype=bool)
    inside[y0 + margin : y1 - margin, x0 + margin : x1 - margin] = True
    outside = np.ones((height, width), dtype=bool)
    outside[max(0, y0 - margin) : y1 + margin, max(0, x0 - margin) : x1 + margin] = False

    report = {
        "source": str(args.source),
        "masked": str(args.masked),
        "control": str(args.control),
        "imageSize": [width, height],
        "rectPct": [x_pct, y_pct, w_pct, h_pct],
        "maskBlurPct": args.blur,
        "rectPixels": [x0, y0, x1, y1],
        "marginPixels": margin,
        "changedThreshold": CHANGED_THRESHOLD,
        "inside": {
            "sourceVsMasked": diff_stats(source, masked, inside),
            "maskedVsControl": diff_stats(masked, control, inside),
            "sourceVsControl": diff_stats(source, control, inside),
        },
        "outside": {
            "sourceVsMasked": diff_stats(source, masked, outside),
            "maskedVsControl": diff_stats(masked, control, outside),
            "sourceVsControl": diff_stats(source, control, outside),
        },
    }

    # The verdict. Containment means the masked run preserved the source outside the rectangle while
    # actually changing something inside it, and the unmasked control did neither.
    out_masked = report["outside"]["sourceVsMasked"]["meanAbsDiff"]
    out_control = report["outside"]["sourceVsControl"]["meanAbsDiff"]
    in_masked = report["inside"]["sourceVsMasked"]["meanAbsDiff"]
    contained = out_masked * 2.0 < out_control and in_masked > out_masked
    report["verdict"] = {
        "outsidePreservedByMask": bool(out_masked * 2.0 < out_control),
        "insideChanged": bool(in_masked > out_masked),
        "contained": bool(contained),
        "outsideRatioControlOverMasked": round(float(out_control / out_masked), 2) if out_masked > 0 else None,
    }

    out_path = args.out or (args.masked.parent / "measure.json")
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print(f"image            : {width}x{height}")
    print(f"mask rectangle   : pct={report['rectPct']} -> px={report['rectPixels']}  (margin {margin} px excluded each side)")
    print("")
    print("OUTSIDE the rectangle (the containment test)")
    print(f"  source vs MASKED  : mean|diff| {out_masked:>7}   changed {report['outside']['sourceVsMasked']['pctPixelsChanged']:>6}%")
    print(f"  source vs CONTROL : mean|diff| {out_control:>7}   changed {report['outside']['sourceVsControl']['pctPixelsChanged']:>6}%")
    print("")
    print("INSIDE the rectangle (the edit happened test)")
    print(f"  source vs MASKED  : mean|diff| {in_masked:>7}   changed {report['inside']['sourceVsMasked']['pctPixelsChanged']:>6}%")
    print(f"  source vs CONTROL : mean|diff| {report['inside']['sourceVsControl']['meanAbsDiff']:>7}   changed {report['inside']['sourceVsControl']['pctPixelsChanged']:>6}%")
    print("")
    print(f"CONTROL/MASKED outside ratio : {report['verdict']['outsideRatioControlOverMasked']}x")
    print(f"VERDICT                      : {'CONTAINED' if contained else 'NOT CONTAINED'}")
    print(f"report                       : {out_path}")

    return 0 if contained else 1


if __name__ == "__main__":
    sys.exit(main())
