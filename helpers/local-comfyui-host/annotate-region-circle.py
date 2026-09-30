#!/usr/bin/env python
"""Draw the coloured region circle that the C2b proof (B-129 capability table) needs.

WHY THIS EXISTS
---------------
C2b asks whether a coloured circle DRAWN ON THE IMAGE, named in the instruction, is enough to contain
an edit - no mask node, no latent masking, just an annotation the model can see. The mechanism proven
for C2a (a masked latent through VAEEncodeForInpaint) is a different route, and the app can only offer
the cheaper one if this one works.

The circle has to be drawn the SAME WAY every time, at the SAME place, or the two runs cannot be
compared and the containment measurement has no fixed region to measure against. That is all this
script is: a deterministic annotation, no model, no judgement.

The ellipse is inset from the measured rectangle so its stroke sits clear of the rectangle's edge
band, which the measurement excludes.

Usage
-----
  d:/src/DreamGenClone/.venv/Scripts/python.exe helpers/local-comfyui-host/annotate-region-circle.py \
      --input  artifacts/tmp/qwen-2-1/p0Base/result_0.png \
      --output artifacts/tmp/qwen-2-1/p0Circle/source.png \
      --rect-pct 32,55,35,33 --colour 0,64,255 --width 10 --inset 20
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


def parse_ints(value: str) -> list[int]:
    return [int(part) for part in value.split(",") if part.strip()]


def main() -> None:
    parser = argparse.ArgumentParser(description="Annotate an image with the region circle the C2b proof names.")
    parser.add_argument("--input", required=True, help="Source image to annotate.")
    parser.add_argument("--output", required=True, help="Annotated image to write (git-ignored).")
    parser.add_argument("--rect-pct", default="32,55,35,33", help="x,y,width,height as PERCENT of the frame.")
    parser.add_argument("--colour", default="0,64,255", help="RGB of the circle stroke.")
    parser.add_argument("--width", type=int, default=10, help="Stroke width in pixels.")
    parser.add_argument("--inset", type=int, default=20, help="Pixels to pull the ellipse inside the rectangle.")
    args = parser.parse_args()

    x_pct, y_pct, w_pct, h_pct = parse_ints(args.rect_pct)
    red, green, blue = parse_ints(args.colour)

    source = Path(args.input)
    if not source.is_file():
        raise SystemExit(f"source image not found: {source}")

    image = Image.open(source).convert("RGB")
    width, height = image.size

    left = int(round(width * x_pct / 100)) + args.inset
    top = int(round(height * y_pct / 100)) + args.inset
    right = int(round(width * (x_pct + w_pct) / 100)) - args.inset
    bottom = int(round(height * (y_pct + h_pct) / 100)) - args.inset
    if right <= left or bottom <= top:
        raise SystemExit("the rectangle is too small for the requested inset")

    draw = ImageDraw.Draw(image)
    draw.ellipse((left, top, right, bottom), outline=(red, green, blue), width=args.width)

    target = Path(args.output)
    target.parent.mkdir(parents=True, exist_ok=True)
    image.save(target)

    print(f"wrote {target}  size={width}x{height}  ellipse=({left},{top})-({right},{bottom})  colour=({red},{green},{blue})")
    print(f"measured rectangle (percent, as the measurement takes it): {x_pct},{y_pct},{w_pct},{h_pct}")


if __name__ == "__main__":
    main()
